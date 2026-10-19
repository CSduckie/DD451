using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

// 真实01/04/05/06数据验证；仅反射调用07同步结算，不代替正式场景键鼠试玩。
public class GameFlowSettlementTests
{
    private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
    private ItemManager items;
    private BackpackManager backpack;
    private WarehouseManager warehouse;
    private ShelterManager shelter;
    private LootRuntimeManager loot;
    private ItemTransferCoordinator transfers;
    private GameFlowManager flow;
    private PlayerController player;
    private LootTableSO table;
    private LootBox box;
    private int serial;

    [SetUp]
    public void SetUp()
    {
        if (UnityEngine.Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0
            || UnityEngine.Object.FindObjectsByType<LootBox>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
        {
            Assert.Ignore("Run in the Test Runner clean test scene; existing loaded sources belong to the user.");
        }
        GameObject module = NewObject("07 real settlement modules");
        items = module.AddComponent<ItemManager>();
        Assert.That(items.BeginRound(1), Is.True);
        backpack = module.AddComponent<BackpackManager>();
        backpack.config = NewAsset<BackpackConfigSO>();
        Assert.That(backpack.Initialize(items), Is.True);
        warehouse = module.AddComponent<WarehouseManager>();
        warehouse.config = NewAsset<WarehouseConfigSO>();
        Assert.That(warehouse.Initialize(items), Is.True);
        warehouse.SetShelterContext(true, module.scene);
        shelter = module.AddComponent<ShelterManager>();
        shelter.itemManager = items;
        shelter.warehouseManager = warehouse;
        shelter.firstUpgrade = NewAsset<ShelterUpgradeSO>();
        Assert.That(shelter.Initialize(), Is.True);
        loot = module.AddComponent<LootRuntimeManager>();
        loot.itemManager = items;
        player = NewObject("07 real player").AddComponent<PlayerController>();
        player.config = NewAsset<PlayerConfigSO>();
        player.config.damageInvulnerabilitySeconds = 0f;
        player.health = player.GetComponent<PlayerHealth>();
        player.health.config = player.config;
        player.interaction = player.GetComponent<PlayerInteraction>();
        player.interaction.obstructionLayers = 0;
        Assert.That(player.ResetForExploration(1, Vector3.zero), Is.True);
        transfers = module.AddComponent<ItemTransferCoordinator>();
        transfers.itemManager = items;
        transfers.backpackManager = backpack;
        transfers.warehouseManager = warehouse;
        transfers.shelterManager = shelter;
        transfers.lootRuntime = loot;
        transfers.player = player;
        flow = module.AddComponent<GameFlowManager>();
        flow.items = items;
        flow.backpack = backpack;
        flow.warehouse = warehouse;
        flow.shelter = shelter;
        flow.loot = loot;
        flow.transfers = transfers;
        SetFlowPlayer(player);
        table = NewAsset<LootTableSO>();
        table.entries.Add(new LootTableEntry { entryId = "unique", item = Definition(5, true),
            useDefaultProbability = false, overrideProbability = 1f });
        table.entries.Add(new LootTableEntry { entryId = "ordinary", item = Definition(0),
            useDefaultProbability = false, overrideProbability = 1f });
        box = NewBox();
        Assert.That(loot.PrepareRound(1, out string error), Is.True, error);
        loot.SetExplorationContext("flow-test", true);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null) { UnityEngine.Object.DestroyImmediate(created[i]); }
        }
        created.Clear();
    }

    [Test]
    public void ExtractionRetainsLayoutIdentityAndUsageAndPublishesOnlyCompleteState()
    {
        ItemDataSO medicine = Definition(2, true);
        medicine.width = 2;
        medicine.height = 2;
        medicine.canUse = true;
        medicine.usesPerRound = 1;
        HealEffectSO heal = NewAsset<HealEffectSO>();
        medicine.effects.Add(heal);
        ItemInstance carried = Carried(medicine, 1, 1, true);
        Assert.That(player.health.TakeDamage(1), Is.True);
        Assert.That(backpack.TryUseItem(carried.InstanceId, player.health, out ItemUseResult use), Is.EqualTo(BackpackResult.Success));
        Assert.That(use, Is.EqualTo(ItemUseResult.Success));
        Assert.That(player.health.TakeDamage(1), Is.True);
        int health = player.health.CurrentHealth;
        ItemInstance stored = Stored(Definition(3));
        Assert.That(items.TrySetRetained(stored.InstanceId, true), Is.True);
        ItemInstance left = SourceItem("unique");
        Assert.That(items.TrySetRetained(left.InstanceId, true), Is.True);
        shelter.RefreshEv();
        Assert.That(shelter.CurrentEv, Is.EqualTo(8));
        BackpackPlacement before = backpack.GetPlacements()[0];
        string origin = carried.OriginalSpawnPointId;
        string source = carried.SourceId;
        int notices = 0;
        bool observedPartialData = false;
        backpack.Changed += () =>
        {
            notices++;
            observedPartialData |= !carried.IsRetained || left.Owner != ItemOwner.PendingRecovery
                || SourceItem("ordinary").Owner != ItemOwner.Lost;
        };

        Assert.That(Settle(out string error), Is.True, error);

        Assert.That(observedPartialData, Is.False);
        Assert.That(notices, Is.EqualTo(1));
        Assert.That(backpack.GetPlacements()[0], Is.SameAs(before));
        Assert.That(before.Item, Is.SameAs(carried));
        Assert.That(new[] { before.X, before.Y }, Is.EqualTo(new[] { 1, 1 }));
        Assert.That(before.Rotated, Is.True);
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(carried.ContainerId, Is.EqualTo(backpack.ContainerId));
        Assert.That(carried.SourceId, Is.EqualTo(source));
        Assert.That(carried.OriginalSpawnPointId, Is.EqualTo(origin));
        Assert.That(carried.UsesThisRound, Is.EqualTo(1));
        Assert.That(player.health.CurrentHealth, Is.EqualTo(health));
        Assert.That(stored.Owner, Is.EqualTo(ItemOwner.Storage));
        Assert.That(stored.IsRetained, Is.True);
        Assert.That(shelter.CurrentEv, Is.EqualTo(5));
        Assert.That(items.CurrentRound, Is.EqualTo(1));
        Assert.That(flow.SettledRound, Is.EqualTo(1));
        Assert.That(loot.GetRemainingWorldItems(), Is.Empty);

        Assert.That(Settle(out error), Is.True, error);
        Assert.That(notices, Is.EqualTo(1), "Retry after commit must not publish another settlement.");
        Assert.That(left.RecoveryRound, Is.EqualTo(2));
        Assert.That(carried.UsesThisRound, Is.EqualTo(1));
    }

    [Test]
    public void LeftUniqueRecoversTheSameIdentityNextRoundWhileOrdinaryGetsNewIdentity()
    {
        ItemInstance unique = SourceItem("unique");
        ItemInstance ordinary = SourceItem("ordinary");
        string uniqueId = unique.InstanceId;
        string originalPoint = unique.OriginalSpawnPointId;
        Assert.That(items.TrySetRetained(uniqueId, true), Is.True);
        shelter.RefreshEv();
        Assert.That(shelter.CurrentEv, Is.EqualTo(5));
        Assert.That(Settle(out string error), Is.True, error);
        Assert.That(unique.LastLossReason, Is.EqualTo(ItemLossReason.LeftBehindOnExtraction));
        Assert.That(unique.RecoveryRound, Is.EqualTo(2), "Loss is recorded before next BeginRound.");
        Assert.That(unique.IsRetained, Is.False);
        Assert.That(shelter.CurrentEv, Is.Zero);
        Assert.That(items.GetRecoverableItems(), Is.Empty);
        UnityEngine.Object.DestroyImmediate(box.gameObject);
        table.entries[0].overrideProbability = 0f;
        Assert.That(items.BeginRound(2), Is.True);
        Assert.That(loot.PrepareRound(2, out error), Is.True, error);
        box = NewBox();
        Assert.That(loot.PrepareRound(2, out error), Is.True, error);

        Assert.That(SourceItem("unique"), Is.SameAs(unique));
        Assert.That(SourceItem("unique").InstanceId, Is.EqualTo(uniqueId));
        Assert.That(unique.OriginalSpawnPointId, Is.EqualTo(originalPoint));
        Assert.That(unique.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(unique.ContainerId, Is.EqualTo(box.SourceKey));
        Assert.That(unique.SpawnClaimed, Is.True);
        Assert.That(unique.IsRetained, Is.False);
        Assert.That(SourceItem("ordinary").InstanceId, Is.Not.EqualTo(ordinary.InstanceId));
        Assert.That(ordinary.Owner, Is.EqualTo(ItemOwner.Lost));
    }

    [Test]
    public void CarriedUniqueIsNotDuplicatedWhenNextRoundRebindsTheOriginalSource()
    {
        ItemInstance unique = SourceItem("unique");
        box.State.AdvanceSearch(0f, items);
        player.SetBackpackOpen(true);
        Physics.SyncTransforms();
        Assert.That(transfers.TrySourceToBackpack(box.State, "unique", 0, 0, false, () => true),
            Is.EqualTo(ItemTransferResult.Success));
        player.SetBackpackOpen(false);
        Assert.That(Settle(out string error), Is.True, error);
        UnityEngine.Object.DestroyImmediate(box.gameObject);
        Assert.That(items.BeginRound(2), Is.True);
        Assert.That(loot.PrepareRound(2, out error), Is.True, error);
        box = NewBox();
        Assert.That(loot.PrepareRound(2, out error), Is.True, error);

        Assert.That(box.State.TryGetEntry("unique", out _), Is.False);
        Assert.That(items.TryGetUniqueInstance(unique.UniqueId, out ItemInstance current), Is.True);
        Assert.That(current, Is.SameAs(unique));
        Assert.That(unique.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(unique.IsRetained, Is.True);
        Assert.That(backpack.TryGetPlacement(unique.InstanceId, out _), Is.True);
    }

    [Test]
    public void InvalidLaterPlacementRejectsAllChangesAndCanRetryAfterRepair()
    {
        ItemInstance first = Carried(Definition(2), 0, 0, false);
        ItemInstance second = Carried(Definition(1), 1, 0, false);
        ItemInstance left = SourceItem("unique");
        Assert.That(items.TrySetOwner(second.InstanceId, ItemOwner.Backpack, "wrong-container"), Is.True);
        int notices = 0;
        backpack.Changed += () => notices++;

        Assert.That(Settle(out string error), Is.False);

        Assert.That(error, Is.Not.Null.And.Not.Empty);
        Assert.That(first.IsRetained, Is.False, "Validate the entire snapshot before mutating the first item.");
        Assert.That(left.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(notices, Is.Zero);
        Assert.That(flow.SettledRound, Is.Zero);
        Assert.That(items.TrySetOwner(second.InstanceId, ItemOwner.Backpack, backpack.ContainerId), Is.True);
        Assert.That(Settle(out error), Is.True, error);
        Assert.That(first.IsRetained && second.IsRetained, Is.True);
        Assert.That(notices, Is.EqualTo(1));
    }

    [Test]
    public void OrphanBackpackOwnerCannotSilentlyBypassSettlementValidation()
    {
        ItemInstance first = Carried(Definition(2), 0, 0, false);
        Assert.That(items.TryCreateInstance(Definition(1), ItemOwner.Backpack, backpack.ContainerId,
            "test", "test/orphan", out ItemInstance orphan), Is.True);

        Assert.That(Settle(out string error), Is.False);

        Assert.That(error, Is.Not.Null.And.Not.Empty);
        Assert.That(first.IsRetained || orphan.IsRetained, Is.False);
        Assert.That(SourceItem("unique").Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(flow.SettledRound, Is.Zero);
    }

    [Test]
    public void RoundOverflowIsRejectedBeforeAnyRetainedOrLossMutation()
    {
        ItemInstance carried = Carried(Definition(2), 0, 0, false);
        ItemInstance left = SourceItem("unique");
        Assert.That(items.BeginRound(int.MaxValue), Is.True);

        Assert.That(Settle(out string error), Is.False);

        Assert.That(error, Is.Not.Null.And.Not.Empty);
        Assert.That(carried.IsRetained, Is.False);
        Assert.That(left.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(flow.SettledRound, Is.Zero);
    }

    private bool Settle(out string error)
    {
        MethodInfo method = typeof(GameFlowManager).GetMethod("TrySettleExtraction", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        object[] arguments = { null };
        bool success = (bool)method.Invoke(flow, arguments);
        error = arguments[0] as string;
        return success;
    }

    private void SetFlowPlayer(PlayerController value)
    {
        foreach (FieldInfo field in typeof(GameFlowManager).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (field.FieldType == typeof(PlayerController)) { field.SetValue(flow, value); return; }
        }
        Assert.Fail("GameFlowManager has no runtime player field to bind in the settlement fixture.");
    }

    private LootBox NewBox()
    {
        LootBox result = NewObject("07 stable test source").AddComponent<LootBox>();
        result.mapId = "flow-test";
        result.sourceId = "origin-box";
        result.lootTable = table;
        result.transform.position = Vector3.up + Vector3.right;
        return result;
    }

    private ItemInstance SourceItem(string entryId)
    {
        Assert.That(box.State.TryGetEntry(entryId, out LootEntryState entry), Is.True);
        Assert.That(items.TryGetInstance(entry.InstanceId, out ItemInstance item), Is.True);
        return item;
    }

    private ItemInstance Carried(ItemDataSO definition, int x, int y, bool rotated)
    {
        Assert.That(items.TryCreateInstance(definition, ItemOwner.Backpack, backpack.ContainerId,
            "test-carried", "test-carried/" + definition.definitionId, out ItemInstance item), Is.True);
        Assert.That(backpack.TryAddPlacement(item.InstanceId, x, y, rotated), Is.EqualTo(BackpackResult.Success));
        return item;
    }

    private ItemInstance Stored(ItemDataSO definition)
    {
        Assert.That(items.TryCreateInstance(definition, ItemOwner.Storage, warehouse.ContainerId,
            "test-stored", "test-stored/" + definition.definitionId, out ItemInstance item), Is.True);
        Assert.That(warehouse.TryAddPlacement(item.InstanceId, 0, 0, false), Is.EqualTo(WarehouseResult.Success));
        return item;
    }

    private ItemDataSO Definition(int ev, bool unique = false)
    {
        ItemDataSO definition = NewAsset<ItemDataSO>();
        definition.definitionId = "flow-definition-" + ++serial;
        definition.nameZh = "流程测试物品";
        definition.nameEn = "Flow test item";
        definition.descriptionZh = "独立测试配置";
        definition.descriptionEn = "Independent test configuration";
        definition.nameKey = definition.definitionId + ".name";
        definition.descriptionKey = definition.definitionId + ".description";
        definition.ev = ev;
        definition.isUnique = unique;
        definition.uniqueId = unique ? "unique-" + definition.definitionId : string.Empty;
        return definition;
    }

    private GameObject NewObject(string name)
    {
        GameObject result = new GameObject(name);
        created.Add(result);
        return result;
    }

    private T NewAsset<T>() where T : ScriptableObject
    {
        T result = ScriptableObject.CreateInstance<T>();
        created.Add(result);
        return result;
    }
}
