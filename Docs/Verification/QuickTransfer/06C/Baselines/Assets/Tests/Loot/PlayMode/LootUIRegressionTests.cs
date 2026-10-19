#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
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
