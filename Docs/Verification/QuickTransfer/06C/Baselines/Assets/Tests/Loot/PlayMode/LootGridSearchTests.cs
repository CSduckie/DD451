#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public partial class LootIntegrationTests
{
    private readonly List<Object> gridTestFixtures = new List<Object>();

    [UnityTest]
    public IEnumerator QueuedGridUnknownOutlinesHaveOneCenteredCountdownAndMatchBackpackPixels()
    {
        yield return LoadScene();
        LootBox box = CreateGridTestBox("grid-mixed", new Vector3(1, 1, 3), new Vector3(1, 2, 4),
            new Vector3(2, 2, 1), new Vector3(2, 3, 1));
        Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
        yield return null;
        Canvas.ForceUpdateCanvases();
        LootSourceState source = box.State;
        Assert.That(Screen.width, Is.EqualTo(1920));
        Assert.That(Screen.height, Is.EqualTo(1080));
        LootView view = driver.lootUI.view;
        Assert.That(source.GridWidth, Is.EqualTo(6));
        Assert.That(source.GridHeight, Is.EqualTo(6));
        Assert.That(view.cellSize, Is.EqualTo(66));
        Assert.That(view.gap, Is.EqualTo(4));
        Assert.That(view.padding, Is.EqualTo(6));
        Assert.That(driver.backpackUI.view.cellSize, Is.EqualTo(view.cellSize));
        Assert.That(driver.backpackUI.view.gap, Is.EqualTo(view.gap));
        Assert.That(driver.backpackUI.view.padding, Is.EqualTo(view.padding));
        Assert.That(source.Entries.All(entry => !entry.Revealed && !entry.Taken), Is.True);
        AssertGridBackground(source);
        string background = GridBackgroundSnapshot();
        string layout = GridLayoutSnapshot(source);
        string data = TransferSnapshot();
        string selection = driver.backpackUI.SelectedInstanceId;
        Color neutral = GridItemView(source.Entries[0].EntryId).outline[0].color;
        foreach (LootEntryState entry in source.Entries)
        {
            LootItemView tile = GridItemView(entry.EntryId);
            AssertUnknownGridTile(tile);
            Assert.That(tile.outline, Has.Length.EqualTo(4));
            foreach (Image border in tile.outline)
            {
                Assert.That(border.gameObject.activeInHierarchy, Is.True);
                Assert.That(border.raycastTarget, Is.False);
                Assert.That(border.color, Is.EqualTo(neutral));
            }
            Vector2 actual = GridScreenSize((RectTransform)tile.transform);
            Assert.That(actual.x, Is.EqualTo(66 * entry.LayoutWidth + 4 * (entry.LayoutWidth - 1)).Within(0.1f));
            Assert.That(actual.y, Is.EqualTo(66 * entry.LayoutHeight + 4 * (entry.LayoutHeight - 1)).Within(0.1f));
        }
        Vector2 backpackStep = BackpackPoint(1, 2) - BackpackPoint(0, 2);
        Assert.That(backpackStep.x, Is.EqualTo(70).Within(0.1f), "The backpack's actual screen step must match the 66+4 loot unit.");
        foreach (BackpackPlacement placement in driver.backpack.GetPlacements())
        {
            LootEntryState matching = source.Entries.FirstOrDefault(entry => entry.LayoutWidth == placement.Width && entry.LayoutHeight == placement.Height);
            if (matching == null) { continue; }
            BackpackItemView carriedView = driver.backpackUI.view.itemRoot.GetComponentsInChildren<BackpackItemView>()
                .Single(value => value.InstanceId == placement.InstanceId);
            Assert.That(Vector2.Distance(GridScreenSize((RectTransform)carriedView.transform),
                GridScreenSize((RectTransform)GridItemView(matching.EntryId).transform)), Is.LessThan(0.1f),
                "Equal multi-cell items must have equal actual screen rectangles in backpack and loot.");
        }
        AssertSingleGridCountdown(source);
        yield return Pointer(EntryPoint(source.Entries[0].EntryId), false);
        yield return Click(EntryPoint(source.Entries[0].EntryId));
        yield return RightPointer(EntryPoint(source.Entries[0].EntryId));
        yield return BeginPointerDrag(EntryPoint(source.Entries[0].EntryId));
        yield return Pointer(BackpackPoint(1, 2), false);
        Assert.That(driver.backpackUI.IsDragging, Is.False, "Unknown outlines must not start a transferable drag.");
        Assert.That(driver.backpackUI.SelectedInstanceId, Is.EqualTo(selection));
        Assert.That(TransferSnapshot(), Is.EqualTo(data));
        foreach (LootEntryState entry in source.Entries) { AssertUnknownGridTile(GridItemView(entry.EntryId)); }
        Assert.That(LocalizationManager.Instance.CurrentLanguage, Is.EqualTo(GameLanguage.Chinese));
        AssertLootPanelTextFits();
        float elapsed = source.SearchElapsed;
        string searchId = source.SearchEntryId;
        yield return Press(Key.F10);
        Assert.That(source.SearchEntryId, Is.EqualTo(searchId));
        Assert.That(source.SearchElapsed, Is.GreaterThanOrEqualTo(elapsed));
        Assert.That(GridLayoutSnapshot(source), Is.EqualTo(layout));
        Assert.That(TransferSnapshot(), Is.EqualTo(data));
        AssertSingleGridCountdown(source);
        Assert.That(GridItemView(searchId).countdownText.text, Does.EndWith("s"));
        Assert.That(LocalizationManager.Instance.CurrentLanguage, Is.EqualTo(GameLanguage.English));
        AssertLootPanelTextFits();
        AssertGridBackground(source);
        Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
        yield return WaitForRevealed(source, 0);
        Assert.That(GridItemView(source.Entries[0].EntryId).nameText.text, Is.Not.Empty);
        Assert.That(GridItemView(source.Entries[0].EntryId).icon.gameObject.activeInHierarchy, Is.True);
        AssertLootPanelTextFits();
        Assert.That(source.SearchEntryId, Is.EqualTo(source.Entries[1].EntryId));
        AssertSingleGridCountdown(source);
        Assert.That(source.Entries.All(entry => !entry.Taken), Is.True, "Identification must not pick items up automatically.");
        Assert.That(driver.items.GetInstances().Where(item => item.ContainerId == source.SourceId)
            .All(item => item.Owner == ItemOwner.Container), Is.True);
        Assert.That(GridLayoutSnapshot(source), Is.EqualTo(layout));
        AssertGridBackground(source);
        Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
    }

    [UnityTest]
    public IEnumerator QueuedGridInterruptedSecondSearchAndPickupReturnKeepEveryOriginalSlot()
    {
        yield return LoadScene();
        LootBox box = CreateGridTestBox("grid-stable", new Vector3(1, 1, 0.25f), new Vector3(1, 2, 4),
            new Vector3(2, 2, 1), new Vector3(2, 3, 1));
        Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
        LootSourceState source = box.State;
        yield return WaitForRevealed(source, 0);
        LootEntryState first = source.Entries[0];
        LootEntryState second = source.Entries[1];
        float limit = Time.realtimeSinceStartup + 5f;
        while (source.SearchElapsed < 2f && Time.realtimeSinceStartup < limit) { yield return null; }
        Assert.That(source.SearchEntryId, Is.EqualTo(second.EntryId));
        Assert.That(second.Revealed, Is.False);
        Assert.That(source.SearchElapsed, Is.GreaterThanOrEqualTo(2f));
        string layout = GridLayoutSnapshot(source);
        AssertGridBackground(source);
        string background = GridBackgroundSnapshot();
        Vector2 originalPosition = ((RectTransform)GridItemView(first.EntryId).transform).anchoredPosition;
        Color viewportColor = ReturnViewportColor();
        Button closeButton = driver.lootUI.view.closeButton;
        RectTransform close = (RectTransform)closeButton.transform;
        Vector2 closePoint = RectTransformUtility.WorldToScreenPoint(null, close.TransformPoint(close.rect.center));
        System.Func<string> pointerState = () =>
        {
            UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
            List<UnityEngine.EventSystems.RaycastResult> hits = new List<UnityEngine.EventSystems.RaycastResult>();
            if (events != null)
            {
                events.RaycastAll(new UnityEngine.EventSystems.PointerEventData(events) { position = closePoint }, hits);
            }
            var module = events != null ? events.currentInputModule as UnityEngine.InputSystem.UI.InputSystemUIInputModule : null;
            UnityEngine.EventSystems.RaycastResult actualRaycast = module != null ? module.GetLastRaycastResult(mouse.deviceId) : default;
            string actionPoint = module != null && module.point != null && module.point.action != null
                ? module.point.action.ReadValue<Vector2>() + "/" + module.point.action.activeControl?.device : "none";
            string actionClick = module != null && module.leftClick != null && module.leftClick.action != null
                ? module.leftClick.action.ReadValue<float>() + "/" + module.leftClick.action.activeControl?.device : "none";
            return "frame=" + Time.frameCount + " screen=" + Screen.width + "x" + Screen.height
                + " focused=" + Application.isFocused + " background=" + Application.runInBackground
                + " close=" + closePoint + " active=" + closeButton.gameObject.activeInHierarchy + " interactable=" + closeButton.IsInteractable()
                + " top=" + (hits.Count > 0 ? hits[0].gameObject.name : "none")
                + " topIsClose=" + (hits.Count > 0 && (hits[0].gameObject == closeButton.gameObject
                    || hits[0].gameObject.transform.IsChildOf(closeButton.transform)))
                + " allHits=" + string.Join(",", hits.Take(6).Select(hit => hit.gameObject.name))
                + " selected=" + (events != null && events.currentSelectedGameObject != null ? events.currentSelectedGameObject.name : "none")
                + " testMouse=" + mouse.deviceId + "/" + mouse.position.ReadValue() + "/" + mouse.leftButton.isPressed
                + " currentIsTest=" + (Mouse.current == mouse) + " current=" + Mouse.current
                + " moduleRaycast=" + actualRaycast.screenPosition + "/" + (actualRaycast.gameObject != null ? actualRaycast.gameObject.name : "none")
                + " pointAction=" + actionPoint + " clickAction=" + actionClick
                + " mice=" + string.Join(";", InputSystem.devices.OfType<Mouse>().Select(device => device.deviceId + "/native="
                    + device.native + "/enabled=" + device.enabled + "/" + device.position.ReadValue() + "/left=" + device.leftButton.isPressed));
        };
        // 与Click相同的三次排队事件，诊断分别记录悬停、按下和松开，避免把未送达的输入当成业务失败。
        yield return Pointer(closePoint, false);
        string beforeClick = pointerState();
        bool closeWasActive = closeButton.IsActive() && closeButton.IsInteractable();
        yield return Pointer(closePoint, true);
        string whilePressed = pointerState();
        yield return Pointer(closePoint, false);
        string afterClick = pointerState();
        string closeDiagnostic = "Before: " + beforeClick + "\nPressed: " + whilePressed + "\nAfter: " + afterClick;
        Assert.That(closeWasActive, Is.True, closeDiagnostic);
        Assert.That(Vector2.Distance(mouse.position.ReadValue(), closePoint), Is.LessThan(0.1f), closeDiagnostic);
        Assert.That(driver.lootUI.IsOpen, Is.False, closeDiagnostic);
        Assert.That(source.SearchElapsed, Is.Zero);
        Assert.That(first.Revealed && !second.Revealed, Is.True);
        Assert.That(GridLayoutSnapshot(source), Is.EqualTo(layout));
        Assert.That(driver.lootUI.TryOpenSource(source, box, driver.player.interaction), Is.True);
        Assert.That(source.SearchElapsed, Is.Zero);
        Assert.That(source.SearchEntryId, Is.EqualTo(second.EntryId));
        AssertSingleGridCountdown(source);
        Assert.That(GridLayoutSnapshot(source), Is.EqualTo(layout));

        float elapsed = source.SearchElapsed;
        yield return BeginPointerDrag(EntryPoint(first.EntryId));
        yield return Pointer(BackpackPoint(1, 2), true);
        yield return Pointer(BackpackPoint(1, 2), false);
        Assert.That(first.Taken, Is.True);
        Assert.That(GridItemView(first.EntryId, true).gameObject.activeSelf, Is.False, "Taken items leave an empty slot, not a visible taken tile.");
        Assert.That(source.SearchEntryId, Is.EqualTo(second.EntryId));
        Assert.That(source.SearchElapsed, Is.GreaterThanOrEqualTo(elapsed));
        Assert.That(GridLayoutSnapshot(source), Is.EqualTo(layout));
        Assert.That(driver.items.TryGetInstance(first.InstanceId, out ItemInstance firstItem), Is.True);
        yield return BeginPointerDrag(BackpackPoint(1, 2));
        yield return Pointer(ReturnPoint(), true);
        Rect originalPreview = AssertLocalReturnPreview(source, firstItem, viewportColor);
        AssertSingleGridCountdown(source);
        AssertUnknownGridTile(GridItemView(second.EntryId));
        Assert.That(GridLayoutSnapshot(source), Is.EqualTo(layout));
        Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
        yield return Pointer(ReturnPoint(), false);
        AssertCommittedReturnPreview(source, firstItem, originalPreview, viewportColor);
        Assert.That(first.Revealed && !first.Taken, Is.True);
        Assert.That(((RectTransform)GridItemView(first.EntryId).transform).anchoredPosition, Is.EqualTo(originalPosition));
        Assert.That(GridLayoutSnapshot(source), Is.EqualTo(layout));
        AssertGridBackground(source);
        Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
        int originalCount = source.Entries.Count;
        ItemInstance externalItem = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        Assert.That(driver.backpackUI.IsDragging, Is.False, "Returning the original entry must finish the previous backpack drag.");
        elapsed = source.SearchElapsed;
        yield return BeginPointerDrag(BackpackPoint(0, 2));
        Assert.That(driver.backpackUI.IsDragging, Is.True, "The external item's real pointer drag must start: " + driver.backpackUI.FeedbackKey);
        Assert.That(driver.backpackUI.IsExternalPreview, Is.False);
        Assert.That(driver.backpackUI.DragInstanceId, Is.EqualTo(externalItem.InstanceId));
        int externalToken = driver.backpackUI.DragToken;
        Vector2 destination = ReturnPoint();
        Assert.That(Vector2.Distance(mouse.position.ReadValue(), destination), Is.GreaterThan(20f), "The queued cross-panel move must change the mouse position.");
        Assert.That(driver.lootUI.view.TryHitReturnTarget(destination, null), Is.True);
        yield return Pointer(destination, true);
        Assert.That(driver.backpackUI.IsDragValid(externalToken), Is.True, driver.backpackUI.FeedbackKey);
        Rect externalPreview = AssertLocalReturnPreview(source, externalItem, viewportColor);
        AssertSingleGridCountdown(source);
        AssertUnknownGridTile(GridItemView(second.EntryId));
        Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
        yield return Pointer(destination, false);
        AssertCommittedReturnPreview(source, externalItem, externalPreview, viewportColor);
        Assert.That(externalItem.Owner, Is.EqualTo(ItemOwner.Container), "External item return failed: "
            + driver.backpackUI.FeedbackKey + " / " + driver.lootUI.FeedbackKey);
        Assert.That(externalItem.ContainerId, Is.EqualTo(source.SourceId));
        Assert.That(driver.backpack.TryGetPlacement(externalItem.InstanceId, out _), Is.False);
        Assert.That(source.Entries.Count, Is.EqualTo(originalCount + 1));
        LootEntryState appended = source.Entries.Last();
        Assert.That(appended.HasLayout && appended.Revealed && !appended.Taken, Is.True);
        Assert.That(GridLayoutSnapshot(source, originalCount), Is.EqualTo(layout));
        Assert.That(source.SearchEntryId, Is.EqualTo(second.EntryId));
        Assert.That(source.SearchElapsed, Is.GreaterThanOrEqualTo(elapsed));
        string allSlots = GridLayoutSnapshot(source);
        string data = TransferSnapshot();
        elapsed = source.SearchElapsed;
        yield return Press(Key.F10);
        Assert.That(GridLayoutSnapshot(source), Is.EqualTo(allSlots));
        Assert.That(TransferSnapshot(), Is.EqualTo(data));
        Assert.That(source.SearchEntryId, Is.EqualTo(second.EntryId));
        Assert.That(source.SearchElapsed, Is.GreaterThanOrEqualTo(elapsed));
        AssertSingleGridCountdown(source);
        AssertGridBackground(source);
        Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
    }

    [UnityTest]
    public IEnumerator GridZeroSecondAndEmptySourcesShowCompletionWhileWideItemsRemainFullSize()
    {
        yield return LoadScene();
        LootBox box = CreateGridTestBox("grid-zero-wide", 7, 2, new Vector3(1, 1, 0), new Vector3(7, 1, 0));
        Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
        yield return null;
        Canvas.ForceUpdateCanvases();
        LootSourceState source = box.State;
        Assert.That(source.Entries.All(entry => entry.Revealed && !entry.Taken), Is.True);
        Assert.That(source.SearchEntryId, Is.Empty);
        Assert.That(source.SearchElapsed, Is.Zero);
        Assert.That(ActiveGridCountdowns().Length, Is.Zero);
        Assert.That(driver.lootUI.view.statusText.text, Does.StartWith(LocalizationManager.Instance.GetText("loot.complete")));
        Assert.That(driver.lootUI.view.statusText.text, Does.Contain(LocalizationManager.Instance.GetText("loot.wide_hint")));
        LootEntryState wide = source.Entries[1];
        Assert.That(wide.LayoutWidth, Is.EqualTo(7));
        Assert.That(source.LayoutColumns, Is.EqualTo(7));
        Assert.That(source.LayoutRows, Is.EqualTo(2));
        AssertGridBackground(source);
        string background = GridBackgroundSnapshot();
        Assert.That(GridScreenSize((RectTransform)GridItemView(wide.EntryId).transform).x, Is.EqualTo(486f).Within(0.1f));
        ScrollRect scroll = driver.lootUI.view.contentRoot.GetComponentInParent<ScrollRect>();
        Assert.That(scroll.horizontal, Is.True, "A valid wide container must remain reachable at its actual unit size.");
        Assert.That(scroll.content.rect.width, Is.GreaterThan(scroll.viewport.rect.width));
        RectTransform wideRect = (RectTransform)GridItemView(wide.EntryId).transform;
        Vector3 rightEdge = new Vector3(wideRect.rect.xMax - 2f, wideRect.rect.center.y, 0f);
        Assert.That(RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport,
            RectTransformUtility.WorldToScreenPoint(null, wideRect.TransformPoint(rightEdge)), null), Is.False);
        Vector2 wheelPoint = GridCellPoint(5, 0);
        Assert.That(RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport, wheelPoint, null), Is.True);
        List<RaycastResult> emptyCellHits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = wheelPoint }, emptyCellHits);
        Assert.That(emptyCellHits, Is.Not.Empty);
        Assert.That(emptyCellHits[0].gameObject, Is.EqualTo(scroll.viewport.gameObject), "Empty background cells must pass wheel input to the viewport.");
        yield return Pointer(wheelPoint, false);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = wheelPoint, scroll = new Vector2(-120f, 0f) });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = wheelPoint, scroll = Vector2.zero });
        yield return null;
        Assert.That(scroll.horizontalNormalizedPosition, Is.GreaterThan(0f));
        Assert.That(RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport,
            RectTransformUtility.WorldToScreenPoint(null, wideRect.TransformPoint(rightEdge)), null), Is.True);
        string layout = GridLayoutSnapshot(source);
        yield return Press(Key.F10);
        Assert.That(GridLayoutSnapshot(source), Is.EqualTo(layout));
        Assert.That(driver.lootUI.view.statusText.text, Does.StartWith(LocalizationManager.Instance.GetText("loot.complete")));
        Assert.That(driver.lootUI.view.statusText.text, Does.Contain(LocalizationManager.Instance.GetText("loot.wide_hint")));
        Assert.That(ActiveGridCountdowns().Length, Is.Zero);
        AssertGridBackground(source);
        Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
        driver.lootUI.CloseSource();
        LootBox empty = CreateGridTestBox("grid-empty");
        Assert.That(driver.lootUI.TryOpenSource(empty.State, empty, driver.player.interaction), Is.True);
        yield return null;
        Assert.That(empty.State.Entries, Is.Empty);
        Assert.That(driver.lootUI.view.contentRoot.GetComponentsInChildren<LootItemView>(), Is.Empty);
        Assert.That(driver.lootUI.view.statusText.text, Is.EqualTo(LocalizationManager.Instance.GetText("loot.empty")));
        Assert.That(ActiveGridCountdowns().Length, Is.Zero);
        AssertGridBackground(empty.State);
    }

    [UnityTest]
    public IEnumerator QueuedEmptyContainersWithEqualCellCountsKeepTheirOwnShapeAndLanguageState()
    {
        yield return LoadScene();
        LootBox wide = CreateGridTestBox("grid-empty-four-three", 4, 3);
        LootBox tall = CreateGridTestBox("grid-empty-three-four", 3, 4);
        LootView view = driver.lootUI.view;
        string data = TransferSnapshot();
        foreach (LootBox box in new[] { wide, tall, wide })
        {
            Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.That(box.State.GridWidth, Is.EqualTo(box.gridWidth));
            Assert.That(box.State.GridHeight, Is.EqualTo(box.gridHeight));
            Assert.That(box.State.Entries, Is.Empty);
            Assert.That(view.contentRoot.GetComponentsInChildren<LootItemView>(), Is.Empty);
            Assert.That(ActiveGridCountdowns(), Is.Empty);
            AssertGridBackground(box.State);
            Vector2 point = GridCellPoint(box.gridWidth - 1, box.gridHeight - 1);
            yield return Pointer(point, false);
            List<RaycastResult> hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Assert.That(hits, Is.Not.Empty);
            Assert.That(hits[0].gameObject, Is.EqualTo(view.GetReturnViewport().gameObject),
                "An empty background cell must leave the visible viewport as the pointer target.");
            Assert.That(view.TryHitReturnTarget(point, null), Is.True);
            string background = GridBackgroundSnapshot();
            yield return Press(Key.F10);
            Assert.That(view.statusText.text, Is.EqualTo(LocalizationManager.Instance.GetText("loot.empty")));
            AssertGridBackground(box.State);
            Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
            Assert.That(TransferSnapshot(), Is.EqualTo(data));
            driver.lootUI.CloseSource();
        }
    }

    [UnityTest]
    public IEnumerator QueuedFullContainerReturnRejectsWithoutChangingIdentityLayoutOrNotifications()
    {
        yield return LoadScene();
        LootBox box = CreateGridTestBox("grid-full", 1, 2, new Vector3(1, 2, 0));
        Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
        yield return null;
        LootSourceState source = box.State;
        Assert.That(source.Entries[0].Revealed, Is.True);
        Assert.That(source.SearchEntryId, Is.Empty);
        AssertGridBackground(source);
        ItemInstance item = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        string identity = ItemIdentitySnapshot(item);
        string data = TransferSnapshot();
        string entries = SourceEntrySnapshot(source);
        string layout = GridLayoutSnapshot(source);
        string background = GridBackgroundSnapshot();
        Color viewportColor = ReturnViewportColor();
        int backpackChanges = 0;
        int sourceChanges = 0;
        int completions = 0;
        System.Action onBackpack = () => backpackChanges++;
        System.Action<LootSourceState> onSource = value => { if (ReferenceEquals(value, source)) { sourceChanges++; } };
        System.Action<string> onCompleted = value => completions++;
        driver.backpack.Changed += onBackpack;
        driver.runtime.SourceChanged += onSource;
        driver.coordinator.Completed += onCompleted;
        try
        {
            yield return BeginPointerDrag(BackpackPoint(0, 2));
            Assert.That(driver.backpackUI.DragInstanceId, Is.EqualTo(item.InstanceId));
            Vector2 target = GridCellPoint(0, 0);
            yield return Pointer(target, true);
            Assert.That(driver.lootUI.view.IsReturnPreviewVisible, Is.False, "A full container has no valid rectangle to highlight.");
            Assert.That(driver.lootUI.view.ReturnPreviewRect == null || !driver.lootUI.view.ReturnPreviewRect.gameObject.activeSelf, Is.True);
            Assert.That(ReturnViewportColor(), Is.EqualTo(viewportColor));
            AssertReturnFeedback("loot.return_full");
            Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
            yield return Pointer(target, false);
            AssertReturnFeedback(ItemTransferCoordinator.ResultKey(ItemTransferResult.Occupied));
            AssertReturnPreviewCleared(viewportColor);
            Assert.That(driver.backpackUI.IsDragging, Is.False);
            Assert.That(driver.lootUI.view.IsReturnPreviewVisible, Is.False);
            Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
            Assert.That(ItemIdentitySnapshot(item), Is.EqualTo(identity));
            Assert.That(TransferSnapshot(), Is.EqualTo(data));
            Assert.That(SourceEntrySnapshot(source), Is.EqualTo(entries));
            Assert.That(GridLayoutSnapshot(source), Is.EqualTo(layout));
            Assert.That(source.SearchEntryId, Is.Empty);
            Assert.That(source.SearchElapsed, Is.Zero);
            AssertGridBackground(source);
            Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
            Assert.That(backpackChanges, Is.Zero);
            Assert.That(sourceChanges, Is.Zero);
            Assert.That(completions, Is.Zero);
        }
        finally
        {
            driver.backpack.Changed -= onBackpack;
            driver.runtime.SourceChanged -= onSource;
            driver.coordinator.Completed -= onCompleted;
        }
    }

    [UnityTest]
    public IEnumerator QueuedTakenCellsAreReusableAndOriginalReturnFindsAnotherSlotWithoutMovingItems()
    {
        yield return LoadScene();
        LootBox box = CreateGridTestBox("grid-reuse", 3, 2, new Vector3(1, 2, 0), new Vector3(1, 2, 0));
        Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
        yield return null;
        LootSourceState source = box.State;
        LootEntryState original = source.Entries[0];
        LootEntryState stationary = source.Entries[1];
        RectInt oldBounds = GridEntryBounds(original);
        RectInt stationaryBounds = GridEntryBounds(stationary);
        Vector2 stationaryViewPosition = ((RectTransform)GridItemView(stationary.EntryId).transform).anchoredPosition;
        Assert.That(driver.items.TryGetInstance(original.InstanceId, out ItemInstance originalItem), Is.True);
        ItemInstance external = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        string originalIdentity = ItemIdentitySnapshot(originalItem);
        string externalIdentity = ItemIdentitySnapshot(external);
        int instances = driver.items.InstanceCount;
        AssertGridBackground(source);
        string background = GridBackgroundSnapshot();
        Color viewportColor = ReturnViewportColor();

        yield return BeginPointerDrag(EntryPoint(original.EntryId));
        Assert.That(driver.backpackUI.DragInstanceId, Is.EqualTo(original.InstanceId));
        yield return Pointer(BackpackPoint(1, 2), true);
        yield return Pointer(BackpackPoint(1, 2), false);
        Assert.That(original.Taken, Is.True);
        Assert.That(originalItem.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(GridItemView(original.EntryId, true).gameObject.activeSelf, Is.False);
        AssertGridBackground(source);
        Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));

        yield return BeginPointerDrag(BackpackPoint(0, 2));
        Assert.That(driver.backpackUI.DragInstanceId, Is.EqualTo(external.InstanceId));
        Vector2 target = GridCellPoint(0, 0);
        yield return Pointer(target, true);
        Rect replacementPreview = AssertLocalReturnPreview(source, external, viewportColor);
        yield return Pointer(target, false);
        AssertCommittedReturnPreview(source, external, replacementPreview, viewportColor);
        Assert.That(external.Owner, Is.EqualTo(ItemOwner.Container));
        LootEntryState replacement = source.Entries.Single(entry => entry.InstanceId == external.InstanceId);
        Assert.That(GridEntryBounds(replacement), Is.EqualTo(oldBounds), "A taken entry must not permanently reserve its empty cells.");
        Assert.That(original.Taken, Is.True);
        Assert.That(GridEntryBounds(stationary), Is.EqualTo(stationaryBounds));

        yield return BeginPointerDrag(BackpackPoint(1, 2));
        Assert.That(driver.backpackUI.DragInstanceId, Is.EqualTo(original.InstanceId));
        yield return Pointer(target, true);
        Rect originalPreview = AssertLocalReturnPreview(source, originalItem, viewportColor);
        Assert.That(originalPreview.position, Is.Not.EqualTo(replacementPreview.position), "The visible target must move when the historical cells are now occupied.");
        yield return Pointer(target, false);
        AssertCommittedReturnPreview(source, originalItem, originalPreview, viewportColor);
        Assert.That(source.Entries.Count, Is.EqualTo(3), "Returning the original identity must reuse its entry record.");
        Assert.That(source.Entries.Single(entry => entry.InstanceId == original.InstanceId), Is.SameAs(original));
        Assert.That(original.Revealed && !original.Taken, Is.True);
        Assert.That(originalItem.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(originalItem.ContainerId, Is.EqualTo(source.SourceId));
        Assert.That(GridEntryBounds(original), Is.Not.EqualTo(oldBounds), "The old slot is now owned by the replacement item.");
        Assert.That(GridEntryBounds(replacement), Is.EqualTo(oldBounds));
        Assert.That(GridEntryBounds(stationary), Is.EqualTo(stationaryBounds));
        Assert.That(((RectTransform)GridItemView(stationary.EntryId).transform).anchoredPosition, Is.EqualTo(stationaryViewPosition));
        foreach (LootEntryState entry in source.Entries)
        {
            RectInt bounds = GridEntryBounds(entry);
            Assert.That(bounds.xMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(bounds.yMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(bounds.xMax, Is.LessThanOrEqualTo(source.GridWidth));
            Assert.That(bounds.yMax, Is.LessThanOrEqualTo(source.GridHeight));
            foreach (LootEntryState other in source.Entries)
            {
                if (!ReferenceEquals(entry, other)) { Assert.That(bounds.Overlaps(GridEntryBounds(other)), Is.False); }
            }
        }
        Assert.That(driver.items.InstanceCount, Is.EqualTo(instances));
        Assert.That(ItemIdentitySnapshot(originalItem), Is.EqualTo(originalIdentity));
        Assert.That(ItemIdentitySnapshot(external), Is.EqualTo(externalIdentity));
        Assert.That(driver.backpack.TryGetPlacement(original.InstanceId, out _), Is.False);
        Assert.That(driver.backpack.TryGetPlacement(external.InstanceId, out _), Is.False);
        string layout = GridLayoutSnapshot(source);
        string data = TransferSnapshot();
        yield return Press(Key.F10);
        Assert.That(GridLayoutSnapshot(source), Is.EqualTo(layout));
        Assert.That(TransferSnapshot(), Is.EqualTo(data));
        AssertGridBackground(source);
        Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
    }

    private static RectInt GridEntryBounds(LootEntryState entry)
    {
        return new RectInt(entry.LayoutX, entry.LayoutY, entry.LayoutWidth, entry.LayoutHeight);
    }

    private LootBox CreateGridTestBox(string id, params Vector3[] sizesAndSeconds)
    {
        return CreateGridTestBox(id, 6, 6, sizesAndSeconds);
    }

    private LootBox CreateGridTestBox(string id, int width, int height, params Vector3[] sizesAndSeconds)
    {
        LootTableSO table = ScriptableObject.CreateInstance<LootTableSO>();
        gridTestFixtures.Add(table);
        ItemDataSO prototype = driver.items.itemLibrary.FindDefinition("battery");
        for (int i = 0; i < sizesAndSeconds.Length; i++)
        {
            ItemDataSO definition = Object.Instantiate(prototype);
            gridTestFixtures.Add(definition);
            definition.definitionId = id + "-item-" + i;
            definition.width = (int)sizesAndSeconds[i].x;
            definition.height = (int)sizesAndSeconds[i].y;
            definition.searchSeconds = sizesAndSeconds[i].z;
            // 独立定义复用现有物品文本键，不向共享ItemLibrary或语言表追加可编辑数据。
            definition.isUnique = false;
            definition.uniqueId = string.Empty;
            table.entries.Add(new LootTableEntry { entryId = "entry-" + i, item = definition,
                useDefaultProbability = false, overrideProbability = 1f });
        }
        GameObject target = new GameObject(id);
        target.SetActive(false);
        gridTestFixtures.Add(target);
        SceneManager.MoveGameObjectToScene(target, driver.gameObject.scene);
        LootBox reference = Box("guaranteed");
        target.layer = reference.gameObject.layer;
        target.transform.position = new Vector3(driver.player.transform.position.x - 1.2f,
            reference.transform.position.y, driver.player.transform.position.z);
        LootBox box = target.AddComponent<LootBox>();
        box.mapId = "loot-test";
        box.sourceId = id;
        box.gridWidth = width;
        box.gridHeight = height;
        box.lootTable = table;
        box.runtimeManager = driver.runtime;
        box.lootUI = driver.lootUI;
        BoxCollider collider = target.GetComponent<BoxCollider>();
        collider.size = reference.GetComponent<BoxCollider>().size;
        collider.isTrigger = true;
        target.SetActive(true);
        Physics.SyncTransforms();
        Assert.That(box.State, Is.Not.Null, "The fixture must be generated by the real runtime binding.");
        Assert.That(driver.runtime.IsSourceReady(box.State), Is.True);
        return box;
    }

    private void ClearGridTestFixtures()
    {
        foreach (Object fixture in gridTestFixtures) { if (fixture != null) { Object.Destroy(fixture); } }
        gridTestFixtures.Clear();
    }

    private LootItemView GridItemView(string entryId, bool includeInactive = false)
    {
        return driver.lootUI.view.contentRoot.GetComponentsInChildren<LootItemView>(includeInactive)
            .Single(value => value.EntryId == entryId);
    }

    private LootItemView[] ActiveGridCountdowns()
    {
        return driver.lootUI.view.contentRoot.GetComponentsInChildren<LootItemView>()
            .Where(value => value.countdownText != null && value.countdownText.gameObject.activeInHierarchy
                && !string.IsNullOrWhiteSpace(value.countdownText.text)).ToArray();
    }

    private void AssertSingleGridCountdown(LootSourceState source)
    {
        LootItemView[] counting = ActiveGridCountdowns();
        Assert.That(counting.Length, Is.EqualTo(1));
        LootItemView tile = counting[0];
        Assert.That(tile.EntryId, Is.EqualTo(source.SearchEntryId));
        Assert.That(tile.countdownText.raycastTarget, Is.False);
        RectTransform rect = (RectTransform)tile.transform;
        Vector2 center = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        RectTransform textRect = tile.countdownText.rectTransform;
        Vector2 textCenter = RectTransformUtility.WorldToScreenPoint(null, textRect.TransformPoint(textRect.rect.center));
        Assert.That(Vector2.Distance(center, textCenter), Is.LessThan(0.1f), "Countdown belongs at the full item's geometric center.");
        Assert.That(source.TryGetEntry(source.SearchEntryId, out LootEntryState entry), Is.True);
        Assert.That(driver.items.TryGetInstance(entry.InstanceId, out ItemInstance item), Is.True);
        float remaining = Mathf.Max(0f, item.Definition.searchSeconds - source.SearchElapsed);
        Assert.That(tile.countdownText.text, Is.EqualTo(LocalizationManager.Instance.FormatText("loot.countdown", remaining)));
    }

    private void AssertLootPanelTextFits()
    {
        Canvas.ForceUpdateCanvases();
        LootView view = driver.lootUI.view;
        List<TMP_Text> labels = new List<TMP_Text> { view.titleText, view.statusText, view.feedbackText, view.closeText };
        foreach (LootItemView tile in view.contentRoot.GetComponentsInChildren<LootItemView>())
        {
            labels.Add(tile.nameText);
            labels.Add(tile.countdownText);
        }
        foreach (TMP_Text label in labels)
        {
            if (label == null || !label.isActiveAndEnabled) { continue; }
            label.ForceMeshUpdate();
            Assert.That(label.isTextOverflowing, Is.False, LocalizationManager.Instance.CurrentLanguage + " loot text overflows: "
                + label.name + " text='" + label.text + "' rect=" + label.rectTransform.rect.size
                + " preferred=" + new Vector2(label.preferredWidth, label.preferredHeight));
        }
    }

    private static void AssertUnknownGridTile(LootItemView tile)
    {
        Assert.That(tile.nameText.text, Is.Empty);
        Assert.That(tile.descriptionText.text, Is.Empty);
        Assert.That(tile.statusText.text, Is.Empty);
        Assert.That(tile.icon.gameObject.activeInHierarchy, Is.False);
        Assert.That(tile.background.color.a, Is.Zero);
    }

    private Image[] GridBackgroundCells()
    {
        return driver.lootUI.view.gridRoot.GetComponentsInChildren<Image>()
            .Where(cell => cell != driver.lootUI.view.cellPrefab && cell.gameObject.activeInHierarchy).ToArray();
    }

    private void AssertGridBackground(LootSourceState source)
    {
        LootView view = driver.lootUI.view;
        Assert.That(view.gridRoot, Is.Not.Null);
        Assert.That(view.cellPrefab, Is.Not.Null);
        Assert.That(view.gridRoot.parent, Is.EqualTo(view.contentRoot));
        Assert.That(view.gridRoot.GetSiblingIndex(), Is.Zero, "Background cells must remain below every item and countdown.");
        Assert.That(view.gridRoot.GetComponentsInChildren<TMP_Text>(true), Is.Empty, "Background cells must not contain item information.");
        Image[] cells = GridBackgroundCells();
        Assert.That(cells.Length, Is.EqualTo(source.GridWidth * source.GridHeight));
        Assert.That(source.LayoutColumns, Is.EqualTo(source.GridWidth));
        Assert.That(source.LayoutRows, Is.EqualTo(source.GridHeight));
        Vector2 expectedSize = new Vector2(source.GridWidth * 66f + (source.GridWidth - 1) * 4f + 12f,
            source.GridHeight * 66f + (source.GridHeight - 1) * 4f + 12f);
        Assert.That(view.contentRoot.sizeDelta, Is.EqualTo(expectedSize));
        Assert.That(view.gridRoot.sizeDelta, Is.EqualTo(expectedSize));
        foreach (Image cell in cells)
        {
            Assert.That(cell.raycastTarget, Is.False, "Empty cells must let the real viewport handle pointer input.");
            Assert.That(cell.color, Is.EqualTo(view.cellPrefab.color));
            Assert.That(cell.color, Is.EqualTo(driver.backpackUI.view.cellPrefab.color), "Loot cells use the established backpack background style.");
            Vector2 size = GridScreenSize(cell.rectTransform);
            Assert.That(size.x, Is.EqualTo(66f).Within(0.1f));
            Assert.That(size.y, Is.EqualTo(66f).Within(0.1f));
        }
        for (int y = 0; y < source.GridHeight; y++)
        {
            for (int x = 0; x < source.GridWidth; x++)
            {
                Image cell = cells.Single(value => value.name == "Cell_" + x + "_" + y);
                Assert.That(cell.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(6f + x * 70f, -6f - y * 70f)));
            }
        }
    }

    private string GridBackgroundSnapshot()
    {
        return string.Join("|", GridBackgroundCells().OrderBy(cell => cell.name).Select(cell => cell.GetInstanceID()
            + ":" + cell.name + ":" + cell.rectTransform.anchoredPosition + ":" + cell.rectTransform.sizeDelta + ":" + cell.color));
    }

    private Vector2 GridCellPoint(int x, int y)
    {
        LootView view = driver.lootUI.view;
        return RectTransformUtility.WorldToScreenPoint(null, view.contentRoot.TransformPoint(new Vector3(
            view.padding + x * (view.cellSize + view.gap) + view.cellSize * 0.5f,
            -view.padding - y * (view.cellSize + view.gap) - view.cellSize * 0.5f, 0f)));
    }

    private static Vector2 GridScreenSize(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return RectTransformUtility.WorldToScreenPoint(null, corners[2]) - RectTransformUtility.WorldToScreenPoint(null, corners[0]);
    }

    private string GridLayoutSnapshot(LootSourceState source, int count = int.MaxValue)
    {
        return string.Join("|", source.Entries.Take(count).Select(entry => entry.EntryId + ":" + entry.LayoutX + ":"
            + entry.LayoutY + ":" + entry.LayoutWidth + ":" + entry.LayoutHeight + ":"
            + ((RectTransform)GridItemView(entry.EntryId, true).transform).anchoredPosition + ":"
            + ((RectTransform)GridItemView(entry.EntryId, true).transform).sizeDelta));
    }
}
#endif
