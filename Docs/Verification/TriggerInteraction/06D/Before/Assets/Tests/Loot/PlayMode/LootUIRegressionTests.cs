#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public partial class LootIntegrationTests
{
    [UnityTest]
    public IEnumerator QueuedCrossPreviewCancelledByClaimNotificationReleasesWarehouseAndAllowsNextDrag()
    {
        yield return LoadScene();
        driver.EnterShelter();
        yield return null;
        ItemTransferUIBridge bridge = Object.FindFirstObjectByType<ItemTransferUIBridge>();
        Assert.That(bridge, Is.Not.Null);
        ItemInstance carried = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        ItemInstance stored = driver.warehouse.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        BackpackPlacement original = driver.backpack.GetPlacements().Single(value => value.InstanceId == carried.InstanceId);
        bool closedByClaim = false;
        System.Action closeDuringClaim = () =>
        {
            if (!driver.warehouse.OwnsInteraction(bridge)) { return; }
            closedByClaim = true;
            driver.backpackUI.Close();
        };
        driver.warehouse.ContextChanged += closeDuringClaim;
        try
        {
            yield return BeginPointerDrag(BackpackPoint(0, 2));
            Assert.That(driver.backpackUI.IsDragging, Is.True);
            yield return Pointer(WarehousePoint(6, 0), true);
            Assert.That(closedByClaim, Is.True, "The real 05 claim notification must interrupt the queued pointer preview.");
            Assert.That(driver.backpackUI.IsOpen, Is.False);
            Assert.That(driver.backpackUI.IsDragging, Is.False);
            Assert.That(driver.warehouse.HasPendingInteraction, Is.False, "Cancelling inside the claim notification must release the new claim.");
            Assert.That(driver.warehouseUI.view.preview.gameObject.activeSelf, Is.False, "The returned callback must not redraw a stale target preview.");
            Assert.That(driver.backpackUI.view.preview.gameObject.activeSelf, Is.False);
            yield return Pointer(WarehousePoint(6, 0), false);
            Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Backpack));
            Assert.That(driver.backpack.TryGetPlacement(carried.InstanceId, out BackpackPlacement after), Is.True);
            Assert.That(after.X, Is.EqualTo(original.X));
            Assert.That(after.Y, Is.EqualTo(original.Y));
            Assert.That(after.Rotated, Is.EqualTo(original.Rotated));
            Assert.That(driver.warehouse.TryGetPlacement(carried.InstanceId, out _), Is.False);
        }
        finally { driver.warehouse.ContextChanged -= closeDuringClaim; }

        Assert.That(driver.backpackUI.TryOpen(), Is.True);
        Assert.That(driver.warehouseUI.TryOpen(), Is.True);
        yield return BeginPointerDrag(WarehousePoint(3, 0));
        Assert.That(driver.warehouseUI.IsDragging, Is.True, "The next real warehouse drag must not be blocked by the cancelled bridge claim.");
        yield return Pointer(WarehousePoint(6, 0), true);
        yield return Pointer(WarehousePoint(6, 0), false);
        Assert.That(driver.warehouse.TryGetPlacement(stored.InstanceId, out WarehousePlacement moved), Is.True);
        Assert.That(moved.X, Is.EqualTo(6));
        Assert.That(moved.Y, Is.Zero);
        Assert.That(stored.Owner, Is.EqualTo(ItemOwner.Storage));
        Assert.That(driver.warehouse.HasPendingInteraction, Is.False);
    }

    [UnityTest]
    public IEnumerator QueuedBackpackAndWarehousePointerReordersStillUseTheirOwnGridAndOneR()
    {
        yield return LoadScene();
        driver.EnterShelter();
        yield return null;
        ItemInstance carried = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        ItemInstance stored = driver.warehouse.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        yield return BeginPointerDrag(BackpackPoint(0, 2));
        Assert.That(driver.warehouseUI.BeginItemDrag(stored.InstanceId, WarehousePoint(3, 0), null), Is.Zero,
            "The second controller must not start a competing rotation source.");
        yield return Press(Key.R);
        yield return Pointer(BackpackPoint(1, 2), true);
        yield return Pointer(BackpackPoint(1, 2), false);
        Assert.That(driver.backpack.TryGetPlacement(carried.InstanceId, out BackpackPlacement moved), Is.True);
        Assert.That(moved.X, Is.EqualTo(1));
        Assert.That(moved.Y, Is.EqualTo(2));
        Assert.That(moved.Rotated, Is.True);
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(driver.warehouse.TryGetPlacement(carried.InstanceId, out _), Is.False);

        yield return BeginPointerDrag(WarehousePoint(3, 0));
        Assert.That(driver.backpackUI.BeginItemDrag(carried.InstanceId, BackpackPoint(1, 2), null), Is.Zero);
        yield return Press(Key.R);
        yield return Pointer(WarehousePoint(6, 0), true);
        yield return Pointer(WarehousePoint(6, 0), false);
        Assert.That(driver.warehouse.TryGetPlacement(stored.InstanceId, out WarehousePlacement rearranged), Is.True);
        Assert.That(rearranged.X, Is.EqualTo(6));
        Assert.That(rearranged.Y, Is.Zero);
        Assert.That(rearranged.Rotated, Is.True);
        Assert.That(stored.Owner, Is.EqualTo(ItemOwner.Storage));
        Assert.That(driver.backpack.TryGetPlacement(stored.InstanceId, out _), Is.False);
    }

    [UnityTest]
    public IEnumerator ExistingRightClickStillRejectsFullHealthAndUsesOneRealFoodAfterDamage()
    {
        yield return LoadScene();
        ItemInstance food = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "food").Item;
        yield return Press(Key.Tab);
        yield return RightPointer(BackpackPoint(2, 0));
        Assert.That(driver.backpackUI.FeedbackKey, Is.EqualTo("items.use.full_health"));
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(food.UsesThisRound, Is.Zero);
        driver.DamageOne();
        Assert.That(driver.backpackUI.IsOpen, Is.False);
        yield return Press(Key.Tab);
        yield return RightPointer(BackpackPoint(2, 0));
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Consumed), driver.backpackUI.FeedbackKey);
        Assert.That(food.UsesThisRound, Is.EqualTo(1));
        Assert.That(driver.backpack.TryGetPlacement(food.InstanceId, out _), Is.False);
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(driver.player.health.MaxHealth));
    }

    [UnityTest]
    public IEnumerator GroundPointerCancelAndFullBackpackKeepTheSameVisibleSource()
    {
        yield return LoadScene();
        GroundItem ground = Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None)
            .Single(value => value.sourceId == "ground-battery");
        Assert.That(driver.lootUI.TryOpenSource(ground.State, ground, driver.player.interaction), Is.True);
        LootEntryState entry = ground.State.Entries.Single();
        Assert.That(driver.items.TryGetInstance(entry.InstanceId, out ItemInstance item), Is.True);
        int count = driver.items.InstanceCount;
        yield return BeginPointerDrag(EntryPoint(entry.EntryId));
        int cancelledToken = driver.backpackUI.DragToken;
        Assert.That(cancelledToken, Is.Not.Zero);
        yield return Pointer(new Vector2(Screen.width / 2f, Screen.height / 2f), true);
        yield return Pointer(new Vector2(Screen.width / 2f, Screen.height / 2f), false);
        Assert.That(entry.Taken, Is.False);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.World));
        Assert.That(ground.CanInteract(driver.player.interaction), Is.True);
        Assert.That(driver.items.InstanceCount, Is.EqualTo(count));

        // 仅安排真实01/04测试初态；正式拾取仍通过真实指针与06，而不是假容器。
        ItemDataSO battery = driver.items.itemLibrary.FindDefinition("battery");
        for (int x = 1; x <= 3; x++)
        {
            Assert.That(driver.items.TryCreateInstance(battery, ItemOwner.Backpack, driver.backpack.ContainerId,
                "ui-regression", "ui-regression/fill-" + x, out ItemInstance filler), Is.True);
            Assert.That(driver.backpack.TryAddPlacement(filler.InstanceId, x, 2, false), Is.EqualTo(BackpackResult.Success));
        }
        driver.backpack.NotifyChanged();
        Assert.That(driver.backpack.GetPlacements().Sum(value => value.Width * value.Height), Is.EqualTo(16));
        count = driver.items.InstanceCount;
        string fullState = TransferSnapshot();
        yield return ControlClick(Key.RightCtrl, EntryPoint(entry.EntryId));
        AssertReturnFeedback(ItemTransferCoordinator.ResultKey(ItemTransferResult.Occupied));
        Assert.That(TransferSnapshot(), Is.EqualTo(fullState));
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.World));
        Assert.That(entry.Taken, Is.False);
        yield return BeginPointerDrag(EntryPoint(entry.EntryId));
        Assert.That(driver.backpackUI.DragToken, Is.Not.EqualTo(cancelledToken));
        driver.lootUI.EndItemDrag(cancelledToken, BackpackPoint(0, 0), null);
        Assert.That(driver.backpackUI.IsDragging, Is.True, "A late release cannot cancel the new drag.");
        yield return Pointer(BackpackPoint(0, 0), true);
        Assert.That(driver.backpackUI.PreviewResult, Is.EqualTo(BackpackResult.Overlap));
        yield return Pointer(BackpackPoint(0, 0), false);
        Assert.That(entry.Taken, Is.False);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.World));
        Assert.That(item.ContainerId, Is.EqualTo(ground.State.SourceId));
        Assert.That(driver.backpack.TryGetPlacement(item.InstanceId, out _), Is.False);
        Assert.That(ground.CanInteract(driver.player.interaction), Is.True);
        Assert.That(driver.items.InstanceCount, Is.EqualTo(count));
    }

    [UnityTest]
    public IEnumerator ClosingEndsSourceAndPreviewBeforeReleasingBackpackControl()
    {
        yield return LoadScene();
        GroundItem ground = Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None)
            .Single(value => value.sourceId == "ground-battery");
        Assert.That(driver.lootUI.TryOpenSource(ground.State, ground, driver.player.interaction), Is.True);
        LootEntryState entry = ground.State.Entries.Single();
        yield return BeginPointerDrag(EntryPoint(entry.EntryId));
        Assert.That(driver.backpackUI.IsExternalPreview, Is.True);
        bool notified = false;
        bool lockedDuringClosing = false;
        bool sourceEndedDuringClosing = false;
        bool previewEndedDuringClosing = false;
        driver.backpackUI.Closing += () =>
        {
            notified = true;
            lockedDuringClosing = driver.player.IsBackpackOpen;
            sourceEndedDuringClosing = !driver.lootUI.IsOpen;
            previewEndedDuringClosing = !driver.backpackUI.IsDragging;
        };
        driver.DamageOne();
        Assert.That(notified, Is.True);
        Assert.That(lockedDuringClosing, Is.True);
        Assert.That(sourceEndedDuringClosing, Is.True);
        Assert.That(previewEndedDuringClosing, Is.True);
        Assert.That(driver.player.IsBackpackOpen, Is.False);
        Assert.That(entry.Taken, Is.False);
    }

    [UnityTest]
    public IEnumerator ReentrantLootPreviewRestartKeepsNewBridgeSourceWhenOldBeginReturnsFailure()
    {
        yield return LoadScene();
        LootBox box = CreateGridTestBox("reentrant-preview", 6, 4, new Vector3(1, 2, 0));
        Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
        yield return null;
        LootEntryState entry = box.State.Entries.Single();
        Assert.That(entry.Revealed, Is.True);
        Assert.That(driver.items.TryGetInstance(entry.InstanceId, out ItemInstance item), Is.True);
        string before = TransferSnapshot();
        string identity = ItemIdentitySnapshot(item);
        int instanceCount = driver.items.InstanceCount;
        int session = driver.lootUI.SessionToken;
        Vector2 origin = EntryPoint(entry.EntryId);
        Vector2 destination = BackpackPoint(1, 2);
        int oldToken = 0;
        int replacementToken = 0;
        int interceptedUpdates = 0;
        int completions = 0;
        System.Action<int, Vector2, Camera> originalUpdated = driver.backpackUI.DragUpdated;
        Assert.That(originalUpdated, Is.Not.Null, "The real bridge callback must already be bound.");
        System.Action<string> onCompleted = _ => completions++;
        driver.coordinator.Completed += onCompleted;
        try
        {
            driver.backpackUI.DragUpdated = (token, position, eventCamera) =>
            {
                interceptedUpdates++;
                oldToken = token;
                driver.lootUI.CancelItemDrag(token);
                driver.backpackUI.DragUpdated = originalUpdated;
                // 同会话在04首次UpdateDrag回调里重启，迫使旧BeginExternalPreview返回false。
                replacementToken = driver.lootUI.BeginItemDrag(entry.EntryId, origin, null);
            };
            int outerToken = driver.lootUI.BeginItemDrag(entry.EntryId, origin, null);
            Assert.That(interceptedUpdates, Is.EqualTo(1));
            Assert.That(outerToken, Is.Zero);
            Assert.That(oldToken, Is.Not.Zero);
            Assert.That(replacementToken, Is.Not.Zero.And.Not.EqualTo(oldToken));
            Assert.That(driver.backpackUI.IsDragValid(replacementToken), Is.True);
            Assert.That(driver.backpackUI.IsExternalPreview, Is.True);
            Assert.That(driver.backpackUI.DragInstanceId, Is.EqualTo(entry.InstanceId));
            Assert.That(driver.lootUI.SessionToken, Is.EqualTo(session));
            Assert.That(driver.lootUI.Source, Is.SameAs(box.State));
            Assert.That(TransferSnapshot(), Is.EqualTo(before));
            Assert.That(completions, Is.Zero);

            driver.lootUI.UpdateItemDrag(replacementToken, destination, null);
            Assert.That(driver.backpackUI.PreviewResult, Is.EqualTo(BackpackResult.Success));
            Assert.That(new Vector2Int(driver.backpackUI.PreviewX, driver.backpackUI.PreviewY),
                Is.EqualTo(new Vector2Int(1, 2)));
            driver.lootUI.UpdateItemDrag(oldToken, BackpackPoint(0, 0), null);
            driver.lootUI.EndItemDrag(oldToken, BackpackPoint(0, 0), null);
            Assert.That(driver.backpackUI.IsDragValid(replacementToken), Is.True,
                "A late callback from the failed outer begin must not cancel or move the replacement preview.");
            Assert.That(new Vector2Int(driver.backpackUI.PreviewX, driver.backpackUI.PreviewY),
                Is.EqualTo(new Vector2Int(1, 2)));
            Assert.That(TransferSnapshot(), Is.EqualTo(before));

            driver.lootUI.EndItemDrag(replacementToken, destination, null);
            Assert.That(completions, Is.EqualTo(1), "The new source record must survive the old bridge's failure cleanup.");
            Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
            Assert.That(entry.Taken, Is.True);
            Assert.That(driver.backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement placement), Is.True);
            Assert.That(new RectInt(placement.X, placement.Y, placement.Width, placement.Height),
                Is.EqualTo(new RectInt(1, 2, 1, 2)));
            Assert.That(driver.items.InstanceCount, Is.EqualTo(instanceCount));
            Assert.That(ItemIdentitySnapshot(item), Is.EqualTo(identity));
            AssertNoActiveTransferDrag();
            string committed = TransferSnapshot();
            driver.lootUI.UpdateItemDrag(oldToken, destination, null);
            driver.lootUI.EndItemDrag(oldToken, destination, null);
            Assert.That(TransferSnapshot(), Is.EqualTo(committed));
            Assert.That(completions, Is.EqualTo(1));
        }
        finally
        {
            driver.backpackUI.DragUpdated = originalUpdated;
            driver.coordinator.Completed -= onCompleted;
        }
    }

    [UnityTest]
    public IEnumerator QueuedControlFailuresNeverBecomeDragUseOrDiscardAndDoNotNotifySuccess()
    {
        yield return LoadScene();
        yield return Press(Key.Tab);
        ItemInstance battery = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        ItemInstance food = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "food").Item;
        ItemTransferUIBridge bridge = Object.FindFirstObjectByType<ItemTransferUIBridge>();
        int completions = 0;
        System.Action<string> onCompleted = _ => completions++;
        driver.coordinator.Completed += onCompleted;
        try
        {
            string before = TransferSnapshot();
            yield return ControlClick(Key.LeftCtrl, BackpackPoint(0, 2));
            Assert.That(driver.backpackUI.FeedbackKey, Is.EqualTo("loot.quick_no_target"));
            Assert.That(TransferSnapshot(), Is.EqualTo(before));

            LootBox box = CreateGridTestBox("quick-full-unknown", 1, 2, new Vector3(1, 2, 30));
            Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
            yield return null;
            before = TransferSnapshot();
            string entries = SourceEntrySnapshot(box.State);
            string layout = GridLayoutSnapshot(box.State);
            yield return ControlClick(Key.RightCtrl, BackpackPoint(0, 2));
            AssertReturnFeedback(ItemTransferCoordinator.ResultKey(ItemTransferResult.Occupied));
            Assert.That(TransferSnapshot(), Is.EqualTo(before));
            yield return ControlClick(Key.LeftCtrl, EntryPoint(box.State.Entries[0].EntryId));
            AssertReturnFeedback(ItemTransferCoordinator.ResultKey(ItemTransferResult.SourceHidden));
            AssertUnknownGridTile(GridItemView(box.State.Entries[0].EntryId));
            AssertSingleGridCountdown(box.State);
            Assert.That(SourceEntrySnapshot(box.State), Is.EqualTo(entries));
            Assert.That(GridLayoutSnapshot(box.State), Is.EqualTo(layout));

            // Ctrl按压跨拖拽阈值也不能退回普通拖拽；失败没有使用食物或改变布局。
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
            yield return null;
            yield return Pointer(BackpackPoint(2, 0), false);
            yield return Pointer(BackpackPoint(2, 0), true);
            yield return Pointer(GridCellPoint(0, 0), true);
            AssertNoActiveTransferDrag();
            yield return Pointer(GridCellPoint(0, 0), false);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            Assert.That(TransferSnapshot(), Is.EqualTo(before));
            Assert.That(food.UsesThisRound, Is.Zero);

            // 有效目标恰好一个才可快捷转移；此API夹具同时打开两侧，不凭鼠标位置猜目标。
            driver.warehouse.SetShelterContext(true, driver.gameObject.scene);
            Assert.That(driver.warehouseUI.TryOpen(), Is.True);
            yield return ControlClick(Key.RightCtrl, BackpackPoint(0, 2));
            Assert.That(driver.backpackUI.FeedbackKey, Is.EqualTo("loot.quick_ambiguous"));
            Assert.That(TransferSnapshot(), Is.EqualTo(before));
            driver.warehouseUI.Close();
            driver.warehouse.SetShelterContext(false, driver.gameObject.scene);

            yield return Click(BackpackPoint(0, 2));
            yield return Click(ButtonPoint("DiscardItem"));
            Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.True);
            int confirmation = bridge.DiscardToken;
            Assert.That(driver.backpackUI.TryQuickTransfer(battery.InstanceId), Is.False,
                "The public quick entry must reject while discard confirmation owns the interaction.");
            yield return ControlClick(Key.LeftCtrl, BackpackPoint(2, 0));
            Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.True);
            Assert.That(bridge.DiscardToken, Is.EqualTo(confirmation));
            Assert.That(TransferSnapshot(), Is.EqualTo(before));
            bridge.CancelDiscard(confirmation);
            yield return BeginPointerDrag(BackpackPoint(0, 2));
            int token = driver.backpackUI.DragToken;
            Assert.That(driver.backpackUI.TryQuickTransfer(food.InstanceId), Is.False);
            yield return Press(Key.LeftCtrl);
            Assert.That(driver.backpackUI.IsDragValid(token), Is.True);
            Assert.That(TransferSnapshot(), Is.EqualTo(before), "Pressing Ctrl after a normal drag began must not convert it into a quick action.");
            driver.backpackUI.CancelDrag();
            yield return Pointer(BackpackPoint(0, 2), false);
            AssertNoActiveTransferDrag();
            Assert.That(TransferSnapshot(), Is.EqualTo(before));
            Assert.That(completions, Is.Zero);
        }
        finally { driver.coordinator.Completed -= onCompleted; }
    }

    [UnityTest]
    public IEnumerator QueuedHeldControlReleaseAfterCloseTabDamageOrReloadCannotTransferInANewSession()
    {
        yield return LoadScene();
        LootBox box = Box("guaranteed");
        Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
        yield return WaitForRevealed(box.State, 0);
        string instanceId = box.State.Entries[0].InstanceId;
        foreach (string interruption in new[] { "source", "tab", "damage", "reload" })
        {
            box = Box("guaranteed");
            if (!driver.lootUI.IsOpen) { Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True); }
            string before = TransferSnapshot();
            int session = driver.lootUI.SessionToken;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.RightCtrl));
            yield return null;
            Vector2 point = EntryPoint(box.State.Entries[0].EntryId);
            yield return Pointer(point, false);
            yield return Pointer(point, true);
            AssertNoActiveTransferDrag();
            if (interruption == "source") { driver.lootUI.CloseSource(); }
            else if (interruption == "tab") { yield return Press(Key.Tab); }
            else if (interruption == "damage") { driver.DamageOne(); }
            else
            {
                yield return SceneManager.UnloadSceneAsync(driver.gameObject.scene);
                yield return LoadScene();
            }
            Assert.That(driver.lootUI.IsOpen, Is.False);
            box = Box("guaranteed");
            Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
            if (interruption != "reload") { Assert.That(driver.lootUI.SessionToken, Is.Not.EqualTo(session)); }
            yield return Pointer(EntryPoint(box.State.Entries[0].EntryId), false);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            Assert.That(TransferSnapshot(), Is.EqualTo(before));
            Assert.That(driver.items.TryGetInstance(instanceId, out ItemInstance item), Is.True);
            Assert.That(item.Owner, Is.EqualTo(ItemOwner.Container));
            Assert.That(box.State.Entries[0].Taken, Is.False);
            AssertNoActiveTransferDrag();
        }
        yield return ControlClick(Key.LeftCtrl, EntryPoint(box.State.Entries[0].EntryId));
        Assert.That(driver.items.TryGetInstance(instanceId, out ItemInstance carried), Is.True);
        Assert.That(carried.Owner, Is.EqualTo(ItemOwner.Backpack), "A fresh complete gesture remains usable after every cancellation.");
    }

    private IEnumerator RightPointer(Vector2 point)
    {
        yield return Pointer(point, false);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 2 });
        yield return null;
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 0 });
        yield return null;
        yield return null;
    }
}
#endif
