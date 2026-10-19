using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class LootSourceStateTests
{
    private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
    private ItemManager items;
    private LootRuntimeManager runtime;
    private string mapId;

    [SetUp]
    public void SetUp()
    {
        mapId = "loot-state-test-" + Guid.NewGuid().ToString("N");
        GameObject root = Object("LootStateTestRuntime");
        items = root.AddComponent<ItemManager>();
        runtime = root.AddComponent<LootRuntimeManager>();
        runtime.itemManager = items;
        Assert.That(items.BeginRound(1), Is.True);
        runtime.SetExplorationContext(mapId, true);
    }

    [TearDown]
    public void TearDown()
    {
        // 动态丢地表现不是测试配置对象，先清理它们，再移除测试运行模块。
        foreach (GroundItem ground in UnityEngine.Object.FindObjectsByType<GroundItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (ground.IsRuntimePresentation && ground.runtimeManager == runtime) { UnityEngine.Object.DestroyImmediate(ground.gameObject); }
        }
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null) { UnityEngine.Object.DestroyImmediate(created[i]); }
        }
        created.Clear();
    }

    [Test]
    public void RevealedPickupDoesNotResetTheNextSearchAndInterruptOnlyResetsUnfinishedProgress()
    {
        LootBox box = Box("box", 1f, 3f, 0f);
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        LootSourceState state = box.State;
        Assert.That(state.AdvanceSearch(2f, items), Is.True);
        Assert.That(state.Entries[0].Revealed, Is.True);
        Assert.That(state.Entries[1].Revealed, Is.False);
        Assert.That(state.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(state.SearchElapsed, Is.EqualTo(1f));
        Assert.That(runtime.TryMarkTaken(state.SourceId, "entry-0"), Is.True);
        Assert.That(items.TrySetOwner(state.Entries[0].InstanceId, ItemOwner.Backpack, "pack"), Is.True);
        Assert.That(state.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(state.SearchElapsed, Is.EqualTo(1f));
        state.InterruptSearch();
        Assert.That(state.Entries[0].Revealed, Is.True);
        Assert.That(state.Entries[0].Taken, Is.True);
        Assert.That(state.SearchElapsed, Is.Zero);
        Assert.That(state.AdvanceSearch(2.9f, items), Is.False);
        Assert.That(state.Entries[1].Revealed, Is.False);
        Assert.That(state.AdvanceSearch(0.2f, items), Is.True);
        Assert.That(state.Entries[1].Revealed, Is.True);
        Assert.That(state.Entries[2].Revealed, Is.True, "0秒下一项应正常完成，且循环有界。");
        Assert.That(state.GetNextUnrevealed(), Is.Null);
    }

    [Test]
    public void HiddenEntriesCannotBeMarkedTakenAndFailedTransferCanRestoreRevealedEntry()
    {
        LootBox box = Box("box", 2f);
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        Assert.That(runtime.TryMarkTaken(box.SourceKey, "entry-0"), Is.False);
        Assert.That(box.State.AdvanceSearch(2f, items), Is.True);
        Assert.That(runtime.TryMarkTaken(box.SourceKey, "entry-0"), Is.True);
        Assert.That(runtime.TryMarkTaken(box.SourceKey, "entry-0"), Is.False);
        Assert.That(runtime.TryRestoreTaken(box.SourceKey, "entry-0"), Is.True);
        Assert.That(box.State.Entries[0].Revealed, Is.True);
        Assert.That(box.State.Entries[0].Taken, Is.False);
    }

    [Test]
    public void SameRoundPreparationAndRebindingKeepEvenAnEmptyRoll()
    {
        LootBox box = Box("box", 1f);
        box.lootTable.entries[0].overrideProbability = 0f;
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        LootSourceState original = box.State;
        Assert.That(original.Generated, Is.True);
        Assert.That(original.Entries.Count, Is.Zero);
        box.lootTable.entries[0].overrideProbability = 1f;
        Assert.That(runtime.PrepareRound(1, out error), Is.True, error);
        Assert.That(runtime.BindBox(box, out error), Is.True, error);
        Assert.That(box.State, Is.SameAs(original));
        Assert.That(box.State.Entries.Count, Is.Zero);
    }

    [Test]
    public void NewRoundCannotEraseUnsettledWorldItems()
    {
        LootBox box = Box("box", 1f);
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        LootSourceState previous = box.State;
        string id = previous.Entries[0].InstanceId;
        Assert.That(items.BeginRound(2), Is.True);
        Assert.That(runtime.PrepareRound(2, out error), Is.False);
        Assert.That(runtime.PreparedRound, Is.EqualTo(1));
        Assert.That(runtime.TryGetSource(box.SourceKey, out LootSourceState saved), Is.True);
        Assert.That(saved, Is.SameAs(previous));
        Assert.That(items.RecordLoss(id, ItemLossReason.LeftBehindOnExtraction), Is.True);
        Assert.That(runtime.PrepareRound(2, out error), Is.True, error);
        Assert.That(box.State, Is.Not.SameAs(previous));
        Assert.That(box.State.Entries[0].InstanceId, Is.Not.EqualTo(id));
    }

    [Test]
    public void UniqueConflictIsFoundBeforeAnySourceCreatesAnInstance()
    {
        LootBox first = Box("first", 1f);
        ItemDataSO unique = first.lootTable.entries[0].item;
        unique.isUnique = true;
        unique.uniqueId = mapId + "-unique";
        first.lootTable.entries[0].overrideProbability = 0f;
        LootBox second = Box("second", 1f);
        second.lootTable.entries[0].item = unique;
        Assert.That(runtime.PrepareRound(1, out string error), Is.False);
        Assert.That(error, Does.Contain("原点"));
        Assert.That(items.InstanceCount, Is.Zero);
        Assert.That(runtime.PreparedRound, Is.Zero);
    }

    [Test]
    public void UniqueRecoveryReturnsToContainerWithSameIdentityDespiteZeroProbability()
    {
        LootBox box = Box("box", 2f);
        ItemDataSO unique = box.lootTable.entries[0].item;
        unique.isUnique = true;
        unique.uniqueId = mapId + "-unique";
        box.lootTable.entries[0].overrideProbability = 0f;
        string origin = box.SourceKey + "/entry-0";
        Assert.That(items.TryCreateInstance(unique, ItemOwner.Backpack, "pack", box.SourceKey, origin, out ItemInstance original), Is.True);
        Assert.That(items.RecordLoss(original.InstanceId, ItemLossReason.ExplorationDeath), Is.True);
        Assert.That(items.BeginRound(2), Is.True);
        Assert.That(runtime.PrepareRound(2, out string error), Is.True, error);
        Assert.That(box.State.Entries.Count, Is.EqualTo(1));
        Assert.That(box.State.Entries[0].InstanceId, Is.EqualTo(original.InstanceId));
        Assert.That(original.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(original.ContainerId, Is.EqualTo(box.SourceKey));
        Assert.That(original.OriginalSpawnPointId, Is.EqualTo(origin));
        Assert.That(original.SpawnClaimed, Is.True);
        Assert.That(box.State.Entries[0].Revealed, Is.False);
        Assert.That(runtime.BindBox(box, out error), Is.True, error);
        Assert.That(items.InstanceCount, Is.EqualTo(1));
    }

    [Test]
    public void BusyUniqueClaimKeepsRecordUnavailableUntilSameRoundRetrySucceeds()
    {
        LootBox box = Box("box", 0f);
        ItemDataSO unique = box.lootTable.entries[0].item;
        unique.isUnique = true;
        unique.uniqueId = mapId + "-unique";
        string origin = box.SourceKey + "/entry-0";
        Assert.That(items.TryCreateInstance(unique, ItemOwner.Container, box.SourceKey, box.SourceKey,
            origin, out ItemInstance original), Is.True);
        Assert.That(original.SpawnClaimed, Is.False);
        ItemDataSO medicine = Definition("claim-busy-medicine", 0f);
        medicine.canUse = true;
        HealEffectSO effect = ScriptableObject.CreateInstance<HealEffectSO>();
        created.Add(effect);
        medicine.effects.Add(effect);
        Assert.That(items.TryCreateInstance(medicine, ItemOwner.Backpack, "pack", "test", "", out ItemInstance carried), Is.True);
        bool checkedWhileBusy = false;
        CallbackHealth target = new CallbackHealth(() =>
        {
            // 真实01正在执行效果，此时许可入口明确拒绝；不改internal字段或伪造ItemManager。
            Assert.That(runtime.PrepareRound(1, out _), Is.False);
            Assert.That(box.State.Entries[0].InstanceId, Is.EqualTo(original.InstanceId));
            Assert.That(original.SpawnClaimed, Is.False);
            Assert.That(runtime.IsSourceReady(box.State), Is.False);
            box.State.AdvanceSearch(0f, items);
            Assert.That(runtime.TryMarkTaken(box.SourceKey, "entry-0"), Is.False);
            checkedWhileBusy = true;
        });
        Assert.That(items.TryUseItem(carried.InstanceId, target), Is.EqualTo(ItemUseResult.Success));
        Assert.That(checkedWhileBusy, Is.True);
        Assert.That(runtime.BindBox(box, out string error), Is.True, error);
        Assert.That(runtime.IsSourceReady(box.State), Is.True);
        Assert.That(original.SpawnClaimed, Is.True);
        Assert.That(box.State.Entries[0].InstanceId, Is.EqualTo(original.InstanceId));
        Assert.That(items.InstanceCount, Is.EqualTo(2));
    }

    [Test]
    public void SourceBindingBetweenItemRoundAndLootPreparationDoesNotGenerateUnderOldRound()
    {
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        Assert.That(items.BeginRound(2), Is.True);
        LootBox box = Box("box", 1f);
        Assert.That(runtime.BindBox(box, out error), Is.True, error);
        Assert.That(box.State, Is.Null);
        Assert.That(items.InstanceCount, Is.Zero);
        Assert.That(runtime.PrepareRound(2, out error), Is.True, error);
        Assert.That(box.State.Round, Is.EqualTo(2));
        Assert.That(items.InstanceCount, Is.EqualTo(1));
    }

    [Test]
    public void DisabledRuntimeMakesExistingSourceUnavailableWithoutErasingIt()
    {
        LootBox box = Box("box", 0f);
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        LootSourceState state = box.State;
        Assert.That(state.AdvanceSearch(0f, items), Is.True);
        runtime.enabled = false;
        Assert.That(runtime.IsSourceReady(state), Is.False);
        Assert.That(runtime.TryMarkTaken(box.SourceKey, "entry-0"), Is.False);
        runtime.enabled = true;
        Assert.That(runtime.IsSourceReady(state), Is.True);
        Assert.That(box.State, Is.SameAs(state));
    }

    [Test]
    public void InactiveGroundPreparationKeepsCarriedOwnershipUntilCommitAndCanBeCancelled()
    {
        PlayerController player = PrepareDropScene(out ItemInstance carried);
        Assert.That(runtime.TryPrepareDrop(carried, player, out LootDropPreparation prepared, out string error), Is.True, error);
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(runtime.TryGetSource(prepared.SourceId, out _), Is.False);
        Assert.That(runtime.CanCommitPreparedDrop(prepared), Is.True);
        runtime.CancelPreparedDrop(prepared);
        Assert.That(runtime.TryGetSource(prepared.SourceId, out _), Is.False);
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(runtime.GetRemainingWorldItems(), Is.Empty);
    }

    [Test]
    public void PreparedDropRechecksConfigurationBeforeOwnerCommit()
    {
        PlayerController player = PrepareDropScene(out ItemInstance carried);
        Assert.That(runtime.TryPrepareDrop(carried, player, out LootDropPreparation prepared, out string error), Is.True, error);
        runtime.dropClearance = float.NaN;
        Assert.That(runtime.CanCommitPreparedDrop(prepared), Is.False);
        runtime.CancelPreparedDrop(prepared);
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Backpack));
    }

    [Test]
    public void SourcePickupOwnerFailureRestoresTakenSearchAndRealBackpackLayout()
    {
        LootBox box = Box("box", 0f, 4f);
        box.transform.position = new Vector3(0.8f, 1f, 0f);
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        box.gameObject.SetActive(true);
        PlayerController player = PreparedPlayer();
        RejectingItemTransferCoordinator coordinator = Coordinator(player, out BackpackManager backpack);
        LootSourceState state = box.State;
        Assert.That(state.AdvanceSearch(1.25f, items), Is.True);
        Assert.That(items.TryGetInstance(state.Entries[0].InstanceId, out ItemInstance original), Is.True);
        Assert.That(items.TrySetRetained(original.InstanceId, true), Is.True);
        int notifications = 0;
        backpack.Changed += () => notifications++;
        runtime.SourceChanged += source => notifications++;
        Physics.SyncTransforms();
        Assert.That(runtime.TryValidatePickup(state.SourceId, "entry-0", player.interaction, out _), Is.True);
        coordinator.failStep = 3;
        Assert.That(coordinator.TrySourceToBackpack(state, "entry-0", 0, 0, false, () => true),
            Is.EqualTo(ItemTransferResult.OwnerRejected));
        Assert.That(coordinator.reachedStep, Is.EqualTo(3), "必须实际添加目标和标记来源后才测试归属失败。");
        Assert.That(backpack.GetPlacements().Count, Is.Zero);
        Assert.That(state.Entries[0].Taken, Is.False);
        Assert.That(state.Entries[0].Revealed, Is.True);
        Assert.That(state.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(state.SearchElapsed, Is.EqualTo(1.25f));
        Assert.That(original.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(original.ContainerId, Is.EqualTo(state.SourceId));
        Assert.That(original.IsRetained, Is.True);
        Assert.That(items.InstanceCount, Is.EqualTo(2));
        Assert.That(notifications, Is.Zero);
        Assert.That(runtime.TryValidatePickup(state.SourceId, "entry-0", player.interaction, out ItemInstance restored), Is.True);
        Assert.That(restored, Is.SameAs(original));
    }

    [TestCase(2, ItemTransferResult.SourceRemovalFailed)]
    [TestCase(3, ItemTransferResult.OwnerRejected)]
    public void DiscardFailureRestoresRealBackpackWithoutCreatingWorldRecord(int failStep, ItemTransferResult expected)
    {
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        ItemDataSO definition = Definition("discard-item", 0f);
        definition.height = 2;
        Assert.That(items.TryCreateInstance(definition, ItemOwner.Backpack, "pack", "test", "", out ItemInstance carried), Is.True);
        PlayerController player = PreparedPlayer();
        RejectingItemTransferCoordinator coordinator = Coordinator(player, out BackpackManager backpack);
        Assert.That(backpack.TryAddPlacement(carried.InstanceId, 1, 1, true), Is.EqualTo(BackpackResult.Success));
        Assert.That(items.TrySetRetained(carried.InstanceId, true), Is.True);
        string sourceBefore = carried.SourceId;
        string originBefore = carried.OriginalSpawnPointId;
        int notifications = 0;
        backpack.Changed += () => notifications++;
        runtime.SourceChanged += source => notifications++;
        coordinator.failStep = failStep;
        Assert.That(runtime.groundItemPrefab, Is.Null, "确认丢弃不依赖地面Prefab或安全落点。");
        Assert.That(coordinator.TryDiscardFromBackpack(carried.InstanceId, () => true), Is.EqualTo(expected));
        Assert.That(coordinator.reachedStep, Is.EqualTo(failStep), "必须到达实际移格或01记损提交点。");
        Assert.That(backpack.TryGetCarriedItem(carried.InstanceId, out ItemInstance restored), Is.EqualTo(BackpackResult.Success));
        Assert.That(restored, Is.SameAs(carried));
        Assert.That(backpack.TryGetPlacement(carried.InstanceId, out BackpackPlacement placement), Is.True);
        Assert.That(new[] { placement.X, placement.Y }, Is.EqualTo(new[] { 1, 1 }));
        Assert.That(placement.Rotated, Is.True);
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(carried.ContainerId, Is.EqualTo(backpack.ContainerId));
        Assert.That(carried.IsRetained, Is.True);
        Assert.That(carried.SourceId, Is.EqualTo(sourceBefore));
        Assert.That(carried.OriginalSpawnPointId, Is.EqualTo(originBefore));
        Assert.That(carried.UsesThisRound, Is.Zero);
        Assert.That(carried.LastLossReason, Is.Null);
        Assert.That(carried.RecoveryRound, Is.Zero);
        Assert.That(items.InstanceCount, Is.EqualTo(1));
        Assert.That(runtime.GetRemainingWorldItems(), Is.Empty);
        Assert.That(notifications, Is.Zero);
        foreach (GroundItem ground in UnityEngine.Object.FindObjectsByType<GroundItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Assert.That(ground.IsRuntimePresentation && ground.runtimeManager == runtime, Is.False,
                "失败的丢弃不能产生可拾取地面表现。");
        }
        // 重试通过既有入口也采用销毁语义，不恢复旧的临时落地行为。
        coordinator.failStep = 0;
        Assert.That(coordinator.TryDropFromBackpack(carried.InstanceId, () => true), Is.EqualTo(ItemTransferResult.Success));
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Lost));
        Assert.That(carried.LastLossReason, Is.EqualTo(ItemLossReason.BackpackDiscard));
        Assert.That(backpack.TryGetPlacement(carried.InstanceId, out _), Is.False);
        runtime.RebuildSceneRepresentations();
        Assert.That(runtime.GetRemainingWorldItems(), Is.Empty);
        foreach (GroundItem ground in UnityEngine.Object.FindObjectsByType<GroundItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Assert.That(ground.IsRuntimePresentation && ground.runtimeManager == runtime, Is.False);
        }
        Assert.That(items.BeginRound(2), Is.True);
        Assert.That(items.GetRecoverableItems(), Is.Empty);
        Assert.That(items.TrySetOwner(carried.InstanceId, ItemOwner.Backpack, "pack"), Is.False);
    }

    [Test]
    public void DiscardedUniqueWaitsUntilNextRoundAndRecoversSameIdentityAtOriginalBox()
    {
        RejectingItemTransferCoordinator coordinator = PrepareReturn(false, out _,
            out ItemInstance unique, out BackpackManager backpack);
        string origin = unique.OriginalSpawnPointId;
        string sourceId = unique.SourceId;
        string instanceId = unique.InstanceId;
        Assert.That(runtime.TryGetBinding(sourceId, out Interactable boundOrigin), Is.True);
        LootBox originBox = boundOrigin as LootBox;
        Assert.That(originBox, Is.Not.Null);
        LootSourceState originalState = originBox.State;
        originBox.lootTable.entries[0].useDefaultProbability = false;
        originBox.lootTable.entries[0].overrideProbability = 0f;
        Assert.That(runtime.groundItemPrefab, Is.Null);
        Assert.That(coordinator.TryDiscardFromBackpack(instanceId, () => true), Is.EqualTo(ItemTransferResult.Success));
        Assert.That(unique.Owner, Is.EqualTo(ItemOwner.PendingRecovery));
        Assert.That(unique.ContainerId, Is.Empty);
        Assert.That(unique.LastLossReason, Is.EqualTo(ItemLossReason.BackpackDiscard));
        Assert.That(unique.RecoveryRound, Is.EqualTo(2));
        Assert.That(unique.IsRetained || unique.SpawnClaimed, Is.False);
        Assert.That(unique.SourceId, Is.EqualTo(sourceId));
        Assert.That(unique.OriginalSpawnPointId, Is.EqualTo(origin));
        Assert.That(unique.UseRound, Is.EqualTo(1));
        Assert.That(unique.UsesThisRound, Is.Zero);
        Assert.That(items.CurrentRound, Is.EqualTo(1));
        Assert.That(backpack.TryGetPlacement(instanceId, out _), Is.False);
        Assert.That(originalState.Entries[0].Taken, Is.True);
        Assert.That(items.GetRecoverableItems(), Is.Empty);
        Assert.That(items.TryClaimUniqueSpawn(instanceId, origin), Is.False);
        Assert.That(coordinator.TryDropFromBackpack(instanceId, () => true), Is.EqualTo(ItemTransferResult.SourceInvalid));
        Assert.That(unique.RecoveryRound, Is.EqualTo(2));
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        Assert.That(originBox.State, Is.SameAs(originalState));
        Assert.That(originalState.Entries[0].Taken, Is.True);
        Assert.That(unique.Owner, Is.EqualTo(ItemOwner.PendingRecovery));

        foreach (ItemInstance remaining in runtime.GetRemainingWorldItems())
        {
            Assert.That(items.RecordLoss(remaining.InstanceId, ItemLossReason.LeftBehindOnExtraction), Is.True);
        }
        Assert.That(items.BeginRound(2), Is.True);
        Assert.That(runtime.PrepareRound(2, out error), Is.True, error);
        Assert.That(items.TryGetUniqueInstance(unique.UniqueId, out ItemInstance recovered), Is.True);
        Assert.That(recovered, Is.SameAs(unique));
        Assert.That(recovered.InstanceId, Is.EqualTo(instanceId));
        Assert.That(recovered.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(recovered.ContainerId, Is.EqualTo(sourceId));
        Assert.That(recovered.OriginalSpawnPointId, Is.EqualTo(origin));
        Assert.That(recovered.SpawnClaimed, Is.True);
        Assert.That(recovered.RecoveryRound, Is.Zero);
        Assert.That(originBox.State.Entries.Count, Is.EqualTo(1));
        Assert.That(originBox.State.Entries[0].InstanceId, Is.EqualTo(instanceId));
        Assert.That(originBox.State.Entries[0].Taken, Is.False);
        Assert.That(items.GetInstances().FindAll(value => value.UniqueId == unique.UniqueId).Count, Is.EqualTo(1));
        Assert.That(items.TryClaimUniqueSpawn(instanceId, origin), Is.False);
        Assert.That(runtime.BindBox(originBox, out error), Is.True, error);
        Assert.That(originBox.State.Entries.Count, Is.EqualTo(1));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ReturningToBoxReusesOrAppendsOneRevealedEntryWithoutChangingSearch(bool reuseOriginalEntry)
    {
        RejectingItemTransferCoordinator coordinator = PrepareReturn(reuseOriginalEntry, out LootBox target,
            out ItemInstance item, out BackpackManager backpack);
        LootSourceState source = target.State;
        int entryCount = source.Entries.Count;
        int instanceCount = items.InstanceCount;
        string originalSource = item.SourceId;
        string originalSpawn = item.OriginalSpawnPointId;
        string expectedEntryId = reuseOriginalEntry ? "entry-0" : "returned-" + item.InstanceId;
        int notifications = 0;
        Action verifyCompleteState = () =>
        {
            Assert.That(item.Owner, Is.EqualTo(ItemOwner.Container));
            Assert.That(item.ContainerId, Is.EqualTo(source.SourceId));
            Assert.That(backpack.TryGetPlacement(item.InstanceId, out _), Is.False);
            Assert.That(runtime.IsSourceReady(source), Is.True);
            notifications++;
        };
        backpack.Changed += verifyCompleteState;
        runtime.SourceChanged += changed => { if (changed == source) { verifyCompleteState(); } };
        Assert.That(runtime.TryValidateReturnTarget(source, coordinator.player.interaction), Is.True);
        Assert.That(coordinator.TryBackpackToSource(source, item.InstanceId, reuseOriginalEntry ? 0 : 2, 0, true,
            () => true), Is.EqualTo(ItemTransferResult.Success));
        Assert.That(source.Entries.Count, Is.EqualTo(entryCount + (reuseOriginalEntry ? 0 : 1)));
        Assert.That(source.TryGetEntry(expectedEntryId, out LootEntryState returned), Is.True);
        Assert.That(returned.InstanceId, Is.EqualTo(item.InstanceId));
        Assert.That(returned.Revealed, Is.True);
        Assert.That(returned.Taken, Is.False);
        Assert.That(returned.LayoutRotated, Is.True);
        Assert.That(source.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(source.SearchElapsed, Is.EqualTo(1.25f));
        Assert.That(item.SourceId, Is.EqualTo(originalSource));
        Assert.That(item.OriginalSpawnPointId, Is.EqualTo(originalSpawn));
        Assert.That(item.IsRetained, Is.True);
        Assert.That(item.UsesThisRound, Is.Zero);
        Assert.That(notifications, Is.EqualTo(2));
        Assert.That(runtime.BindBox(target, out string error), Is.True, error);
        Assert.That(target.State, Is.SameAs(source));
        Assert.That(source.Entries.Count, Is.EqualTo(entryCount + (reuseOriginalEntry ? 0 : 1)));
        Assert.That(items.InstanceCount, Is.EqualTo(instanceCount));
        Assert.That(coordinator.TryBackpackToSource(source, item.InstanceId, reuseOriginalEntry ? 0 : 2, 0, true,
            () => true), Is.Not.EqualTo(ItemTransferResult.Success));
        Assert.That(items.InstanceCount, Is.EqualTo(instanceCount));
    }

    [TestCase(true, 1, ItemTransferResult.TargetRejected)]
    [TestCase(true, 2, ItemTransferResult.SourceRemovalFailed)]
    [TestCase(true, 3, ItemTransferResult.OwnerRejected)]
    [TestCase(false, 1, ItemTransferResult.TargetRejected)]
    [TestCase(false, 2, ItemTransferResult.SourceRemovalFailed)]
    [TestCase(false, 3, ItemTransferResult.OwnerRejected)]
    public void ReturnFailureRestoresOldTakenEntryOrRemovesNewAppend(bool reuseOriginalEntry, int failStep,
        ItemTransferResult expected)
    {
        RejectingItemTransferCoordinator coordinator = PrepareReturn(reuseOriginalEntry, out LootBox target,
            out ItemInstance item, out BackpackManager backpack);
        LootSourceState source = target.State;
        int entryCount = source.Entries.Count;
        int instanceCount = items.InstanceCount;
        int notifications = 0;
        backpack.Changed += () => notifications++;
        runtime.SourceChanged += changed => notifications++;
        coordinator.failStep = failStep;
        coordinator.reachedStep = 0;
        Assert.That(coordinator.TryBackpackToSource(source, item.InstanceId, reuseOriginalEntry ? 0 : 2, 0, true,
            () => true), Is.EqualTo(expected));
        Assert.That(coordinator.reachedStep, Is.EqualTo(failStep));
        Assert.That(source.Entries.Count, Is.EqualTo(entryCount));
        if (reuseOriginalEntry)
        {
            Assert.That(source.Entries[0].InstanceId, Is.EqualTo(item.InstanceId));
            Assert.That(source.Entries[0].Taken, Is.True);
            Assert.That(source.Entries[0].Revealed, Is.True);
            Assert.That(source.Entries[0].LayoutRotated, Is.False);
        }
        else
        {
            Assert.That(source.TryGetEntry("returned-" + item.InstanceId, out _), Is.False);
        }
        Assert.That(backpack.TryGetCarriedItem(item.InstanceId, out ItemInstance carried), Is.EqualTo(BackpackResult.Success));
        Assert.That(carried, Is.SameAs(item));
        Assert.That(backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement placement), Is.True);
        Assert.That(new[] { placement.X, placement.Y }, Is.EqualTo(new[] { 1, 1 }));
        Assert.That(placement.Rotated, Is.True);
        Assert.That(source.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(source.SearchElapsed, Is.EqualTo(1.25f));
        Assert.That(runtime.IsSourceReady(source), Is.True);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(item.IsRetained, Is.True);
        Assert.That(items.InstanceCount, Is.EqualTo(instanceCount));
        Assert.That(notifications, Is.Zero);
        Assert.That(runtime.BindBox(target, out string error), Is.True, error);
        Assert.That(source.Entries.Count, Is.EqualTo(entryCount));
    }

    [Test]
    public void FullBoxRejectsReturnBeforeCommitWithoutChangingIdentityLayoutSearchOrNotifications()
    {
        RejectingItemTransferCoordinator coordinator = PrepareReturn(false, out LootBox target,
            out ItemInstance carried, out BackpackManager backpack, 2, 1);
        LootSourceState source = target.State;
        Assert.That(source.EnsureLayout(items), Is.True);
        Assert.That(source.GridWidth, Is.EqualTo(2));
        Assert.That(source.GridHeight, Is.EqualTo(1));
        LootEntryState first = source.Entries[0];
        LootEntryState searching = source.Entries[1];
        Assert.That(items.TryGetInstance(first.InstanceId, out ItemInstance firstItem), Is.True);
        Assert.That(items.TryGetInstance(searching.InstanceId, out ItemInstance searchingItem), Is.True);
        string originalSource = carried.SourceId;
        string originalSpawn = carried.OriginalSpawnPointId;
        int instanceCount = items.InstanceCount;
        int notifications = 0;
        backpack.Changed += () => notifications++;
        runtime.SourceChanged += changed => notifications++;
        coordinator.Completed += id => notifications++;
        coordinator.failStep = 3;
        coordinator.reachedStep = 0;

        Assert.That(runtime.CanPlaceReturnedItem(source, carried, 0, 0, true), Is.EqualTo(ItemTransferResult.Occupied));
        Assert.That(runtime.TryFindReturnedItemPosition(source, carried, true, out _, out _), Is.False);
        Assert.That(coordinator.TryBackpackToSource(source, carried.InstanceId, 0, 0, true, () => true),
            Is.EqualTo(ItemTransferResult.Occupied));
        Assert.That(coordinator.reachedStep, Is.Zero, "满箱必须在任何静默提交前拒绝。");
        Assert.That(source.Entries.Count, Is.EqualTo(2));
        Assert.That(source.Entries[0], Is.SameAs(first));
        Assert.That(source.Entries[1], Is.SameAs(searching));
        Assert.That(source.TryGetEntry("returned-" + carried.InstanceId, out _), Is.False);
        Assert.That(first.Revealed, Is.True);
        Assert.That(first.Taken, Is.False);
        Assert.That(searching.Revealed, Is.False);
        Assert.That(searching.Taken, Is.False);
        AssertLayout(first, 0, 0);
        AssertLayout(searching, 1, 0);
        Assert.That(items.TryGetInstance(first.InstanceId, out ItemInstance savedFirst), Is.True);
        Assert.That(savedFirst, Is.SameAs(firstItem));
        Assert.That(items.TryGetInstance(searching.InstanceId, out ItemInstance savedSearching), Is.True);
        Assert.That(savedSearching, Is.SameAs(searchingItem));
        foreach (ItemInstance existing in new[] { firstItem, searchingItem })
        {
            Assert.That(existing.Owner, Is.EqualTo(ItemOwner.Container));
            Assert.That(existing.ContainerId, Is.EqualTo(source.SourceId));
        }
        AssertCarriedReturnState(backpack, carried, originalSource, originalSpawn);
        Assert.That(source.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(source.SearchElapsed, Is.EqualTo(1.25f));
        Assert.That(items.InstanceCount, Is.EqualTo(instanceCount));
        Assert.That(notifications, Is.Zero);
        Assert.That(runtime.BindBox(target, out string error), Is.True, error);
        Assert.That(target.State, Is.SameAs(source));
        Assert.That(source.LayoutColumns, Is.EqualTo(2));
        Assert.That(source.LayoutRows, Is.EqualTo(1));
    }

    [Test]
    public void ReusedTakenCellMakesOriginalReturnToAnotherFreeCellWithoutMovingOtherEntries()
    {
        RejectingItemTransferCoordinator coordinator = PrepareReusedReturnCell(out LootBox target,
            out ItemInstance original, out ItemInstance replacement, out BackpackManager backpack);
        LootSourceState source = target.State;
        LootEntryState taken = source.Entries[0];
        AssertLayout(taken, 0, 0);
        Assert.That(taken.Taken, Is.True);
        Assert.That(source.TryGetEntry("returned-" + replacement.InstanceId, out LootEntryState occupying), Is.True);
        AssertLayout(occupying, 0, 0);
        string originalSource = original.SourceId;
        string originalSpawn = original.OriginalSpawnPointId;
        int instanceCount = items.InstanceCount;

        Assert.That(coordinator.TryBackpackToSource(source, original.InstanceId, 2, 0, false, () => true),
            Is.EqualTo(ItemTransferResult.Success));
        Assert.That(source.Entries.Count, Is.EqualTo(3), "返还必须复用原条目，不能复制身份或条目。");
        Assert.That(source.Entries[0], Is.SameAs(taken));
        Assert.That(taken.InstanceId, Is.EqualTo(original.InstanceId));
        Assert.That(taken.Taken, Is.False);
        Assert.That(taken.Revealed, Is.True);
        AssertLayout(taken, 2, 0);
        AssertLayout(source.Entries[1], 1, 0);
        AssertLayout(occupying, 0, 0);
        Assert.That(source.Entries[1].Revealed, Is.False);
        Assert.That(source.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(source.SearchElapsed, Is.EqualTo(1.25f));
        Assert.That(original.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(original.ContainerId, Is.EqualTo(source.SourceId));
        Assert.That(original.SourceId, Is.EqualTo(originalSource));
        Assert.That(original.OriginalSpawnPointId, Is.EqualTo(originalSpawn));
        Assert.That(original.IsRetained, Is.True);
        Assert.That(replacement.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(backpack.TryGetPlacement(original.InstanceId, out _), Is.False);
        Assert.That(items.InstanceCount, Is.EqualTo(instanceCount));
        Assert.That(runtime.BindBox(target, out string error), Is.True, error);
        Assert.That(target.State, Is.SameAs(source));
        Assert.That(source.EnsureLayout(items), Is.True);
        AssertLayout(taken, 2, 0);
        AssertLayout(occupying, 0, 0);
    }

    [TestCase(2, ItemTransferResult.SourceRemovalFailed)]
    [TestCase(3, ItemTransferResult.OwnerRejected)]
    public void FailedReturnAfterTakenCellReuseRestoresHistoricalLayoutAndBackpackPlacement(
        int failStep, ItemTransferResult expected)
    {
        RejectingItemTransferCoordinator coordinator = PrepareReusedReturnCell(out LootBox target,
            out ItemInstance original, out ItemInstance replacement, out BackpackManager backpack);
        LootSourceState source = target.State;
        LootEntryState taken = source.Entries[0];
        LootEntryState searching = source.Entries[1];
        Assert.That(source.TryGetEntry("returned-" + replacement.InstanceId, out LootEntryState occupying), Is.True);
        string originalSource = original.SourceId;
        string originalSpawn = original.OriginalSpawnPointId;
        int instanceCount = items.InstanceCount;
        int notifications = 0;
        backpack.Changed += () => notifications++;
        runtime.SourceChanged += changed => notifications++;
        coordinator.Completed += id => notifications++;
        coordinator.failStep = failStep;
        coordinator.reachedStep = 0;

        Assert.That(coordinator.TryBackpackToSource(source, original.InstanceId, 2, 0, true, () => true), Is.EqualTo(expected));
        Assert.That(coordinator.reachedStep, Is.EqualTo(failStep));
        Assert.That(source.Entries.Count, Is.EqualTo(3));
        Assert.That(source.Entries[0], Is.SameAs(taken));
        Assert.That(source.Entries[1], Is.SameAs(searching));
        Assert.That(source.Entries[2], Is.SameAs(occupying));
        Assert.That(taken.InstanceId, Is.EqualTo(original.InstanceId));
        Assert.That(taken.Taken, Is.True);
        Assert.That(taken.Revealed, Is.True);
        Assert.That(taken.LayoutRotated, Is.False);
        AssertLayout(taken, 0, 0);
        AssertLayout(searching, 1, 0);
        AssertLayout(occupying, 0, 0);
        Assert.That(searching.Taken, Is.False);
        Assert.That(searching.Revealed, Is.False);
        Assert.That(occupying.Taken, Is.False);
        Assert.That(occupying.Revealed, Is.True);
        Assert.That(occupying.InstanceId, Is.EqualTo(replacement.InstanceId));
        Assert.That(replacement.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(replacement.ContainerId, Is.EqualTo(source.SourceId));
        AssertCarriedReturnState(backpack, original, originalSource, originalSpawn);
        Assert.That(backpack.GetPlacements().Count, Is.EqualTo(1));
        Assert.That(source.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(source.SearchElapsed, Is.EqualTo(1.25f));
        Assert.That(source.EnsureLayout(items), Is.True, "Taken历史位置允许和当前占位重叠，但不能重新占容量。");
        Assert.That(runtime.IsSourceReady(source), Is.True);
        Assert.That(items.InstanceCount, Is.EqualTo(instanceCount));
        Assert.That(notifications, Is.Zero);
        Assert.That(runtime.BindBox(target, out string error), Is.True, error);
        Assert.That(target.State, Is.SameAs(source));
        AssertLayout(taken, 0, 0);
        AssertLayout(occupying, 0, 0);
    }

    [Test]
    public void PreciseRotatedReturnAndPickupKeepTheRequestedRectangleAndSearchState()
    {
        RejectingItemTransferCoordinator coordinator = PrepareNonSquareReturn(out LootBox target,
            out ItemInstance item, out BackpackManager backpack);
        LootSourceState source = target.State;
        LootEntryState returned = source.Entries[0];
        int notifications = 0;
        backpack.Changed += () => notifications++;
        runtime.SourceChanged += changed => notifications++;
        coordinator.Completed += id => notifications++;
        coordinator.reachedStep = 0;

        Assert.That(coordinator.TryBackpackToSource(source, item.InstanceId, 1, 0, true, () => true),
            Is.EqualTo(ItemTransferResult.Occupied), "指定格包含未揭示项时拒绝，即使其它格有空位。");
        Assert.That(coordinator.reachedStep, Is.Zero);
        Assert.That(notifications, Is.Zero);
        Assert.That(returned.Taken, Is.True);
        AssertCarriedReturnState(backpack, item, item.SourceId, item.OriginalSpawnPointId);

        Assert.That(coordinator.TryBackpackToSource(source, item.InstanceId, 4, 2, true, () => true),
            Is.EqualTo(ItemTransferResult.Success));
        Assert.That(returned.LayoutRotated, Is.True);
        Assert.That(new RectInt(returned.LayoutX, returned.LayoutY, returned.LayoutWidth, returned.LayoutHeight),
            Is.EqualTo(new RectInt(4, 2, 2, 1)));
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(backpack.TryGetPlacement(item.InstanceId, out _), Is.False);
        Assert.That(source.EnsureLayout(items), Is.True);
        Assert.That(notifications, Is.EqualTo(3));
        Assert.That(runtime.BindBox(target, out string error), Is.True, error);
        Assert.That(target.State, Is.SameAs(source));
        Assert.That(returned.LayoutRotated, Is.True);

        Assert.That(coordinator.TrySourceToBackpack(source, returned.EntryId, 1, 1,
            returned.LayoutRotated, () => true), Is.EqualTo(ItemTransferResult.Success));
        Assert.That(backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement placement), Is.True);
        Assert.That(placement.Rotated, Is.True);
        Assert.That(placement.Width, Is.EqualTo(2));
        Assert.That(placement.Height, Is.EqualTo(1));
        Assert.That(returned.Taken, Is.True);
        Assert.That(returned.LayoutRotated, Is.True);
        Assert.That(notifications, Is.EqualTo(6));
        AssertLayout(source.Entries[1], 1, 0);
        Assert.That(source.Entries[1].Revealed, Is.False);
        Assert.That(source.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(source.SearchElapsed, Is.EqualTo(1.25f));
        Assert.That(source.Entries.Count, Is.EqualTo(2));
        Assert.That(items.InstanceCount, Is.EqualTo(2));
    }

    [TestCase(2, ItemTransferResult.SourceRemovalFailed)]
    [TestCase(3, ItemTransferResult.OwnerRejected)]
    public void FailedDirectionChangeRestoresThePreviouslyRotatedTakenEntry(int failStep, ItemTransferResult expected)
    {
        RejectingItemTransferCoordinator coordinator = PrepareNonSquareReturn(out LootBox target,
            out ItemInstance item, out BackpackManager backpack);
        LootSourceState source = target.State;
        LootEntryState returned = source.Entries[0];
        Assert.That(coordinator.TryBackpackToSource(source, item.InstanceId, 4, 2, true, () => true),
            Is.EqualTo(ItemTransferResult.Success));
        Assert.That(coordinator.TrySourceToBackpack(source, returned.EntryId, 1, 1, returned.LayoutRotated,
            () => true), Is.EqualTo(ItemTransferResult.Success));
        string originalSource = item.SourceId;
        string originalSpawn = item.OriginalSpawnPointId;
        int notifications = 0;
        backpack.Changed += () => notifications++;
        runtime.SourceChanged += changed => notifications++;
        coordinator.Completed += id => notifications++;
        coordinator.failStep = failStep;
        coordinator.reachedStep = 0;

        Assert.That(coordinator.TryBackpackToSource(source, item.InstanceId, 2, 1, false, () => true), Is.EqualTo(expected));
        Assert.That(coordinator.reachedStep, Is.EqualTo(failStep));
        Assert.That(returned.Taken, Is.True);
        Assert.That(returned.Revealed, Is.True);
        Assert.That(returned.LayoutRotated, Is.True);
        Assert.That(new RectInt(returned.LayoutX, returned.LayoutY, returned.LayoutWidth, returned.LayoutHeight),
            Is.EqualTo(new RectInt(4, 2, 2, 1)));
        AssertCarriedReturnState(backpack, item, originalSource, originalSpawn);
        Assert.That(backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement placement), Is.True);
        Assert.That(placement.Width, Is.EqualTo(2));
        Assert.That(placement.Height, Is.EqualTo(1));
        Assert.That(source.EnsureLayout(items), Is.True);
        Assert.That(source.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(source.SearchElapsed, Is.EqualTo(1.25f));
        AssertLayout(source.Entries[1], 1, 0);
        Assert.That(source.Entries.Count, Is.EqualTo(2));
        Assert.That(items.InstanceCount, Is.EqualTo(2));
        Assert.That(notifications, Is.Zero);
    }

    [Test]
    public void GroundSourceCannotReceiveBackpackReturnsThroughEitherPublicEntry()
    {
        RejectingItemTransferCoordinator coordinator = PrepareReturn(true, out LootBox target,
            out ItemInstance carried, out BackpackManager backpack);
        GameObject groundObject = Object("GroundIsNotReturnContainer");
        groundObject.SetActive(false);
        GroundItem ground = groundObject.AddComponent<GroundItem>();
        ConfigureInteractionTrigger(ground);
        ground.mapId = mapId;
        ground.sourceId = "ground-return-rejected";
        ground.initialItem = Definition("ground-return-item", 0f);
        ground.runtimeManager = runtime;
        Assert.That(runtime.BindGround(ground, out string error), Is.True, error);
        LootSourceState source = ground.State;
        string originalGroundInstance = source.Entries[0].InstanceId;
        coordinator.reachedStep = 0;
        Assert.That(runtime.TryValidateReturnTarget(source, coordinator.player.interaction), Is.False);
        Assert.That(runtime.TryAddReturnedItem(source, carried, 0, 0, false, out _, out _), Is.False);
        Assert.That(coordinator.TryBackpackToSource(source, carried.InstanceId, 0, 0, false, () => true), Is.EqualTo(ItemTransferResult.NotAllowed));
        Assert.That(coordinator.reachedStep, Is.Zero);
        Assert.That(source.Entries.Count, Is.EqualTo(1));
        Assert.That(source.Entries[0].InstanceId, Is.EqualTo(originalGroundInstance));
        Assert.That(backpack.TryGetCarriedItem(carried.InstanceId, out _), Is.EqualTo(BackpackResult.Success));
        Assert.That(target.State.Entries[0].Taken, Is.True);
        Assert.That(target.State.SearchElapsed, Is.EqualTo(1.25f));
    }

    [TestCase("request", ItemTransferResult.StaleRequest)]
    [TestCase("distance", ItemTransferResult.Unreachable)]
    [TestCase("obstruction", ItemTransferResult.Unreachable)]
    [TestCase("map", ItemTransferResult.Unreachable)]
    [TestCase("stale-source", ItemTransferResult.SourceInvalid)]
    public void InvalidReturnRequestIsRejectedBeforeAnyTargetWrite(string invalidReason, ItemTransferResult expected)
    {
        RejectingItemTransferCoordinator coordinator = PrepareReturn(true, out LootBox target,
            out ItemInstance carried, out BackpackManager backpack);
        LootSourceState source = target.State;
        LootSourceState requestedSource = source;
        if (invalidReason == "distance") { target.transform.position += Vector3.left * 10f; }
        if (invalidReason == "obstruction")
        {
            GameObject wall = Object("ReturnObstruction");
            wall.transform.position = new Vector3(-0.15f, 1.75f, 0f);
            wall.AddComponent<BoxCollider>().size = new Vector3(0.1f, 0.2f, 1f);
        }
        if (invalidReason == "map") { runtime.SetExplorationContext("another-map", true); }
        if (invalidReason == "stale-source")
        {
            requestedSource = new LootSourceState(source.Round, source.MapId, source.SourceId, false);
        }
        Physics.SyncTransforms();
        int notifications = 0;
        backpack.Changed += () => notifications++;
        runtime.SourceChanged += changed => notifications++;
        coordinator.reachedStep = 0;
        Assert.That(coordinator.TryBackpackToSource(requestedSource, carried.InstanceId, 0, 0, false,
            () => invalidReason != "request"),
            Is.EqualTo(expected));
        Assert.That(coordinator.reachedStep, Is.Zero);
        Assert.That(backpack.TryGetCarriedItem(carried.InstanceId, out _), Is.EqualTo(BackpackResult.Success));
        Assert.That(source.Entries.Count, Is.EqualTo(2));
        Assert.That(source.Entries[0].Taken, Is.True);
        Assert.That(source.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(source.SearchElapsed, Is.EqualTo(1.25f));
        Assert.That(notifications, Is.Zero);
    }

    [Test]
    public void UniqueReturnedToAnotherBoxRecoversAtItsOriginalBoxNextRound()
    {
        RejectingItemTransferCoordinator coordinator = PrepareReturn(false, out LootBox target,
            out ItemInstance unique, out _);
        string originalSpawn = unique.OriginalSpawnPointId;
        string originalSourceId = unique.SourceId;
        Assert.That(coordinator.TryBackpackToSource(target.State, unique.InstanceId, 2, 0, false,
            () => true), Is.EqualTo(ItemTransferResult.Success));
        foreach (ItemInstance remaining in runtime.GetRemainingWorldItems())
        {
            Assert.That(items.RecordLoss(remaining.InstanceId, ItemLossReason.LeftBehindOnExtraction), Is.True);
        }
        Assert.That(items.BeginRound(2), Is.True);
        Assert.That(runtime.PrepareRound(2, out string error), Is.True, error);
        Assert.That(runtime.TryGetSource(originalSourceId, out LootSourceState originalSource), Is.True);
        Assert.That(originalSource.Entries.Count, Is.EqualTo(1));
        Assert.That(originalSource.Entries[0].InstanceId, Is.EqualTo(unique.InstanceId));
        Assert.That(unique.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(unique.ContainerId, Is.EqualTo(originalSourceId));
        Assert.That(unique.OriginalSpawnPointId, Is.EqualTo(originalSpawn));
        Assert.That(target.State.TryGetEntry("returned-" + unique.InstanceId, out _), Is.False);
        Assert.That(target.State.Entries.Count, Is.EqualTo(2));
        Assert.That(unique.SpawnClaimed, Is.True);
    }

    [Test]
    public void CommittedDropCanRebuildMissingPresentationWithoutAnotherIdentityOrClaim()
    {
        PlayerController player = PrepareDropScene(out ItemInstance carried);
        Assert.That(runtime.TryPrepareDrop(carried, player, out LootDropPreparation prepared, out string error), Is.True, error);
        Assert.That(items.TrySetOwner(carried.InstanceId, ItemOwner.World, prepared.SourceId), Is.True);
        foreach (GroundItem ground in UnityEngine.Object.FindObjectsByType<GroundItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (ground.IsRuntimePresentation && ground.InstanceId == carried.InstanceId) { UnityEngine.Object.DestroyImmediate(ground.gameObject); }
        }
        Assert.That(runtime.CommitPreparedDrop(prepared), Is.True);
        Assert.That(runtime.TryGetSource(prepared.SourceId, out LootSourceState state), Is.True);
        Assert.That(state.Entries[0].InstanceId, Is.EqualTo(carried.InstanceId));
        runtime.RebuildSceneRepresentations();
        runtime.RebuildSceneRepresentations();
        int count = 0;
        foreach (GroundItem ground in UnityEngine.Object.FindObjectsByType<GroundItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (ground.IsRuntimePresentation && ground.InstanceId == carried.InstanceId) { count++; }
        }
        Assert.That(count, Is.EqualTo(1));
        Assert.That(items.InstanceCount, Is.EqualTo(1));
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.World));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BoundLootInteractionRangesDoNotOccupyDropSpaceButRootFootprintsDo(bool boxRange)
    {
        PlayerController player = PrepareDropScene(out ItemInstance carried);
        GameObject range = Object("ordinary-range-trigger");
        BoxCollider rangeCollider = range.AddComponent<BoxCollider>();
        rangeCollider.isTrigger = true;
        rangeCollider.size = new Vector3(5f, 5f, 5f);
        GameObject rangeOwner = Object("distant-loot-with-large-range");
        rangeOwner.transform.position = new Vector3(0f, 6f, 0f);
        Interactable target = boxRange ? (Interactable)rangeOwner.AddComponent<LootBox>() : rangeOwner.AddComponent<GroundItem>();
        ConfigureInteractionTrigger(target).size = Vector3.one * 20f;
        Assert.That(runtime.TryPrepareDrop(carried, player, out LootDropPreparation prepared, out string error), Is.True, error);
        Assert.That(runtime.CanCommitPreparedDrop(prepared), Is.True);
        runtime.CancelPreparedDrop(prepared);
        foreach (float side in new[] { -1f, 1f })
        {
            GameObject blocker = Object("occupied-ground");
            blocker.transform.position = new Vector3(side * 0.9f, 0.21f, 0f);
            BoxCollider collider = blocker.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.8f, 0.36f, 0.36f);
            collider.isTrigger = true;
            Interactable occupied = boxRange ? (Interactable)blocker.AddComponent<LootBox>() : blocker.AddComponent<GroundItem>();
            ConfigureInteractionTrigger(occupied);
        }
        Assert.That(runtime.TryPrepareDrop(carried, player, out _, out _), Is.False);
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(runtime.GetRemainingWorldItems(), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PreparedDropRechecksActualOccupancyWithoutTreatingBoundRangeAsVolume(bool triggerFootprint)
    {
        PlayerController player = PrepareDropScene(out ItemInstance carried);
        Assert.That(runtime.TryPrepareDrop(carried, player, out LootDropPreparation prepared, out string error), Is.True, error);
        GameObject obstacle = Object("new-drop-obstacle");
        obstacle.transform.position = prepared.Position + Vector3.up * 6f;
        GroundItem ground = obstacle.AddComponent<GroundItem>();
        ConfigureInteractionTrigger(ground).size = Vector3.one * 20f;
        Assert.That(runtime.CanCommitPreparedDrop(prepared), Is.True, "只有明确交互范围覆盖落点仍然安全。");
        GameObject footprint = Object("actual-footprint");
        footprint.transform.SetParent(obstacle.transform, false);
        footprint.transform.position = prepared.Position;
        BoxCollider collider = footprint.AddComponent<BoxCollider>();
        collider.isTrigger = triggerFootprint;
        collider.size = Vector3.one * 0.2f;
        Physics.SyncTransforms();
        Assert.That(runtime.CanCommitPreparedDrop(prepared), Is.False, "新增实体或未绑定的Loot占位Trigger必须使提交失效。");
        runtime.CancelPreparedDrop(prepared);
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(runtime.GetRemainingWorldItems(), Is.Empty);
    }

    [Test]
    public void InactiveDropTemplateSeparatesLargeRangeFromSmallFootprintAndClonesTheBinding()
    {
        PlayerController player = PrepareDropScene(out ItemInstance carried);
        GroundItem template = runtime.groundItemPrefab;
        template.interactionTrigger.size = new Vector3(12f, 8f, 6f);
        template.interactionTrigger.center = new Vector3(0.5f, 1f, 0f);
        Assert.That(template.gameObject.activeInHierarchy, Is.False);
        Assert.That(runtime.ValidateDropPrefab(out string error), Is.True, error);
        Assert.That(runtime.TryPrepareDrop(carried, player, out LootDropPreparation prepared, out error), Is.True, error);
        GroundItem clone = null;
        foreach (GroundItem candidate in UnityEngine.Object.FindObjectsByType<GroundItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (candidate.IsRuntimePresentation && candidate.InstanceId == carried.InstanceId) { clone = candidate; }
        }
        Assert.That(clone, Is.Not.Null);
        Assert.That(clone.gameObject.activeInHierarchy, Is.False);
        Assert.That(clone.interactionTrigger, Is.Not.SameAs(template.interactionTrigger));
        Assert.That(clone.interactionTrigger.GetComponentInParent<Interactable>(true), Is.SameAs(clone));
        Assert.That(clone.GetComponent<BoxCollider>().size, Is.EqualTo(Vector3.one * 0.36f));
        Assert.That(clone.interactionTrigger.size, Is.EqualTo(template.interactionTrigger.size));
        Assert.That(runtime.CanCommitPreparedDrop(prepared), Is.True);
        runtime.CancelPreparedDrop(prepared);
        Assert.That(clone == null, Is.True);
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(items.InstanceCount, Is.EqualTo(1));
    }

    [TestCase("missing")]
    [TestCase("shared-root")]
    [TestCase("second-root-box")]
    [TestCase("not-trigger")]
    [TestCase("wrong-owner")]
    [TestCase("disabled-range")]
    [TestCase("inactive-range-parent")]
    [TestCase("extra-collider")]
    [TestCase("oversized-footprint")]
    [TestCase("invalid-range-size")]
    public void InvalidDropTemplateCannotUseInteractionRangeAsItsFootprint(string invalid)
    {
        PlayerController player = PrepareDropScene(out ItemInstance carried);
        GroundItem template = runtime.groundItemPrefab;
        if (invalid == "missing") { template.interactionTrigger = null; }
        if (invalid == "shared-root") { template.interactionTrigger = template.GetComponent<BoxCollider>(); }
        if (invalid == "second-root-box")
        {
            UnityEngine.Object.DestroyImmediate(template.interactionTrigger.gameObject);
            template.interactionTrigger = template.gameObject.AddComponent<BoxCollider>();
            template.interactionTrigger.isTrigger = true;
        }
        if (invalid == "not-trigger") { template.interactionTrigger.isTrigger = false; }
        if (invalid == "wrong-owner") { template.interactionTrigger = Box("other-owner").interactionTrigger; }
        if (invalid == "disabled-range") { template.interactionTrigger.enabled = false; }
        if (invalid == "inactive-range-parent")
        {
            GameObject parent = Object("DisabledRangeParent");
            parent.transform.SetParent(template.transform, false);
            template.interactionTrigger.transform.SetParent(parent.transform, false);
            parent.SetActive(false);
        }
        if (invalid == "extra-collider")
        {
            GameObject extra = Object("UnboundRange");
            extra.transform.SetParent(template.transform, false);
            extra.AddComponent<BoxCollider>().isTrigger = true;
        }
        if (invalid == "oversized-footprint") { template.GetComponent<BoxCollider>().size = Vector3.one; }
        if (invalid == "invalid-range-size") { template.interactionTrigger.size = new Vector3(0f, 3f, 3f); }
        Assert.That(runtime.ValidateDropPrefab(out string error), Is.False);
        Assert.That(error, Is.Not.Empty);
        Assert.That(runtime.TryPrepareDrop(carried, player, out _, out _), Is.False);
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(items.InstanceCount, Is.EqualTo(1));
        Assert.That(runtime.GetRemainingWorldItems(), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GroundBindingAndRepeatedHideRestoreOnlyPreviouslyEnabledComponents(bool rangeInitiallyEnabled)
    {
        GameObject groundObject = Object("GroundPresentationState");
        groundObject.SetActive(false);
        GroundItem ground = groundObject.AddComponent<GroundItem>();
        BoxCollider range = ConfigureInteractionTrigger(ground);
        range.enabled = rangeInitiallyEnabled;
        ground.mapId = mapId;
        ground.sourceId = "presentation-state";
        ground.initialItem = Definition("presentation-item", 0f);
        ground.runtimeManager = runtime;
        MeshRenderer visible = groundObject.AddComponent<MeshRenderer>();
        GameObject hiddenVisual = Object("ManuallyHiddenVisual");
        hiddenVisual.transform.SetParent(groundObject.transform, false);
        MeshRenderer manuallyHidden = hiddenVisual.AddComponent<MeshRenderer>();
        manuallyHidden.enabled = false;
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        Assert.That(range.enabled, Is.EqualTo(rangeInitiallyEnabled), "首次绑定不能启用手动禁用的范围。");
        Assert.That(manuallyHidden.enabled, Is.False);
        Assert.That(visible.enabled, Is.True);
        string instanceId = ground.State.Entries[0].InstanceId;
        Assert.That(runtime.TryMarkTaken(ground.SourceKey, "item"), Is.True);
        runtime.CompletePickupVisual(instanceId);
        Assert.That(runtime.BindGround(ground, out error), Is.True, error);
        Assert.That(range.enabled, Is.False);
        Assert.That(ground.GetComponent<BoxCollider>().enabled, Is.False);
        Assert.That(visible.enabled, Is.False);
        Assert.That(manuallyHidden.enabled, Is.False);
        Assert.That(runtime.TryRestoreTaken(ground.SourceKey, "item"), Is.True);
        Assert.That(runtime.BindGround(ground, out error), Is.True, error);
        Assert.That(runtime.BindGround(ground, out error), Is.True, error);
        Assert.That(range.enabled, Is.EqualTo(rangeInitiallyEnabled), "重复隐藏不能丢失原启用状态，也不能启用原禁用组件。");
        Assert.That(ground.GetComponent<BoxCollider>().enabled, Is.True);
        Assert.That(visible.enabled, Is.True);
        Assert.That(manuallyHidden.enabled, Is.False);
        Assert.That(ground.State.Entries[0].InstanceId, Is.EqualTo(instanceId));
        Assert.That(items.InstanceCount, Is.EqualTo(1));
    }

    [TestCase(float.NaN, 0.03f)]
    [TestCase(float.PositiveInfinity, 0.03f)]
    [TestCase(0.18f, float.NaN)]
    [TestCase(0.18f, -1f)]
    public void NonFiniteOrNegativeDropSettingsRefuseWithoutMovingItem(float radius, float clearance)
    {
        PlayerController player = PrepareDropScene(out ItemInstance carried);
        runtime.dropRadius = radius;
        runtime.dropClearance = clearance;
        Assert.That(runtime.TryPrepareDrop(carried, player, out _, out _), Is.False);
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(runtime.GetRemainingWorldItems(), Is.Empty);
    }

    private PlayerController PrepareDropScene(out ItemInstance carried)
    {
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        ItemDataSO definition = Definition("drop-item", 0f);
        Assert.That(items.TryCreateInstance(definition, ItemOwner.Backpack, "pack", "test", "", out carried), Is.True);
        GameObject floor = Object("LootTestFloor");
        floor.transform.position = new Vector3(0f, -0.25f, 0f);
        floor.AddComponent<BoxCollider>().size = new Vector3(20f, 0.5f, 4f);
        GameObject template = Object("InactiveGroundTemplate");
        template.SetActive(false);
        BoxCollider groundCollider = template.AddComponent<BoxCollider>();
        groundCollider.size = Vector3.one * 0.36f;
        groundCollider.isTrigger = true;
        runtime.groundItemPrefab = template.AddComponent<GroundItem>();
        ConfigureInteractionTrigger(runtime.groundItemPrefab);
        return PreparedPlayer();
    }

    private PlayerController PreparedPlayer()
    {
        GameObject playerObject = Object("LootTestPlayer");
        PlayerController player = playerObject.AddComponent<PlayerController>();
        PlayerConfigSO config = ScriptableObject.CreateInstance<PlayerConfigSO>();
        created.Add(config);
        player.config = config;
        Assert.That(player.ResetForExploration(1, new Vector3(0f, 1f, 0f)), Is.True);
        Assert.That(player.GetComponent<CapsuleCollider>().enabled, Is.True);
        Assert.That(player.GetComponent<CapsuleCollider>().isTrigger, Is.False);
        player.SetBackpackOpen(true);
        Assert.That(player.CanAct, Is.False);
        Physics.SyncTransforms();
        return player;
    }

    private RejectingItemTransferCoordinator Coordinator(PlayerController player, out BackpackManager backpack)
    {
        backpack = items.gameObject.AddComponent<BackpackManager>();
        BackpackConfigSO config = ScriptableObject.CreateInstance<BackpackConfigSO>();
        created.Add(config);
        backpack.config = config;
        backpack.containerId = "pack";
        Assert.That(backpack.Initialize(items), Is.True);
        RejectingItemTransferCoordinator coordinator = items.gameObject.AddComponent<RejectingItemTransferCoordinator>();
        coordinator.itemManager = items;
        coordinator.backpackManager = backpack;
        coordinator.lootRuntime = runtime;
        coordinator.player = player;
        Assert.That(coordinator.Initialize(), Is.True);
        return coordinator;
    }

    private RejectingItemTransferCoordinator PrepareReturn(bool reuseOriginalEntry, out LootBox target,
        out ItemInstance item, out BackpackManager backpack, int gridWidth = 0, int gridHeight = 0)
    {
        target = Box("return-target", 0f, 4f);
        if (gridWidth > 0) { target.gridWidth = gridWidth; }
        if (gridHeight > 0) { target.gridHeight = gridHeight; }
        target.transform.position = new Vector3(-0.8f, 1f, 0f);
        LootBox origin = target;
        if (!reuseOriginalEntry)
        {
            origin = Box("unique-origin", 0f);
            origin.transform.position = new Vector3(0.8f, 1f, 0f);
            origin.lootTable.entries[0].item.isUnique = true;
            origin.lootTable.entries[0].item.uniqueId = mapId + "-returned-unique";
        }
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        target.gameObject.SetActive(true);
        origin.gameObject.SetActive(true);
        PlayerController player = PreparedPlayer();
        RejectingItemTransferCoordinator coordinator = Coordinator(player, out backpack);
        Assert.That(target.State.AdvanceSearch(1.25f, items), Is.True);
        if (!reuseOriginalEntry) { Assert.That(origin.State.AdvanceSearch(0f, items), Is.True); }
        Assert.That(items.TryGetInstance(origin.State.Entries[0].InstanceId, out item), Is.True);
        Physics.SyncTransforms();
        Assert.That(coordinator.TrySourceToBackpack(origin.State, "entry-0", 1, 1, true, () => true),
            Is.EqualTo(ItemTransferResult.Success));
        Assert.That(items.TrySetRetained(item.InstanceId, true), Is.True);
        Assert.That(target.State.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(target.State.SearchElapsed, Is.EqualTo(1.25f));
        return coordinator;
    }

    private RejectingItemTransferCoordinator PrepareNonSquareReturn(out LootBox target,
        out ItemInstance item, out BackpackManager backpack)
    {
        target = Box("non-square-return", 0f, 4f);
        target.transform.position = new Vector3(-0.8f, 1f, 0f);
        target.lootTable.entries[0].item.width = 1;
        target.lootTable.entries[0].item.height = 2;
        Assert.That(runtime.PrepareRound(1, out string error), Is.True, error);
        target.gameObject.SetActive(true);
        PlayerController player = PreparedPlayer();
        RejectingItemTransferCoordinator coordinator = Coordinator(player, out backpack);
        Assert.That(target.State.AdvanceSearch(1.25f, items), Is.True);
        Assert.That(items.TryGetInstance(target.State.Entries[0].InstanceId, out item), Is.True);
        Physics.SyncTransforms();
        Assert.That(coordinator.TrySourceToBackpack(target.State, "entry-0", 1, 1, true, () => true),
            Is.EqualTo(ItemTransferResult.Success));
        Assert.That(items.TrySetRetained(item.InstanceId, true), Is.True);
        return coordinator;
    }

    private RejectingItemTransferCoordinator PrepareReusedReturnCell(out LootBox target,
        out ItemInstance original, out ItemInstance replacement, out BackpackManager backpack)
    {
        RejectingItemTransferCoordinator coordinator = PrepareReturn(true, out target,
            out original, out backpack, 3, 1);
        Assert.That(items.TryCreateInstance(Definition("replacement", 0f), ItemOwner.Backpack,
            backpack.ContainerId, mapId + "/other-source", mapId + "/other-source/item", out replacement), Is.True);
        Assert.That(backpack.TryAddPlacement(replacement.InstanceId, 0, 0, false), Is.EqualTo(BackpackResult.Success));
        Assert.That(coordinator.TryBackpackToSource(target.State, replacement.InstanceId, 0, 0, false, () => true),
            Is.EqualTo(ItemTransferResult.Success));
        Assert.That(target.State.Entries[0].Taken, Is.True);
        Assert.That(target.State.SearchEntryId, Is.EqualTo("entry-1"));
        Assert.That(target.State.SearchElapsed, Is.EqualTo(1.25f));
        return coordinator;
    }

    private static void AssertLayout(LootEntryState entry, int x, int y)
    {
        Assert.That(entry.HasLayout, Is.True);
        Assert.That(new RectInt(entry.LayoutX, entry.LayoutY, entry.LayoutWidth, entry.LayoutHeight),
            Is.EqualTo(new RectInt(x, y, 1, 1)));
    }

    private void AssertCarriedReturnState(BackpackManager backpack, ItemInstance item,
        string originalSource, string originalSpawn)
    {
        Assert.That(backpack.TryGetCarriedItem(item.InstanceId, out ItemInstance carried), Is.EqualTo(BackpackResult.Success));
        Assert.That(carried, Is.SameAs(item));
        Assert.That(backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement placement), Is.True);
        Assert.That(new[] { placement.X, placement.Y }, Is.EqualTo(new[] { 1, 1 }));
        Assert.That(placement.Rotated, Is.True);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(item.ContainerId, Is.EqualTo(backpack.ContainerId));
        Assert.That(item.IsRetained, Is.True);
        Assert.That(item.SourceId, Is.EqualTo(originalSource));
        Assert.That(item.OriginalSpawnPointId, Is.EqualTo(originalSpawn));
        Assert.That(item.UsesThisRound, Is.Zero);
    }

    private LootBox Box(string sourceId, params float[] durations)
    {
        LootTableSO table = ScriptableObject.CreateInstance<LootTableSO>();
        created.Add(table);
        for (int i = 0; i < durations.Length; i++)
        {
            table.entries.Add(new LootTableEntry { entryId = "entry-" + i,
                item = Definition(sourceId + "-" + i, durations[i]), useDefaultProbability = false, overrideProbability = 1f });
        }
        GameObject objectWithBox = Object("LootTestBox-" + sourceId);
        objectWithBox.SetActive(false);
        LootBox box = objectWithBox.AddComponent<LootBox>();
        ConfigureInteractionTrigger(box);
        box.mapId = mapId;
        box.sourceId = sourceId;
        box.lootTable = table;
        box.runtimeManager = runtime;
        return box;
    }

    private BoxCollider ConfigureInteractionTrigger(Interactable target)
    {
        GameObject range = Object("TriggerBox");
        range.transform.SetParent(target.transform, false);
        BoxCollider collider = range.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(3f, 3f, 2f);
        target.interactionTrigger = collider;
        return collider;
    }

    private ItemDataSO Definition(string id, float duration)
    {
        ItemDataSO definition = ScriptableObject.CreateInstance<ItemDataSO>();
        created.Add(definition);
        definition.definitionId = mapId + "-" + id;
        definition.nameZh = "测试";
        definition.nameEn = "Test";
        definition.descriptionZh = "说明";
        definition.descriptionEn = "Description";
        definition.nameKey = definition.definitionId + ".name";
        definition.descriptionKey = definition.definitionId + ".description";
        definition.searchSeconds = duration;
        return definition;
    }

    private GameObject Object(string name)
    {
        GameObject instance = new GameObject(name);
        created.Add(instance);
        return instance;
    }

    private sealed class CallbackHealth : IHealthTarget
    {
        private readonly Action onHeal;
        public int CurrentHealth { get; private set; } = 1;
        public int MaxHealth { get { return 3; } }
        public CallbackHealth(Action callback) { onHeal = callback; }
        public void Heal(int amount)
        {
            CurrentHealth = Math.Min(MaxHealth, CurrentHealth + amount);
            onHeal();
        }
    }
}
