#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

public partial class LootIntegrationTests
{
    [UnityTest]
    public IEnumerator QueuedBoxRoundTripReusesEntryIdentityAndKeepsTheNextSearch()
    {
        yield return LoadScene();
        yield return Press(Key.E);
        LootSourceState source = driver.lootUI.Source;
        yield return WaitForRevealed(source, 0);
        LootEntryState entry = source.Entries[0];
        Assert.That(driver.items.TryGetInstance(entry.InstanceId, out ItemInstance item), Is.True);
        string identity = ItemIdentitySnapshot(item);
        int instanceCount = driver.items.InstanceCount;
        int entryCount = source.Entries.Count;
        yield return BeginPointerDrag(EntryPoint(entry.EntryId));
        yield return Pointer(BackpackPoint(1, 2), true);
        yield return Pointer(BackpackPoint(1, 2), false);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack), driver.backpackUI.FeedbackKey);
        Assert.That(entry.Taken, Is.True);

        LootEntryState searching = source.GetNextUnrevealed();
        float elapsed = source.SearchElapsed;
        Color viewportColor = ReturnViewportColor();
        string background = GridBackgroundSnapshot();
        yield return BeginPointerDrag(BackpackPoint(1, 2));
        int token = driver.backpackUI.DragToken;
        yield return Pointer(ReturnPoint(), true);
        Rect preview = AssertLocalReturnPreview(source, item, viewportColor);
        Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
        Assert.That(driver.backpackUI.IsExternalPreview, Is.False);
        Assert.That(driver.warehouseUI.IsDragging, Is.False);
        yield return Press(Key.R);
        Assert.That(driver.backpackUI.PreviewRotated, Is.True, "The existing backpack drag is the only R owner.");
        yield return Press(Key.F10);
        Assert.That(driver.backpackUI.DragToken, Is.EqualTo(token));
        AssertLocalReturnPreview(source, item, viewportColor);
        yield return Pointer(ReturnPoint(), false);
        AssertCommittedReturnPreview(source, item, preview, viewportColor);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Container), driver.backpackUI.FeedbackKey);
        Assert.That(item.ContainerId, Is.EqualTo(source.SourceId));
        Assert.That(source.Entries.Count, Is.EqualTo(entryCount));
        Assert.That(source.Entries.Single(value => value.InstanceId == item.InstanceId), Is.SameAs(entry));
        Assert.That(entry.Revealed && !entry.Taken, Is.True);
        Assert.That(driver.backpack.TryGetPlacement(item.InstanceId, out _), Is.False);
        Assert.That(ItemIdentitySnapshot(item), Is.EqualTo(identity));
        Assert.That(driver.lootUI.IsOpen, Is.True);
        Assert.That(driver.lootUI.view.IsReturnPreviewVisible, Is.False);
        if (searching != null && !searching.Revealed)
        {
            Assert.That(source.SearchEntryId, Is.EqualTo(searching.EntryId));
            Assert.That(source.SearchElapsed, Is.GreaterThanOrEqualTo(elapsed));
        }

        yield return BeginPointerDrag(EntryPoint(entry.EntryId));
        yield return Pointer(BackpackPoint(1, 2), true);
        yield return Pointer(BackpackPoint(1, 2), false);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack), driver.backpackUI.FeedbackKey);
        Assert.That(entry.Taken, Is.True);
        Assert.That(driver.items.InstanceCount, Is.EqualTo(instanceCount));
        Assert.That(source.Entries.Count, Is.EqualTo(entryCount));
        Assert.That(ItemIdentitySnapshot(item), Is.EqualTo(identity));
        Assert.That(driver.backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement placement), Is.True);
        Assert.That(placement.X, Is.EqualTo(1));
        Assert.That(placement.Y, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator QueuedUnrelatedBackpackItemAppendsRevealedEntryToAnEmptyBox()
    {
        yield return LoadScene();
        LootBox box = Box("empty");
        Vector3 position = new Vector3(box.transform.position.x - 1.1f, driver.player.transform.position.y, driver.player.transform.position.z);
        driver.player.transform.position = position;
        driver.player.Body.position = position;
        Physics.SyncTransforms();
        yield return null;
        Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
        Assert.That(box.State.Entries.Count, Is.Zero);
        Assert.That(driver.lootUI.view.contentRoot.GetComponentsInChildren<LootItemView>().Length, Is.Zero);
        ItemInstance item = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        string identity = ItemIdentitySnapshot(item);
        int count = driver.items.InstanceCount;
        Color viewportColor = ReturnViewportColor();
        string background = GridBackgroundSnapshot();
        yield return BeginPointerDrag(BackpackPoint(0, 2));
        yield return Pointer(ReturnPoint(), true);
        Rect preview = AssertLocalReturnPreview(box.State, item, viewportColor);
        yield return Press(Key.R);
        Assert.That(driver.backpackUI.PreviewRotated, Is.True);
        AssertLocalReturnPreview(box.State, item, viewportColor);
        Assert.That(preview.width, Is.EqualTo(66f).Within(0.1f));
        Assert.That(preview.height, Is.EqualTo(136f).Within(0.1f), "Box return uses the definition footprint even while the backpack preview is rotated.");
        Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
        yield return Pointer(ReturnPoint(), false);
        AssertCommittedReturnPreview(box.State, item, preview, viewportColor);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Container), driver.backpackUI.FeedbackKey);
        Assert.That(item.ContainerId, Is.EqualTo(box.State.SourceId));
        LootEntryState appended = box.State.Entries.Single();
        Assert.That(appended.InstanceId, Is.EqualTo(item.InstanceId));
        Assert.That(appended.Revealed && !appended.Taken, Is.True);
        Assert.That(driver.items.InstanceCount, Is.EqualTo(count));
        Assert.That(ItemIdentitySnapshot(item), Is.EqualTo(identity));
        Assert.That(driver.backpack.TryGetPlacement(item.InstanceId, out _), Is.False);
    }

    [UnityTest]
    public IEnumerator QueuedFoodReturnUsesLocalAutomaticPreviewAcrossEmptyCellsAndViewportBlank()
    {
        yield return LoadScene();
        LootBox box = CreateGridTestBox("return-food-empty", 4, 3);
        Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
        yield return null;
        LootSourceState source = box.State;
        ItemInstance food = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "food").Item;
        Assert.That(new Vector2Int(food.Definition.width, food.Definition.height), Is.EqualTo(new Vector2Int(2, 2)));
        string identity = ItemIdentitySnapshot(food);
        int instanceCount = driver.items.InstanceCount;
        Color viewportColor = ReturnViewportColor();
        AssertGridBackground(source);
        string background = GridBackgroundSnapshot();
        int cancellations = 0;
        System.Action onCancelled = () => cancellations++;
        driver.backpackUI.DragCancelled += onCancelled;
        try
        {
            // 第一次是外来食物；取回背包后第二次复用本箱Taken记录。
            for (int visit = 0; visit < 2; visit++)
            {
                string before = TransferSnapshot();
                string entries = SourceEntrySnapshot(source);
                string layout = GridLayoutSnapshot(source);
                yield return BeginPointerDrag(BackpackPoint(2, 0));
                int token = driver.backpackUI.DragToken;
                yield return Pointer(GridCellPoint(3, 2), true);
                Rect preview = AssertLocalReturnPreview(source, food, viewportColor);
                Assert.That(preview.width, Is.EqualTo(136f).Within(0.1f));
                Assert.That(preview.height, Is.EqualTo(136f).Within(0.1f));
                Vector2 blank = ReturnBlankPoint();
                yield return Pointer(blank, true);
                Assert.That(RectTransformUtility.RectangleContainsScreenPoint(driver.lootUI.view.contentRoot, blank, null), Is.False);
                Assert.That(driver.lootUI.view.TryHitReturnTarget(blank, null), Is.True, "Viewport blank space still accepts automatic returns.");
                AssertScreenRect(AssertLocalReturnPreview(source, food, viewportColor), preview);
                Assert.That(TransferSnapshot(), Is.EqualTo(before));
                Assert.That(SourceEntrySnapshot(source), Is.EqualTo(entries));
                Assert.That(GridLayoutSnapshot(source), Is.EqualTo(layout));
                Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));

                yield return Pointer(BackpackPoint(2, 0), true);
                AssertReturnPreviewCleared(viewportColor);
                Assert.That(driver.backpackUI.FeedbackKey, Is.EqualTo(BackpackUIController.ResultKey(BackpackResult.Success)));
                Assert.That(driver.lootUI.FeedbackKey, Is.EqualTo("loot.return_hint"));
                Assert.That(driver.backpackUI.IsDragValid(token), Is.True);
                yield return Pointer(blank, true);
                AssertLocalReturnPreview(source, food, viewportColor);
                yield return Press(Key.F10);
                AssertLocalReturnPreview(source, food, viewportColor);
                int cancellationsBefore = cancellations;
                yield return Pointer(blank, false);
                AssertCommittedReturnPreview(source, food, preview, viewportColor);
                Assert.That(cancellations, Is.GreaterThan(cancellationsBefore), "Synchronous completion cancels the native drag but must preserve both result messages.");
                LootEntryState entry = source.Entries.Single();
                Assert.That(GridEntryBounds(entry), Is.EqualTo(new RectInt(0, 0, 2, 2)));
                Assert.That(entry.InstanceId, Is.EqualTo(food.InstanceId));
                Assert.That(entry.Revealed && !entry.Taken, Is.True);
                Assert.That(food.Owner, Is.EqualTo(ItemOwner.Container));
                Assert.That(food.ContainerId, Is.EqualTo(source.SourceId));
                Assert.That(driver.backpack.TryGetPlacement(food.InstanceId, out _), Is.False);
                Assert.That(ItemIdentitySnapshot(food), Is.EqualTo(identity));
                Assert.That(driver.items.InstanceCount, Is.EqualTo(instanceCount));
                Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
                if (visit == 0)
                {
                    yield return BeginPointerDrag(EntryPoint(entry.EntryId));
                    yield return Pointer(BackpackPoint(2, 0), true);
                    yield return Pointer(BackpackPoint(2, 0), false);
                    Assert.That(entry.Taken, Is.True);
                    Assert.That(food.Owner, Is.EqualTo(ItemOwner.Backpack));
                    Assert.That(driver.backpack.TryGetPlacement(food.InstanceId, out BackpackPlacement placement), Is.True);
                    Assert.That(new Vector2Int(placement.X, placement.Y), Is.EqualTo(new Vector2Int(2, 0)));
                }
            }
        }
        finally { driver.backpackUI.DragCancelled -= onCancelled; }
    }

    [UnityTest]
    public IEnumerator QueuedCancelAndTabClearReturnPreviewAndLateReleaseCannotCommit()
    {
        yield return LoadScene();
        LootBox box = CreateGridTestBox("return-cancel-empty", 4, 3);
        ItemInstance item = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        for (int cancellation = 0; cancellation < 2; cancellation++)
        {
            Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
            yield return null;
            Color viewportColor = ReturnViewportColor();
            string before = TransferSnapshot();
            string entries = SourceEntrySnapshot(box.State);
            string background = GridBackgroundSnapshot();
            yield return BeginPointerDrag(BackpackPoint(0, 2));
            yield return Pointer(ReturnBlankPoint(), true);
            AssertLocalReturnPreview(box.State, item, viewportColor);
            int oldToken = driver.backpackUI.DragToken;
            Vector2 target = ReturnBlankPoint();
            if (cancellation == 0) { driver.backpackUI.CancelDrag(); }
            else { yield return Press(Key.Tab); }
            AssertReturnPreviewCleared(viewportColor);
            Assert.That(driver.backpackUI.IsDragging, Is.False);
            if (cancellation == 1) { Assert.That(driver.backpackUI.IsOpen || driver.lootUI.IsOpen, Is.False); }
            yield return Pointer(target, false);
            driver.backpackUI.EndDrag(oldToken, target, null);
            Assert.That(TransferSnapshot(), Is.EqualTo(before));
            Assert.That(SourceEntrySnapshot(box.State), Is.EqualTo(entries));
            if (cancellation == 1)
            {
                Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
                yield return null;
            }
            Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
            Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
            AssertReturnPreviewCleared(viewportColor);
        }
    }

    [UnityTest]
    public IEnumerator QueuedDamageCancelsValidBoxReturnAndLateTokenCannotAffectNextDrag()
    {
        yield return LoadScene();
        yield return Press(Key.E);
        LootSourceState source = driver.lootUI.Source;
        ItemInstance item = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        string identity = ItemIdentitySnapshot(item);
        int instanceCount = driver.items.InstanceCount;
        Color viewportColor = ReturnViewportColor();
        yield return BeginPointerDrag(BackpackPoint(0, 2));
        Vector2 target = ReturnPoint();
        yield return Pointer(target, true);
        AssertLocalReturnPreview(source, item, viewportColor);
        int oldToken = driver.backpackUI.DragToken;
        yield return Press(Key.R);
        Assert.That(driver.backpackUI.PreviewRotated, Is.True);
        Assert.That(driver.backpackUI.DragToken, Is.EqualTo(oldToken));
        AssertLocalReturnPreview(source, item, viewportColor);
        string before = TransferSnapshot();
        string sourceBefore = SourceEntrySnapshot(source);
        int health = driver.player.health.CurrentHealth;
        driver.DamageOne();
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health - 1));
        Assert.That(driver.player.IsDead, Is.False);
        Assert.That(driver.backpackUI.IsOpen || driver.lootUI.IsOpen || driver.backpackUI.IsDragging, Is.False);
        Assert.That(driver.lootUI.view.IsReturnPreviewVisible, Is.False);
        AssertReturnPreviewCleared(viewportColor);
        Assert.That(driver.backpackUI.view.preview.gameObject.activeSelf, Is.False);
        Assert.That(TransferSnapshot(), Is.EqualTo(before));
        Assert.That(SourceEntrySnapshot(source), Is.EqualTo(sourceBefore));
        yield return Pointer(target, false);
        driver.backpackUI.EndDrag(oldToken, target, null);
        Assert.That(TransferSnapshot(), Is.EqualTo(before));
        Assert.That(SourceEntrySnapshot(source), Is.EqualTo(sourceBefore));

        yield return Press(Key.E);
        Assert.That(driver.lootUI.Source, Is.SameAs(source));
        yield return BeginPointerDrag(BackpackPoint(0, 2));
        int currentToken = driver.backpackUI.DragToken;
        Assert.That(currentToken, Is.Not.EqualTo(oldToken));
        yield return Pointer(ReturnPoint(), true);
        Rect preview = AssertLocalReturnPreview(source, item, viewportColor);
        driver.backpackUI.EndDrag(oldToken, ReturnPoint(), null);
        Assert.That(driver.backpackUI.IsDragValid(currentToken), Is.True);
        Assert.That(TransferSnapshot(), Is.EqualTo(before));
        yield return Pointer(ReturnPoint(), false);
        AssertCommittedReturnPreview(source, item, preview, viewportColor);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Container), driver.backpackUI.FeedbackKey);
        Assert.That(item.ContainerId, Is.EqualTo(source.SourceId));
        Assert.That(ItemIdentitySnapshot(item), Is.EqualTo(identity));
        Assert.That(source.Entries.Count(value => value.InstanceId == item.InstanceId && value.Revealed && !value.Taken), Is.EqualTo(1));

        string committed = TransferSnapshot();
        string committedSource = SourceEntrySnapshot(source);
        driver.backpackUI.EndDrag(currentToken, ReturnPoint(), null);
        Assert.That(TransferSnapshot(), Is.EqualTo(committed));
        Assert.That(SourceEntrySnapshot(source), Is.EqualTo(committedSource));
        yield return Pointer(ReturnPoint(), false);
        Assert.That(TransferSnapshot(), Is.EqualTo(committed));
        Assert.That(driver.items.InstanceCount, Is.EqualTo(instanceCount));
        Assert.That(driver.items.TryGetInstance(item.InstanceId, out ItemInstance registered), Is.True);
        Assert.That(registered, Is.SameAs(item));
        Assert.That(driver.items.GetInstances().Count(value => value.InstanceId == item.InstanceId), Is.EqualTo(1));
        Assert.That(source.Entries.Count(value => value.InstanceId == item.InstanceId), Is.EqualTo(1));
        LootEntryState returned = source.Entries.Single(value => value.InstanceId == item.InstanceId);
        Assert.That(returned.Revealed && !returned.Taken, Is.True);
        Assert.That(driver.backpack.GetPlacements().Count(value => value.InstanceId == item.InstanceId), Is.Zero);
        Assert.That(driver.warehouse.TryGetPlacement(item.InstanceId, out _), Is.False);
        Assert.That(driver.backpackUI.IsDragging, Is.False);
    }

    [UnityTest]
    public IEnumerator QueuedDamageCancelsValidBoxPickupAndLateTokenCannotAffectNextDrag()
    {
        yield return LoadScene();
        yield return Press(Key.E);
        LootSourceState source = driver.lootUI.Source;
        yield return WaitForRevealed(source, 0);
        LootEntryState entry = source.Entries[0];
        Assert.That(driver.items.TryGetInstance(entry.InstanceId, out ItemInstance item), Is.True);
        string identity = ItemIdentitySnapshot(item);
        int instanceCount = driver.items.InstanceCount;
        yield return BeginPointerDrag(EntryPoint(entry.EntryId));
        Vector2 target = BackpackPoint(1, 2);
        yield return Pointer(target, true);
        Assert.That(driver.backpackUI.IsExternalPreview, Is.True);
        Assert.That(driver.backpackUI.PreviewResult, Is.EqualTo(BackpackResult.Success));
        Assert.That(driver.backpackUI.view.preview.gameObject.activeSelf, Is.True);
        int oldToken = driver.backpackUI.DragToken;
        yield return Press(Key.R);
        Assert.That(driver.backpackUI.PreviewRotated, Is.True);
        Assert.That(driver.backpackUI.DragToken, Is.EqualTo(oldToken));
        Assert.That(driver.backpackUI.PreviewResult, Is.EqualTo(BackpackResult.Success));
        Assert.That(driver.backpackUI.view.preview.gameObject.activeSelf, Is.True);
        string before = TransferSnapshot();
        string sourceBefore = SourceEntrySnapshot(source);
        int health = driver.player.health.CurrentHealth;
        driver.DamageOne();
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health - 1));
        Assert.That(driver.player.IsDead, Is.False);
        Assert.That(driver.backpackUI.IsOpen || driver.lootUI.IsOpen || driver.backpackUI.IsDragging, Is.False);
        Assert.That(driver.backpackUI.view.preview.gameObject.activeSelf, Is.False);
        Assert.That(TransferSnapshot(), Is.EqualTo(before));
        Assert.That(SourceEntrySnapshot(source), Is.EqualTo(sourceBefore));
        yield return Pointer(target, false);
        driver.lootUI.EndItemDrag(oldToken, target, null);
        Assert.That(TransferSnapshot(), Is.EqualTo(before));
        Assert.That(SourceEntrySnapshot(source), Is.EqualTo(sourceBefore));

        yield return Press(Key.E);
        yield return BeginPointerDrag(EntryPoint(entry.EntryId));
        int currentToken = driver.backpackUI.DragToken;
        Assert.That(currentToken, Is.Not.EqualTo(oldToken));
        yield return Pointer(BackpackPoint(1, 2), true);
        driver.lootUI.EndItemDrag(oldToken, BackpackPoint(1, 2), null);
        Assert.That(driver.backpackUI.IsDragValid(currentToken), Is.True);
        Assert.That(TransferSnapshot(), Is.EqualTo(before));
        yield return Pointer(BackpackPoint(1, 2), false);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack), driver.backpackUI.FeedbackKey);
        Assert.That(entry.Taken, Is.True);
        Assert.That(ItemIdentitySnapshot(item), Is.EqualTo(identity));
        Assert.That(driver.backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement placement), Is.True);
        Assert.That(placement.X, Is.EqualTo(1));
        Assert.That(placement.Y, Is.EqualTo(2));

        string committed = TransferSnapshot();
        string committedSource = SourceEntrySnapshot(source);
        driver.lootUI.EndItemDrag(currentToken, BackpackPoint(1, 2), null);
        Assert.That(TransferSnapshot(), Is.EqualTo(committed));
        Assert.That(SourceEntrySnapshot(source), Is.EqualTo(committedSource));
        yield return Pointer(BackpackPoint(1, 2), false);
        Assert.That(TransferSnapshot(), Is.EqualTo(committed));
        Assert.That(driver.items.InstanceCount, Is.EqualTo(instanceCount));
        Assert.That(driver.items.TryGetInstance(item.InstanceId, out ItemInstance registered), Is.True);
        Assert.That(registered, Is.SameAs(item));
        Assert.That(driver.items.GetInstances().Count(value => value.InstanceId == item.InstanceId), Is.EqualTo(1));
        Assert.That(item.ContainerId, Is.EqualTo(driver.backpack.ContainerId));
        Assert.That(source.Entries.Count(value => value.InstanceId == item.InstanceId), Is.EqualTo(1));
        Assert.That(source.Entries.Single(value => value.InstanceId == item.InstanceId), Is.SameAs(entry));
        Assert.That(entry.Taken, Is.True);
        Assert.That(driver.backpack.GetPlacements().Count(value => value.InstanceId == item.InstanceId), Is.EqualTo(1));
        Assert.That(placement.Item, Is.SameAs(item));
        Assert.That(placement.Rotated, Is.False, "The cancelled rotation must not leak into the next drag.");
        Assert.That(driver.warehouse.TryGetPlacement(item.InstanceId, out _), Is.False);
        Assert.That(driver.backpackUI.IsDragging, Is.False);
    }

    [UnityTest]
    public IEnumerator QueuedReturnRequiresTopmostListAndOldSessionCannotCommitAfterReopen()
    {
        yield return LoadScene();
        yield return Press(Key.E);
        LootSourceState source = driver.lootUI.Source;
        LootView view = driver.lootUI.view;
        Color viewportColor = ReturnViewportColor();
        Vector2 target = ReturnPoint();
        Assert.That(view.TryHitReturnTarget(target, null), Is.True);
        RectTransform close = (RectTransform)view.closeButton.transform;
        Assert.That(view.TryHitReturnTarget(RectTransformUtility.WorldToScreenPoint(null, close.TransformPoint(close.rect.center)), null), Is.False);
        Assert.That(view.TryHitReturnTarget(RectTransformUtility.WorldToScreenPoint(null,
            view.titleText.rectTransform.TransformPoint(view.titleText.rectTransform.rect.center)), null), Is.False);
        GameObject blocker = new GameObject("ReturnRaycastBlocker", typeof(RectTransform), typeof(Image));
        try
        {
            RectTransform blockedRect = (RectTransform)blocker.transform;
            blockedRect.SetParent(view.panelRoot.transform, false);
            blockedRect.anchorMin = Vector2.zero;
            blockedRect.anchorMax = Vector2.one;
            blockedRect.offsetMin = blockedRect.offsetMax = Vector2.zero;
            blockedRect.SetAsLastSibling();
            // 新Graphic要经过实际Canvas帧才具有射线深度；UGUI会忽略depth=-1的尚未绘制图形。
            yield return null;
            Canvas.ForceUpdateCanvases();
            Image blockerImage = blocker.GetComponent<Image>();
            Assert.That(blockerImage.depth, Is.GreaterThanOrEqualTo(0), "The blocking image must be rendered before testing pointer occlusion.");
            Assert.That(RectTransformUtility.RectangleContainsScreenPoint(blockedRect, target, null), Is.True);
            Assert.That(view.TryHitReturnTarget(target, null), Is.False, "An overlay outside the list must block returns.");
        }
        finally { Object.Destroy(blocker); }
        yield return null;

        yield return BeginPointerDrag(BackpackPoint(0, 2));
        yield return Pointer(target, true);
        int token = driver.backpackUI.DragToken;
        int session = driver.lootUI.SessionToken;
        Assert.That(view.IsReturnPreviewVisible, Is.True);
        string before = TransferSnapshot();
        int count = source.Entries.Count;
        // 同帧API探针：在Update自动关窗之前撤销探索权限，核对失效悬停的双方反馈。
        driver.runtime.SetExplorationContext(source.MapId, false);
        try
        {
            driver.backpackUI.UpdateDrag(token, target, null);
            AssertReturnFeedback("loot.return_unavailable");
            Assert.That(view.IsReturnPreviewVisible, Is.False);
            Assert.That(ReturnViewportColor(), Is.EqualTo(viewportColor));
            Assert.That(TransferSnapshot(), Is.EqualTo(before));
        }
        finally { driver.runtime.SetExplorationContext(source.MapId, true); }
        driver.backpackUI.UpdateDrag(token, target, null);
        AssertReturnFeedback("loot.return_ready");
        driver.lootUI.CloseSource();
        AssertReturnPreviewCleared(viewportColor);
        Assert.That(driver.backpackUI.IsDragValid(token), Is.False, "Closing the return target must cancel its native drag immediately.");
        Assert.That(driver.lootUI.TryOpenSource(source, Box("guaranteed"), driver.player.interaction), Is.True);
        Assert.That(driver.lootUI.SessionToken, Is.Not.EqualTo(session));
        yield return Pointer(ReturnPoint(), true);
        AssertReturnPreviewCleared(viewportColor);
        yield return Pointer(ReturnPoint(), false);
        driver.backpackUI.EndDrag(token, ReturnPoint(), null);
        Assert.That(TransferSnapshot(), Is.EqualTo(before));
        Assert.That(source.Entries.Count, Is.EqualTo(count));
        Assert.That(view.IsReturnPreviewVisible, Is.False);
        yield return BeginPointerDrag(BackpackPoint(0, 2));
        yield return Pointer(ReturnPoint(), true);
        Assert.That(driver.lootUI.FeedbackKey, Is.EqualTo("loot.return_ready"));
        yield return Pointer(ReturnPoint(), false);
        Assert.That(source.Entries.Count, Is.EqualTo(count + 1));
    }

    [UnityTest]
    public IEnumerator QueuedWheelScrollRevealsLastLootDragAndMovesTheRealWarehouseViewport()
    {
        yield return LoadScene();
        // 使用独立6×14固定容量，验证超出视口的背景空格与末项都可滚动访问。
        Vector3[] sizes = Enumerable.Repeat(new Vector3(2f, 3f, 0f), 12)
            .Concat(new[] { new Vector3(1f, 2f, 0f) }).ToArray();
        LootBox box = CreateGridTestBox("grid-scroll", 6, 14, sizes);
        Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
        LootSourceState source = driver.lootUI.Source;
        int lastIndex = source.Entries.Count - 1;
        yield return WaitForRevealed(source, lastIndex);
        yield return null;
        Canvas.ForceUpdateCanvases();
        LootEntryState last = source.Entries[lastIndex];
        Assert.That(driver.items.TryGetInstance(last.InstanceId, out ItemInstance item), Is.True);
        string identity = ItemIdentitySnapshot(item);
        int count = driver.items.InstanceCount;
        LootView view = driver.lootUI.view;
        AssertGridBackground(source);
        string background = GridBackgroundSnapshot();
        ScrollRect lootScroll = view.contentRoot.GetComponentInParent<ScrollRect>();
        RectTransform viewport = lootScroll.viewport;
        Assert.That(lootScroll.content.rect.height, Is.GreaterThan(viewport.rect.height), "The generated grid rows must overflow the real viewport.");
        LootItemView lastView = view.contentRoot.GetComponentsInChildren<LootItemView>().Single(value => value.EntryId == last.EntryId);
        RectTransform lastRect = (RectTransform)lastView.transform;
        Vector2 clippedBottom = RectTransformUtility.WorldToScreenPoint(null,
            lastRect.TransformPoint(new Vector3(lastRect.rect.center.x, lastRect.rect.yMin + 2f, 0f)));
        Assert.That(RectTransformUtility.RectangleContainsScreenPoint(viewport, clippedBottom, null), Is.False);
        Vector2 initialPosition = lootScroll.content.anchoredPosition;
        float initialNormalized = lootScroll.verticalNormalizedPosition;
        string beforeScroll = TransferSnapshot();
        Vector2 scrollPoint = RectTransformUtility.WorldToScreenPoint(null, viewport.TransformPoint(viewport.rect.center));
        for (int tick = 0; tick < 8 && lootScroll.verticalNormalizedPosition > 0.001f; tick++)
        {
            yield return QueueWheelDown(scrollPoint);
        }
        Assert.That(lootScroll.verticalNormalizedPosition, Is.LessThan(initialNormalized - 0.001f));
        Assert.That(Vector2.Distance(lootScroll.content.anchoredPosition, initialPosition), Is.GreaterThan(1f));
        Assert.That(TransferSnapshot(), Is.EqualTo(beforeScroll), "Scrolling cannot mutate items or either grid.");
        AssertGridBackground(source);
        Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
        Vector2 emptyPoint = GridCellPoint(5, 13);
        Assert.That(RectTransformUtility.RectangleContainsScreenPoint(viewport, emptyPoint, null), Is.True);
        List<UnityEngine.EventSystems.RaycastResult> emptyHits = new List<UnityEngine.EventSystems.RaycastResult>();
        UnityEngine.EventSystems.EventSystem.current.RaycastAll(
            new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current) { position = emptyPoint }, emptyHits);
        Assert.That(emptyHits, Is.Not.Empty);
        Assert.That(emptyHits[0].gameObject, Is.EqualTo(viewport.gameObject));
        float beforeEmptyWheel = lootScroll.verticalNormalizedPosition;
        yield return Pointer(emptyPoint, false);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = emptyPoint, scroll = new Vector2(0f, 120f) });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = emptyPoint, scroll = Vector2.zero });
        yield return null;
        Assert.That(lootScroll.verticalNormalizedPosition, Is.GreaterThan(beforeEmptyWheel + 0.001f),
            "Wheel input over an empty background cell must scroll the actual source viewport.");
        yield return QueueWheelDown(scrollPoint);
        Assert.That(TransferSnapshot(), Is.EqualTo(beforeScroll));
        Vector2 lastPoint = EntryPoint(last.EntryId);
        Assert.That(RectTransformUtility.RectangleContainsScreenPoint(viewport, lastPoint, null), Is.True);
        Assert.That(view.TryHitReturnTarget(lastPoint, null), Is.True, "The last row is inside the actually hit list after scrolling.");
        yield return BeginPointerDrag(lastPoint);
        Assert.That(driver.backpackUI.IsExternalPreview, Is.True);
        Assert.That(driver.backpackUI.DragInstanceId, Is.EqualTo(last.InstanceId));
        yield return Pointer(BackpackPoint(1, 2), true);
        Assert.That(driver.backpackUI.PreviewResult, Is.EqualTo(BackpackResult.Success));
        yield return Pointer(BackpackPoint(1, 2), false);
        Assert.That(last.Taken, Is.True);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack), driver.backpackUI.FeedbackKey);
        Assert.That(driver.backpack.TryGetPlacement(last.InstanceId, out _), Is.True);
        Assert.That(driver.items.InstanceCount, Is.EqualTo(count));
        Assert.That(ItemIdentitySnapshot(item), Is.EqualTo(identity));

        // 原位返还仍使用滚动后的Content坐标，预览不能浮在未滚动的viewport坐标上。
        Color viewportColor = ReturnViewportColor();
        yield return BeginPointerDrag(BackpackPoint(1, 2));
        yield return Pointer(lastPoint, true);
        Rect returnPreview = AssertLocalReturnPreview(source, item, viewportColor);
        Assert.That(RectTransformUtility.RectangleContainsScreenPoint(viewport, returnPreview.center, null), Is.True);
        yield return Pointer(lastPoint, false);
        AssertCommittedReturnPreview(source, item, returnPreview, viewportColor);
        Assert.That(last.Taken, Is.False);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(driver.backpack.TryGetPlacement(item.InstanceId, out _), Is.False);
        Assert.That(driver.items.InstanceCount, Is.EqualTo(count));
        Assert.That(ItemIdentitySnapshot(item), Is.EqualTo(identity));
        Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));

        driver.EnterShelter();
        yield return null;
        Canvas.ForceUpdateCanvases();
        ScrollRect warehouseScroll = driver.warehouseUI.view.scrollRect;
        Assert.That(driver.warehouseUI.IsOpen, Is.True);
        Assert.That(warehouseScroll.content.rect.height, Is.GreaterThan(warehouseScroll.viewport.rect.height));
        Vector2 warehousePosition = warehouseScroll.content.anchoredPosition;
        float warehouseNormalized = warehouseScroll.verticalNormalizedPosition;
        beforeScroll = TransferSnapshot();
        scrollPoint = RectTransformUtility.WorldToScreenPoint(null,
            warehouseScroll.viewport.TransformPoint(warehouseScroll.viewport.rect.center));
        yield return QueueWheelDown(scrollPoint);
        Assert.That(warehouseScroll.verticalNormalizedPosition, Is.LessThan(warehouseNormalized - 0.001f));
        Assert.That(Vector2.Distance(warehouseScroll.content.anchoredPosition, warehousePosition), Is.GreaterThan(1f));
        Assert.That(warehouseScroll.horizontalNormalizedPosition, Is.EqualTo(0f).Within(0.001f));
        Assert.That(TransferSnapshot(), Is.EqualTo(beforeScroll));
    }

    private IEnumerator QueueWheelDown(Vector2 point)
    {
        yield return Pointer(point, false);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 0, scroll = new Vector2(0f, -120f) });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 0, scroll = Vector2.zero });
        yield return null;
        yield return null;
    }

    private Vector2 ReturnPoint()
    {
        Canvas.ForceUpdateCanvases();
        RectTransform rect = driver.lootUI.view.GetReturnViewport();
        Assert.That(rect, Is.Not.Null);
        return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
    }

    private Vector2 ReturnBlankPoint()
    {
        RectTransform rect = driver.lootUI.view.GetReturnViewport();
        Canvas.ForceUpdateCanvases();
        return RectTransformUtility.WorldToScreenPoint(null,
            rect.TransformPoint(new Vector3(rect.rect.center.x, rect.rect.yMin + 30f, 0f)));
    }

    private Color ReturnViewportColor()
    {
        return driver.lootUI.view.GetReturnViewport().GetComponent<Image>().color;
    }

    private Rect AssertLocalReturnPreview(LootSourceState source, ItemInstance item, Color viewportColor)
    {
        LootView view = driver.lootUI.view;
        Canvas.ForceUpdateCanvases();
        Assert.That(driver.runtime.TryGetReturnedItemPosition(source, item, out RectInt planned), Is.True);
        Assert.That(view.IsReturnPreviewVisible, Is.True);
        Assert.That(view.ReturnPreviewRect, Is.Not.Null);
        RectTransform preview = view.ReturnPreviewRect;
        Assert.That(preview.gameObject.activeInHierarchy, Is.True);
        Assert.That(preview.parent, Is.EqualTo(view.contentRoot));
        Assert.That(preview.GetComponent<Image>(), Is.Null, "The local target is an outline, not a filled panel.");
        Assert.That(preview.GetComponentsInChildren<TMPro.TMP_Text>(true), Is.Empty);
        Image[] borders = preview.GetComponentsInChildren<Image>();
        Assert.That(borders, Has.Length.EqualTo(4));
        foreach (Image border in borders)
        {
            Assert.That(border.raycastTarget, Is.False, "Return preview graphics must not intercept the real pointer.");
            Assert.That(Mathf.Min(border.rectTransform.rect.width, border.rectTransform.rect.height), Is.LessThanOrEqualTo(3.1f));
        }
        Assert.That(planned.width, Is.EqualTo(item.Definition.width));
        Assert.That(planned.height, Is.EqualTo(item.Definition.height));
        Assert.That(preview.anchoredPosition, Is.EqualTo(new Vector2(view.padding + planned.x * (view.cellSize + view.gap),
            -view.padding - planned.y * (view.cellSize + view.gap))));
        Vector2 size = GridScreenSize(preview);
        Assert.That(size.x, Is.EqualTo(planned.width * 66f + (planned.width - 1) * 4f).Within(0.1f));
        Assert.That(size.y, Is.EqualTo(planned.height * 66f + (planned.height - 1) * 4f).Within(0.1f));
        Assert.That(ReturnViewportColor(), Is.EqualTo(viewportColor));
        AssertReturnFeedback("loot.return_ready");
        Canvas.ForceUpdateCanvases();
        return ReturnScreenRect(preview);
    }

    private void AssertCommittedReturnPreview(LootSourceState source, ItemInstance item, Rect preview, Color viewportColor)
    {
        LootEntryState entry = source.Entries.Single(value => value.InstanceId == item.InstanceId && !value.Taken);
        Canvas.ForceUpdateCanvases();
        AssertScreenRect(ReturnScreenRect((RectTransform)GridItemView(entry.EntryId).transform), preview);
        Assert.That(driver.lootUI.view.IsReturnPreviewVisible, Is.False);
        Assert.That(driver.lootUI.view.ReturnPreviewRect.gameObject.activeSelf, Is.False);
        Assert.That(ReturnViewportColor(), Is.EqualTo(viewportColor));
        AssertReturnFeedback(ItemTransferCoordinator.ResultKey(ItemTransferResult.Success));
    }

    private void AssertReturnFeedback(string key)
    {
        Assert.That(driver.backpackUI.FeedbackKey, Is.EqualTo(key), "The backpack must describe the same return result as the source.");
        Assert.That(driver.lootUI.FeedbackKey, Is.EqualTo(key));
        string text = LocalizationManager.Instance.GetText(key);
        Assert.That(driver.backpackUI.view.feedbackText.text, Is.EqualTo(text));
        Assert.That(driver.lootUI.view.feedbackText.text, Is.EqualTo(text));
    }

    private void AssertReturnPreviewCleared(Color viewportColor)
    {
        LootView view = driver.lootUI.view;
        Assert.That(view.IsReturnPreviewVisible, Is.False);
        Assert.That(view.ReturnPreviewRect == null || !view.ReturnPreviewRect.gameObject.activeSelf, Is.True);
        Assert.That(ReturnViewportColor(), Is.EqualTo(viewportColor));
        Assert.That(driver.lootUI.FeedbackKey, Is.Not.EqualTo("loot.return_ready"));
        Assert.That(driver.lootUI.FeedbackKey, Is.Not.EqualTo("loot.return_full"));
        Assert.That(driver.lootUI.FeedbackKey, Is.Not.EqualTo("loot.return_unavailable"));
    }

    private static Rect ReturnScreenRect(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        return new Rect(min, max - min);
    }

    private static void AssertScreenRect(Rect actual, Rect expected)
    {
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.1f));
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.1f));
        Assert.That(actual.width, Is.EqualTo(expected.width).Within(0.1f));
        Assert.That(actual.height, Is.EqualTo(expected.height).Within(0.1f));
    }

    private string TransferSnapshot()
    {
        return driver.items.InstanceCount + ":" + driver.shelter.CurrentEv + "\n"
            + string.Join("\n", driver.items.GetInstances().OrderBy(value => value.InstanceId)
                .Select(value => ItemIdentitySnapshot(value) + ":" + value.Owner + ":" + value.ContainerId + ":" + value.SpawnClaimed))
            + "\nBackpack:" + string.Join("|", driver.backpack.GetPlacements().OrderBy(value => value.InstanceId)
                .Select(value => value.InstanceId + ":" + value.X + ":" + value.Y + ":" + value.Rotated))
            + "\nWarehouse:" + string.Join("|", driver.warehouse.GetPlacements().OrderBy(value => value.InstanceId)
                .Select(value => value.InstanceId + ":" + value.X + ":" + value.Y + ":" + value.Rotated));
    }

    private static string SourceEntrySnapshot(LootSourceState source)
    {
        return source.Entries.Count + ":" + string.Join("|", source.Entries.Select(value =>
            value.EntryId + ":" + value.InstanceId + ":" + value.Revealed + ":" + value.Taken));
    }

    private static string ItemIdentitySnapshot(ItemInstance item)
    {
        return item.InstanceId + ":" + item.DefinitionId + ":" + item.UniqueId + ":" + item.SourceId
            + ":" + item.OriginalSpawnPointId + ":" + item.UseRound + ":" + item.UsesThisRound + ":" + item.IsRetained
            + ":" + item.RecoveryRound + ":" + item.LastLossReason;
    }
}
#endif
