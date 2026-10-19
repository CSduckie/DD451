#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// 调用正式场景与真实流程，验证加载/轮次/失败重试；不冒充原生键鼠或搜刮UI试玩。
public class MinimumLoopSceneTests
{
    private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";
    private const string ShelterPath = "Assets/Scenes/Shelter.unity";
    private const string RuinsPath = "Assets/Scenes/Ruins.unity";
    private GameFlowManager flow;
    private GameObject persistentRoot;
    private UnityAction<Scene, LoadSceneMode> sceneLoadedHandler;
    private bool startedSession;

    [SetUp]
    public void SetUp()
    {
        startedSession = false;
        flow = null;
        persistentRoot = null;
        sceneLoadedHandler = null;
        if (UnityEngine.Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
        {
            Assert.Ignore("Run from the Test Runner clean test scene, without an existing game session.");
        }
        Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapPath), Is.Not.Null,
            "Generate and wire the real minimum-loop assets before running scene integration tests.");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        // SetUp因用户已有运行而跳过时，绝不能清理用户的入口或场景。
        if (!startedSession) { yield break; }
        SceneManager.sceneLoaded -= IsolateEnemies;
        if (sceneLoadedHandler != null) { SceneManager.sceneLoaded -= sceneLoadedHandler; }
        if (flow != null) { flow.StopAllCoroutines(); }
        foreach (string path in new[] { RuinsPath, ShelterPath, BootstrapPath })
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            if (scene.IsValid() && scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(scene); }
        }
        if (persistentRoot == null && GameBootstrap.Instance != null) { persistentRoot = GameBootstrap.Instance.gameObject; }
        if (persistentRoot != null) { UnityEngine.Object.Destroy(persistentRoot); }
        yield return null;
    }

    [UnityTest]
    public IEnumerator BootstrapStartsEmptyAtRoundZeroAndTwoTransitionsKeepOnePlayer()
    {
        yield return LoadBootstrap();
        PlayerController samePlayer = flow.Player;
        Assert.That(flow.CurrentRound, Is.Zero);
        Assert.That(flow.SettledRound, Is.Zero);
        Assert.That(samePlayer.health.CurrentRound, Is.Zero);
        Assert.That(flow.Backpack.GetPlacements(), Is.Empty);
        Assert.That(flow.Warehouse.GetPlacements(), Is.Empty);
        Assert.That(flow.Items.InstanceCount, Is.Zero);
        Assert.That(flow.Shelter.CurrentEv, Is.Zero);
        AssertOneActiveRuntime();
        Assert.That(samePlayer.CanAct, Is.True);
        Assert.That(flow.TryOpenStorage(), Is.True);
        GameFlowSceneBindings bindings = SceneBindings(SceneManager.GetSceneByPath(ShelterPath));
        Assert.That(bindings.backpackUI.IsOpen && bindings.warehouseUI.IsOpen, Is.True);
        bindings.CloseSessions();
        Assert.That(samePlayer.CanAct, Is.True);

        for (int expectedRound = 1; expectedRound <= 2; expectedRound++)
        {
            Assert.That(flow.TryDepart(), Is.True);
            Assert.That(flow.TryDepart(), Is.False, "An in-flight transition cannot allocate another round.");
            yield return WaitForStage(GameFlowStage.Exploring);
            Assert.That(flow.CurrentRound, Is.EqualTo(expectedRound));
            Assert.That(samePlayer.health.CurrentRound, Is.EqualTo(expectedRound));
            Assert.That(flow.Player, Is.SameAs(samePlayer));
            Assert.That(samePlayer.gameObject.scene.path, Is.EqualTo(RuinsPath));
            AssertOneActiveRuntime();
            Assert.That(flow.TryExtract(), Is.True);
            Assert.That(flow.TryExtract(), Is.False, "Repeated extraction cannot start another settlement.");
            yield return WaitForStage(GameFlowStage.Shelter);
            Assert.That(flow.Player, Is.SameAs(samePlayer));
            Assert.That(flow.CurrentRound, Is.EqualTo(expectedRound));
            Assert.That(flow.SettledRound, Is.EqualTo(expectedRound));
            Assert.That(samePlayer.gameObject.scene.path, Is.EqualTo(ShelterPath));
            Assert.That(flow.Loot.GetRemainingWorldItems(), Is.Empty);
            Assert.That(flow.Warehouse.IsManagementAllowed, Is.True);
            AssertOneActiveRuntime();
        }
    }

    [UnityTest]
    public IEnumerator LoadedTargetBindingFailureRetriesTheSameSceneAndAllocatedRound()
    {
        yield return LoadBootstrap();
        GameFlowSceneBindings failedTarget = null;
        Transform originalSpawn = null;
        sceneLoadedHandler = (scene, mode) =>
        {
            if (scene.path != RuinsPath) { return; }
            failedTarget = SceneBindings(scene);
            originalSpawn = failedTarget.spawnPoint;
            // 故障限于本次测试的场景实例；资源与生产接线不改动。
            failedTarget.spawnPoint = null;
        };
        SceneManager.sceneLoaded += sceneLoadedHandler;
        Assert.That(flow.TryDepart(), Is.True);
        yield return WaitForStage(GameFlowStage.Failed);
        SceneManager.sceneLoaded -= sceneLoadedHandler;
        sceneLoadedHandler = null;
        Assert.That(flow.CanRetry, Is.True);
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        Assert.That(flow.Player.health.CurrentRound, Is.Zero, "The failed binding must precede player preparation.");
        Assert.That(flow.Player.IsSceneTransitionBlocked, Is.True);
        Assert.That(failedTarget, Is.Not.Null);
        int loadedHandle = failedTarget.gameObject.scene.handle;
        int generatedCount = flow.Items.InstanceCount;
        Assert.That(SceneManager.GetSceneByPath(ShelterPath).isLoaded, Is.True);
        failedTarget.spawnPoint = originalSpawn;

        flow.Retry();
        yield return WaitForStage(GameFlowStage.Exploring);

        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        Assert.That(flow.Player.health.CurrentRound, Is.EqualTo(1));
        Assert.That(flow.Items.InstanceCount, Is.EqualTo(generatedCount), "Same-round rebind must not regenerate loot.");
        Assert.That(flow.Player.gameObject.scene.handle, Is.EqualTo(loadedHandle));
        Assert.That(LoadedSceneCount(RuinsPath), Is.EqualTo(1));
        Assert.That(flow.Player.IsSceneTransitionBlocked, Is.False);
        AssertOneActiveRuntime();
    }

    [UnityTest]
    public IEnumerator ReturnLoadFailurePreservesWorldItemsAndRetrySettlesOnlyOnce()
    {
        yield return LoadBootstrap();
        Assert.That(flow.TryDepart(), Is.True);
        yield return WaitForStage(GameFlowStage.Exploring);
        List<ItemInstance> before = flow.Loot.GetRemainingWorldItems();
        Assert.That(before, Is.Not.Empty);
        string configuredShelter = flow.shelterScenePath;
        flow.shelterScenePath = "Assets/Scenes/Missing-MinimumLoop-Test.unity";
        Assert.That(flow.TryExtract(), Is.True);
        yield return WaitForStage(GameFlowStage.Failed);
        Assert.That(flow.SettledRound, Is.Zero);
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        Assert.That(flow.Player.gameObject.scene.path, Is.EqualTo(RuinsPath));
        CollectionAssert.AreEquivalent(before, flow.Loot.GetRemainingWorldItems());
        foreach (ItemInstance item in before)
        {
            Assert.That(item.Owner == ItemOwner.World || item.Owner == ItemOwner.Container, Is.True);
            Assert.That(item.LastLossReason, Is.Null);
        }
        int refreshes = 0;
        flow.Backpack.Changed += () => refreshes++;
        flow.shelterScenePath = configuredShelter;

        flow.Retry();
        yield return WaitForStage(GameFlowStage.Shelter);

        Assert.That(flow.SettledRound, Is.EqualTo(1));
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        Assert.That(refreshes, Is.EqualTo(1));
        foreach (ItemInstance item in before)
        {
            Assert.That(item.Owner, Is.EqualTo(string.IsNullOrEmpty(item.UniqueId) ? ItemOwner.Lost : ItemOwner.PendingRecovery));
            Assert.That(item.LastLossReason, Is.EqualTo(ItemLossReason.LeftBehindOnExtraction));
        }
        flow.Retry();
        Assert.That(flow.TryExtract(), Is.False);
        yield return null;
        Assert.That(refreshes, Is.EqualTo(1));
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        AssertOneActiveRuntime();
    }

    [UnityTest]
    public IEnumerator UsedMedkitAndHealthSurviveReturnAndUiReopenUntilTheNextDeparture()
    {
        yield return LoadBootstrap();
        Assert.That(flow.TryDepart(), Is.True);
        yield return WaitForStage(GameFlowStage.Exploring);
        PlayerController samePlayer = flow.Player;
        ItemInstance medkit = TakeFormalBoxItem("medkit", 1, 1, true);
        Assert.That(flow.Backpack.TryGetPlacement(medkit.InstanceId, out BackpackPlacement placement), Is.True);
        Assert.That(samePlayer.health.MaxHealth, Is.GreaterThan(1));

        // 伤害只安排在测试会话，正式白盒不增加伤害源或测试Driver。
        Assert.That(samePlayer.health.TakeDamage(1), Is.True);
        Assert.That(flow.Backpack.TryUseItem(medkit.InstanceId, samePlayer.health, out ItemUseResult use),
            Is.EqualTo(BackpackResult.Success));
        Assert.That(use, Is.EqualTo(ItemUseResult.Success));
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        Assert.That(samePlayer.health.CurrentHealth, Is.EqualTo(samePlayer.health.MaxHealth));
        yield return new WaitForSeconds(samePlayer.config.damageInvulnerabilitySeconds + 0.05f);
        Assert.That(samePlayer.health.TakeDamage(1), Is.True);
        int woundedHealth = samePlayer.health.CurrentHealth;
        Assert.That(woundedHealth, Is.LessThan(samePlayer.health.MaxHealth));

        Assert.That(flow.TryExtract(), Is.True);
        yield return WaitForStage(GameFlowStage.Shelter);
        Assert.That(flow.Player, Is.SameAs(samePlayer));
        Assert.That(samePlayer.health.CurrentHealth, Is.EqualTo(woundedHealth));
        Assert.That(samePlayer.health.CurrentRound, Is.EqualTo(1));
        Assert.That(medkit.UseRound, Is.EqualTo(1));
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        Assert.That(medkit.IsRetained, Is.True);
        AssertCarriedPlacementUnchanged(placement);

        GameFlowSceneBindings home = SceneBindings(SceneManager.GetSceneByPath(ShelterPath));
        for (int reopen = 0; reopen < 2; reopen++)
        {
            Assert.That(flow.TryOpenStorage(), Is.True);
            Assert.That(flow.Backpack.TryUseItem(medkit.InstanceId, samePlayer.health, out use),
                Is.EqualTo(BackpackResult.Success));
            Assert.That(use, Is.EqualTo(ItemUseResult.RoundLimitReached));
            home.CloseSessions();
            Assert.That(samePlayer.health.CurrentHealth, Is.EqualTo(woundedHealth));
            Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
            Assert.That(flow.CurrentRound, Is.EqualTo(1));
            AssertCarriedPlacementUnchanged(placement);
        }

        Assert.That(flow.TryDepart(), Is.True);
        yield return WaitForStage(GameFlowStage.Exploring);
        Assert.That(flow.Player, Is.SameAs(samePlayer));
        Assert.That(flow.CurrentRound, Is.EqualTo(2));
        Assert.That(samePlayer.health.CurrentRound, Is.EqualTo(2));
        Assert.That(samePlayer.health.CurrentHealth, Is.EqualTo(samePlayer.health.MaxHealth));
        Assert.That(medkit.UseRound, Is.EqualTo(2));
        Assert.That(medkit.UsesThisRound, Is.Zero);
        AssertCarriedPlacementUnchanged(placement);
        Assert.That(FormalBox().State.TryGetEntry("medkit", out _), Is.False,
            "The retained medkit must not be generated again at its original box.");
        Assert.That(samePlayer.health.TakeDamage(1), Is.True);
        Assert.That(flow.Backpack.TryUseItem(medkit.InstanceId, samePlayer.health, out use),
            Is.EqualTo(BackpackResult.Success));
        Assert.That(use, Is.EqualTo(ItemUseResult.Success));
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        Assert.That(samePlayer.health.CurrentHealth, Is.EqualTo(samePlayer.health.MaxHealth));
        AssertOneActiveRuntime();
    }

    [UnityTest]
    public IEnumerator FullWarehouseAllowsReturnAndAnotherDepartureWithoutMovingCarriedLoot()
    {
        yield return LoadBootstrap();
        ItemDataSO battery = flow.Items.itemLibrary.FindDefinition("battery");
        Assert.That(battery, Is.Not.Null);
        Assert.That(flow.Warehouse.Width % battery.width, Is.Zero);
        Assert.That(flow.Warehouse.Height % battery.height, Is.Zero);
        // 仅测试初态通过真实01/05公开入口填满格子；不向正式入口添加赠物逻辑。
        for (int y = 0; y < flow.Warehouse.Height; y += battery.height)
        {
            for (int x = 0; x < flow.Warehouse.Width; x += battery.width)
            {
                Assert.That(flow.Items.TryCreateInstance(battery, ItemOwner.Storage, flow.Warehouse.ContainerId,
                    "minimum-loop-full-storage-test", string.Empty, out ItemInstance stored), Is.True);
                Assert.That(flow.Warehouse.TryAddPlacement(stored.InstanceId, x, y, false), Is.EqualTo(WarehouseResult.Success));
                Assert.That(flow.Items.TrySetRetained(stored.InstanceId, true), Is.True);
            }
        }
        flow.Warehouse.NotifyChanged();
        var storedPlacements = new List<WarehousePlacement>(flow.Warehouse.GetPlacements());
        Assert.That(storedPlacements.Count * battery.width * battery.height,
            Is.EqualTo(flow.Warehouse.Width * flow.Warehouse.Height));

        Assert.That(flow.TryDepart(), Is.True, "A full warehouse must not prevent departure.");
        yield return WaitForStage(GameFlowStage.Exploring);
        ItemInstance doll = TakeFormalBoxItem("doll", 1, 0, true);
        Assert.That(flow.Backpack.TryGetPlacement(doll.InstanceId, out BackpackPlacement carried), Is.True);
        Assert.That(flow.TryExtract(), Is.True, "Extraction keeps loot in the backpack even when storage is full.");
        yield return WaitForStage(GameFlowStage.Shelter);
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        Assert.That(flow.SettledRound, Is.EqualTo(1));
        Assert.That(doll.IsRetained, Is.True);
        Assert.That(flow.Shelter.CurrentEv, Is.EqualTo(doll.Definition.ev));
        AssertCarriedPlacementUnchanged(carried);
        AssertStoredPlacementsUnchanged(storedPlacements);

        Assert.That(flow.TryOpenStorage(), Is.True);
        GameFlowSceneBindings home = SceneBindings(SceneManager.GetSceneByPath(ShelterPath));
        Assert.That(flow.Transfers.TryBackpackToWarehouse(doll.InstanceId, 0, 0, true,
            requestIsValid: () => home.backpackUI.IsOpen && home.warehouseUI.IsOpen), Is.EqualTo(ItemTransferResult.Occupied));
        AssertCarriedPlacementUnchanged(carried);
        AssertStoredPlacementsUnchanged(storedPlacements);
        home.CloseSessions();

        Assert.That(flow.TryDepart(), Is.True, "Neither a full warehouse nor carried loot blocks the next round.");
        yield return WaitForStage(GameFlowStage.Exploring);
        Assert.That(flow.CurrentRound, Is.EqualTo(2));
        AssertCarriedPlacementUnchanged(carried);
        AssertStoredPlacementsUnchanged(storedPlacements);
        Assert.That(FormalBox().State.TryGetEntry("doll", out _), Is.False);
        AssertOneActiveRuntime();
    }

    private ItemInstance TakeFormalBoxItem(string entryId, int x, int y, bool rotated)
    {
        LootBox box = FormalBox();
        GameFlowSceneBindings bindings = SceneBindings(SceneManager.GetSceneByPath(RuinsPath));
        Assert.That(box.TryGetInteractionTrigger(out BoxCollider trigger), Is.True);
        CapsuleCollider body = flow.Player.GetComponent<CapsuleCollider>();
        Physics.SyncTransforms();
        Vector3 reachablePosition = bindings.spawnPoint.position;
        reachablePosition.x = trigger.bounds.min.x - body.bounds.extents.x + 0.2f;
        Assert.That(flow.Player.RelocateWithinRun(reachablePosition), Is.True);
        Physics.SyncTransforms();
        Assert.That(Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
            trigger, trigger.transform.position, trigger.transform.rotation, out _, out _), Is.True);
        Assert.That(flow.Player.interaction.CanReachTarget(box), Is.True);
        Assert.That(bindings.lootUI.TryOpenSource(box.State, box, flow.Player.interaction), Is.True);
        // 本组只安排搜刮初态；搜索时序及原生鼠标由对应测试和实际试玩证明。
        box.State.AdvanceSearch(30f, flow.Items);
        Assert.That(box.State.TryGetEntry(entryId, out LootEntryState entry), Is.True);
        Assert.That(entry.Revealed, Is.True);
        Assert.That(flow.Items.TryGetInstance(entry.InstanceId, out ItemInstance item), Is.True);
        int token = bindings.lootUI.SessionToken;
        Assert.That(flow.Transfers.TrySourceToBackpack(box.State, entryId, x, y, rotated,
            () => bindings.lootUI.IsSessionValid(token, box.State)), Is.EqualTo(ItemTransferResult.Success));
        bindings.CloseSessions();
        return item;
    }

    private static LootBox FormalBox()
    {
        foreach (LootBox box in UnityEngine.Object.FindObjectsByType<LootBox>(FindObjectsSortMode.None))
        {
            if (box.gameObject.scene.path == RuinsPath && box.sourceId == "supplies-box") { return box; }
        }
        Assert.Fail("Formal ruins scene has no supplies-box.");
        return null;
    }

    private void AssertCarriedPlacementUnchanged(BackpackPlacement expected)
    {
        Assert.That(flow.Backpack.TryGetPlacement(expected.InstanceId, out BackpackPlacement current), Is.True);
        Assert.That(current, Is.SameAs(expected));
        Assert.That(flow.Items.TryGetInstance(expected.InstanceId, out ItemInstance item), Is.True);
        Assert.That(item, Is.SameAs(expected.Item));
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(item.ContainerId, Is.EqualTo(flow.Backpack.ContainerId));
    }

    private void AssertStoredPlacementsUnchanged(List<WarehousePlacement> expected)
    {
        Assert.That(flow.Warehouse.GetPlacements().Count, Is.EqualTo(expected.Count));
        foreach (WarehousePlacement placement in expected)
        {
            Assert.That(flow.Warehouse.TryGetPlacement(placement.InstanceId, out WarehousePlacement current), Is.True);
            Assert.That(current, Is.SameAs(placement));
            Assert.That(flow.Warehouse.TryGetStoredItem(placement.InstanceId, out ItemInstance item), Is.EqualTo(WarehouseResult.Success));
            Assert.That(item, Is.SameAs(placement.Item));
        }
    }

    private IEnumerator LoadBootstrap()
    {
        startedSession = true;
        SceneManager.sceneLoaded += IsolateEnemies;
        UnityAction<Scene, LoadSceneMode> activateBootstrap = (scene, mode) =>
        {
            if (scene.path == BootstrapPath) { SceneManager.SetActiveScene(scene); }
        };
        SceneManager.sceneLoaded += activateBootstrap;
        try
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(BootstrapPath,
                new LoadSceneParameters(LoadSceneMode.Additive));
        }
        finally { SceneManager.sceneLoaded -= activateBootstrap; }
        float deadline = Time.realtimeSinceStartup + 20f;
        while (flow == null && Time.realtimeSinceStartup < deadline)
        {
            flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
            yield return null;
        }
        Assert.That(flow, Is.Not.Null);
        persistentRoot = GameBootstrap.Instance.gameObject;
        yield return WaitForStage(GameFlowStage.Shelter);
    }

    private IEnumerator WaitForStage(GameFlowStage expected)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (flow.Stage != expected && Time.realtimeSinceStartup < deadline)
        {
            if (flow.Stage == GameFlowStage.Failed && expected != GameFlowStage.Failed)
            {
                Assert.Fail(flow.LastError);
            }
            yield return null;
        }
        Assert.That(flow.Stage, Is.EqualTo(expected), flow.LastError);
        if (expected == GameFlowStage.Shelter || expected == GameFlowStage.Exploring)
        {
            // 等真实Brain完成一次LateUpdate；不ManualUpdate、不移动相机来制造边界通过。
            yield return null;
            AssertSceneCamera(flow, SceneBindings(SceneManager.GetActiveScene()));
        }
    }

    private void AssertOneActiveRuntime()
    {
        Assert.That(UnityEngine.Object.FindObjectsByType<GameBootstrap>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        Assert.That(UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        Assert.That(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        Assert.That(UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        Assert.That(flow.Player.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()));
        Scene scene = SceneManager.GetActiveScene();
        GameFlowSceneBindings bindings = SceneBindings(scene);
        Assert.That(bindings.presentationRoot.activeInHierarchy, Is.True);
        Interactable[] worldTargets = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Interactable>(true)).ToArray();
        Assert.That(worldTargets.Length, Is.GreaterThanOrEqualTo(bindings.isShelter ? 2 : 1));
        foreach (Interactable target in worldTargets)
        {
            Assert.That(target.interactionTrigger, Is.Not.Null, target.name + " must not use legacy range fallback.");
            Assert.That(target.interactionTrigger.isTrigger, Is.True);
            Assert.That(target.interactionTrigger.GetComponentInParent<Interactable>(true), Is.SameAs(target));
            Assert.That(target.interactionTrigger.gameObject.scene, Is.EqualTo(scene));
            bool available = target.isActiveAndEnabled && target.interactionTrigger.enabled
                && target.interactionTrigger.gameObject.activeInHierarchy;
            Assert.That(target.TryGetInteractionTrigger(out _), Is.EqualTo(available), target.name);
        }
        Assert.That(bindings.transferUI.coordinator, Is.SameAs(flow.Transfers));
        Assert.That(bindings.transferUI.backpack, Is.SameAs(bindings.backpackUI));
        Assert.That(bindings.transferUI.loot, Is.SameAs(bindings.lootUI));
        Assert.That(bindings.backpackUI.QuickTransferRequested, Is.Not.Null, "The active formal scene must bind its quick-transfer entry.");
        if (bindings.warehouseUI != null)
        {
            Assert.That(bindings.transferUI.warehouse, Is.SameAs(bindings.warehouseUI));
            Assert.That(bindings.warehouseUI.QuickTransferRequested, Is.Not.Null);
        }
        AssertSceneCamera(flow, bindings);
        Assert.That(flow.bootstrapPresentationRoot.activeInHierarchy, Is.False);
    }

    // 两轮正式排队用例复用同一组输出断言；校验正式资产，不创建或修正任何边界。
    internal static void AssertSceneCamera(GameFlowManager flow, GameFlowSceneBindings bindings)
    {
        Scene scene = bindings.gameObject.scene;
        Assert.That(scene, Is.EqualTo(SceneManager.GetActiveScene()));
        Assert.That(flow.Player.gameObject.scene, Is.EqualTo(scene));
        PlayerCameraTargetBinder binder = bindings.cameraBinder;
        Assert.That(binder, Is.Not.Null);
        Assert.That(binder.gameObject.scene, Is.EqualTo(scene));
        Assert.That(binder.player, Is.SameAs(flow.Player));
        Assert.That(binder.followCamera, Is.Not.Null);
        Assert.That(binder.followCamera.isActiveAndEnabled, Is.True);
        Assert.That(binder.followCamera.gameObject.scene, Is.EqualTo(scene));
        Assert.That(binder.followCamera.Follow,
            Is.SameAs(flow.Player.cameraTarget != null ? flow.Player.cameraTarget : flow.Player.transform));
        Assert.That(binder.followCamera.Follow.gameObject.scene, Is.EqualTo(scene), "A scene switch cannot retain the previous scene's follow target.");
        Assert.That(binder.confiner, Is.Not.Null, "The formal instance must serialize its native Confiner3D reference.");
        Assert.That(binder.confiner.gameObject, Is.SameAs(binder.followCamera.gameObject));
        Assert.That(binder.followCamera.GetComponents<CinemachineConfiner3D>().Length, Is.EqualTo(1));
        Assert.That(binder.ValidateBounds(out string boundsError), Is.True, boundsError);
        Assert.That(binder.LastBoundsError, Is.Null.Or.Empty);
        Assert.That(binder.confiner.IsValid, Is.True);
        Assert.That(binder.confiner.BoundingVolume, Is.InstanceOf<BoxCollider>());
        BoxCollider volume = (BoxCollider)binder.confiner.BoundingVolume;
        Assert.That(volume.gameObject.scene, Is.EqualTo(scene), "The active camera cannot retain an unloaded or previous map's bounds.");
        Assert.That(volume.gameObject.activeInHierarchy && volume.enabled && volume.isTrigger, Is.True);

        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
            .Where(camera => camera.isActiveAndEnabled).ToArray();
        Assert.That(cameras.Length, Is.EqualTo(1), "Only the current map's output camera may render.");
        Assert.That(cameras[0].gameObject.scene, Is.EqualTo(scene));
        CinemachineBrain brain = cameras[0].GetComponent<CinemachineBrain>();
        Assert.That(brain, Is.Not.Null);
        Assert.That(brain.isActiveAndEnabled, Is.True);
        Assert.That(UnityEngine.Object.FindObjectsByType<CinemachineBrain>(FindObjectsSortMode.None)
            .Count(value => value.isActiveAndEnabled), Is.EqualTo(1));
        AudioListener[] listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)
            .Where(value => value.isActiveAndEnabled).ToArray();
        Assert.That(listeners.Length, Is.EqualTo(1));
        Assert.That(listeners[0].gameObject.scene, Is.EqualTo(scene));
        Assert.That(brain.OutputCamera, Is.SameAs(cameras[0]));
        Assert.That(brain.ActiveVirtualCamera, Is.SameAs(binder.followCamera));
        Assert.That(Vector3.Distance(volume.ClosestPoint(cameras[0].transform.position), cameras[0].transform.position),
            Is.LessThan(0.05f), "Check the real Brain output after scene binding or relocation, not only the authored bounds reference.");
        Assert.That(cameras[0].orthographic, Is.False);
        Assert.That(cameras[0].fieldOfView, Is.EqualTo(50f).Within(0.01f));
        Assert.That(binder.followCamera.Lens.FieldOfView, Is.EqualTo(50f).Within(0.01f));
        CinemachinePositionComposer composer = binder.followCamera.GetComponent<CinemachinePositionComposer>();
        Assert.That(composer, Is.Not.Null);
        Assert.That(composer.CameraDistance, Is.EqualTo(12f).Within(0.01f));
        Assert.That(cameras[0].transform.position.z, Is.EqualTo(flow.Player.config.planeZ - 12f).Within(0.05f));
    }

    private static GameFlowSceneBindings SceneBindings(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            GameFlowSceneBindings found = root.GetComponentInChildren<GameFlowSceneBindings>(true);
            if (found != null) { return found; }
        }
        Assert.Fail("Formal scene has no GameFlowSceneBindings: " + scene.path);
        return null;
    }

    private static int LoadedSceneCount(string path)
    {
        int count = 0;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).path == path) { count++; }
        }
        return count;
    }

    // 原用例专测物品/生命/接线；敌人风险由RuinsEnemyIntegrationTests验证，仅隔离测试实例。
    internal static void IsolateEnemies(Scene scene, LoadSceneMode mode)
    {
        if (scene.path != RuinsPath) { return; }
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (DroneController drone in root.GetComponentsInChildren<DroneController>(true))
            {
                drone.enabled = false;
            }
        }
    }
}
#endif
