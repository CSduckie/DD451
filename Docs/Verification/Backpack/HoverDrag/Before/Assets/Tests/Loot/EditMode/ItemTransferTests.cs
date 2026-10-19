using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ItemTransferTests
{
    private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
    private ItemManager items;
    private BackpackManager backpack;
    private WarehouseManager warehouse;
    private PlayerController player;
    private ShelterManager shelter;
    private RejectingItemTransferCoordinator coordinator;
    private int serial;

    [SetUp]
    public void SetUp()
    {
        GameObject module = NewObject("06 real transfer modules");
        items = module.AddComponent<ItemManager>();
        items.BeginRound(1);
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
        Assert.That(shelter.Initialize(), Is.True);
        player = NewObject("06 real player").AddComponent<PlayerController>();
        player.config = NewAsset<PlayerConfigSO>();
        player.health = player.GetComponent<PlayerHealth>();
        player.health.config = player.config;
        Assert.That(player.ResetForExploration(1, Vector3.zero), Is.True);
        player.SetBackpackOpen(true);
        coordinator = module.AddComponent<RejectingItemTransferCoordinator>();
        coordinator.itemManager = items;
        coordinator.backpackManager = backpack;
        coordinator.warehouseManager = warehouse;
        coordinator.shelterManager = shelter;
        coordinator.player = player;
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
    public void BidirectionalTransferPreservesIdentityRetainedUsesAndEv()
    {
        ItemInstance item = Put(true, 0, 0, 1, 2, true);
        items.TrySetRetained(item.InstanceId, true);
        int ev = shelter.RefreshEv();
        string originalSource = item.SourceId;
        string originalSpawn = item.OriginalSpawnPointId;
        int useRound = item.UseRound;
        Assert.That(Move(true, item.InstanceId, 3, 2, true), Is.EqualTo(ItemTransferResult.Success));
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Storage));
        Assert.That(backpack.TryGetPlacement(item.InstanceId, out _), Is.False);
        Assert.That(warehouse.TryGetPlacement(item.InstanceId, out WarehousePlacement stored), Is.True);
        Assert.That(stored.Item, Is.SameAs(item));
        Assert.That(stored.Rotated, Is.True);
        Assert.That(Move(false, item.InstanceId, 2, 1, false), Is.EqualTo(ItemTransferResult.Success));
        Assert.That(backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement carried), Is.True);
        Assert.That(carried.Item, Is.SameAs(item));
        Assert.That(item.SourceId, Is.EqualTo(originalSource));
        Assert.That(item.OriginalSpawnPointId, Is.EqualTo(originalSpawn));
        Assert.That(item.IsRetained, Is.True);
        Assert.That(item.UseRound, Is.EqualTo(useRound));
        Assert.That(item.UsesThisRound, Is.Zero);
        Assert.That(shelter.CurrentEv, Is.EqualTo(ev));
        Assert.That(items.InstanceCount, Is.EqualTo(1));
    }

    [TestCase(true, 1, ItemTransferResult.Busy)]
    [TestCase(true, 2, ItemTransferResult.SourceRemovalFailed)]
    [TestCase(true, 3, ItemTransferResult.OwnerRejected)]
    [TestCase(false, 1, ItemTransferResult.Busy)]
    [TestCase(false, 2, ItemTransferResult.SourceRemovalFailed)]
    [TestCase(false, 3, ItemTransferResult.OwnerRejected)]
    public void FailureAtEachCommitStepRestoresRealLayouts(bool fromBackpack, int step, ItemTransferResult expected)
    {
        ItemInstance item = Put(fromBackpack, 1, 1, 1, 2, true);
        items.TrySetRetained(item.InstanceId, true);
        ItemOwner owner = item.Owner;
        string container = item.ContainerId;
        int notifications = 0;
        backpack.Changed += () => notifications++;
        warehouse.Changed += () => notifications++;
        coordinator.failStep = step;
        Assert.That(Move(fromBackpack, item.InstanceId, 0, 0, false), Is.EqualTo(expected));
        Assert.That(coordinator.reachedStep, Is.EqualTo(step), "必须到达实际提交点，不能只验证最早拒绝。");
        Assert.That(notifications, Is.Zero);
        Assert.That(item.Owner, Is.EqualTo(owner));
        Assert.That(item.ContainerId, Is.EqualTo(container));
        Assert.That(item.IsRetained, Is.True);
        Assert.That(items.InstanceCount, Is.EqualTo(1));
        if (fromBackpack)
        {
            Assert.That(backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement original), Is.True);
            Assert.That(new[] { original.X, original.Y }, Is.EqualTo(new[] { 1, 1 }));
            Assert.That(original.Rotated, Is.True);
            Assert.That(warehouse.GetPlacements().Count, Is.Zero);
        }
        else
        {
            Assert.That(warehouse.TryGetPlacement(item.InstanceId, out WarehousePlacement original), Is.True);
            Assert.That(new[] { original.X, original.Y }, Is.EqualTo(new[] { 1, 1 }));
            Assert.That(original.Rotated, Is.True);
            Assert.That(backpack.GetPlacements().Count, Is.Zero);
        }
    }

    [Test]
    public void RealItemManagerBusyRejectsOwnerAfterBothLayoutChangesAndRestoresThem()
    {
        ItemInstance moving = Put(true, 0, 0);
        ItemInstance medicine = Put(true, 1, 0);
        HealEffectSO heal = NewAsset<HealEffectSO>();
        heal.amount = 1;
        medicine.Definition.canUse = true;
        medicine.Definition.effects.Add(heal);
        ItemTransferResult result = ItemTransferResult.Success;
        TransferDuringHealTarget health = new TransferDuringHealTarget
        {
            duringHeal = () => result = Move(true, moving.InstanceId, 0, 0, false)
        };
        Assert.That(items.TryUseItem(medicine.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(coordinator.reachedStep, Is.EqualTo(3));
        Assert.That(result, Is.EqualTo(ItemTransferResult.OwnerRejected));
        Assert.That(backpack.TryGetCarriedItem(moving.InstanceId, out _), Is.EqualTo(BackpackResult.Success));
        Assert.That(warehouse.GetPlacements().Count, Is.Zero);
    }

    [Test]
    public void NotificationsOnlyReadCompleteStateAndReentryIsBusy()
    {
        ItemInstance item = Put(true, 0, 0);
        int observed = 0;
        Action verify = () =>
        {
            Assert.That(item.Owner, Is.EqualTo(ItemOwner.Storage));
            Assert.That(backpack.TryGetPlacement(item.InstanceId, out _), Is.False);
            Assert.That(warehouse.TryGetStoredItem(item.InstanceId, out _), Is.EqualTo(WarehouseResult.Success));
            Assert.That(Move(false, item.InstanceId, 0, 0, false), Is.EqualTo(ItemTransferResult.Busy));
            observed++;
        };
        backpack.Changed += verify;
        warehouse.Changed += verify;
        Assert.That(Move(true, item.InstanceId, 0, 0, false), Is.EqualTo(ItemTransferResult.Success));
        Assert.That(observed, Is.EqualTo(2));
        Assert.That(Move(true, item.InstanceId, 1, 1, false), Is.EqualTo(ItemTransferResult.SourceInvalid));
    }

    [Test]
    public void FailedDisplayListenerDoesNotReportCompletedTransferAsFailure()
    {
        ItemInstance item = Put(true, 0, 0);
        coordinator.Completed += id => throw new InvalidOperationException("06 expected display failure");
        LogAssert.Expect(LogType.Exception, "InvalidOperationException: 06 expected display failure");
        Assert.That(Move(true, item.InstanceId, 0, 0, false), Is.EqualTo(ItemTransferResult.Success));
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Storage));
    }

    [Test]
    public void StalePermissionAndOtherConfirmationCannotCommit()
    {
        ItemInstance item = Put(true, 0, 0);
        Assert.That(coordinator.TryBackpackToWarehouse(item.InstanceId, 0, 0, false), Is.EqualTo(ItemTransferResult.StaleRequest));
        Assert.That(coordinator.TryBackpackToWarehouse(item.InstanceId, 0, 0, false, null, () => false), Is.EqualTo(ItemTransferResult.StaleRequest));
        GameObject other = NewObject("pending abandonment");
        Assert.That(warehouse.TryClaimInteraction(other), Is.True);
        Assert.That(Move(true, item.InstanceId, 0, 0, false), Is.EqualTo(ItemTransferResult.Busy));
        warehouse.ReleaseInteraction(other);
        warehouse.SetShelterContext(false, other.scene);
        Assert.That(Move(true, item.InstanceId, 0, 0, false), Is.EqualTo(ItemTransferResult.NotAllowed));
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
    }

    [Test]
    public void ExistingWarehouseDragClaimCanComplete()
    {
        ItemInstance item = Put(false, 0, 0);
        Assert.That(warehouse.TryClaimInteraction(coordinator), Is.True);
        Assert.That(coordinator.TryWarehouseToBackpack(item.InstanceId, 0, 0, false, coordinator, () => true), Is.EqualTo(ItemTransferResult.Success));
        Assert.That(warehouse.OwnsInteraction(coordinator), Is.True, "已有来源UI在结束拖拽时负责释放。");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ShelterAllowsUnpreparedOrAbsentPlayerWithoutGrantingExploration(bool absent)
    {
        ItemInstance item = Put(true, 0, 0);
        PlayerController shelterPlayer = absent ? null : NewObject("unprepared shelter player").AddComponent<PlayerController>();
        if (shelterPlayer != null)
        {
            // EditMode不会执行Awake，显式接回真实RequiredComponent，不准备探索或补血。
            shelterPlayer.inputReader = shelterPlayer.GetComponent<PlayerInputReader>();
            shelterPlayer.interaction = shelterPlayer.GetComponent<PlayerInteraction>();
            shelterPlayer.health = shelterPlayer.GetComponent<PlayerHealth>();
            Assert.That(shelterPlayer.IsPrepared, Is.False);
            shelterPlayer.SetBackpackOpen(true);
        }
        coordinator.player = shelterPlayer;
        Assert.That(Move(true, item.InstanceId, 0, 0), Is.EqualTo(ItemTransferResult.Success));
        Assert.That(Move(false, item.InstanceId, 0, 0), Is.EqualTo(ItemTransferResult.Success));
        if (shelterPlayer != null) { Assert.That(shelterPlayer.IsPrepared, Is.False); }
        warehouse.SetShelterContext(false, warehouse.gameObject.scene);
        Assert.That(Move(true, item.InstanceId, 0, 0), Is.EqualTo(ItemTransferResult.NotAllowed));
        Assert.That(coordinator.TryDropFromBackpack(item.InstanceId, () => true), Is.EqualTo(ItemTransferResult.NotAllowed));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InteractionClaimNotificationCannotCommitCancelledRequest(bool closePlayer)
    {
        ItemInstance item = Put(true, 0, 0);
        bool valid = true;
        warehouse.ContextChanged += () =>
        {
            if (!warehouse.HasPendingInteraction) { return; }
            if (closePlayer) { player.SetBackpackOpen(false); }
            else { valid = false; }
        };
        ItemTransferResult result = coordinator.TryBackpackToWarehouse(item.InstanceId, 0, 0, false, null, () => valid);
        Assert.That(result, Is.EqualTo(closePlayer ? ItemTransferResult.NotAllowed : ItemTransferResult.StaleRequest));
        Assert.That(coordinator.reachedStep, Is.Zero, "同步取消后不能开始静默写入。");
        Assert.That(backpack.TryGetCarriedItem(item.InstanceId, out _), Is.EqualTo(BackpackResult.Success));
        Assert.That(warehouse.GetPlacements().Count, Is.Zero);
        Assert.That(warehouse.HasPendingInteraction, Is.False);
    }

    [Test]
    public void BoundsOverlapAndFragmentedFreeAreaKeepSource()
    {
        ItemInstance item = Put(false, 0, 0, 2, 2);
        for (int y = 0; y < 4; y++) { Put(true, 1, y); }
        Assert.That(Move(false, item.InstanceId, -1, 0, false), Is.EqualTo(ItemTransferResult.OutOfBounds));
        Assert.That(Move(false, item.InstanceId, 0, 0, false), Is.EqualTo(ItemTransferResult.Occupied));
        for (int y = 0; y < 4; y++) { Put(true, 3, y); }
        Assert.That(backpack.GetPlacements().Count, Is.EqualTo(8));
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                Assert.That(Move(false, item.InstanceId, x, y, false), Is.EqualTo(ItemTransferResult.Occupied));
            }
        }
        Assert.That(warehouse.TryGetStoredItem(item.InstanceId, out _), Is.EqualTo(WarehouseResult.Success));
    }

    [Test]
    public void DeadOrClosedPlayerCannotUseAnOldRequest()
    {
        ItemInstance item = Put(true, 0, 0);
        player.SetBackpackOpen(false);
        Assert.That(Move(true, item.InstanceId, 0, 0, false), Is.EqualTo(ItemTransferResult.NotAllowed));
        player.SetBackpackOpen(true);
        player.health.TakeDamage(player.health.MaxHealth);
        Assert.That(Move(true, item.InstanceId, 0, 0, false), Is.EqualTo(ItemTransferResult.NotAllowed));
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DiscardWithoutLootRuntimePublishesCompleteStateAndRejectsReentry(bool useLegacyDropEntry)
    {
        ItemInstance item = Put(true, 1, 1, 1, 2, true);
        Assert.That(items.TrySetRetained(item.InstanceId, true), Is.True);
        warehouse.SetShelterContext(false, warehouse.gameObject.scene);
        Assert.That(shelter.RefreshEv(), Is.EqualTo(item.Definition.ev));
        Assert.That(coordinator.lootRuntime, Is.Null);
        string source = item.SourceId;
        string origin = item.OriginalSpawnPointId;
        int observed = 0;
        int evNotifications = 0;
        Action verifyCompleteState = () =>
        {
            Assert.That(coordinator.IsBusy, Is.True);
            Assert.That(item.Owner, Is.EqualTo(ItemOwner.Lost));
            Assert.That(backpack.TryGetPlacement(item.InstanceId, out _), Is.False);
            Assert.That(coordinator.TryDiscardFromBackpack(item.InstanceId, () => true), Is.EqualTo(ItemTransferResult.Busy));
            observed++;
        };
        backpack.Changed += verifyCompleteState;
        coordinator.Completed += id =>
        {
            Assert.That(id, Is.EqualTo(item.InstanceId));
            verifyCompleteState();
        };
        shelter.Changed += () =>
        {
            Assert.That(shelter.CurrentEv, Is.Zero);
            Assert.That(coordinator.IsBusy, Is.True);
            Assert.That(coordinator.TryDropFromBackpack(item.InstanceId, () => true), Is.EqualTo(ItemTransferResult.Busy));
            evNotifications++;
        };

        ItemTransferResult result = useLegacyDropEntry
            ? coordinator.TryDropFromBackpack(item.InstanceId, () => true)
            : coordinator.TryDiscardFromBackpack(item.InstanceId, () => true);
        Assert.That(result, Is.EqualTo(ItemTransferResult.Success));
        Assert.That(observed, Is.EqualTo(2));
        Assert.That(evNotifications, Is.EqualTo(1));
        Assert.That(coordinator.IsBusy, Is.False);
        Assert.That(item.ContainerId, Is.Empty);
        Assert.That(item.IsRetained || item.SpawnClaimed, Is.False);
        Assert.That(item.LastLossReason, Is.EqualTo(ItemLossReason.BackpackDiscard));
        Assert.That(item.RecoveryRound, Is.Zero);
        Assert.That(item.SourceId, Is.EqualTo(source));
        Assert.That(item.OriginalSpawnPointId, Is.EqualTo(origin));
        Assert.That(item.UseRound, Is.EqualTo(1));
        Assert.That(item.UsesThisRound, Is.Zero);
        Assert.That(items.CurrentRound, Is.EqualTo(1));
        Assert.That(items.InstanceCount, Is.EqualTo(1));
        Assert.That(items.TryGetInstance(item.InstanceId, out ItemInstance saved), Is.True);
        Assert.That(saved, Is.SameAs(item));
        Assert.That(coordinator.TryDiscardFromBackpack(item.InstanceId, () => true), Is.EqualTo(ItemTransferResult.SourceInvalid));
        Assert.That(coordinator.TryDropFromBackpack(item.InstanceId, () => true), Is.EqualTo(ItemTransferResult.SourceInvalid));
        Assert.That(observed, Is.EqualTo(2));
    }

    [TestCase("missing-request", ItemTransferResult.StaleRequest)]
    [TestCase("stale-request", ItemTransferResult.StaleRequest)]
    [TestCase("closed-player", ItemTransferResult.NotAllowed)]
    [TestCase("dead-player", ItemTransferResult.NotAllowed)]
    [TestCase("shelter", ItemTransferResult.NotAllowed)]
    [TestCase("inactive-exploration", ItemTransferResult.NotAllowed)]
    [TestCase("disabled-runtime", ItemTransferResult.NotReady)]
    public void InvalidDiscardRequestCannotRemovePlacement(string invalidReason, ItemTransferResult expected)
    {
        ItemInstance item = Put(true, 1, 1, 1, 2, true);
        Assert.That(items.TrySetRetained(item.InstanceId, true), Is.True);
        if (invalidReason != "shelter") { warehouse.SetShelterContext(false, warehouse.gameObject.scene); }
        if (invalidReason == "closed-player") { player.SetBackpackOpen(false); }
        if (invalidReason == "dead-player") { player.health.TakeDamage(player.health.MaxHealth); }
        if (invalidReason == "inactive-exploration" || invalidReason == "disabled-runtime")
        {
            LootRuntimeManager runtime = NewObject("discard runtime permission").AddComponent<LootRuntimeManager>();
            runtime.itemManager = items;
            runtime.SetExplorationContext("discard-map", false);
            coordinator.lootRuntime = runtime;
            if (invalidReason == "disabled-runtime") { runtime.enabled = false; }
        }
        int notifications = 0;
        backpack.Changed += () => notifications++;
        Func<bool> request = () => invalidReason != "stale-request";
        if (invalidReason == "missing-request") { request = null; }
        Assert.That(coordinator.TryDiscardFromBackpack(item.InstanceId, request), Is.EqualTo(expected));
        Assert.That(coordinator.reachedStep, Is.Zero);
        Assert.That(backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement placement), Is.True);
        Assert.That(placement.Item, Is.SameAs(item));
        Assert.That(new[] { placement.X, placement.Y }, Is.EqualTo(new[] { 1, 1 }));
        Assert.That(placement.Rotated, Is.True);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(item.ContainerId, Is.EqualTo(backpack.ContainerId));
        Assert.That(item.IsRetained, Is.True);
        Assert.That(item.LastLossReason, Is.Null);
        Assert.That(items.InstanceCount, Is.EqualTo(1));
        Assert.That(notifications, Is.Zero);
    }

    [Test]
    public void RealItemManagerBusyRejectsDiscardAndRestoresPlacement()
    {
        ItemInstance discarded = Put(true, 0, 0, 1, 2, true);
        ItemInstance medicine = Put(true, 2, 0);
        Assert.That(items.TrySetRetained(discarded.InstanceId, true), Is.True);
        warehouse.SetShelterContext(false, warehouse.gameObject.scene);
        HealEffectSO heal = NewAsset<HealEffectSO>();
        heal.amount = 1;
        medicine.Definition.canUse = true;
        medicine.Definition.effects.Add(heal);
        ItemTransferResult result = ItemTransferResult.Success;
        TransferDuringHealTarget health = new TransferDuringHealTarget
        {
            duringHeal = () => result = coordinator.TryDiscardFromBackpack(discarded.InstanceId, () => true)
        };
        int notifications = 0;
        backpack.Changed += () => notifications++;
        coordinator.Completed += id => notifications++;
        Assert.That(items.TryUseItem(medicine.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(coordinator.reachedStep, Is.EqualTo(3));
        Assert.That(result, Is.EqualTo(ItemTransferResult.OwnerRejected));
        Assert.That(backpack.TryGetCarriedItem(discarded.InstanceId, out ItemInstance saved), Is.EqualTo(BackpackResult.Success));
        Assert.That(saved, Is.SameAs(discarded));
        Assert.That(backpack.TryGetPlacement(discarded.InstanceId, out BackpackPlacement placement), Is.True);
        Assert.That(new[] { placement.X, placement.Y }, Is.EqualTo(new[] { 0, 0 }));
        Assert.That(placement.Rotated, Is.True);
        Assert.That(discarded.IsRetained, Is.True);
        Assert.That(discarded.LastLossReason, Is.Null);
        Assert.That(discarded.RecoveryRound, Is.Zero);
        Assert.That(items.InstanceCount, Is.EqualTo(2));
        Assert.That(notifications, Is.Zero);
    }

    private ItemTransferResult Move(bool fromBackpack, string id, int x, int y, bool rotated = false)
    {
        return fromBackpack
            ? coordinator.TryBackpackToWarehouse(id, x, y, rotated, null, () => true)
            : coordinator.TryWarehouseToBackpack(id, x, y, rotated, null, () => true);
    }

    private ItemInstance Put(bool inBackpack, int x, int y, int width = 1, int height = 1, bool rotated = false)
    {
        ItemDataSO definition = NewAsset<ItemDataSO>();
        definition.definitionId = "06.test." + ++serial;
        definition.nameZh = "测试物品";
        definition.nameEn = "Test item";
        definition.descriptionZh = "用于转移验证。";
        definition.descriptionEn = "Used by transfer tests.";
        definition.nameKey = "items.test.name";
        definition.descriptionKey = "items.test.description";
        definition.width = width;
        definition.height = height;
        definition.canRotate = true;
        definition.ev = 2;
        Assert.That(items.TryCreateInstance(definition, inBackpack ? ItemOwner.Backpack : ItemOwner.Storage,
            inBackpack ? backpack.ContainerId : warehouse.ContainerId, "map/original", "map/original/entry", out ItemInstance item), Is.True);
        if (inBackpack) { Assert.That(backpack.TryAddPlacement(item.InstanceId, x, y, rotated), Is.EqualTo(BackpackResult.Success)); }
        else { Assert.That(warehouse.TryAddPlacement(item.InstanceId, x, y, rotated), Is.EqualTo(WarehouseResult.Success)); }
        return item;
    }

    private GameObject NewObject(string name)
    {
        GameObject value = new GameObject(name);
        created.Add(value);
        return value;
    }

    private T NewAsset<T>() where T : ScriptableObject
    {
        T value = ScriptableObject.CreateInstance<T>();
        created.Add(value);
        return value;
    }

    private sealed class TransferDuringHealTarget : IHealthTarget
    {
        public Action duringHeal;
        public int CurrentHealth { get; private set; } = 2;
        public int MaxHealth { get { return 3; } }
        public void Heal(int amount)
        {
            duringHeal();
            CurrentHealth = Math.Min(MaxHealth, CurrentHealth + amount);
        }
    }
}
