#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// 正式 Bootstrap/场景/业务模块集成测试。伤害走真实 PlayerHealth.TakeDamage。
// API 安排与断言，不冒充原生键鼠试玩；仅测试实例隔离无人机并固定箱内 food 概率。
public class GameFlowDeathReturnTests
{
    private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";
    private const string ShelterPath = "Assets/Scenes/Shelter.unity";
    private const string RuinsPath = "Assets/Scenes/Ruins.unity";
    private GameFlowManager flow;
    private GameObject persistentRoot;
    private bool startedSession;
    private UnityAction<Scene, LoadSceneMode> sceneLoadedHandler;
    private readonly List<LootTableSO> runtimeTables = new List<LootTableSO>();

    [SetUp]
    public void SetUp()
    {
        flow = null;
        persistentRoot = null;
        startedSession = false;
        sceneLoadedHandler = null;
        runtimeTables.Clear();
        if (UnityEngine.Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include,
            FindObjectsSortMode.None).Length > 0)
        {
            Assert.Ignore("Run from the clean Test Runner scene, without an existing game session.");
        }
        Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapPath), Is.Not.Null);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        // 既存用户会话导致 SetUp 跳过时，不删除用户的场景或常驻入口。
        if (!startedSession) { yield break; }
        SceneManager.sceneLoaded -= PrepareRuntimeTestScene;
        if (sceneLoadedHandler != null) { SceneManager.sceneLoaded -= sceneLoadedHandler; }
        if (flow != null)
        {
            flow.StopAllCoroutines();
            SetPrivate(flow.Backpack, "busy", false);
        }
        foreach (string path in new[] { RuinsPath, ShelterPath, BootstrapPath })
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            if (scene.IsValid() && scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(scene); }
        }
        if (persistentRoot == null && GameBootstrap.Instance != null)
        {
            persistentRoot = GameBootstrap.Instance.gameObject;
        }
        if (persistentRoot != null) { UnityEngine.Object.Destroy(persistentRoot); }
        foreach (LootTableSO table in runtimeTables)
        {
            if (table != null) { UnityEngine.Object.Destroy(table); }
        }
        runtimeTables.Clear();
        yield return null;
    }

    [UnityTest]
    public IEnumerator DeathLosesOldAndNewCarryBeforeShelterLoadButKeepsStorageUpgradeAndRound()
    {
        yield return LoadBootstrap();
        PlayerController samePlayer = flow.Player;
        Vector3 homeSpawn = Bindings(SceneManager.GetSceneByPath(ShelterPath)).spawnPoint.position;
        ItemManager sameItems = flow.Items;
        BackpackManager sameBackpack = flow.Backpack;
        WarehouseManager sameWarehouse = flow.Warehouse;
        ShelterManager sameShelter = flow.Shelter;
        LootRuntimeManager sameLoot = flow.Loot;
        ItemTransferCoordinator sameTransfers = flow.Transfers;
        ItemInstance battery = CreateStoredBattery();

        yield return Depart();
        ItemInstance doll = TakeBoxItem("doll");
        ItemInstance medkit = TakeBoxItem("medkit");
        yield return Extract();
        Assert.That(flow.Shelter.CurrentEv, Is.EqualTo(doll.Definition.ev + medkit.Definition.ev));
        Assert.That(flow.Shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.Success));
        Assert.That(flow.TryOpenStorage(), Is.True);
        GameFlowSceneBindings home = Bindings(SceneManager.GetSceneByPath(ShelterPath));
        Assert.That(flow.Transfers.TryBackpackToWarehouse(medkit.InstanceId, 3, 4, true,
            requestIsValid: () => home.backpackUI.IsOpen && home.warehouseUI.IsOpen
                && flow.Stage == GameFlowStage.Shelter),
            Is.EqualTo(ItemTransferResult.Success));
        home.CloseSessions();
        List<WarehousePlacement> stored = new List<WarehousePlacement>(flow.Warehouse.GetPlacements());
        int storedWidth = flow.Warehouse.Width;
        int storedHeight = flow.Warehouse.Height;
        int storageEv = medkit.Definition.ev;
        string originalDollOrigin = doll.OriginalSpawnPointId;

        yield return Depart();
        Assert.That(flow.CurrentRound, Is.EqualTo(2));
        Assert.That(doll.IsRetained, Is.True);
        ItemInstance freshFood = TakeBoxItem("food");
        Assert.That(freshFood.IsRetained, Is.False);
        List<ItemInstance> abandoned = flow.Loot.GetRemainingWorldItems();
        bool shelterLoadObserved = false;
        bool lossesCompleteAtLoad = false;
        sceneLoadedHandler = (scene, mode) =>
        {
            if (scene.path != ShelterPath) { return; }
            shelterLoadObserved = true;
            lossesCompleteAtLoad = flow.Backpack.GetPlacements().Count == 0
                && !doll.IsRetained && doll.Owner == ItemOwner.PendingRecovery
                && freshFood.Owner == ItemOwner.Lost
                && flow.Shelter.CurrentEv == storageEv
                && flow.Loot.GetRemainingWorldItems().Count == 0
                && !flow.Loot.IsExplorationActive;
        };
        SceneManager.sceneLoaded += sceneLoadedHandler;

        Assert.That(samePlayer.health.TakeDamage(samePlayer.health.MaxHealth), Is.True);
        Assert.That(flow.Stage, Is.EqualTo(GameFlowStage.DeathReturning));
        Assert.That(samePlayer.IsSceneTransitionBlocked, Is.True);
        Assert.That(flow.TryExtract(), Is.False, "Death accepted first prevents successful extraction.");
        Assert.That(samePlayer.health.TakeDamage(1), Is.False, "Repeated contacts cannot restart death.");
        yield return WaitForStage(GameFlowStage.Shelter);

        Assert.That(shelterLoadObserved && lossesCompleteAtLoad, Is.True,
            "Death inventory/EV/world settlement must precede loading Shelter.");
        Assert.That(flow.Player, Is.SameAs(samePlayer));
        Assert.That(flow.Items, Is.SameAs(sameItems));
        Assert.That(flow.Backpack, Is.SameAs(sameBackpack));
        Assert.That(flow.Warehouse, Is.SameAs(sameWarehouse));
        Assert.That(flow.Shelter, Is.SameAs(sameShelter));
        Assert.That(flow.Loot, Is.SameAs(sameLoot));
        Assert.That(flow.Transfers, Is.SameAs(sameTransfers));
        Assert.That(flow.CurrentRound, Is.EqualTo(2));
        Assert.That(flow.SettledRound, Is.EqualTo(2));
        Assert.That(samePlayer.health.CurrentRound, Is.EqualTo(2));
        Assert.That(samePlayer.IsDead, Is.False);
        Assert.That(samePlayer.health.CurrentHealth, Is.EqualTo(samePlayer.health.MaxHealth));
        Assert.That(Mathf.Abs(samePlayer.Position.x - homeSpawn.x), Is.LessThan(0.02f));
        Assert.That(Mathf.Abs(samePlayer.Position.z - homeSpawn.z), Is.LessThan(0.02f));
        Assert.That(samePlayer.CanAct, Is.True);
        Assert.That(flow.Backpack.GetPlacements(), Is.Empty);
        Assert.That(flow.Shelter.CurrentEv, Is.EqualTo(storageEv));
        Assert.That(flow.Shelter.IsUpgradeActivated, Is.True);
        Assert.That(flow.Warehouse.Width, Is.EqualTo(storedWidth));
        Assert.That(flow.Warehouse.Height, Is.EqualTo(storedHeight));
        AssertStoredPlacements(stored);
        Assert.That(battery.Owner, Is.EqualTo(ItemOwner.Storage));
        Assert.That(medkit.IsRetained, Is.True);
        AssertDeathLoss(doll, 3);
        AssertDeathLoss(freshFood, 0);
        foreach (ItemInstance item in abandoned) { AssertTerminalLoss(item); }
        Assert.That(flow.TryOpenStorage(), Is.True);
        Bindings(SceneManager.GetSceneByPath(ShelterPath)).CloseSessions();
        AssertOneRuntime();

        yield return Depart();
        Assert.That(flow.CurrentRound, Is.EqualTo(3), "Only actual departure advances the round.");
        LootBox nextBox = FormalBox();
        Assert.That(nextBox.State.TryGetEntry("doll", out LootEntryState recovered), Is.True);
        Assert.That(recovered.InstanceId, Is.EqualTo(doll.InstanceId));
        Assert.That(doll.OriginalSpawnPointId, Is.EqualTo(originalDollOrigin));
        Assert.That(doll.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(doll.SpawnClaimed, Is.True);
        Assert.That(flow.Items.GetInstances().Count(item => item.UniqueId == doll.UniqueId), Is.EqualTo(1));
        Assert.That(nextBox.State.TryGetEntry("medkit", out _), Is.False,
            "A stored unique item must not respawn alongside the recovered doll.");
        AssertStoredPlacements(stored);
        AssertOneRuntime();
    }

    [UnityTest]
    public IEnumerator DeathClosesSearchAndDragSynchronouslyAndUsedMedkitKeepsItsRoundAllowance()
    {
        yield return LoadBootstrap();
        yield return Depart();
        ItemInstance medkit = TakeBoxItem("medkit", false);
        Assert.That(flow.Player.health.TakeDamage(1), Is.True);
        Assert.That(flow.Backpack.TryUseItem(medkit.InstanceId, flow.Player.health, out ItemUseResult used),
            Is.EqualTo(BackpackResult.Success));
        Assert.That(used, Is.EqualTo(ItemUseResult.Success));
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        yield return new WaitForSeconds(flow.Player.config.damageInvulnerabilitySeconds + 0.05f);

        LootBox box = OpenFormalBox();
        GameFlowSceneBindings ruins = Bindings(SceneManager.GetSceneByPath(RuinsPath));
        LootSourceState source = box.State;
        // TakeBoxItem selectively reveals preceding entries only; food remains searchable.
        Assert.That(source.GetNextUnrevealed(), Is.Not.Null);
        source.AdvanceSearch(0.1f, flow.Items);
        Assert.That(source.SearchElapsed, Is.GreaterThan(0f));
        int sourceSession = ruins.lootUI.SessionToken;
        int drag = ruins.backpackUI.BeginItemDrag(medkit.InstanceId, Vector2.zero, null);
        Assert.That(drag, Is.Not.Zero);
        Assert.That(ruins.backpackUI.IsDragging, Is.True);
        int deaths = 0;
        flow.Player.health.Died += () => deaths++;

        Assert.That(flow.Player.health.TakeDamage(flow.Player.health.MaxHealth), Is.True);
        Assert.That(ruins.backpackUI.IsOpen, Is.False);
        Assert.That(ruins.backpackUI.IsDragging, Is.False);
        Assert.That(ruins.lootUI.IsOpen, Is.False);
        Assert.That(source.SearchElapsed, Is.Zero);
        Assert.That(source.SearchEntryId, Is.Empty);
        Assert.That(ruins.lootUI.IsSessionValid(sourceSession, source), Is.False);
        Assert.That(flow.Player.IsSceneTransitionBlocked, Is.True);
        Assert.That(flow.TryExtract(), Is.False);
        Assert.That(flow.Player.health.TakeDamage(1), Is.False);
        Assert.That(flow.Transfers.TrySourceToBackpack(source, "doll", 0, 0, false),
            Is.Not.EqualTo(ItemTransferResult.Success));
        Assert.That(flow.Backpack.TryUseItem(medkit.InstanceId, flow.Player.health, out ItemUseResult afterDeath),
            Is.EqualTo(BackpackResult.Success));
        Assert.That(afterDeath, Is.EqualTo(ItemUseResult.InvalidTarget));
        ruins.backpackUI.EndDrag(drag, Vector2.zero, null);
        Assert.That(medkit.Owner, Is.EqualTo(ItemOwner.Backpack), "A stale pointer release cannot move the item.");

        yield return WaitForStage(GameFlowStage.Shelter);
        Assert.That(deaths, Is.EqualTo(1));
        Assert.That(medkit.UseRound, Is.EqualTo(1));
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        AssertDeathLoss(medkit, 2);
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        Assert.That(flow.Player.health.CurrentHealth, Is.EqualTo(flow.Player.health.MaxHealth));
        yield return Depart();
        Assert.That(medkit.UseRound, Is.EqualTo(2));
        Assert.That(medkit.UsesThisRound, Is.Zero);
        Assert.That(FormalBox().State.TryGetEntry("medkit", out LootEntryState recovered), Is.True);
        Assert.That(recovered.InstanceId, Is.EqualTo(medkit.InstanceId));
    }

    [UnityTest]
    public IEnumerator DeathSettlementCompletesEvenWhenShelterPathFailsAndRetryDoesNotRepeatLoss()
    {
        yield return LoadBootstrap();
        yield return Depart();
        ItemInstance doll = TakeBoxItem("doll");
        string configuredShelter = flow.shelterScenePath;
        flow.shelterScenePath = "Assets/Scenes/Missing-DeathReturn-Test.unity";
        LogAssert.Expect(LogType.Warning, new Regex("^GameFlow: 目标场景未配置"));
        Assert.That(flow.Player.health.TakeDamage(flow.Player.health.MaxHealth), Is.True);
        yield return WaitForStage(GameFlowStage.Failed);
        Assert.That(flow.Backpack.GetPlacements(), Is.Empty);
        AssertDeathLoss(doll, 2);
        Assert.That(flow.Loot.GetRemainingWorldItems(), Is.Empty);
        Assert.That(flow.Player.IsDead, Is.True);
        Assert.That(flow.Player.gameObject.scene.path, Is.EqualTo(RuinsPath));
        Assert.That(flow.CanRetry, Is.True);
        int instances = flow.Items.InstanceCount;
        flow.shelterScenePath = configuredShelter;
        flow.Retry();
        yield return WaitForStage(GameFlowStage.Shelter);
        Assert.That(flow.Items.InstanceCount, Is.EqualTo(instances));
        AssertDeathLoss(doll, 2);
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        Assert.That(flow.SettledRound, Is.EqualTo(1));
        Assert.That(flow.Player.IsDead, Is.False);
        flow.Retry();
        yield return null;
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        AssertDeathLoss(doll, 2);
        AssertOneRuntime();
    }

    [UnityTest]
    public IEnumerator RetryRemovesLayoutAfterRecordLossAlreadySucceeded()
    {
        yield return LoadBootstrap();
        yield return Depart();
        ItemInstance doll = TakeBoxItem("doll");
        // 仅测试反射制造已有04事务占用，不添加生产故障注入接口。
        SetPrivate(flow.Backpack, "busy", true);
        LogAssert.Expect(LogType.Warning, new Regex("^GameFlow: 死亡背包布局清理失败"));
        Assert.That(flow.Player.health.TakeDamage(flow.Player.health.MaxHealth), Is.True);
        yield return WaitForStage(GameFlowStage.Failed);
        AssertDeathLoss(doll, 2);
        Assert.That(flow.Backpack.TryGetPlacement(doll.InstanceId, out _), Is.True,
            "RecordLoss succeeded before the deliberately blocked grid removal.");
        Assert.That(flow.Player.IsDead, Is.True);
        SetPrivate(flow.Backpack, "busy", false);
        flow.Retry();
        yield return WaitForStage(GameFlowStage.Shelter);
        Assert.That(flow.Backpack.GetPlacements(), Is.Empty);
        AssertDeathLoss(doll, 2);
        Assert.That(flow.Player.IsDead, Is.False);
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        AssertOneRuntime();
    }

    [UnityTest]
    public IEnumerator RetryAfterRevivalDoesNotCreateAnotherPlayerOrHealTwice()
    {
        yield return LoadBootstrap();
        yield return Depart();
        PlayerController samePlayer = flow.Player;
        ItemInstance doll = TakeBoxItem("doll");
        GameFlowSceneBindings failedTarget = null;
        Transform originalSpawn = null;
        int positiveHealthNotifications = 0;
        samePlayer.health.HealthChanged += (current, maximum) =>
        {
            if (current > 0) { positiveHealthNotifications++; }
        };
        Action<Vector3> invalidateAfterRelocation = delta =>
        {
            if (samePlayer.gameObject.scene.path != ShelterPath) { return; }
            failedTarget = Bindings(samePlayer.gameObject.scene);
            originalSpawn = failedTarget.spawnPoint;
            failedTarget.spawnPoint = null;
        };
        samePlayer.Teleported += invalidateAfterRelocation;
        LogAssert.Expect(LogType.Warning, new Regex("^GameFlow: 场景接线不完整"));
        Assert.That(samePlayer.health.TakeDamage(samePlayer.health.MaxHealth), Is.True);
        yield return WaitForStage(GameFlowStage.Failed);
        samePlayer.Teleported -= invalidateAfterRelocation;
        Assert.That(failedTarget, Is.Not.Null);
        Assert.That(samePlayer.IsDead, Is.False, "The fault occurs after actual revival and relocation.");
        Assert.That(samePlayer.IsSceneTransitionBlocked, Is.True);
        Assert.That(positiveHealthNotifications, Is.EqualTo(1));
        Assert.That(flow.Backpack.GetPlacements(), Is.Empty);
        AssertDeathLoss(doll, 2);
        failedTarget.spawnPoint = originalSpawn;
        flow.Retry();
        yield return WaitForStage(GameFlowStage.Shelter);
        Assert.That(flow.Player, Is.SameAs(samePlayer));
        Assert.That(positiveHealthNotifications, Is.EqualTo(1), "Retry must resume migration without a second recovery.");
        Assert.That(samePlayer.CanAct, Is.True);
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        AssertDeathLoss(doll, 2);
        AssertOneRuntime();
    }

    [UnityTest]
    public IEnumerator ExtractionAcceptedFirstRejectsLaterLethalDamageAndPreservesExistingInjury()
    {
        yield return LoadBootstrap();
        yield return Depart();
        PlayerController samePlayer = flow.Player;
        ItemInstance doll = TakeBoxItem("doll");
        Assert.That(samePlayer.health.TakeDamage(1), Is.True);
        int wounded = samePlayer.health.CurrentHealth;
        yield return new WaitForSeconds(samePlayer.config.damageInvulnerabilitySeconds + 0.05f);
        int deaths = 0;
        samePlayer.health.Died += () => deaths++;
        Assert.That(flow.TryExtract(), Is.True);
        Assert.That(samePlayer.IsSceneTransitionBlocked, Is.True);
        Assert.That(samePlayer.health.TakeDamage(samePlayer.health.MaxHealth), Is.False,
            "Extraction already owns this transition; later collision cannot start death settlement.");
        yield return WaitForStage(GameFlowStage.Shelter);
        Assert.That(deaths, Is.Zero);
        Assert.That(samePlayer.health.CurrentHealth, Is.EqualTo(wounded));
        Assert.That(doll.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(doll.IsRetained, Is.True);
        Assert.That(doll.LastLossReason, Is.Null);
        Assert.That(flow.Shelter.CurrentEv, Is.EqualTo(doll.Definition.ev));
        Assert.That(flow.CurrentRound, Is.EqualTo(1));
        Assert.That(flow.SettledRound, Is.EqualTo(1));
        AssertOneRuntime();
    }

    private IEnumerator LoadBootstrap()
    {
        startedSession = true;
        SceneManager.sceneLoaded += PrepareRuntimeTestScene;
        UnityAction<Scene, LoadSceneMode> activate = (scene, mode) =>
        {
            if (scene.path == BootstrapPath) { SceneManager.SetActiveScene(scene); }
        };
        SceneManager.sceneLoaded += activate;
        try
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(BootstrapPath,
                new LoadSceneParameters(LoadSceneMode.Additive));
        }
        finally { SceneManager.sceneLoaded -= activate; }
        float deadline = Time.realtimeSinceStartup + 25f;
        while (flow == null && Time.realtimeSinceStartup < deadline)
        {
            flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
            yield return null;
        }
        Assert.That(flow, Is.Not.Null);
        persistentRoot = GameBootstrap.Instance.gameObject;
        yield return WaitForStage(GameFlowStage.Shelter);
    }

    private IEnumerator Depart()
    {
        Assert.That(flow.TryDepart(), Is.True);
        yield return WaitForStage(GameFlowStage.Exploring);
    }

    private IEnumerator Extract()
    {
        Assert.That(flow.TryExtract(), Is.True);
        yield return WaitForStage(GameFlowStage.Shelter);
    }

    private IEnumerator WaitForStage(GameFlowStage expected)
    {
        float deadline = Time.realtimeSinceStartup + 25f;
        while (flow.Stage != expected && Time.realtimeSinceStartup < deadline)
        {
            if (flow.Stage == GameFlowStage.Failed && expected != GameFlowStage.Failed)
            {
                Assert.Fail(flow.LastError);
            }
            yield return null;
        }
        Assert.That(flow.Stage, Is.EqualTo(expected), flow.LastError);
        if (expected == GameFlowStage.Shelter || expected == GameFlowStage.Exploring) { yield return null; }
    }

    private void PrepareRuntimeTestScene(Scene scene, LoadSceneMode mode)
    {
        if (scene.path != RuinsPath) { return; }
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (DroneController drone in root.GetComponentsInChildren<DroneController>(true))
            {
                drone.enabled = false;
            }
            foreach (LootBox box in root.GetComponentsInChildren<LootBox>(true))
            {
                if (box.sourceId != "supplies-box" || box.lootTable == null) { continue; }
                // 本处理器在Bootstrap加载前订阅，必须早于常驻Loot的sceneLoaded生成。
                Assert.That(box.State == null || !box.State.Generated, Is.True,
                    "The probability fixture must run before this scene's source is generated.");
                LootTableSO originalTable = box.lootTable;
                LootTableSO table = ScriptableObject.CreateInstance<LootTableSO>();
                table.name = "DeathReturn test runtime guaranteed food";
                runtimeTables.Add(table);
                foreach (LootTableEntry original in originalTable.entries)
                {
                    // 明确复制可编辑条目，SO物品定义仍只读引用；绝不改原表中的概率。
                    table.entries.Add(new LootTableEntry
                    {
                        entryId = original.entryId,
                        item = original.item,
                        useDefaultProbability = original.entryId == "food" ? false : original.useDefaultProbability,
                        overrideProbability = original.entryId == "food" ? 1f : original.overrideProbability
                    });
                }
                box.lootTable = table;
            }
        }
    }

    private LootBox OpenFormalBox()
    {
        LootBox box = FormalBox();
        GameFlowSceneBindings bindings = Bindings(SceneManager.GetSceneByPath(RuinsPath));
        bindings.CloseSessions();
        Assert.That(box.TryGetInteractionTrigger(out BoxCollider trigger), Is.True);
        CapsuleCollider body = flow.Player.GetComponent<CapsuleCollider>();
        Physics.SyncTransforms();
        Vector3 reachable = bindings.spawnPoint.position;
        reachable.x = trigger.bounds.min.x - body.bounds.extents.x + 0.2f;
        Assert.That(flow.Player.RelocateWithinRun(reachable), Is.True);
        Physics.SyncTransforms();
        Assert.That(flow.Player.interaction.CanReachTarget(box), Is.True);
        Assert.That(bindings.lootUI.TryOpenSource(box.State, box, flow.Player.interaction), Is.True);
        return box;
    }

    private ItemInstance TakeBoxItem(string entryId, bool revealAll = true)
    {
        LootBox box = OpenFormalBox();
        GameFlowSceneBindings bindings = Bindings(SceneManager.GetSceneByPath(RuinsPath));
        Assert.That(box.State.TryGetEntry(entryId, out LootEntryState entry), Is.True);
        if (revealAll) { box.State.AdvanceSearch(30f, flow.Items); }
        else
        {
            // 推进到目标条目刚揭示，不提前搜完后续 food，供死亡中断搜索验证。
            for (int i = 0; i < box.State.Entries.Count && !entry.Revealed; i++)
            {
                LootEntryState next = box.State.GetNextUnrevealed();
                Assert.That(next, Is.Not.Null);
                Assert.That(flow.Items.TryGetInstance(next.InstanceId, out ItemInstance nextItem), Is.True);
                box.State.AdvanceSearch(nextItem.Definition.searchSeconds, flow.Items);
            }
        }
        Assert.That(entry.Revealed, Is.True);
        Assert.That(flow.Items.TryGetInstance(entry.InstanceId, out ItemInstance item), Is.True);
        Assert.That(flow.Backpack.TryFindSpaceForExternal(item.InstanceId, false,
            out Vector2Int cell, out bool rotated), Is.EqualTo(BackpackResult.Success));
        int session = bindings.lootUI.SessionToken;
        Assert.That(flow.Transfers.TrySourceToBackpack(box.State, entryId, cell.x, cell.y, rotated,
            () => bindings.lootUI.IsSessionValid(session, box.State)), Is.EqualTo(ItemTransferResult.Success));
        bindings.CloseSessions();
        return item;
    }

    private ItemInstance CreateStoredBattery()
    {
        ItemDataSO definition = flow.Items.itemLibrary.FindDefinition("battery");
        Assert.That(flow.Items.TryCreateInstance(definition, ItemOwner.Storage, flow.Warehouse.ContainerId,
            "death-return-existing-storage", string.Empty, out ItemInstance battery), Is.True);
        Assert.That(flow.Warehouse.TryAddPlacement(battery.InstanceId, 1, 1, true), Is.EqualTo(WarehouseResult.Success));
        Assert.That(flow.Items.TrySetRetained(battery.InstanceId, true), Is.True);
        flow.Warehouse.NotifyChanged();
        return battery;
    }

    private static LootBox FormalBox()
    {
        foreach (GameObject root in SceneManager.GetSceneByPath(RuinsPath).GetRootGameObjects())
        {
            foreach (LootBox box in root.GetComponentsInChildren<LootBox>(true))
            {
                if (box.sourceId == "supplies-box") { return box; }
            }
        }
        Assert.Fail("Formal ruins supplies-box is missing.");
        return null;
    }

    private static GameFlowSceneBindings Bindings(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            GameFlowSceneBindings value = root.GetComponentInChildren<GameFlowSceneBindings>(true);
            if (value != null) { return value; }
        }
        Assert.Fail("Missing scene bindings: " + scene.path);
        return null;
    }

    private static void AssertDeathLoss(ItemInstance item, int recoveryRound)
    {
        AssertTerminalLoss(item);
        Assert.That(item.LastLossReason, Is.EqualTo(ItemLossReason.ExplorationDeath));
        Assert.That(item.RecoveryRound, Is.EqualTo(recoveryRound));
    }

    private static void AssertTerminalLoss(ItemInstance item)
    {
        Assert.That(item.Owner, Is.EqualTo(string.IsNullOrEmpty(item.UniqueId) ? ItemOwner.Lost : ItemOwner.PendingRecovery));
        Assert.That(item.IsRetained, Is.False);
        Assert.That(item.ContainerId, Is.Empty);
        Assert.That(item.LastLossReason, Is.Not.Null);
    }

    private void AssertStoredPlacements(List<WarehousePlacement> expected)
    {
        Assert.That(flow.Warehouse.GetPlacements().Count, Is.EqualTo(expected.Count));
        foreach (WarehousePlacement old in expected)
        {
            Assert.That(flow.Warehouse.TryGetPlacement(old.InstanceId, out WarehousePlacement current), Is.True);
            Assert.That(current, Is.SameAs(old));
            Assert.That(current.X, Is.EqualTo(old.X));
            Assert.That(current.Y, Is.EqualTo(old.Y));
            Assert.That(current.Rotated, Is.EqualTo(old.Rotated));
            Assert.That(flow.Warehouse.TryGetStoredItem(old.InstanceId, out ItemInstance item), Is.EqualTo(WarehouseResult.Success));
            Assert.That(item, Is.SameAs(old.Item));
            Assert.That(item.Owner, Is.EqualTo(ItemOwner.Storage));
            Assert.That(item.LastLossReason, Is.Null);
        }
    }

    private void AssertOneRuntime()
    {
        Assert.That(UnityEngine.Object.FindObjectsByType<GameBootstrap>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        Assert.That(UnityEngine.Object.FindObjectsByType<PlayerController>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        Assert.That(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)
            .Count(value => value.isActiveAndEnabled), Is.EqualTo(1));
        Assert.That(UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None)
            .Count(value => value.isActiveAndEnabled), Is.EqualTo(1));
        Assert.That(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None)
            .Count(value => value.isActiveAndEnabled), Is.EqualTo(1));
        Assert.That(flow.Player.gameObject.scene, Is.EqualTo(SceneManager.GetActiveScene()));
    }

    private static void SetPrivate(object target, string name, object value)
    {
        if (target == null) { return; }
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        field.SetValue(target, value);
    }
}
#endif
