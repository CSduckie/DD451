#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class WarehouseIntegrationTests
{
    private const string ScenePath = "Assets/Scenes/Tests/Warehouse/WarehouseTest.unity";
    private readonly List<GameObject> ownedObjects = new List<GameObject>();
    private readonly List<Scene> ownedContextScenes = new List<Scene>();
    private Keyboard keyboard;
    private Mouse mouse;
    private InputSettings originalInputSettings;
    private InputSettings testInputSettings;
    private bool originalRunInBackground;
    private bool loadedScene;
    private WarehouseManager warehouse;
    private ShelterManager shelter;
    private WarehouseUIController controller;
    private ShelterView shelterView;

    [SetUp]
    public void RequireCleanTestScene()
    {
        if (Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
        {
            Assert.Ignore("请从Test Runner的干净测试场景运行仓库PlayMode测试。");
        }
        originalInputSettings = InputSystem.settings;
        originalRunInBackground = Application.runInBackground;
        testInputSettings = Object.Instantiate(originalInputSettings);
        testInputSettings.hideFlags = HideFlags.HideAndDontSave;
        testInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        testInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings = testInputSettings;
        Application.runInBackground = true;
        keyboard = InputSystem.AddDevice<Keyboard>();
        mouse = InputSystem.AddDevice<Mouse>();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (keyboard != null && keyboard.added) { InputSystem.RemoveDevice(keyboard); }
        if (mouse != null && mouse.added) { InputSystem.RemoveDevice(mouse); }
        keyboard = null;
        mouse = null;
        foreach (GameObject owned in ownedObjects)
        {
            if (owned != null) { Object.Destroy(owned); }
        }
        ownedObjects.Clear();
        yield return null;
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (loadedScene && scene.IsValid() && scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(scene); }
        loadedScene = false;
        foreach (Scene context in ownedContextScenes)
        {
            if (context.IsValid() && context.isLoaded) { yield return SceneManager.UnloadSceneAsync(context); }
        }
        ownedContextScenes.Clear();
        if (originalInputSettings != null)
        {
            InputSystem.settings = originalInputSettings;
            Application.runInBackground = originalRunInBackground;
            Object.Destroy(testInputSettings);
            originalInputSettings = null;
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator QueuedPointerDragUsesRealEventSystemAndRAndF10OnlyChangePreview()
    {
        yield return LoadScene();
        Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        Assert.That(controller.TryOpen(), Is.True);
        Assert.That(warehouse.Width * warehouse.Height, Is.EqualTo(400));
        Assert.That(shelter.RefreshEv(), Is.EqualTo(7));
        Assert.That(LocalizationManager.Instance.CurrentLanguage, Is.EqualTo(GameLanguage.Chinese));
        WarehousePlacement original = VisibleRotatablePlacement();
        string before = Snapshot();
        Vector2 start = CellPoint(original.X, original.Y);
        yield return SetPointer(start, false);
        yield return SetPointer(start, true);
        yield return SetPointer(start + Vector2.right * 16f, true);
        Assert.That(controller.IsDragging, Is.True, "排队鼠标事件必须经真实UGUI回调开始拖拽。");
        Assert.That(controller.DragInstanceId, Is.EqualTo(original.InstanceId));
        yield return Press(Key.R);
        Assert.That(controller.PreviewRotated, Is.EqualTo(!original.Rotated));
        int previewX = controller.PreviewX;
        int previewY = controller.PreviewY;
        yield return Press(Key.F10);
        Assert.That(LocalizationManager.Instance.CurrentLanguage, Is.EqualTo(GameLanguage.English));
        Assert.That(controller.IsDragging, Is.True);
        Assert.That(controller.PreviewRotated, Is.EqualTo(!original.Rotated));
        Assert.That(controller.PreviewX, Is.EqualTo(previewX));
        Assert.That(controller.PreviewY, Is.EqualTo(previewY));
        Assert.That(controller.view.detailsText.text, Does.Contain(original.Item.Definition.nameEn));
        Assert.That(Snapshot(), Is.EqualTo(before));
        yield return SetPointer(new Vector2(-100f, -100f), true);
        yield return SetPointer(new Vector2(-100f, -100f), false);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(Snapshot(), Is.EqualTo(before), "面板外松手只取消，不转移或丢失。");

        yield return SetPointer(start, false);
        yield return SetPointer(start, true);
        yield return SetPointer(start + Vector2.right * 16f, true);
        Assert.That(controller.IsDragging, Is.True);
        Vector2Int target = FindFreeVisibleCell(original);
        yield return SetPointer(CellPoint(target.x, target.y), true);
        yield return SetPointer(CellPoint(target.x, target.y), false);
        Assert.That(warehouse.TryGetPlacement(original.InstanceId, out WarehousePlacement moved), Is.True);
        Assert.That(moved.X, Is.EqualTo(target.x));
        Assert.That(moved.Y, Is.EqualTo(target.y));
        Assert.That(moved.Rotated, Is.EqualTo(original.Rotated));
        Assert.That(shelter.CurrentEv, Is.EqualTo(7));
    }

    [UnityTest]
    public IEnumerator VerticalScrollingKeepsEveryColumnVisibleAndMapsPointerWithoutRebuildingCells()
    {
        yield return LoadScene();
        Assert.That(controller.TryOpen(), Is.True);
        WarehousePlacement original = warehouse.GetPlacements().First(placement => placement.Item.DefinitionId == "battery");
        int farX = warehouse.Width - original.Width;
        int farY = warehouse.Height - original.Height;
        Assert.That(warehouse.TryMove(original.InstanceId, farX, farY, original.Rotated), Is.EqualTo(WarehouseResult.Success));
        WarehouseView view = controller.view;
        Transform firstCell = view.gridRoot.Find("Cell_0_0");
        Assert.That(firstCell, Is.Not.Null);
        Assert.That(view.scrollRect.horizontal, Is.False);
        Assert.That(view.scrollRect.vertical, Is.True);
        Assert.That(view.gridRoot.rect.width, Is.LessThanOrEqualTo(view.viewport.rect.width + 0.01f));
        view.scrollRect.verticalNormalizedPosition = 0f;
        view.scrollRect.velocity = Vector2.zero;
        yield return null;
        Canvas.ForceUpdateCanvases();
        AssertEveryColumnVisibleInLastRow();
        float verticalPosition = view.gridRoot.anchoredPosition.y;
        view.scrollRect.horizontalNormalizedPosition = 1f;
        view.gridRoot.anchoredPosition = new Vector2(-140f, verticalPosition);
        view.scrollRect.velocity = new Vector2(30f, 0f);
        yield return null;
        Assert.That(view.gridRoot.anchoredPosition.x, Is.EqualTo(0f).Within(0.01f), "尝试横向偏移后应归零，不留下可横向滚动的死角。");
        Assert.That(view.scrollRect.horizontalNormalizedPosition, Is.EqualTo(0f).Within(0.01f));
        Assert.That(view.scrollRect.velocity.x, Is.EqualTo(0f));
        AssertEveryColumnVisibleInLastRow();
        Vector2 start = CellPoint(farX, farY);
        Assert.That(view.TryScreenToCell(start, null, out Vector2Int cell), Is.True);
        Assert.That(cell, Is.EqualTo(new Vector2Int(farX, farY)));
        yield return SetPointer(start, false);
        yield return SetPointer(start, true);
        yield return SetPointer(start + Vector2.left * 16f, true);
        Assert.That(controller.IsDragging, Is.True);
        Vector2 target = CellPoint(farX - 1, farY);
        yield return SetPointer(target, true);
        yield return SetPointer(target, false);
        Assert.That(warehouse.TryGetPlacement(original.InstanceId, out WarehousePlacement moved), Is.True);
        Assert.That(moved.X, Is.EqualTo(farX - 1));
        Assert.That(moved.Y, Is.EqualTo(farY));
        yield return Press(Key.F10);
        Assert.That(view.gridRoot.Find("Cell_0_0"), Is.SameAs(firstCell), "重排和语言刷新不应重建400格。");

        Canvas.ForceUpdateCanvases();
        int originalHeight = warehouse.Height;
        float originalCellSize = view.EffectiveCellSize;
        Vector2 originalGridSize = view.gridRoot.rect.size;
        Dictionary<string, RectTransform> originalCells = view.gridRoot.Cast<Transform>()
            .Where(child => child.name.StartsWith("Cell_"))
            .ToDictionary(child => child.name, child => (RectTransform)child);
        Dictionary<string, Vector2> originalCellPositions = originalCells.ToDictionary(pair => pair.Key, pair => pair.Value.anchoredPosition);
        Dictionary<string, Vector2> originalCellSizes = originalCells.ToDictionary(pair => pair.Key, pair => pair.Value.rect.size);
        Dictionary<string, RectTransform> originalItems = view.itemRoot.GetComponentsInChildren<WarehouseItemView>()
            .ToDictionary(item => item.InstanceId, item => (RectTransform)item.transform);
        Dictionary<string, Vector2> originalItemPositions = originalItems.ToDictionary(pair => pair.Key, pair => pair.Value.anchoredPosition);
        Dictionary<string, Vector2> originalItemSizes = originalItems.ToDictionary(pair => pair.Key, pair => pair.Value.rect.size);
        Assert.That(originalCells.Count, Is.EqualTo(400));
        Assert.That(originalItems.Count, Is.EqualTo(warehouse.GetPlacements().Count));

        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.Success));
        view.scrollRect.verticalNormalizedPosition = 0f;
        view.scrollRect.velocity = Vector2.zero;
        yield return null;
        Canvas.ForceUpdateCanvases();
        Assert.That(warehouse.Width, Is.EqualTo(20));
        Assert.That(warehouse.Height, Is.EqualTo(24));
        Assert.That(view.EffectiveCellSize, Is.EqualTo(originalCellSize).Within(0.01f), "升级只增加行数，不得缩小格子。");
        Assert.That(view.gridRoot.rect.width, Is.EqualTo(originalGridSize.x).Within(0.01f));
        Assert.That(view.gridRoot.rect.height, Is.EqualTo(originalGridSize.y + (warehouse.Height - originalHeight)
            * (originalCellSize + view.gap)).Within(0.01f), "内容高度只增加新增行的高度与间距。");
        Assert.That(view.gridRoot.Cast<Transform>().Count(child => child.name.StartsWith("Cell_")), Is.EqualTo(480));
        foreach (KeyValuePair<string, RectTransform> pair in originalCells)
        {
            Assert.That(view.gridRoot.Find(pair.Key), Is.SameAs(pair.Value), "升级应保留旧格子对象：" + pair.Key);
            Assert.That(Vector2.Distance(pair.Value.anchoredPosition, originalCellPositions[pair.Key]), Is.LessThan(0.01f),
                "只增加底部行，旧格子的局部位置不变：" + pair.Key);
            Assert.That(Vector2.Distance(pair.Value.rect.size, originalCellSizes[pair.Key]), Is.LessThan(0.01f),
                "旧格子显示尺寸不变：" + pair.Key);
        }
        Dictionary<string, RectTransform> upgradedItems = view.itemRoot.GetComponentsInChildren<WarehouseItemView>()
            .ToDictionary(item => item.InstanceId, item => (RectTransform)item.transform);
        CollectionAssert.AreEquivalent(originalItems.Keys, upgradedItems.Keys);
        foreach (KeyValuePair<string, RectTransform> pair in originalItems)
        {
            RectTransform upgradedItem = upgradedItems[pair.Key];
            Assert.That(upgradedItem, Is.SameAs(pair.Value), "升级应保留已有物品表现对象。");
            Assert.That(Vector2.Distance(upgradedItem.anchoredPosition, originalItemPositions[pair.Key]), Is.LessThan(0.01f),
                "升级不得移动既有物品的局部摆放。");
            Assert.That(Vector2.Distance(upgradedItem.rect.size, originalItemSizes[pair.Key]), Is.LessThan(0.01f),
                "升级不得缩小Doll、Battery等已有物品的显示尺寸。");
        }
        AssertEveryColumnVisibleInLastRow();
        int newBottomY = warehouse.Height - moved.Height;
        Assert.That(newBottomY, Is.GreaterThanOrEqualTo(originalHeight), "目标应完全处于新增底部行。");
        start = CellPoint(moved.X, moved.Y);
        Assert.That(view.TryScreenToCell(start, null, out cell), Is.True);
        yield return SetPointer(start, false);
        yield return SetPointer(start, true);
        yield return SetPointer(start + Vector2.left * 16f, true);
        Assert.That(controller.IsDragging, Is.True, "新增行拖放也必须由真实UGUI回调开始。");
        target = CellPoint(moved.X, newBottomY);
        yield return SetPointer(target, true);
        yield return SetPointer(target, false);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(warehouse.TryGetPlacement(original.InstanceId, out WarehousePlacement expandedMove), Is.True);
        Assert.That(expandedMove.X, Is.EqualTo(moved.X));
        Assert.That(expandedMove.Y, Is.EqualTo(newBottomY));
        Assert.That(expandedMove.Item, Is.SameAs(original.Item));
        Assert.That(expandedMove.Rotated, Is.EqualTo(original.Rotated));
        Assert.That(shelter.CurrentEv, Is.EqualTo(7));

        float previousCellSize = view.EffectiveCellSize;
        view.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, view.viewport.rect.width - 92f);
        yield return null;
        Canvas.ForceUpdateCanvases();
        view.scrollRect.verticalNormalizedPosition = 0f;
        yield return null;
        Assert.That(view.EffectiveCellSize, Is.LessThanOrEqualTo(previousCellSize));
        Assert.That(view.gridRoot.rect.width, Is.LessThanOrEqualTo(view.viewport.rect.width + 0.01f));
        AssertEveryColumnVisibleInLastRow();
        Assert.That(view.gridRoot.Find("Cell_0_0"), Is.SameAs(firstCell), "扩容与视口宽度变化只补充或重排格子，保留已有格子对象。");
    }

    [UnityTest]
    public IEnumerator DisableFocusLossContextExitAndLateReleaseCancelWithoutAccumulatingRCallbacks()
    {
        yield return LoadScene();
        Assert.That(controller.TryOpen(), Is.True);
        WarehousePlacement original = VisibleRotatablePlacement();
        Vector2 point = CellPoint(original.X, original.Y);
        string before = Snapshot();
        InputAction shared = controller.inputActions.FindAction("Player/RotateItem");
        bool sharedEnabled = shared.enabled;
        int oldToken = controller.BeginItemDrag(original.InstanceId, point, null);
        Assert.That(oldToken, Is.Not.Zero);
        controller.enabled = false;
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(warehouse.HasPendingInteraction, Is.False);
        controller.EndDrag(oldToken, CellPoint(5, 5), null);
        Assert.That(Snapshot(), Is.EqualTo(before));
        for (int i = 0; i < 3; i++)
        {
            controller.enabled = true;
            controller.enabled = false;
        }
        controller.enabled = true;
        Assert.That(controller.TryOpen(), Is.True);
        int token = controller.BeginItemDrag(original.InstanceId, point, null);
        Assert.That(token, Is.Not.EqualTo(oldToken));
        controller.EndDrag(oldToken, CellPoint(5, 5), null);
        Assert.That(controller.IsDragging, Is.True, "迟到的旧松手不能取消较新的拖拽。");
        yield return Press(Key.R);
        Assert.That(controller.PreviewRotated, Is.EqualTo(!original.Rotated), "重新启用后一次R只旋转一次。");
        Assert.That(shared.enabled, Is.EqualTo(sharedEnabled));
        controller.SendMessage("OnApplicationFocus", false);
        Assert.That(controller.IsDragging, Is.False);
        controller.EndDrag(token, CellPoint(5, 5), null);
        Assert.That(Snapshot(), Is.EqualTo(before));
        controller.SelectItem(original.InstanceId);
        controller.RequestAbandonSelected();
        Assert.That(controller.IsAwaitingAbandonConfirmation, Is.True);
        warehouse.SetShelterContext(false, SceneManager.GetSceneByPath(ScenePath));
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(controller.IsAwaitingAbandonConfirmation, Is.False);
        controller.ConfirmAbandon();
        Assert.That(Snapshot(), Is.EqualTo(before));
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.NotInShelter));
    }

    [UnityTest]
    public IEnumerator ConfirmationLanguageCancelStaleTargetAndRepeatedClickCannotRepeatLoss()
    {
        yield return LoadScene();
        Assert.That(controller.TryOpen(), Is.True);
        ItemInstance doll = warehouse.Items.GetInstances().First(item => item.DefinitionId == "doll");
        shelterView.Upgrade();
        Assert.That(shelter.IsUpgradeActivated, Is.True);
        controller.SelectItem(doll.InstanceId);
        controller.RequestAbandonSelected();
        string before = Snapshot();
        Assert.That(controller.PendingAbandonInstanceId, Is.EqualTo(doll.InstanceId));
        yield return Press(Key.F10);
        Assert.That(controller.PendingAbandonInstanceId, Is.EqualTo(doll.InstanceId));
        Assert.That(controller.view.confirmMessageText.text, Does.Contain(doll.Definition.nameEn));
        Assert.That(shelterView.resultText.text, Is.EqualTo(LocalizationManager.Instance.FormatText(
            "shelter.upgraded", 20, 20, 400, 20, 24, 480)));
        Assert.That(Snapshot(), Is.EqualTo(before));
        controller.CancelAbandon();
        Assert.That(Snapshot(), Is.EqualTo(before));
        controller.RequestAbandonSelected();
        Assert.That(warehouse.Items.TrySetOwner(doll.InstanceId, ItemOwner.Backpack, "test-other-container"), Is.True);
        controller.ConfirmAbandon();
        Assert.That(doll.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(doll.IsRetained, Is.True);
        Assert.That(warehouse.TryGetPlacement(doll.InstanceId, out _), Is.True, "确认时归属变化必须拒绝，不能先删摆放。");
        Assert.That(warehouse.Items.TrySetOwner(doll.InstanceId, ItemOwner.Storage, warehouse.ContainerId), Is.True);
        controller.RequestAbandonSelected();
        controller.ConfirmAbandon();
        Assert.That(doll.Owner, Is.EqualTo(ItemOwner.PendingRecovery));
        Assert.That(warehouse.TryGetPlacement(doll.InstanceId, out _), Is.False);
        Assert.That(shelter.CurrentEv, Is.EqualTo(2));
        Assert.That(warehouse.Width, Is.EqualTo(20));
        Assert.That(warehouse.Height, Is.EqualTo(24));
        before = Snapshot();
        controller.ConfirmAbandon();
        yield return Press(Key.F10);
        Assert.That(Snapshot(), Is.EqualTo(before));
        Assert.That(controller.view.feedbackText.text, Is.EqualTo(LocalizationManager.Instance.GetText("warehouse.abandoned")));
        Assert.That(shelterView.evText.text, Is.EqualTo(LocalizationManager.Instance.FormatText("shelter.ev", 2, 7)));
    }

    [UnityTest]
    public IEnumerator NewAdditiveShelterContextCancelsOldDragAndConfirmationWithoutRevokingNewInteraction()
    {
        yield return LoadScene();
        Assert.That(controller.TryOpen(), Is.True);
        Scene oldContext = SceneManager.GetSceneByPath(ScenePath);
        Scene newContext = SceneManager.CreateScene("Warehouse additive context test");
        ownedContextScenes.Add(newContext);
        GameObject newInteractionOwner = new GameObject("新场景交互来源");
        ownedObjects.Add(newInteractionOwner);
        SceneManager.MoveGameObjectToScene(newInteractionOwner, newContext);
        WarehousePlacement original = VisibleRotatablePlacement();
        int token = controller.BeginItemDrag(original.InstanceId, CellPoint(original.X, original.Y), null);
        Assert.That(token, Is.Not.Zero);
        Assert.That(warehouse.OwnsInteraction(controller), Is.True);
        controller.RotatePreview();
        string before = Snapshot();

        // 两个场景都允许整理，不能只用bool是否仍为true判断旧操作是否有效。
        warehouse.SetShelterContext(true, newContext);
        Assert.That(warehouse.IsManagementAllowed, Is.True);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(warehouse.OwnsInteraction(controller), Is.False);
        Assert.That(warehouse.TryClaimInteraction(newInteractionOwner), Is.True);
        controller.EndDrag(token, CellPoint(5, 3), null);
        warehouse.SetShelterContext(false, oldContext);
        Assert.That(warehouse.IsManagementAllowed, Is.True, "旧场景迟到的关闭不能撤销新场景权限。");
        Assert.That(warehouse.OwnsInteraction(newInteractionOwner), Is.True, "旧UI的取消不能释放新来源的交互权。");
        Assert.That(Snapshot(), Is.EqualTo(before));
        warehouse.ReleaseInteraction(newInteractionOwner);

        warehouse.SetShelterContext(true, oldContext);
        ItemInstance doll = warehouse.Items.GetInstances().First(item => item.DefinitionId == "doll");
        controller.SelectItem(doll.InstanceId);
        controller.RequestAbandonSelected();
        Assert.That(controller.PendingAbandonInstanceId, Is.EqualTo(doll.InstanceId));
        Assert.That(warehouse.OwnsInteraction(controller), Is.True);
        warehouse.SetShelterContext(true, newContext);
        Assert.That(controller.IsAwaitingAbandonConfirmation, Is.False);
        Assert.That(warehouse.TryClaimInteraction(newInteractionOwner), Is.True);
        controller.ConfirmAbandon();
        Assert.That(warehouse.OwnsInteraction(newInteractionOwner), Is.True);
        Assert.That(doll.Owner, Is.EqualTo(ItemOwner.Storage));
        Assert.That(doll.IsRetained, Is.True);
        Assert.That(Snapshot(), Is.EqualTo(before));
        warehouse.ReleaseInteraction(newInteractionOwner);
        yield return SceneManager.UnloadSceneAsync(newContext);
        Assert.That(warehouse.IsManagementAllowed, Is.False);
        Assert.That(controller.IsOpen, Is.False);
    }

    [UnityTest]
    public IEnumerator SceneReloadRebindsPersistentStateAndClearsPermissionUntilNewSceneGrantsIt()
    {
        yield return LoadScene();
        Assert.That(controller.TryOpen(), Is.True);
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.Success));
        WarehousePlacement original = VisibleRotatablePlacement();
        Assert.That(warehouse.TryMove(original.InstanceId, 5, 3, !original.Rotated), Is.EqualTo(WarehouseResult.Success));
        yield return Press(Key.F10);
        Assert.That(controller.BeginItemDrag(original.InstanceId, CellPoint(5, 3), null), Is.Not.Zero);
        string before = Snapshot();
        WarehouseManager originalWarehouse = warehouse;
        ShelterManager originalShelter = shelter;
        ItemManager originalItems = warehouse.Items;
        LocalizationManager originalLanguage = LocalizationManager.Instance;
        yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByPath(ScenePath));
        Assert.That(originalWarehouse.IsManagementAllowed, Is.False);
        Assert.That(originalWarehouse.HasPendingInteraction, Is.False);
        Assert.That(originalShelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.NotInShelter));
        yield return LoadScene();
        Assert.That(warehouse, Is.SameAs(originalWarehouse));
        Assert.That(shelter, Is.SameAs(originalShelter));
        Assert.That(warehouse.Items, Is.SameAs(originalItems));
        Assert.That(LocalizationManager.Instance, Is.SameAs(originalLanguage));
        Assert.That(LocalizationManager.Instance.CurrentLanguage, Is.EqualTo(GameLanguage.English));
        Assert.That(Snapshot(), Is.EqualTo(before));
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(controller.IsAwaitingAbandonConfirmation, Is.False);
        Assert.That(Object.FindObjectsByType<WarehouseManager>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        Assert.That(controller.TryOpen(), Is.True);
        Assert.That(controller.view.sizeText.text, Is.EqualTo(originalLanguage.FormatText("warehouse.size", 20, 24, 480)));
        controller.Close();
        Assert.That(controller.TryOpen(), Is.True);
        Assert.That(Snapshot(), Is.EqualTo(before));
    }

    [UnityTest]
    public IEnumerator DestroyingCompleteRuntimeThenStartingFreshUsesInitialConfigAndChinese()
    {
        yield return LoadScene();
        Assert.That(shelter.TryUpgrade(), Is.EqualTo(ShelterUpgradeResult.Success));
        yield return Press(Key.F10);
        WarehouseManager previous = warehouse;
        Object.Destroy(GameBootstrap.Instance.gameObject);
        yield return null;
        yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByPath(ScenePath));
        yield return LoadScene();
        Assert.That(previous == null, Is.True);
        Assert.That(warehouse.Width, Is.EqualTo(20));
        Assert.That(warehouse.Height, Is.EqualTo(20));
        Assert.That(shelter.IsUpgradeActivated, Is.False);
        Assert.That(shelter.CurrentEv, Is.EqualTo(7));
        Assert.That(LocalizationManager.Instance.CurrentLanguage, Is.EqualTo(GameLanguage.Chinese));
    }

    private IEnumerator LoadScene()
    {
        loadedScene = true;
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
        yield return null;
        yield return null;
        Assert.That(GameBootstrap.Instance, Is.Not.Null);
        GameObject root = GameBootstrap.Instance.gameObject;
        if (!ownedObjects.Contains(root)) { ownedObjects.Add(root); }
        warehouse = WarehouseManager.Resolve();
        shelter = ShelterManager.Resolve();
        controller = Object.FindFirstObjectByType<WarehouseUIController>();
        shelterView = Object.FindFirstObjectByType<ShelterView>();
        Assert.That(warehouse, Is.Not.Null);
        Assert.That(shelter, Is.Not.Null);
        Assert.That(controller, Is.Not.Null);
        Assert.That(shelterView, Is.Not.Null);
        // 共享Bootstrap已内置真实业务模块；场景不能另建副本，停用副本也不能绕过检查。
        ItemManager[] itemModules = Object.FindObjectsByType<ItemManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        LocalizationManager[] languageModules = Object.FindObjectsByType<LocalizationManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Assert.That(itemModules.Length, Is.EqualTo(1), "WarehouseTest必须只有一份真实物品登记表，包含未启用对象。");
        Assert.That(languageModules.Length, Is.EqualTo(1), "WarehouseTest必须只有一份真实语言模块，包含未启用对象。");
        Assert.That(warehouse.Items, Is.SameAs(itemModules[0]), "仓库运行布局必须使用唯一物品登记表。");
        Assert.That(warehouse.itemManager, Is.SameAs(itemModules[0]));
        Assert.That(shelter.itemManager, Is.SameAs(itemModules[0]), "庇护所EV和仓库必须使用同一物品来源。");
        Assert.That(shelter.Warehouse, Is.SameAs(warehouse));
        Assert.That(shelter.warehouseManager, Is.SameAs(warehouse));
        Assert.That(LocalizationManager.Instance, Is.SameAs(languageModules[0]));
        Assert.That(controller.manager, Is.SameAs(warehouse));
        Assert.That(controller.localization, Is.SameAs(languageModules[0]));
        Assert.That(shelterView.manager, Is.SameAs(shelter));
        Assert.That(shelterView.localization, Is.SameAs(languageModules[0]), "两个面板必须使用同一真实语言来源。");
        Assert.That(warehouse.IsManagementAllowed, Is.True);
        Canvas.ForceUpdateCanvases();
        AssertPanelInsideScreen(controller.view.panelRoot, "仓库面板");
        AssertPanelInsideScreen(shelterView.panelRoot, "庇护所面板");
    }

    private static void AssertPanelInsideScreen(GameObject panelRoot, string panelName)
    {
        Assert.That(panelRoot, Is.Not.Null, panelName);
        RectTransform rect = panelRoot.GetComponent<RectTransform>();
        Assert.That(rect, Is.Not.Null, panelName);
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        Assert.That(canvas, Is.Not.Null, panelName);
        Canvas rootCanvas = canvas.rootCanvas;
        Camera camera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        const float tolerance = 0.5f;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
            string context = panelName + " 第" + i + "角，实际窗口 " + Screen.width + "×" + Screen.height;
            Assert.That(point.x, Is.InRange(-tolerance, Screen.width + tolerance), context + "：横向越界。");
            Assert.That(point.y, Is.InRange(-tolerance, Screen.height + tolerance), context + "：纵向越界。");
        }
    }

    private WarehousePlacement VisibleRotatablePlacement()
    {
        Canvas.ForceUpdateCanvases();
        WarehousePlacement placement = warehouse.GetPlacements().FirstOrDefault(candidate => candidate.Item.Definition.canRotate
            && candidate.Width != candidate.Height
            && controller.view.TryScreenToCell(CellPoint(candidate.X, candidate.Y), null, out _));
        Assert.That(placement, Is.Not.Null, "测试场景应有可见的非正方形可旋转物品。");
        return placement;
    }

    private Vector2Int FindFreeVisibleCell(WarehousePlacement placement)
    {
        for (int y = 0; y < warehouse.Height; y++)
        {
            for (int x = 0; x < warehouse.Width; x++)
            {
                if (x == placement.X && y == placement.Y) { continue; }
                if (warehouse.CanMove(placement.InstanceId, x, y, placement.Rotated) == WarehouseResult.Success
                    && controller.view.TryScreenToCell(CellPoint(x, y), null, out _)) { return new Vector2Int(x, y); }
            }
        }
        Assert.Fail("测试视口内没有可用的目标位置。");
        return Vector2Int.zero;
    }

    private IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }

    private IEnumerator SetPointer(Vector2 position, bool left)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = (ushort)(left ? 1 : 0) });
        yield return null;
        yield return null;
    }

    private Vector2 CellPoint(int x, int y)
    {
        Canvas.ForceUpdateCanvases();
        WarehouseView view = controller.view;
        float cellSize = view.EffectiveCellSize;
        Vector3 local = new Vector3(view.gridRoot.rect.xMin + view.padding + x * (cellSize + view.gap) + cellSize * 0.35f,
            view.gridRoot.rect.yMax - view.padding - y * (cellSize + view.gap) - cellSize * 0.35f, 0f);
        return RectTransformUtility.WorldToScreenPoint(null, view.gridRoot.TransformPoint(local));
    }

    private void AssertEveryColumnVisibleInLastRow()
    {
        WarehouseView view = controller.view;
        Assert.That(view.scrollRect.horizontal, Is.False);
        Assert.That(view.EffectiveCellSize, Is.GreaterThan(0f));
        for (int x = 0; x < warehouse.Width; x++)
        {
            Assert.That(view.TryScreenToCell(CellPoint(x, warehouse.Height - 1), null, out Vector2Int cell), Is.True,
                "只纵向滚动到末行后，第" + x + "列仍应可见可命中。");
            Assert.That(cell, Is.EqualTo(new Vector2Int(x, warehouse.Height - 1)));
        }
    }

    private string Snapshot()
    {
        IEnumerable<string> instances = warehouse.Items.GetInstances().OrderBy(item => item.InstanceId)
            .Select(item => item.InstanceId + ":" + item.Owner + ":" + item.ContainerId + ":" + item.UseRound
                + ":" + item.UsesThisRound + ":" + item.IsRetained + ":" + item.RecoveryRound + ":" + item.LastLossReason);
        IEnumerable<string> placements = warehouse.GetPlacements().OrderBy(placement => placement.InstanceId)
            .Select(placement => placement.InstanceId + ":" + placement.X + ":" + placement.Y + ":" + placement.Rotated);
        return warehouse.Width + "x" + warehouse.Height + "|" + shelter.IsUpgradeActivated + "|" + shelter.CurrentEv
            + "|" + warehouse.Items.CurrentRound + "|" + string.Join(";", instances) + "|" + string.Join(";", placements);
    }
}
#endif
