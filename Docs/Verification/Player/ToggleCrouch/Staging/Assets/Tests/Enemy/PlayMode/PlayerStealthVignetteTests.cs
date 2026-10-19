#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// 09I：正式玩家 + 合法场景相机绑定。只验证隐藏到暗角的映射；画面强度另由真实Ruins验收。
public class PlayerStealthVignetteTests
{
    private readonly List<Object> owned = new List<Object>();
    private readonly List<Scene> temporaryScenes = new List<Scene>();
    private readonly List<InputDevice> disabledKeyboards = new List<InputDevice>();
    private readonly Vector3 origin = new Vector3(7000f, 100f, 0f);
    private InputSettings originalSettings;
    private InputSettings testSettings;
    private Keyboard keyboard;
    private bool originalBackground;

    [SetUp]
    public void SetUp()
    {
        originalSettings = InputSystem.settings;
        originalBackground = Application.runInBackground;
        testSettings = Object.Instantiate(originalSettings);
        testSettings.hideFlags = HideFlags.HideAndDontSave;
        testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings = testSettings;
        Application.runInBackground = true;
        foreach (InputDevice device in new List<InputDevice>(InputSystem.devices))
        {
            if (!(device is Keyboard) || !device.enabled) { continue; }
            disabledKeyboards.Add(device);
            InputSystem.DisableDevice(device);
        }
        keyboard = InputSystem.AddDevice<Keyboard>();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        // 先停对象，取消InputReader动作与Binder订阅，再恢复测试借用的输入环境。
        foreach (Object item in owned) { if (item is GameObject go && go != null) { go.SetActive(false); } }
        foreach (Object item in owned) { if (item is GameObject && item != null) { Object.Destroy(item); } }
        if (keyboard != null && keyboard.added) { InputSystem.RemoveDevice(keyboard); }
        if (originalSettings != null) { InputSystem.settings = originalSettings; }
        Application.runInBackground = originalBackground;
        foreach (InputDevice device in disabledKeyboards)
        {
            if (device.added && !device.enabled) { InputSystem.EnableDevice(device); }
        }
        disabledKeyboards.Clear();
        if (testSettings != null) { Object.Destroy(testSettings); }
        yield return null;
        foreach (Scene scene in temporaryScenes)
        {
            if (scene.IsValid() && scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(scene); }
        }
        temporaryScenes.Clear();
        foreach (Object item in owned) { if (item != null) { Object.Destroy(item); } }
        owned.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator OnlyActualCrouchingCoverFadesInAndStandingOrLeavingFadesOut()
    {
        Fixture rig = CreateFixture();
        CoverArea cover = CreateCover(origin + Vector3.up);
        Assert.That(rig.effect.intensity, Is.EqualTo(0.22f));
        Assert.That(rig.effect.smoothness, Is.EqualTo(0.45f));
        Assert.That(rig.effect.transitionSeconds, Is.EqualTo(0.2f));
        // 仅拉长本例采样余量；不改变隐藏条件或每帧强设玩家状态。
        rig.effect.transitionSeconds = 0.4f;
        yield return TwoFrames();
        Assert.That(CoverArea.IsHidden(rig.player), Is.False, "Standing inside cover is visible.");
        Assert.That(rig.volume.weight, Is.Zero);

        MovePlayer(rig.player, origin + Vector3.right * 8f);
        yield return SetCrouching(rig.player, true);
        yield return TwoFrames();
        Assert.That(rig.player.IsCrouching, Is.True);
        Assert.That(CoverArea.IsHidden(rig.player), Is.False, "Crouching outside actual cover is visible.");
        Assert.That(rig.volume.weight, Is.Zero);

        MovePlayer(rig.player, origin);
        Assert.That(CoverArea.IsHidden(rig.player), Is.True);
        yield return AssertGradualWeight(rig.volume, 1f);
        Vignette vignette;
        Assert.That(rig.volume.profile.TryGet(out vignette), Is.True);
        Assert.That(vignette.intensity.value, Is.EqualTo(0.22f));
        Assert.That(vignette.smoothness.value, Is.EqualTo(0.45f));

        // 先记录满权重，再等待松开C；输入准备帧也属于这次正常渐退，不能漏掉起点。
        yield return AssertGradualWeight(rig.volume, 0f, SetCrouching(rig.player, false));
        Assert.That(CoverArea.IsHidden(rig.player), Is.False);
        yield return SetCrouching(rig.player, true);
        yield return Until(() => rig.volume.weight >= 0.999f, "Re-enter crouching cover.");
        MovePlayer(rig.player, origin + Vector3.right * 8f);
        Assert.That(CoverArea.IsHidden(rig.player), Is.False);
        yield return AssertGradualWeight(rig.volume, 0f);
        Assert.That(cover.isActiveAndEnabled, Is.True, "Leaving must not depend on disabling cover.");
    }

    [UnityTest]
    public IEnumerator OverlappingCoversAndBackpackDoNotInterruptAnExistingVignette()
    {
        Fixture rig = CreateFixture();
        CoverArea first = CreateCover(origin + Vector3.up);
        CoverArea second = CreateCover(origin + Vector3.up + Vector3.right * 0.3f);
        yield return SetCrouching(rig.player, true);
        yield return Until(() => rig.volume.weight >= 0.999f, "Both real cover volumes overlap the player.");
        first.enabled = false;
        Assert.That(CoverArea.IsHidden(rig.player), Is.True);
        yield return TwoFrames();
        Assert.That(rig.volume.weight, Is.EqualTo(1f));
        rig.player.SetBackpackOpen(true);
        yield return TwoFrames();
        Assert.That(rig.player.IsBackpackOpen && CoverArea.IsHidden(rig.player), Is.True);
        Assert.That(rig.volume.weight, Is.EqualTo(1f));
        rig.player.SetBackpackOpen(false);
        yield return TwoFrames();
        Assert.That(rig.player.IsCrouching && CoverArea.IsHidden(rig.player), Is.True,
            "The queued C key stays held; closing backpack does not stand the player up.");
        Assert.That(rig.volume.weight, Is.EqualTo(1f));
        second.gameObject.SetActive(false);
        Assert.That(CoverArea.IsHidden(rig.player), Is.False);
        yield return Until(() => rig.volume.weight <= 0.001f, "The last disabled cover allows the normal fade out.");
    }

    [UnityTest]
    public IEnumerator TransitionAndDeathClearImmediatelyAndANewRoundCanHideAgain()
    {
        Fixture rig = CreateFixture();
        CreateCover(origin + Vector3.up);
        yield return SetCrouching(rig.player, true);
        yield return Until(() => rig.volume.weight >= 0.999f, "Start hidden.");
        rig.player.SetSceneTransitionBlocked(true);
        AssertClearsOnOneLateUpdate(rig);
        rig.player.SetSceneTransitionBlocked(false);
        rig.player.Body.isKinematic = true;
        yield return SetCrouching(rig.player, true);
        yield return Until(() => rig.volume.weight >= 0.999f, "A valid unlocked player can hide again.");
        Assert.That(rig.player.health.TakeDamage(rig.player.health.MaxHealth), Is.True);
        Assert.That(rig.player.IsDead, Is.True);
        AssertClearsOnOneLateUpdate(rig);
        Assert.That(rig.player.ResetForExploration(2, origin), Is.True);
        rig.player.Body.isKinematic = true;
        yield return SetCrouching(rig.player, true);
        yield return Until(() => rig.volume.weight >= 0.999f, "A new living round must not inherit the dead clear state.");
    }

    [UnityTest]
    public IEnumerator UnpreparedUnboundOrDisabledBindingCannotLeaveAStaleWeight()
    {
        Fixture rig = CreateFixture();
        CreateCover(origin + Vector3.up);
        yield return SetCrouching(rig.player, true);
        yield return Until(() => rig.volume.weight >= 0.999f, "Start hidden.");
        rig.binder.Bind(null);
        AssertClearsOnOneLateUpdate(rig);
        rig.binder.Bind(rig.player);
        yield return Until(() => rig.volume.weight >= 0.999f, "Rebind the same valid player.");
        rig.binder.enabled = false;
        Assert.That(rig.binder.player, Is.SameAs(rig.player), "Disable retains the Inspector reference.");
        AssertClearsOnOneLateUpdate(rig);
        rig.binder.enabled = true;
        yield return Until(() => rig.volume.weight >= 0.999f, "Enable restores a valid camera binding.");
        PlayerController unprepared = CreatePlayer(origin, false);
        rig.binder.Bind(unprepared);
        Assert.That(unprepared.IsPrepared, Is.False);
        AssertClearsOnOneLateUpdate(rig);
        rig.binder.Bind(rig.player);
        yield return Until(() => rig.volume.weight >= 0.999f, "Binding validation must not poison the original player.");
        rig.effect.enabled = false;
        Assert.That(rig.volume.weight, Is.Zero, "OnDisable must clear synchronously, without another LateUpdate.");
        rig.effect.enabled = true;
        yield return Until(() => rig.volume.weight >= 0.999f, "Re-enable follows actual hiding again.");
    }

    [UnityTest]
    public IEnumerator MovingTheBoundPlayerToAnotherSceneClearsBeforeTheNextBindingRefresh()
    {
        Fixture rig = CreateFixture();
        CreateCover(origin + Vector3.up);
        yield return SetCrouching(rig.player, true);
        yield return Until(() => rig.volume.weight >= 0.999f, "Start hidden.");
        Scene originalScene = rig.player.gameObject.scene;
        Scene otherScene = SceneManager.CreateScene("09I vignette foreign scene " + Guid.NewGuid().ToString("N"));
        temporaryScenes.Add(otherScene);
        SceneManager.MoveGameObjectToScene(rig.player.transform.root.gameObject, otherScene);
        Assert.That(rig.binder.player, Is.SameAs(rig.player));
        Assert.That(rig.player.gameObject.scene, Is.Not.EqualTo(rig.effect.gameObject.scene));
        AssertClearsOnOneLateUpdate(rig);
        // 先完成本模块的同帧判定，再解绑，避免故意把错误引用留给下一帧Binder诊断。
        rig.binder.Bind(null);
        SceneManager.MoveGameObjectToScene(rig.player.transform.root.gameObject, originalScene);
        rig.binder.Bind(rig.player);
        yield return Until(() => rig.volume.weight >= 0.999f, "Returning to a legitimate same-scene binding restores the effect.");
    }

    [UnityTest]
    public IEnumerator ReenableReusesOneOwnedProfileAndDestroyCleansItWithoutChangingOtherVolumes()
    {
        Fixture rig = CreateFixture();
        CreateCover(origin + Vector3.up);
        Volume other = Own(new GameObject("09I untouched atmosphere volume")).AddComponent<Volume>();
        other.isGlobal = true;
        other.weight = 0.73f;
        other.priority = 17f;
        VolumeProfile otherProfile = Own(ScriptableObject.CreateInstance<VolumeProfile>());
        Bloom bloom = Own(otherProfile.Add<Bloom>(true));
        bloom.intensity.Override(3.7f);
        Vignette baseline = Own(otherProfile.Add<Vignette>(true));
        baseline.intensity.Override(0.08f);
        other.sharedProfile = otherProfile;
        string bloomBefore = JsonUtility.ToJson(bloom);
        string baselineBefore = JsonUtility.ToJson(baseline);
        VolumeProfile runtimeProfile = rig.volume.profile;
        Vignette runtimeVignette;
        Assert.That(runtimeProfile.TryGet(out runtimeVignette), Is.True);
        Assert.That(runtimeProfile.components.Count, Is.EqualTo(1), "Stealth owns only a Vignette override.");
        Assert.That(rig.volume.sharedProfile, Is.Null);
        Assert.That(AssetDatabase.Contains(runtimeProfile) || AssetDatabase.Contains(runtimeVignette), Is.False);
        yield return SetCrouching(rig.player, true);
        for (int i = 0; i < 3; i++)
        {
            yield return Until(() => rig.volume.weight >= 0.999f, "Hidden before disabling iteration " + i);
            rig.effect.enabled = false;
            Assert.That(rig.volume.weight, Is.Zero);
            rig.effect.enabled = true;
            Assert.That(rig.volume.profile, Is.SameAs(runtimeProfile));
            Assert.That(rig.volume.profile.components.Count, Is.EqualTo(1));
            Assert.That(rig.volume.profile.components[0], Is.SameAs(runtimeVignette));
            Assert.That(rig.effect.GetComponents<Volume>().Length, Is.EqualTo(1));
        }
        Object.Destroy(rig.effect);
        yield return TwoFrames();
        Assert.That(runtimeVignette == null, Is.True, "The owned Vignette component must be explicitly destroyed.");
        Assert.That(runtimeProfile == null, Is.True, "The runtime ScriptableObject must not leak after its owner dies.");
        Assert.That(rig.volume.HasInstantiatedProfile(), Is.False,
            "Inspect without reading profile here, since its getter would create a new profile.");
        Assert.That(rig.volume.weight, Is.Zero);
        Assert.That(other.sharedProfile, Is.SameAs(otherProfile));
        Assert.That(other.HasInstantiatedProfile(), Is.False);
        Assert.That(other.weight, Is.EqualTo(0.73f));
        Assert.That(other.priority, Is.EqualTo(17f));
        Assert.That(otherProfile.components.Count, Is.EqualTo(2));
        Assert.That(JsonUtility.ToJson(bloom), Is.EqualTo(bloomBefore));
        Assert.That(JsonUtility.ToJson(baseline), Is.EqualTo(baselineBefore));
    }

    private Fixture CreateFixture()
    {
        PlayerController player = CreatePlayer(origin, true);
        GameObject cameraRoot = Own(new GameObject("09I valid camera binding"));
        cameraRoot.SetActive(false);
        GameObject sideFollow = new GameObject("SideFollow");
        sideFollow.transform.SetParent(cameraRoot.transform, false);
        CinemachineCamera follow = sideFollow.AddComponent<CinemachineCamera>();
        CinemachineConfiner3D confiner = sideFollow.AddComponent<CinemachineConfiner3D>();
        GameObject boundsRoot = Own(new GameObject("09I independent CameraBounds"));
        boundsRoot.transform.position = origin;
        BoxCollider bounds = boundsRoot.AddComponent<BoxCollider>();
        bounds.isTrigger = true;
        bounds.size = new Vector3(100f, 100f, 100f);
        confiner.BoundingVolume = bounds;
        PlayerCameraTargetBinder binder = cameraRoot.AddComponent<PlayerCameraTargetBinder>();
        binder.player = player;
        binder.followCamera = follow;
        binder.confiner = confiner;
        cameraRoot.SetActive(true);
        string error;
        Assert.That(binder.ValidateBounds(out error), Is.True, error);
        Assert.That(binder.LastBoundsError, Is.Null);
        Assert.That(follow.Follow, Is.SameAs(player.cameraTarget));
        GameObject effectRoot = Own(new GameObject("09I dedicated stealth volume"));
        effectRoot.SetActive(false);
        Volume volume = effectRoot.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        volume.weight = 0f;
        volume.sharedProfile = null;
        PlayerStealthVignette effect = effectRoot.AddComponent<PlayerStealthVignette>();
        effect.cameraBinder = binder;
        effectRoot.SetActive(true);
        return new Fixture { player = player, binder = binder, volume = volume, effect = effect };
    }

    private PlayerController CreatePlayer(Vector3 position, bool prepare)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab");
        Assert.That(prefab, Is.Not.Null);
        GameObject holder = Own(new GameObject("09I real player holder"));
        holder.SetActive(false);
        PlayerController player = Object.Instantiate(prefab, holder.transform).GetComponent<PlayerController>();
        Assert.That(player, Is.Not.Null);
        PlayerConfigSO config = Own(Object.Instantiate(player.config));
        // 此夹具按住/松开C设置姿态，专注验证暗角；正式玩家默认切换蹲伏。
        config.crouchInputMode = CrouchInputMode.Hold;
        config.planeZ = origin.z;
        config.groundLayers = 0;
        config.damageInvulnerabilitySeconds = 0f;
        player.config = config;
        player.health.config = config;
        player.inputReader.inputActions = Own(Object.Instantiate(player.inputReader.inputActions));
        player.transform.position = position;
        holder.SetActive(true);
        if (prepare) { Assert.That(player.ResetForExploration(1, position), Is.True); }
        player.Body.isKinematic = true;
        // 本夹具直接摆放固定刚体，不让正式角色的渲染插值参与掩体边界采样。
        player.Body.interpolation = RigidbodyInterpolation.None;
        Physics.SyncTransforms();
        return player;
    }

    private CoverArea CreateCover(Vector3 center)
    {
        GameObject root = Own(new GameObject("09I actual cover"));
        root.transform.position = center;
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(3f, 4f, 3f);
        CoverArea cover = root.AddComponent<CoverArea>();
        Physics.SyncTransforms();
        return cover;
    }

    private IEnumerator SetCrouching(PlayerController player, bool crouching)
    {
        // 交给正常输入阶段处理，再确认真实Reader已收到目标状态；不手动插入额外input frame。
        InputSystem.QueueStateEvent(keyboard, crouching ? new KeyboardState(Key.C) : new KeyboardState());
        float deadline = Time.realtimeSinceStartup + 1f;
        yield return null;
        while (player.inputReader.CrouchHeld != crouching && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }
        Assert.That(player.inputReader.CrouchHeld, Is.EqualTo(crouching),
            "Queued crouch must reach the real reader before posing. frame=" + Time.frameCount
            + " update=" + InputState.currentUpdateType + " key=" + keyboard.cKey.isPressed
            + " focused=" + Application.isFocused + " / " + Describe(null));
        player.SetCrouching(crouching);
        Physics.SyncTransforms();
    }

    private static void MovePlayer(PlayerController player, Vector3 position)
    {
        player.Body.position = position;
        player.transform.position = position;
        Physics.SyncTransforms();
    }

    private static void AssertClearsOnOneLateUpdate(Fixture rig)
    {
        // 同步执行一次真正的表现入口，证明失效条件立即清零，不把两帧渐退当作通过。
        rig.effect.SendMessage("LateUpdate", SendMessageOptions.RequireReceiver);
        Assert.That(rig.volume.weight, Is.Zero);
    }

    private static IEnumerator AssertGradualWeight(Volume volume, float destination, IEnumerator change = null)
    {
        float previous = volume.weight;
        bool increasing = destination > previous;
        bool sawIntermediate = false;
        Assert.That(Mathf.Abs(previous - destination), Is.GreaterThan(0.9f));
        if (change != null) { yield return change; }
        float deadline = Time.realtimeSinceStartup + 3f;
        while (Mathf.Abs(volume.weight - destination) > 0.001f && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            float current = volume.weight;
            Assert.That(current, Is.InRange(0f, 1f));
            if (increasing) { Assert.That(current, Is.GreaterThanOrEqualTo(previous - 0.0001f), Describe(volume)); }
            else { Assert.That(current, Is.LessThanOrEqualTo(previous + 0.0001f)); }
            sawIntermediate |= current > 0.001f && current < 0.999f;
            previous = current;
        }
        Assert.That(volume.weight, Is.EqualTo(destination).Within(0.001f));
        Assert.That(sawIntermediate, Is.True, "A visible transition must contain an intermediate weight, not snap.");
    }

    private static IEnumerator Until(Func<bool> condition, string message)
    {
        float deadline = Time.realtimeSinceStartup + 3f;
        while (!condition() && Time.realtimeSinceStartup < deadline) { yield return null; }
        Assert.That(condition(), Is.True, message + " / " + Describe(null));
    }

    private static string Describe(Volume volume)
    {
        PlayerStealthVignette[] effects = Object.FindObjectsByType<PlayerStealthVignette>(FindObjectsSortMode.None);
        var details = new System.Text.StringBuilder();
        foreach (PlayerStealthVignette effect in effects)
        {
            PlayerController player = effect.cameraBinder.player;
            details.Append(" weight=" + effect.GetComponent<Volume>().weight + " pose=" + player.IsCrouching
                + " wants=" + player.WantsCrouch + " input=" + player.inputReader.CrouchHeld + " hidden=" + CoverArea.IsHidden(player)
                + " pos=" + player.transform.position + " body=" + player.Body.position + " state=" + player.CurrentState
                + " prepared=" + player.IsPrepared + " blocked=" + player.IsSceneTransitionBlocked);
        }
        return details.ToString();
    }

    private static IEnumerator TwoFrames() { yield return null; yield return null; }
    private T Own<T>(T item) where T : Object { owned.Add(item); return item; }
    private sealed class Fixture
    {
        public PlayerController player;
        public PlayerCameraTargetBinder binder;
        public Volume volume;
        public PlayerStealthVignette effect;
    }
}
#endif
