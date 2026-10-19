#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

public class PlayerMovementTests : PlayerPlayModeTestBase
{
    private Keyboard keyboard;

    [SetUp]
    public void AddTestKeyboard()
    {
        keyboard = InputSystem.AddDevice<Keyboard>();
    }

    [TearDown]
    public void RemoveTestKeyboard()
    {
        if (keyboard != null && keyboard.added)
        {
            InputSystem.RemoveDevice(keyboard);
        }
        keyboard = null;
    }

    [UnityTest]
    public IEnumerator RealKeyboardMovesCrouchesJumpsOnceAndRespectsWallAndPlane()
    {
        CreateFloor();
        PlayerController player = CreatePlayer(Vector3.zero);
        Assert.That(player.GetComponentsInChildren<Collider>(), Has.Length.EqualTo(1),
            "The placeholder visual must not add another physical collider.");
        Assert.That(player.ResetForExploration(1, new Vector3(0f, 0.05f, 7f)), Is.True);
        yield return new WaitForSeconds(0.3f);
        Assert.That(player.IsGrounded, Is.True);
        float startX = player.transform.position.x;
        SetKeys(Key.D);
        yield return new WaitForSeconds(0.25f);
        Assert.That(player.transform.position.x, Is.GreaterThan(startX + 0.4f));
        Assert.That(player.FacingDirection, Is.EqualTo(1));
        SetKeys(Key.A);
        yield return new WaitForSeconds(0.25f);
        Assert.That(player.FacingDirection, Is.EqualTo(-1));
        SetKeys(Key.C, Key.D);
        float crouchDeadline = Time.time + 1f;
        while ((!player.IsCrouching || Mathf.Abs(player.Body.linearVelocity.x - player.config.crouchSpeed) > 0.15f)
            && Time.time < crouchDeadline)
        {
            yield return new WaitForFixedUpdate();
        }
        Assert.That(player.IsCrouching, Is.True);
        Assert.That(player.Body.linearVelocity.x, Is.EqualTo(player.config.crouchSpeed).Within(0.15f),
            "Crouch movement must converge to its configured speed after smoothly reversing direction.");
        SetKeys(Key.C, Key.Space);
        yield return new WaitForSeconds(0.15f);
        Assert.That(player.IsGrounded, Is.True, "Crouching does not enable a separate crouch jump.");
        SetKeys();
        yield return new WaitForSeconds(0.1f);
        Assert.That(player.IsCrouching, Is.False);

        float groundY = player.transform.position.y;
        float highestY = groundY;
        float previousVelocity = 0f;
        int launches = 0;
        SetKeys(Key.Space);
        for (int i = 0; i < 120; i++)
        {
            yield return new WaitForFixedUpdate();
            float upwardVelocity = player.Body.linearVelocity.y;
            if (upwardVelocity > player.config.jumpSpeed * 0.65f && previousVelocity <= 1f)
            {
                launches++;
            }
            previousVelocity = upwardVelocity;
            highestY = Mathf.Max(highestY, player.transform.position.y);
        }
        Assert.That(launches, Is.EqualTo(1), "One held Space press must produce exactly one jump impulse.");
        Assert.That(highestY, Is.GreaterThan(groundY + 0.8f));
        Assert.That(player.IsGrounded, Is.True);
        SetKeys();
        player.Body.AddForce(Vector3.forward * 50f, ForceMode.VelocityChange);
        Assert.That(player.health.TakeDamage(1), Is.True);
        yield return new WaitForSeconds(0.15f);
        Assert.That(player.transform.position.z, Is.EqualTo(player.config.planeZ).Within(0.001f));

        CreateBox("Solid wall", new Vector3(2f, 2f, 0f), new Vector3(0.4f, 4f, 3f));
        Assert.That(player.ResetForExploration(2, new Vector3(0f, 0.05f, -8f)), Is.True);
        SetKeys(Key.D);
        yield return new WaitForSeconds(0.8f);
        Assert.That(player.transform.position.x, Is.LessThan(1.65f), "The rigidbody must collide with the 3D wall.");
        Assert.That(player.transform.position.z, Is.EqualTo(player.config.planeZ).Within(0.001f));
        Assert.That(player.transform.rotation, Is.EqualTo(Quaternion.identity));
    }

    [UnityTest]
    public IEnumerator ShortJumpBesidePlatformReachesTopThenWalksOffAndLands()
    {
        CreateFloor();
        // 复现白盒平台：从贴近侧面的位置起跳，实际Prefab材质不能让摩擦吃掉Y速度。
        CreateBox("Jump regression platform", new Vector3(3f, 0.65f, 0f), new Vector3(3f, 1.3f, 3f));
        PlayerController player = CreatePlayer(Vector3.zero);
        Assert.That(player.ResetForExploration(1, new Vector3(1f, 0.05f, 9f)), Is.True);
        float deadline = Time.time + 2f;
        while ((!player.IsGrounded || Mathf.Abs(player.Body.linearVelocity.y) > 0.2f) && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
        }
        Assert.That(player.IsGrounded, Is.True, "The jump must start on the floor.");

        float driveUntil = Time.time + 0.55f;
        SetKeys(Key.D, Key.Space);
        yield return new WaitForSeconds(0.1f);
        SetKeys(Key.D);
        float highestY = player.Body.position.y;
        while (Time.time < driveUntil)
        {
            yield return new WaitForFixedUpdate();
            highestY = Mathf.Max(highestY, player.Body.position.y);
            Assert.That(player.Body.position.z, Is.EqualTo(player.config.planeZ).Within(0.001f));
        }
        SetKeys();

        // 等待连续几个物理帧稳定落在台面，而不是把某一帧经过台面高度视为成功。
        int settledFrames = 0;
        deadline = Time.time + 3f;
        while (settledFrames < 3 && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            highestY = Mathf.Max(highestY, player.Body.position.y);
            bool onPlatform = player.IsGrounded && Mathf.Abs(player.Body.position.y - 1.3f) < 0.08f
                && Mathf.Abs(player.Body.linearVelocity.y) < 0.2f;
            settledFrames = onPlatform ? settledFrames + 1 : 0;
        }
        Assert.That(highestY, Is.GreaterThan(1.35f), "Side contact must not cancel the jump's vertical motion.");
        Assert.That(settledFrames, Is.EqualTo(3), "The player must land on the 1.3m platform after a short press.");
        Assert.That(player.Body.position.x, Is.InRange(1.6f, 4.4f));
        Assert.That(player.Body.position.z, Is.EqualTo(player.config.planeZ).Within(0.001f));

        SetKeys(Key.D);
        bool observedFall = false;
        deadline = Time.time + 2f;
        while (!observedFall && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            observedFall = player.Body.position.x > 4.9f && !player.IsGrounded
                && player.CurrentState == PlayerState.Fall && player.Body.linearVelocity.y < -0.1f;
            Assert.That(player.Body.position.z, Is.EqualTo(player.config.planeZ).Within(0.001f));
        }
        SetKeys();
        Assert.That(observedFall, Is.True, "Walking past the platform edge must enter Fall without pressing jump.");

        settledFrames = 0;
        deadline = Time.time + 3f;
        while (settledFrames < 3 && Time.time < deadline)
        {
            yield return new WaitForFixedUpdate();
            bool onFloor = player.IsGrounded && Mathf.Abs(player.Body.position.y) < 0.08f
                && Mathf.Abs(player.Body.linearVelocity.y) < 0.2f;
            settledFrames = onFloor ? settledFrames + 1 : 0;
        }
        Assert.That(settledFrames, Is.EqualTo(3), "After leaving the edge the player must settle on the floor.");
        Assert.That(player.CurrentState, Is.EqualTo(PlayerState.Idle));
        Assert.That(player.Body.position.z, Is.EqualTo(player.config.planeZ).Within(0.001f));
    }

    [UnityTest]
    public IEnumerator BackpackKeepsPoseAndGravityAndDoesNotReplayLockedJump()
    {
        CreateFloor();
        PlayerController player = CreatePlayer(Vector3.zero);
        Assert.That(player.ResetForExploration(1, new Vector3(0f, 0.05f, 0f)), Is.True);
        yield return new WaitForSeconds(0.3f);
        SetKeys(Key.C);
        yield return new WaitForSeconds(0.1f);
        Assert.That(player.IsCrouching, Is.True);
        player.SetBackpackOpen(true);
        float lockedX = player.transform.position.x;
        SetKeys(Key.D, Key.Space);
        yield return new WaitForSeconds(0.2f);
        Assert.That(player.IsCrouching, Is.True, "Opening the bag retains the current ground pose.");
        Assert.That(player.transform.position.x, Is.EqualTo(lockedX).Within(0.03f));
        Assert.That(player.IsGrounded, Is.True);
        SetKeys();
        yield return null;
        player.SetBackpackOpen(false);
        yield return new WaitForSeconds(0.1f);
        Assert.That(player.IsCrouching, Is.False);
        Assert.That(player.IsGrounded, Is.True);

        SetKeys(Key.Space);
        yield return new WaitForSeconds(0.18f);
        Assert.That(player.IsGrounded, Is.False);
        Assert.That(player.transform.position.y, Is.GreaterThan(0.2f));
        player.SetBackpackOpen(true);
        SetKeys();
        yield return new WaitForSeconds(0.08f);
        SetKeys(Key.D, Key.Space);
        lockedX = player.transform.position.x;
        yield return new WaitForSeconds(1.6f);
        Assert.That(player.IsGrounded, Is.True, "The bag must not freeze gravity or landing.");
        Assert.That(player.transform.position.x, Is.EqualTo(lockedX).Within(0.03f));
        Assert.That(player.Body.linearVelocity.x, Is.EqualTo(0f).Within(0.01f));
        player.SetBackpackOpen(false);
        yield return new WaitForSeconds(0.2f);
        Assert.That(player.IsGrounded, Is.True, "The Space press made while locked must not be replayed.");
        Assert.That(player.Body.linearVelocity.y, Is.LessThan(0.5f));
        Assert.That(player.CanAct, Is.True);
        SetKeys();

        player.gameObject.SetActive(false);
        yield return null;
        player.gameObject.SetActive(true);
        yield return null;
        SetKeys(Key.Space);
        yield return new WaitForSeconds(0.15f);
        Assert.That(player.Body.linearVelocity.y, Is.GreaterThan(1f));
        player.health.TakeDamage(100);
        player.SetBackpackOpen(false);
        SetKeys(Key.D, Key.Space);
        yield return new WaitForSeconds(0.2f);
        Assert.That(player.IsDead, Is.True);
        Assert.That(player.CanAct, Is.False);
        Assert.That(player.Body.linearVelocity.x, Is.EqualTo(0f).Within(0.01f));
    }

    [UnityTest]
    public IEnumerator RealKeyboardInteractFiresOnceAndDoesNotReplayThroughLocksOrReenable()
    {
        CreateFloor();
        PlayerController player = CreatePlayer(Vector3.zero);
        GameObject targetObject = new GameObject("Real input interaction target");
        Track(targetObject);
        targetObject.transform.position = new Vector3(1.2f, 1f, 0f);
        targetObject.AddComponent<SphereCollider>().radius = 0.2f;
        PlayerTestInteractable target = targetObject.AddComponent<PlayerTestInteractable>();
        GameObject triggerObject = new GameObject("TriggerBox");
        triggerObject.transform.SetParent(targetObject.transform, false);
        BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(4f, 3f, 2f);
        target.interactionTrigger = trigger;
        Assert.That(player.ResetForExploration(1, new Vector3(0f, 0.05f, 0f)), Is.True);
        yield return new WaitForSeconds(0.3f);
        SetKeys(Key.E);
        yield return new WaitForSeconds(0.3f);
        Assert.That(target.InteractionCount, Is.EqualTo(1));
        player.SetBackpackOpen(true);
        SetKeys();
        yield return null;
        SetKeys(Key.E);
        yield return new WaitForSeconds(0.1f);
        player.SetBackpackOpen(false);
        yield return new WaitForSeconds(0.2f);
        Assert.That(target.InteractionCount, Is.EqualTo(1), "Closing the bag must not replay locked E input.");
        SetKeys();
        yield return null;
        player.gameObject.SetActive(false);
        yield return null;
        player.gameObject.SetActive(true);
        yield return null;
        SetKeys(Key.E);
        yield return new WaitForSeconds(0.2f);
        Assert.That(target.InteractionCount, Is.EqualTo(2), "Reenable must not duplicate input subscriptions.");
        SetKeys();
        yield return null;
        player.SetInteractionUiBlocked(true);
        SetKeys(Key.E);
        yield return new WaitForSeconds(0.1f);
        player.SetInteractionUiBlocked(false);
        yield return new WaitForSeconds(0.1f);
        Assert.That(target.InteractionCount, Is.EqualTo(2));
        SetKeys();
        yield return null;
        Assert.That(player.health.TakeDamage(100), Is.True);
        SetKeys(Key.E);
        yield return new WaitForSeconds(0.1f);
        Assert.That(target.InteractionCount, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator CinemachineFollowsMovementAndWarpAndCanBindReplacementPlayer()
    {
        CreateFloor();
        PlayerController player = CreatePlayer(Vector3.zero);
        GameObject cameraObject = CreatePrefab("Assets/Prefabs/Player/PlayerCamera.prefab");
        PlayerCameraTargetBinder binder = cameraObject.GetComponentInChildren<PlayerCameraTargetBinder>();
        Assert.That(binder, Is.Not.Null);
        Camera output = cameraObject.GetComponentInChildren<Camera>();
        Assert.That(output, Is.Not.Null);
        Assert.That(output.GetComponent<CinemachineBrain>(), Is.Not.Null);
        Assert.That(cameraObject.GetComponentsInChildren<AudioListener>(), Has.Length.EqualTo(1));
        Assert.That(binder.followCamera, Is.Not.Null);
        Assert.That(binder.followCamera.GetComponent<CinemachinePositionComposer>(), Is.Not.Null);
        GameObject boundsObject = new GameObject("Movement test CameraBounds");
        Track(boundsObject);
        boundsObject.transform.position = new Vector3(0f, 0f, -12f);
        BoxCollider bounds = boundsObject.AddComponent<BoxCollider>();
        bounds.isTrigger = true;
        bounds.size = new Vector3(200f, 100f, 20f);
        CinemachineConfiner3D confiner = binder.followCamera.GetComponent<CinemachineConfiner3D>();
        if (confiner == null) { confiner = binder.followCamera.gameObject.AddComponent<CinemachineConfiner3D>(); }
        binder.confiner = confiner;
        confiner.BoundingVolume = bounds;
        confiner.SlowingDistance = 0f;
        binder.Bind(player);
        Assert.That(player.ResetForExploration(1, new Vector3(0f, 0.05f, 0f)), Is.True);
        yield return new WaitForSeconds(0.4f);
        Quaternion cameraRotation = output.transform.rotation;
        float startX = output.transform.position.x;
        SetKeys(Key.D);
        yield return new WaitForSeconds(0.7f);
        Assert.That(output.transform.position.x, Is.GreaterThan(startX + 0.3f),
            "Cinemachine must actually move the output camera.");
        SetKeys(Key.A);
        yield return new WaitForSeconds(0.15f);
        Assert.That(player.FacingDirection, Is.EqualTo(-1));
        Assert.That(Quaternion.Angle(output.transform.rotation, cameraRotation), Is.LessThan(0.1f),
            "Turning the visual must not orbit the side-view camera.");
        SetKeys();
        yield return new WaitForSeconds(0.25f);
        float startY = output.transform.position.y;
        SetKeys(Key.Space);
        yield return new WaitForSeconds(0.45f);
        Assert.That(output.transform.position.y, Is.GreaterThan(startY + 0.1f));
        SetKeys();
        Assert.That(player.ResetForExploration(2, new Vector3(20f, 0.05f, 18f)), Is.True);
        yield return null;
        yield return null;
        Assert.That(Mathf.Abs(output.transform.position.x - player.cameraTarget.position.x), Is.LessThan(4f),
            "A spawn warp must invalidate the old camera damping history.");
        Object.Destroy(player.gameObject);
        yield return null;
        yield return null;
        PlayerController replacement = CreatePlayer(Vector3.zero);
        binder.Bind(replacement);
        Assert.That(replacement.ResetForExploration(3, new Vector3(-10f, 0.05f, -18f)), Is.True);
        yield return null;
        yield return null;
        Assert.That(binder.followCamera.Follow, Is.SameAs(replacement.cameraTarget));
        Assert.That(Mathf.Abs(output.transform.position.x - replacement.cameraTarget.position.x), Is.LessThan(4f));
    }

    private void SetKeys(params Key[] keys)
    {
        // 真实InputSystem设备事件经过共享资产绑定和InputReader，再到物理帧。
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    }

    private void CreateFloor()
    {
        CreateBox("Physics test floor", new Vector3(0f, -0.5f, 0f), new Vector3(100f, 1f, 8f));
    }

    private void CreateBox(string name, Vector3 position, Vector3 scale)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Track(box);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = scale;
    }
}
#endif
