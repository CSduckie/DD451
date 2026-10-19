using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

public class WarehouseShelterTests
{
    private readonly List<Object> createdObjects = new List<Object>();
    private ItemManager items;
    private WarehouseManager warehouse;
    private ShelterManager shelter;
    private int nextDefinition;

    [SetUp]
    public void SetUp()
    {
        GameObject root = CreateObject("仓库与庇护所真实模块测试");
        items = root.AddComponent<ItemManager>();
        warehouse = root.AddComponent<WarehouseManager>();
        warehouse.config = CreateAsset<WarehouseConfigSO>();
        Assert.That(warehouse.Initialize(items), Is.True);
        warehouse.SetShelterContext(true, SceneManager.GetActiveScene());
        shelter = root.AddComponent<ShelterManager>();
        shelter.itemManager = items;
        shelter.warehouseManager = warehouse;
        shelter.firstUpgrade = CreateAsset<ShelterUpgradeSO>();
        Assert.That(shelter.Initialize(), Is.True);
        Assert.That(items.BeginRound(1), Is.True);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null) { Object.DestroyImmediate(createdObjects[i]); }
        }
        createdObjects.Clear();
    }

    [Test]
    public void InternalOperationsRequireContextOwnershipContainerAndRealPlacement()
    {
        ItemInstance item = CreateItem(5, ItemOwner.Storage, true, true);
        Assert.That(warehouse.TryMove(item.InstanceId, 1, 1, false), Is.EqualTo(WarehouseResult.MissingPlacement));
        Assert.That(warehouse.TryAbandon(item.InstanceId), Is.EqualTo(WarehouseResult.MissingPlacement));
        Assert.That(warehouse.TryAddPlacement(item.InstanceId, 0, 0, false), Is.EqualTo(WarehouseResult.Success));
        Assert.That(items.TrySetOwner(item.InstanceId, ItemOwner.Storage, "other-warehouse"), Is.True);
        Assert.That(warehouse.TryMove(item.InstanceId, 1, 1, false), Is.EqualTo(WarehouseResult.WrongContainer));
        Assert.That(warehouse.TryAbandon(item.InstanceId), Is.EqualTo(WarehouseResult.WrongContainer));
        Assert.That(items.TrySetOwner(item.InstanceId, ItemOwner.Backpack, "player-backpack"), Is.True);
        Assert.That(warehouse.TryMove(item.InstanceId, 1, 1, false), Is.EqualTo(WarehouseResult.WrongOwner));
        Assert.That(warehouse.TryAbandon(item.InstanceId), Is.EqualTo(WarehouseResult.WrongOwner));
        Assert.That(items.TrySetOwner(item.InstanceId, ItemOwner.Storage, warehouse.ContainerId), Is.True);
        warehouse.SetShelterContext(false, SceneManager.GetActiveScene());
        Assert.That(warehouse.TryMove(item.InstanceId, 1, 1, false), Is.EqualTo(WarehouseResult.NotInShelter));
        Assert.That(warehouse.TryAbandon(item.InstanceId), Is.EqualTo(WarehouseResult.NotInShelter));
        Assert.That(warehouse.GetPlacements()[0].X, Is.Zero);
        Assert.That(item.IsRetained, Is.True);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Storage));
    }

    [Test]
    public void ExternalPreviewAndSilentLayoutEditsPreserveAuthorityUntilCoordinatorCommits()
    {
        ItemInstance item = CreateItem(5, ItemOwner.Container, false);
        int notifications = 0;
        warehouse.Changed += () => notifications++;
        string container = item.ContainerId;
        Assert.That(warehouse.CanPlaceExternal(item.InstanceId, 0, 0, false), Is.EqualTo(WarehouseResult.Success));
        Assert.That(warehouse.CanPlaceExternal(item.InstanceId, 20, 0, false), Is.EqualTo(WarehouseResult.OutOfBounds));
        Assert.That(warehouse.GetPlacements().Count, Is.Zero);
        Assert.That(warehouse.TryAddPlacement(item.InstanceId, 0, 0, false), Is.EqualTo(WarehouseResult.Success));
        Assert.That(warehouse.TryAddPlacement(item.InstanceId, 2, 2, false), Is.EqualTo(WarehouseResult.DuplicateInstance));
        Assert.That(warehouse.TryRemovePlacement(item.InstanceId, out WarehousePlacement removed), Is.EqualTo(WarehouseResult.Success));
        Assert.That(warehouse.TryRestorePlacement(removed), Is.EqualTo(WarehouseResult.Success));
        Assert.That(notifications, Is.Zero);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(item.ContainerId, Is.EqualTo(container));
        Assert.That(item.IsRetained, Is.False);
        Assert.That(shelter.RefreshEv(), Is.Zero);
        // 这里只验证06可用的静默入口，不把摆放测试写成真实背包转仓联测。
    }

    [Test]
    public void EvCountsRetainedRegistryOnceAcrossBackpackStorageAndTemporaryWorldOwner()
    {
        ItemInstance doll = CreateItem(5, ItemOwner.Backpack, true, true);
        ItemInstance medkit = CreateItem(2, ItemOwner.Backpack, true, true);
        CreateItem(50, ItemOwner.Storage, false);
        Assert.That(shelter.RefreshEv(), Is.EqualTo(7));
        Assert.That(items.TrySetOwner(doll.InstanceId, ItemOwner.Storage, warehouse.ContainerId), Is.True);
        Assert.That(warehouse.TryAddPlacement(doll.InstanceId, 0, 0, false), Is.EqualTo(WarehouseResult.Success));
        warehouse.NotifyChanged();
        warehouse.NotifyChanged();
        Assert.That(shelter.RefreshEv(), Is.EqualTo(7));
        Assert.That(shelter.RefreshEv(), Is.EqualTo(7));
        Assert.That(items.TrySetOwner(medkit.InstanceId, ItemOwner.World, "temporary-ground"), Is.True);
        Assert.That(shelter.RefreshEv(), Is.EqualTo(7), "01临时丢地仍保有，05不能仅按Owner扣分。");
        Assert.That(items.RecordLoss(medkit.InstanceId, ItemLossReason.LeftBehindOnExtraction), Is.True);
        Assert.That(shelter.RefreshEv(), Is.EqualTo(5));
        Assert.That(items.RecordLoss(doll.InstanceId, ItemLossReason.ExplorationDeath), Is.True);
        Assert.That(shelter.RefreshEv(), Is.Zero);
    }

    [Test]
    public void EvSixCannotUpgradeAndSevenNeverAutoActivates()
    {
        ItemInstance item = CreateItem(6, ItemOwner.Backpack, true);
        Assert.That(shelter.RefreshEv(), Is.EqualTo(6));
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.InsufficientEv));
        Assert.That(shelter.IsUpgradeActivated, Is.False);
        Assert.That(warehouse.Width, Is.EqualTo(20));
        item.Definition.ev = 7;
        Assert.That(shelter.RefreshEv(), Is.EqualTo(7));
        Assert.That(shelter.GetUpgradeAvailability(), Is.EqualTo(ShelterUpgradeResult.Success));
        Assert.That(shelter.IsUpgradeActivated, Is.False);
        Assert.That(warehouse.Width, Is.EqualTo(20));
    }

    [Test]
    public void UpgradeRechecksAuthoritativeEvInsteadOfTrustingStaleButton()
    {
        ItemInstance doll = CreateItem(5, ItemOwner.Backpack, true, true);
        CreateItem(2, ItemOwner.Backpack, true, true);
        Assert.That(shelter.RefreshEv(), Is.EqualTo(7));
        Assert.That(shelter.GetUpgradeAvailability(), Is.EqualTo(ShelterUpgradeResult.Success));
        Assert.That(items.RecordLoss(doll.InstanceId, ItemLossReason.ExplorationDeath), Is.True);
        Assert.That(shelter.CurrentEv, Is.EqualTo(7), "刻意制造尚未收到完整操作刷新通知的旧按钮状态。");
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.InsufficientEv));
        Assert.That(shelter.CurrentEv, Is.EqualTo(2));
        Assert.That(shelter.IsUpgradeActivated, Is.False);
        Assert.That(warehouse.Width, Is.EqualTo(20));
    }

    [Test]
    public void FreeUpgradePreservesUsedRetainedItemPlacementIdentityAndNotifiesAfterActivation()
    {
        ItemInstance doll = CreateItem(5, ItemOwner.Storage, true, true);
        ItemInstance medkit = CreateItem(2, ItemOwner.Backpack, true, true, true);
        ItemsTestHealth health = CreateObject("已有测试生命替身").AddComponent<ItemsTestHealth>();
        Assert.That(items.TryUseItem(medkit.InstanceId, health), Is.EqualTo(ItemUseResult.Success));
        Assert.That(items.TrySetOwner(medkit.InstanceId, ItemOwner.Storage, warehouse.ContainerId), Is.True);
        Assert.That(warehouse.TryAddPlacement(doll.InstanceId, 0, 0, true), Is.EqualTo(WarehouseResult.Success));
        Assert.That(warehouse.TryAddPlacement(medkit.InstanceId, 18, 18, false), Is.EqualTo(WarehouseResult.Success));
        IReadOnlyList<WarehousePlacement> placements = warehouse.GetPlacements();
        string before = ItemsSnapshot();
        int notifications = 0;
        bool sawCompleteState = false;
        warehouse.Changed += () =>
        {
            notifications++;
            sawCompleteState = shelter.IsUpgradeActivated && warehouse.Width == 20 && warehouse.Height == 24;
        };
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.Success));
        Assert.That(shelter.CurrentEv, Is.EqualTo(7));
        Assert.That(shelter.IsUpgradeActivated, Is.True);
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(sawCompleteState, Is.True, "底层扩容不能在标记写入前广播中间状态。");
        Assert.That(ItemsSnapshot(), Is.EqualTo(before));
        CollectionAssert.AreEqual(placements, warehouse.GetPlacements());
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.AlreadyActivated));
        Assert.That(notifications, Is.EqualTo(1));
    }

    [TestCase(0, 24)]
    [TestCase(20, -1)]
    [TestCase(19, 30)]
    [TestCase(20, 19)]
    [TestCase(20, 20)]
    [TestCase(24, 24)]
    public void InvalidUpgradeTargetNeverWritesActivationOrChangesLayout(int width, int height)
    {
        ItemInstance item = CreateItem(7, ItemOwner.Storage, true);
        Assert.That(warehouse.TryAddPlacement(item.InstanceId, 18, 18, false), Is.EqualTo(WarehouseResult.Success));
        Assert.That(shelter.RefreshEv(), Is.EqualTo(7));
        IReadOnlyList<WarehousePlacement> before = warehouse.GetPlacements();
        string instancesBefore = ItemsSnapshot();
        int warehouseNotifications = 0;
        int shelterNotifications = 0;
        warehouse.Changed += () => warehouseNotifications++;
        shelter.Changed += () => shelterNotifications++;
        shelter.firstUpgrade.targetWidth = width;
        shelter.firstUpgrade.targetHeight = height;
        Assert.That(shelter.GetUpgradeAvailability(), Is.EqualTo(ShelterUpgradeResult.InvalidConfiguration));
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.InvalidConfiguration));
        Assert.That(shelter.IsUpgradeActivated, Is.False);
        Assert.That(warehouse.Width, Is.EqualTo(20));
        Assert.That(warehouse.Height, Is.EqualTo(20));
        CollectionAssert.AreEqual(before, warehouse.GetPlacements());
        Assert.That(ItemsSnapshot(), Is.EqualTo(instancesBefore));
        Assert.That(warehouseNotifications, Is.Zero);
        Assert.That(shelterNotifications, Is.Zero);
    }

    [Test]
    public void NonDefaultWarehouseWidthCanUpgradeOnlyByAddingRows()
    {
        GameObject root = CreateObject("非默认列数的纵向扩容测试");
        WarehouseManager customWarehouse = root.AddComponent<WarehouseManager>();
        customWarehouse.config = CreateAsset<WarehouseConfigSO>();
        customWarehouse.config.width = 12;
        customWarehouse.config.height = 10;
        Assert.That(customWarehouse.Initialize(items), Is.True);
        customWarehouse.SetShelterContext(true, SceneManager.GetActiveScene());
        ShelterManager customShelter = root.AddComponent<ShelterManager>();
        customShelter.itemManager = items;
        customShelter.warehouseManager = customWarehouse;
        customShelter.firstUpgrade = CreateAsset<ShelterUpgradeSO>();
        customShelter.firstUpgrade.targetWidth = 12;
        customShelter.firstUpgrade.targetHeight = 14;
        Assert.That(customShelter.Initialize(), Is.True);
        ItemInstance item = CreateItem(7, ItemOwner.Storage, true, true);
        Assert.That(customWarehouse.TryAddPlacement(item.InstanceId, 10, 8, false), Is.EqualTo(WarehouseResult.Success));
        IReadOnlyList<WarehousePlacement> before = customWarehouse.GetPlacements();
        string instancesBefore = ItemsSnapshot();
        Assert.That(customShelter.RefreshEv(), Is.EqualTo(7));
        Assert.That(customShelter.GetUpgradeAvailability(), Is.EqualTo(ShelterUpgradeResult.Success));
        Assert.That(customShelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.Success));
        Assert.That(customWarehouse.Width, Is.EqualTo(12), "纵向扩容应比较实际列数，不能硬编码默认20列。");
        Assert.That(customWarehouse.Height, Is.EqualTo(14));
        Assert.That(customShelter.IsUpgradeActivated, Is.True);
        Assert.That(customShelter.CurrentEv, Is.EqualTo(7));
        CollectionAssert.AreEqual(before, customWarehouse.GetPlacements());
        Assert.That(ItemsSnapshot(), Is.EqualTo(instancesBefore));
        Assert.That(customWarehouse.TryMove(item.InstanceId, 10, 12, false), Is.EqualTo(WarehouseResult.Success));
        Assert.That(customWarehouse.TryGetPlacement(item.InstanceId, out WarehousePlacement moved), Is.True);
        Assert.That(moved.Item, Is.SameAs(item));
        Assert.That(moved.X, Is.EqualTo(10));
        Assert.That(moved.Y, Is.EqualTo(12));
        Assert.That(ItemsSnapshot(), Is.EqualTo(instancesBefore));
    }

    [Test]
    public void MissingDependenciesWrongRegistryContextAndPendingInteractionRejectUpgrade()
    {
        CreateItem(7, ItemOwner.Backpack, true);
        shelter.firstUpgrade = null;
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.InvalidConfiguration));
        shelter.firstUpgrade = CreateAsset<ShelterUpgradeSO>();
        shelter.itemManager = null;
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.NotReady));
        shelter.itemManager = CreateObject("另一个物品登记表").AddComponent<ItemManager>();
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.NotReady));
        shelter.itemManager = items;
        shelter.warehouseManager = null;
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.NotReady));
        WarehouseManager otherWarehouse = CreateObject("另一个仓库运行布局").AddComponent<WarehouseManager>();
        otherWarehouse.config = warehouse.config;
        Assert.That(otherWarehouse.Initialize(items), Is.True);
        shelter.warehouseManager = otherWarehouse;
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.NotReady));
        shelter.warehouseManager = warehouse;
        warehouse.SetShelterContext(false, SceneManager.GetActiveScene());
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.NotInShelter));
        warehouse.SetShelterContext(true, SceneManager.GetActiveScene());
        GameObject interactionOwner = CreateObject("待提交拖拽或确认框");
        Assert.That(warehouse.TryClaimInteraction(interactionOwner), Is.True);
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.Busy));
        warehouse.ReleaseInteraction(interactionOwner);
        Assert.That(shelter.IsUpgradeActivated, Is.False);
        Assert.That(warehouse.Width, Is.EqualTo(20));
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.Success));
    }

    [Test]
    public void ActivationSurvivesEvLossReinitializeAndConfigurationEditsDuringRun()
    {
        ItemInstance doll = CreateItem(5, ItemOwner.Backpack, true, true);
        ItemInstance medkit = CreateItem(2, ItemOwner.Backpack, true, true);
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.Success));
        Assert.That(items.RecordLoss(doll.InstanceId, ItemLossReason.ExplorationDeath), Is.True);
        Assert.That(shelter.RefreshEv(), Is.EqualTo(2));
        Assert.That(items.RecordLoss(medkit.InstanceId, ItemLossReason.ExplorationDeath), Is.True);
        Assert.That(shelter.RefreshEv(), Is.Zero);
        warehouse.config.width = 2;
        warehouse.config.height = 3;
        shelter.firstUpgrade.targetWidth = 40;
        shelter.firstUpgrade.targetHeight = 40;
        Assert.That(warehouse.Initialize(items), Is.True);
        Assert.That(shelter.Initialize(), Is.True);
        Assert.That(shelter.IsUpgradeActivated, Is.True);
        Assert.That(warehouse.Width, Is.EqualTo(20));
        Assert.That(warehouse.Height, Is.EqualTo(24));
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.AlreadyActivated));
    }

    [Test]
    public void ReinitializationDoesNotDuplicateEligibilityNotificationsAndOnlyCurrentOwnerCanReleaseBusyState()
    {
        ItemInstance item = CreateItem(7, ItemOwner.Backpack, true);
        Assert.That(shelter.RefreshEv(), Is.EqualTo(7));
        List<ShelterUpgradeResult> observed = new List<ShelterUpgradeResult>();
        shelter.Changed += () => observed.Add(shelter.GetUpgradeAvailability());
        for (int i = 0; i < 3; i++) { Assert.That(shelter.Initialize(), Is.True); }
        GameObject owner = CreateObject("实际拖拽来源");
        GameObject other = CreateObject("其他界面来源");
        Assert.That(warehouse.TryClaimInteraction(owner), Is.True);
        Assert.That(warehouse.TryClaimInteraction(owner), Is.True);
        Assert.That(warehouse.TryClaimInteraction(other), Is.False);
        warehouse.ReleaseInteraction(other);
        CollectionAssert.AreEqual(new[] { ShelterUpgradeResult.Busy }, observed);
        warehouse.ReleaseInteraction(owner);
        warehouse.SetShelterContext(false, SceneManager.GetActiveScene());
        warehouse.SetShelterContext(true, SceneManager.GetActiveScene());
        Assert.That(items.RecordLoss(item.InstanceId, ItemLossReason.ExplorationDeath), Is.True);
        Assert.That(shelter.RefreshEv(), Is.Zero);
        CollectionAssert.AreEqual(new[]
        {
            ShelterUpgradeResult.Busy, ShelterUpgradeResult.Success, ShelterUpgradeResult.NotInShelter,
            ShelterUpgradeResult.Success, ShelterUpgradeResult.InsufficientEv
        }, observed);
        Assert.That(shelter.IsUpgradeActivated, Is.False);
        Assert.That(warehouse.Width, Is.EqualTo(20));
    }

    [Test]
    public void ConfirmedAbandonmentCleansLayoutBeforeOneNotificationAndKeepsUpgrade()
    {
        ItemInstance doll = CreateItem(5, ItemOwner.Storage, true, true);
        CreateItem(2, ItemOwner.Backpack, true, true);
        Assert.That(warehouse.TryAddPlacement(doll.InstanceId, 1, 2, true), Is.EqualTo(WarehouseResult.Success));
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.Success));
        int notifications = 0;
        bool complete = false;
        warehouse.Changed += () =>
        {
            notifications++;
            complete = doll.Owner == ItemOwner.PendingRecovery && !doll.IsRetained
                && !warehouse.TryGetPlacement(doll.InstanceId, out _);
        };
        Assert.That(warehouse.TryAbandon(doll.InstanceId), Is.EqualTo(WarehouseResult.Success));
        Assert.That(complete, Is.True);
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(shelter.CurrentEv, Is.EqualTo(2));
        Assert.That(doll.LastLossReason, Is.EqualTo(ItemLossReason.ShelterAbandonment));
        Assert.That(doll.RecoveryRound, Is.EqualTo(2));
        Assert.That(items.TryGetUniqueInstance(doll.UniqueId, out ItemInstance sameIdentity), Is.True);
        Assert.That(sameIdentity, Is.SameAs(doll));
        Assert.That(warehouse.TryAbandon(doll.InstanceId), Is.Not.EqualTo(WarehouseResult.Success));
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(shelter.CurrentEv, Is.EqualTo(2));
        Assert.That(shelter.IsUpgradeActivated, Is.True);
        Assert.That(warehouse.Width, Is.EqualTo(20));
        Assert.That(warehouse.Height, Is.EqualTo(24));
    }

    [Test]
    public void RejectedAuthoritativeLossKeepsPlacementAndRetainedState()
    {
        ItemInstance item = CreateItem(5, ItemOwner.Storage, true, true);
        Assert.That(warehouse.TryAddPlacement(item.InstanceId, 1, 2, true), Is.EqualTo(WarehouseResult.Success));
        IReadOnlyList<WarehousePlacement> before = warehouse.GetPlacements();
        Assert.That(items.BeginRound(int.MaxValue), Is.True);
        int notifications = 0;
        warehouse.Changed += () => notifications++;
        Assert.That(warehouse.TryAbandon(item.InstanceId), Is.EqualTo(WarehouseResult.LossRejected));
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Storage));
        Assert.That(item.IsRetained, Is.True);
        CollectionAssert.AreEqual(before, warehouse.GetPlacements());
        Assert.That(notifications, Is.Zero);
    }

    private ItemInstance CreateItem(int ev, ItemOwner owner, bool retained, bool unique = false, bool usable = false)
    {
        ItemDataSO definition = CreateAsset<ItemDataSO>();
        definition.definitionId = "warehouse-shelter-" + nextDefinition++;
        definition.nameZh = "测试物品";
        definition.nameEn = "Test item";
        definition.descriptionZh = "仓库与庇护所测试物品";
        definition.descriptionEn = "Warehouse and shelter test item";
        definition.nameKey = definition.definitionId + ".name";
        definition.descriptionKey = definition.definitionId + ".description";
        definition.width = 2;
        definition.height = 2;
        definition.ev = ev;
        definition.isUnique = unique;
        definition.uniqueId = unique ? "story." + definition.definitionId : "";
        definition.canUse = usable;
        if (usable)
        {
            HealEffectSO effect = CreateAsset<HealEffectSO>();
            effect.healToFull = true;
            definition.effects.Add(effect);
            definition.usesPerRound = 1;
        }
        string container = owner == ItemOwner.Storage ? warehouse.ContainerId : "test-source";
        Assert.That(items.TryCreateInstance(definition, owner, container, "test", unique ? "original-spawn" : "",
            out ItemInstance item), Is.True);
        if (retained) { Assert.That(items.TrySetRetained(item.InstanceId, true), Is.True); }
        return item;
    }

    private string ItemsSnapshot()
    {
        List<string> records = new List<string>();
        foreach (ItemInstance item in items.GetInstances())
        {
            records.Add(item.InstanceId + ":" + item.DefinitionId + ":" + item.UniqueId + ":" + item.Owner
                + ":" + item.ContainerId + ":" + item.IsRetained + ":" + item.UseRound + ":" + item.UsesThisRound
                + ":" + item.RecoveryRound + ":" + item.LastLossReason + ":" + item.OriginalSpawnPointId);
        }
        records.Sort(System.StringComparer.Ordinal);
        return items.CurrentRound + "|" + items.InstanceCount + "|" + string.Join(";", records);
    }

    private GameObject CreateObject(string name)
    {
        GameObject created = new GameObject(name);
        createdObjects.Add(created);
        return created;
    }

    private T CreateAsset<T>() where T : ScriptableObject
    {
        T created = ScriptableObject.CreateInstance<T>();
        createdObjects.Add(created);
        return created;
    }
}
