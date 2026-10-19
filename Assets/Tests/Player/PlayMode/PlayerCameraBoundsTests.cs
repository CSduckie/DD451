#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class PlayerCameraBoundsTests : PlayerPlayModeTestBase
{
    private readonly List<Scene> temporaryScenes = new List<Scene>();

    [UnityTearDown]
    public IEnumerator RemoveTemporaryScenes()
    {
        foreach (Scene scene in temporaryScenes)
        {
            if (scene.IsValid() && scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(scene); }
        }
        temporaryScenes.Clear();
    }

    [UnityTest]
    public IEnumerator InactiveAncestorsValidateAndDeferFollowingUntilBoundsAreActive()
    {
        PlayerController player = CreatePlayer(Vector3.zero);
        GameObject environment = new GameObject("Dormant camera bounds environment");
        Track(environment);
        environment.SetActive(false);
        CameraFixture rig = CreateCamera(player, environment.transform);

        Assert.That(rig.binder.ValidateBounds(out string error), Is.True, error);
        Assert.That(rig.volume.gameObject.activeSelf, Is.True);
        Assert.That(rig.volume.gameObject.activeInHierarchy, Is.False);
        rig.root.SetActive(true);
        Assert.That(rig.camera.Follow, Is.Null, "No unconstrained following frame while the environment is dormant.");
        Assert.That(rig.binder.LastBoundsError, Is.Null, "An inactive loading ancestor is not a configuration error.");
        Assert.That(rig.confiner.BoundingVolume, Is.SameAs(rig.volume));

        environment.SetActive(true);
        yield return null;

        Assert.That(rig.camera.Follow, Is.SameAs(player.cameraTarget));
        Assert.That(rig.confiner.IsValid, Is.True);
        Assert.That(rig.binder.LastBoundsError, Is.Null);
    }

    [Test]
    public void MissingDisabledAndSolidBoundsRejectFollowWithDiagnostics()
    {
        PlayerController player = CreatePlayer(Vector3.zero);
        CameraFixture rig = CreateCamera(player);
        rig.root.SetActive(true);
        Assert.That(rig.camera.Follow, Is.SameAs(player.cameraTarget));

        rig.confiner.BoundingVolume = null;
        ExpectBoundsWarning();
        rig.binder.Bind(player);
        AssertRejected(rig);

        rig.confiner.BoundingVolume = rig.volume;
        rig.volume.enabled = false;
        ExpectBoundsWarning();
        rig.binder.Bind(player);
        AssertRejected(rig);

        rig.volume.enabled = true;
        rig.volume.isTrigger = false;
        ExpectBoundsWarning();
        rig.binder.Bind(player);
        AssertRejected(rig);

        rig.volume.isTrigger = true;
        rig.binder.Bind(player);
        Assert.That(rig.camera.Follow, Is.SameAs(player.cameraTarget));
        Assert.That(rig.binder.LastBoundsError, Is.Null);
    }

    [UnityTest]
    public IEnumerator WrongSceneAndDestroyedVolumesCannotKeepAStaleBinding()
    {
        PlayerController player = CreatePlayer(Vector3.zero);
        CameraFixture rig = CreateCamera(player);
        rig.root.SetActive(true);
        Scene otherScene = SceneManager.CreateScene("CameraBounds other additive scene");
        temporaryScenes.Add(otherScene);
        BoxCollider otherVolume = CreateVolume("Wrong scene bounds", null);
        SceneManager.MoveGameObjectToScene(otherVolume.gameObject, otherScene);
        rig.confiner.BoundingVolume = otherVolume;

        ExpectBoundsWarning();
        rig.binder.Bind(player);
        AssertRejected(rig);
        Assert.That(rig.confiner.BoundingVolume, Is.Null, "A rejected foreign-scene reference must not remain in the native extension.");

        rig.confiner.BoundingVolume = rig.volume;
        rig.binder.Bind(player);
        Assert.That(rig.camera.Follow, Is.SameAs(player.cameraTarget));
        ExpectBoundsWarning();
        Object.Destroy(rig.volume.gameObject);
        yield return null;
        yield return null;
        Assert.That(rig.camera.Follow, Is.Null);
        Assert.That(rig.binder.LastBoundsError, Is.Not.Null);
    }

    [UnityTest]
    public IEnumerator NativeBrainConfinesWarpAndReplacementVolumeWithoutChangingLens()
    {
        PlayerController player = CreatePlayer(Vector3.zero);
        CameraFixture rig = CreateCamera(player);
        float fieldOfView = rig.camera.Lens.FieldOfView;
        CinemachinePositionComposer composer = rig.camera.GetComponent<CinemachinePositionComposer>();
        float distance = composer.CameraDistance;
        rig.volume.transform.position = new Vector3(0f, 3f, -12f);
        rig.volume.size = new Vector3(6f, 8f, 2f);
        rig.root.SetActive(true);
        Assert.That(player.ResetForExploration(1, new Vector3(20f, 0.05f, 0f)), Is.True);
        player.Body.useGravity = false;
        yield return null;
        UpdateBrain(rig);

        AssertInside(rig.volume, rig.output.transform.position);
        Assert.That(rig.output.transform.position.x, Is.EqualTo(3f).Within(0.05f));
        Assert.That(rig.confiner.CameraWasDisplaced(rig.camera), Is.True,
            "This checks actual native Brain output, not only the presence of a Confiner component.");

        Assert.That(player.ResetForExploration(2, new Vector3(-20f, 8f, 0f)), Is.True);
        player.Body.useGravity = false;
        Assert.That(rig.camera.PreviousStateIsValid, Is.False, "Teleport must discard previous slowing/damping history.");
        yield return null;
        UpdateBrain(rig);
        AssertInside(rig.volume, rig.output.transform.position);
        Assert.That(rig.output.transform.position.x, Is.EqualTo(-3f).Within(0.05f));

        BoxCollider replacement = CreateVolume("Replacement local bounds", null);
        replacement.transform.position = new Vector3(-12f, 3f, -12f);
        replacement.size = new Vector3(4f, 8f, 2f);
        rig.confiner.BoundingVolume = replacement;
        yield return null;
        Assert.That(rig.camera.PreviousStateIsValid, Is.False, "Changing the sole native volume reference resets its old state.");
        UpdateBrain(rig);
        AssertInside(replacement, rig.output.transform.position);
        Assert.That(rig.output.transform.position.x, Is.EqualTo(-14f).Within(0.05f));

        // Editing this same Box's center/size must not interpolate from the old, now-outside position.
        replacement.center = Vector3.right * 2f;
        replacement.size = new Vector3(2f, 8f, 2f);
        yield return null;
        Assert.That(rig.camera.PreviousStateIsValid, Is.False);
        UpdateBrain(rig);
        AssertInside(replacement, rig.output.transform.position);
        Assert.That(rig.output.transform.position.x, Is.EqualTo(-11f).Within(0.05f));
        Assert.That(rig.camera.Lens.FieldOfView, Is.EqualTo(fieldOfView));
        Assert.That(composer.CameraDistance, Is.EqualTo(distance));
        Assert.That(rig.output.orthographic, Is.False);
        Assert.That(rig.output.transform.position.z, Is.EqualTo(-12f).Within(0.05f));
    }

    [Test]
    public void RebindingAndUnbindingRemoveOldPlayerWarpSubscriptions()
    {
        PlayerController first = CreatePlayer(Vector3.zero);
        PlayerController second = CreatePlayer(Vector3.right * 2f);
        CameraFixture rig = CreateCamera(first);
        rig.root.SetActive(true);
        rig.binder.Bind(second);
        rig.camera.PreviousStateIsValid = true;

        Assert.That(first.ResetForExploration(1, Vector3.right * 4f), Is.True);
        Assert.That(rig.camera.PreviousStateIsValid, Is.True, "An old player's teleport must not reset the replacement camera binding.");
        Assert.That(rig.camera.Follow, Is.SameAs(second.cameraTarget));
        Assert.That(second.ResetForExploration(1, Vector3.right * 8f), Is.True);
        Assert.That(rig.camera.PreviousStateIsValid, Is.False);

        rig.binder.Bind(null);
        Assert.That(rig.camera.Follow, Is.Null);
        Assert.That(rig.confiner.BoundingVolume, Is.SameAs(rig.volume),
            "Unbind clears runtime subscriptions but retains this scene's sole Inspector configuration.");
        rig.camera.PreviousStateIsValid = true;
        Assert.That(second.ResetForExploration(2, Vector3.right * 10f), Is.True);
        Assert.That(rig.camera.PreviousStateIsValid, Is.True);
    }

    [Test]
    public void ReplacingFollowCameraClearsThePreviouslyBoundCamera()
    {
        PlayerController player = CreatePlayer(Vector3.zero);
        CameraFixture rig = CreateCamera(player);
        rig.root.SetActive(true);
        GameObject replacementObject = new GameObject("Replacement SideFollow");
        replacementObject.transform.SetParent(rig.root.transform, false);
        CinemachineCamera replacementCamera = replacementObject.AddComponent<CinemachineCamera>();
        CinemachineConfiner3D replacementConfiner = replacementObject.AddComponent<CinemachineConfiner3D>();
        replacementConfiner.BoundingVolume = rig.volume;
        rig.binder.followCamera = replacementCamera;
        rig.binder.confiner = replacementConfiner;
        rig.binder.Bind(player);

        Assert.That(rig.camera.Follow, Is.Null, "Changing the Inspector reference cannot leave the old camera following.");
        Assert.That(rig.camera.PreviousStateIsValid, Is.False);
        Assert.That(replacementCamera.Follow, Is.SameAs(player.cameraTarget));
        Assert.That(replacementCamera.PreviousStateIsValid, Is.False);
    }

    [Test]
    public void BoundsUnderOutputCameraAreRejectedAsMovingGeometry()
    {
        PlayerController player = CreatePlayer(Vector3.zero);
        CameraFixture rig = CreateCamera(player);
        rig.volume.transform.SetParent(rig.output.transform, true);

        Assert.That(rig.binder.ValidateBounds(out string error), Is.False);
        Assert.That(error, Does.Contain("独立静态范围"));
        ExpectBoundsWarning();
        rig.root.SetActive(true);
        AssertRejected(rig);
    }

    private CameraFixture CreateCamera(PlayerController player, Transform boundsParent = null)
    {
        GameObject holder = new GameObject("Camera bounds test rig");
        holder.SetActive(false);
        Track(holder);
        GameObject instance = CreatePrefab("Assets/Prefabs/Player/PlayerCamera.prefab", holder.transform);
        PlayerCameraTargetBinder binder = instance.GetComponent<PlayerCameraTargetBinder>();
        Assert.That(binder, Is.Not.Null);
        Assert.That(binder.followCamera, Is.Not.Null);
        CinemachineConfiner3D confiner = binder.followCamera.GetComponent<CinemachineConfiner3D>();
        if (confiner == null) { confiner = binder.followCamera.gameObject.AddComponent<CinemachineConfiner3D>(); }
        BoxCollider volume = CreateVolume("CameraBounds", boundsParent);
        confiner.BoundingVolume = volume;
        confiner.SlowingDistance = 2f;
        binder.confiner = confiner;
        binder.player = player;
        Camera output = instance.GetComponentInChildren<Camera>(true);
        CinemachineBrain brain = output.GetComponent<CinemachineBrain>();
        Assert.That(brain, Is.Not.Null);
        brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
        return new CameraFixture
        {
            root = holder, binder = binder, camera = binder.followCamera, confiner = confiner,
            volume = volume, output = output, brain = brain
        };
    }

    private BoxCollider CreateVolume(string name, Transform parent)
    {
        GameObject volumeObject = new GameObject(name);
        Track(volumeObject);
        if (parent != null) { volumeObject.transform.SetParent(parent, false); }
        volumeObject.transform.position = new Vector3(0f, 3f, -12f);
        BoxCollider volume = volumeObject.AddComponent<BoxCollider>();
        volume.isTrigger = true;
        volume.size = new Vector3(60f, 20f, 2f);
        return volume;
    }

    private static void UpdateBrain(CameraFixture rig)
    {
        rig.brain.ManualUpdate();
    }

    private static void AssertInside(BoxCollider volume, Vector3 position)
    {
        Assert.That(Vector3.Distance(volume.ClosestPoint(position), position), Is.LessThan(0.01f));
    }

    private static void ExpectBoundsWarning()
    {
        LogAssert.Expect(LogType.Warning, new Regex("^\\[PlayerCameraBounds\\]"));
    }

    private static void AssertRejected(CameraFixture rig)
    {
        Assert.That(rig.camera.Follow, Is.Null);
        Assert.That(rig.binder.LastBoundsError, Is.Not.Null.And.Not.Empty);
    }

    private sealed class CameraFixture
    {
        public GameObject root;
        public PlayerCameraTargetBinder binder;
        public CinemachineCamera camera;
        public CinemachineConfiner3D confiner;
        public BoxCollider volume;
        public Camera output;
        public CinemachineBrain brain;
    }
}
#endif
