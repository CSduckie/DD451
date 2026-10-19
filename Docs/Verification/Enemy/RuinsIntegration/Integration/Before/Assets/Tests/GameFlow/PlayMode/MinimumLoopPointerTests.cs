#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

// 定位使用API夹具；E与键鼠排队事件经真实Player/InputAction/UGUI提交，不是原生OS试玩。
public class MinimumLoopPointerTests
{
    private const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";
    private const string ShelterPath = "Assets/Scenes/Shelter.unity";
    private const string RuinsPath = "Assets/Scenes/Ruins.unity";
    private Keyboard keyboard;
    private Mouse mouse;
    private Keyboard originalKeyboard;
    private Mouse originalMouse;
    private InputSettings originalSettings;
    private InputSettings testSettings;
    private bool originalRunInBackground;
    private bool startedSession;
    private GameFlowManager flow;
    private GameObject persistentRoot;
    private readonly List<InputDevice> disabledDevices = new List<InputDevice>();
    private readonly List<string> completedTransfers = new List<string>();

    [SetUp]
    public void PrepareQueuedDevices()
    {
        startedSession = false;
        flow = null;
        persistentRoot = null;
        completedTransfers.Clear();
        if (Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
        {
            Assert.Ignore("Run from the Test Runner clean test scene, without an existing game session.");
        }
        Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>(BootstrapPath), Is.Not.Null);
        originalKeyboard = Keyboard.current;
        originalMouse = Mouse.current;
        originalSettings = InputSystem.settings;
        originalRunInBackground = Application.runInBackground;
        try
        {
            testSettings = Object.Instantiate(originalSettings);
            testSettings.hideFlags = HideFlags.HideAndDontSave;
            testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = testSettings;
            Application.runInBackground = true;
            // 仅排队测试隔离原键鼠，避免实体指针抢占UGUI；失败也恢复，正式输入不改。
            foreach (InputDevice device in InputSystem.devices.ToArray())
            {
                if (!device.enabled || (!(device is Keyboard) && !(device is UnityEngine.InputSystem.Pointer))) { continue; }
                disabledDevices.Add(device);
                InputSystem.DisableDevice(device);
            }
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
            keyboard.MakeCurrent();
            mouse.MakeCurrent();
        }
        catch { RestoreDevices(); throw; }
    }

    [UnityTearDown]
    public IEnumerator RestoreSessionAndDevices()
    {
        try
        {
            if (flow != null && flow.Transfers != null) { flow.Transfers.Completed -= OnTransferCompleted; }
            if (startedSession)
            {
                foreach (GameFlowSceneBindings bindings in Object.FindObjectsByType<GameFlowSceneBindings>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    bindings.CloseSessions();
                }
                if (flow != null) { flow.StopAllCoroutines(); }
                foreach (string path in new[] { RuinsPath, ShelterPath, BootstrapPath })
                {
                    Scene scene = SceneManager.GetSceneByPath(path);
                    if (scene.IsValid() && scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(scene); }
                }
                if (persistentRoot == null && GameBootstrap.Instance != null) { persistentRoot = GameBootstrap.Instance.gameObject; }
                if (persistentRoot != null) { Object.Destroy(persistentRoot); }
                yield return null;
            }
        }
        finally
        {
            RestoreDevices();
            startedSession = false;
        }
    }

    private void RestoreDevices()
    {
        try
        {
            if (keyboard != null && keyboard.added) { InputSystem.RemoveDevice(keyboard); }
            if (mouse != null && mouse.added) { InputSystem.RemoveDevice(mouse); }
            keyboard = null;
            mouse = null;
            if (originalSettings != null)
            {
                InputSystem.settings = originalSettings;
                Application.runInBackground = originalRunInBackground;
            }
            if (testSettings != null) { Object.Destroy(testSettings); }
            originalSettings = null;
            testSettings = null;
        }
        finally
        {
            foreach (InputDevice device in disabledDevices)
            {
                if (device.added && !device.enabled) { InputSystem.EnableDevice(device); }
            }
            disabledDevices.Clear();
            if (originalKeyboard != null && originalKeyboard.added) { originalKeyboard.MakeCurrent(); }
            if (originalMouse != null && originalMouse.added) { originalMouse.MakeCurrent(); }
        }
    }

    [UnityTest]
    public IEnumerator FormalSearchPickupExtractionAndBidirectionalStorageUseQueuedPointers()
    {
        yield return LoadBootstrap();
        TestContext.WriteLine("Formal queued-pointer integration at " + Screen.width + "x" + Screen.height + "; not native OS input.");
        Assert.That(new[] { Screen.width, Screen.height }, Is.EqualTo(new[] { 1920, 1080 }), "Current formal acceptance is 1920x1080.");
        Assert.That(flow.CurrentRound, Is.Zero);
        Assert.That(flow.Backpack.GetPlacements(), Is.Empty);
        Assert.That(flow.Warehouse.GetPlacements(), Is.Empty);
        Assert.That(flow.Shelter.CurrentEv, Is.Zero);
        PlayerController samePlayer = flow.Player;
        GameFlowSceneBindings initialHome = Bindings(ShelterPath);
        Vector3 shelterBoundsSize = initialHome.cameraBinder.confiner.BoundingVolume.bounds.size;
        GameFlowInteractable initialStorage = FlowTarget(initialHome, GameFlowInteraction.Storage);
        yield return AssertOutsideTriggerRejectsE(initialHome, initialStorage);
        yield return InteractUsingE(initialHome, initialStorage);
        Assert.That(initialHome.backpackUI.IsOpen && initialHome.warehouseUI.IsOpen, Is.True);
        Assert.That(flow.CurrentRound, Is.Zero);
        yield return CloseUsingTab(initialHome);
        yield return InteractUsingE(initialHome, FlowTarget(initialHome, GameFlowInteraction.Departure));
        yield return WaitForStage(GameFlowStage.Exploring);
        Assert.That(flow.Warehouse.IsManagementAllowed, Is.False);
        Assert.That(flow.TryOpenStorage(), Is.False);

        GameFlowSceneBindings ruins = Bindings(RuinsPath);
        Assert.That(Vector3.Distance(ruins.cameraBinder.confiner.BoundingVolume.bounds.size, shelterBoundsSize),
            Is.GreaterThan(0.01f), "Shelter and Ruins must use independently authored map bounds with different appropriate dimensions.");
        LootBox box = Object.FindObjectsByType<LootBox>(FindObjectsSortMode.None)
            .Single(value => value.gameObject.scene.path == RuinsPath && value.sourceId == "supplies-box");
        yield return InteractUsingE(ruins, box);
        AssertOpenSource(ruins, box, box.State);
        Assert.That(EventSystem.current.currentInputModule, Is.InstanceOf<InputSystemUIInputModule>());
        Assert.That(box.State.TryGetEntry("doll", out LootEntryState dollEntry), Is.True);
        Assert.That(box.State.TryGetEntry("medkit", out LootEntryState medkitEntry), Is.True);
        Assert.That(flow.Items.TryGetInstance(dollEntry.InstanceId, out ItemInstance doll), Is.True);
        Assert.That(flow.Items.TryGetInstance(medkitEntry.InstanceId, out ItemInstance medkit), Is.True);
        int instanceCount = flow.Items.InstanceCount;
        string dollOrigin = doll.OriginalSpawnPointId;
        string medkitOrigin = medkit.OriginalSpawnPointId;
        yield return null;
        AssertLootGrid(ruins, box);
        Dictionary<string, RectInt> originalSlots = LootSlots(box.State);
        AssertFrame(LootItemSize(ruins, dollEntry.EntryId), 2, 3);
        AssertFrame(LootItemSize(ruins, medkitEntry.EntryId), 2, 2);

        // 先证实真实搜索已推进再中断；与稍后的已揭示拖拽分开，避免低帧率时后件已搜完。
        Assert.That(dollEntry.Revealed, Is.False);
        Assert.That(box.State.SearchEntryId, Is.EqualTo(dollEntry.EntryId));
        Assert.That(box.State.SearchElapsed, Is.GreaterThan(0f));
        MoveOutsideTrigger(ruins, box);
        yield return null;
        yield return null;
        Assert.That(ruins.lootUI.IsOpen, Is.False);
        Assert.That(box.State.SearchElapsed, Is.Zero, "Leaving the TriggerBox interrupts a genuinely unfinished search.");
        Assert.That(dollEntry.Revealed || medkitEntry.Revealed, Is.False);
        Assert.That(flow.Items.InstanceCount, Is.EqualTo(instanceCount));
        AssertLootSlots(box.State, originalSlots);
        yield return CloseUsingTab(ruins);
        yield return InteractUsingE(ruins, box);
        AssertOpenSource(ruins, box, box.State);

        // 不调用AdvanceSearch或强行揭示；实际LootUI.Update逐件推进搜索。
        yield return WaitForRevealed(ruins, dollEntry);
        int beforeRangeExit = completedTransfers.Count;
        int oldSourceSession = ruins.lootUI.SessionToken;
        yield return BeginDrag(EntryPoint(ruins, "doll"));
        Assert.That(ruins.backpackUI.IsExternalPreview, Is.True);
        MoveOutsideTrigger(ruins, box);
        yield return null;
        yield return null;
        Assert.That(flow.Player.interaction.CanReachTarget(box), Is.False);
        Assert.That(ruins.lootUI.IsOpen, Is.False);
        Assert.That(ruins.backpackUI.IsDragging, Is.False);
        Assert.That(box.State.SearchElapsed, Is.Zero);
        yield return Pointer(BackpackPoint(ruins, 0, 0), false);
        Assert.That(completedTransfers.Count, Is.EqualTo(beforeRangeExit), "A late release after leaving the TriggerBox cannot submit.");
        Assert.That(dollEntry.Taken, Is.False);
        Assert.That(dollEntry.Revealed, Is.True);
        Assert.That(doll.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(flow.Items.InstanceCount, Is.EqualTo(instanceCount));
        AssertLootSlots(box.State, originalSlots);
        yield return CloseUsingTab(ruins);
        yield return InteractUsingE(ruins, box);
        AssertOpenSource(ruins, box, box.State);
        Assert.That(ruins.lootUI.SessionToken, Is.Not.EqualTo(oldSourceSession));
        Assert.That(box.State.TryGetEntry("doll", out LootEntryState reopenedDoll), Is.True);
        Assert.That(reopenedDoll, Is.SameAs(dollEntry));
        Assert.That(dollEntry.Revealed, Is.True);
        yield return BeginDrag(EntryPoint(ruins, "doll"));
        Assert.That(ruins.backpackUI.IsExternalPreview, Is.True);
        Assert.That(ruins.backpackUI.DragInstanceId, Is.EqualTo(doll.InstanceId));
        yield return Pointer(BackpackPoint(ruins, 0, 0), true);
        Assert.That(ruins.backpackUI.PreviewResult, Is.EqualTo(BackpackResult.Success));
        AssertPreview(ruins.backpackUI.view.preview, 2, 3);
        yield return Pointer(BackpackPoint(ruins, 0, 0), false);
        Assert.That(dollEntry.Taken, Is.True);
        AssertBackpackItem(doll, 0, 0, false);
        AssertFrame(BackpackItemSize(ruins, doll.InstanceId), 2, 3);
        AssertLootGrid(ruins, box);
        AssertLootSlots(box.State, originalSlots);

        yield return WaitForRevealed(ruins, medkitEntry);
        yield return BeginDrag(EntryPoint(ruins, "medkit"));
        Assert.That(ruins.backpackUI.IsExternalPreview, Is.True);
        Assert.That(ruins.backpackUI.DragInstanceId, Is.EqualTo(medkit.InstanceId));
        yield return Pointer(BackpackPoint(ruins, 2, 0), true);
        Assert.That(ruins.backpackUI.PreviewResult, Is.EqualTo(BackpackResult.Success));
        AssertPreview(ruins.backpackUI.view.preview, 2, 2);
        yield return Pointer(BackpackPoint(ruins, 2, 0), false);
        Assert.That(medkitEntry.Taken, Is.True);
        AssertBackpackItem(medkit, 2, 0, false);
        Assert.That(doll.IsRetained || medkit.IsRetained, Is.False);
        Assert.That(flow.Shelter.CurrentEv, Is.Zero, "New loot is not retained until successful extraction.");
        Assert.That(flow.Items.InstanceCount, Is.EqualTo(instanceCount));

        // 正式地面电池补齐1×2；使用真实来源和排队拾取/返箱，不给测试直接发物。
        yield return Click(Center(ruins.lootUI.view.closeButton.transform));
        Assert.That(ruins.lootUI.IsOpen, Is.False);
        GroundItem ground = Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None)
            .Single(value => value.gameObject.scene.path == RuinsPath && value.sourceId == "battery-ground");
        yield return CloseUsingTab(ruins);
        yield return InteractUsingE(ruins, ground);
        AssertOpenSource(ruins, ground, ground.State);
        LootEntryState batteryEntry = ground.State.Entries.Single();
        Assert.That(flow.Items.TryGetInstance(batteryEntry.InstanceId, out ItemInstance battery), Is.True);
        string batteryOrigin = battery.OriginalSpawnPointId;
        yield return WaitForRevealed(ruins, batteryEntry);
        AssertFrame(LootItemSize(ruins, batteryEntry.EntryId), 1, 2);
        yield return BeginDrag(EntryPoint(ruins, batteryEntry.EntryId));
        yield return Pointer(BackpackPoint(ruins, 2, 2), true);
        AssertPreview(ruins.backpackUI.view.preview, 1, 2);
        yield return Pointer(BackpackPoint(ruins, 2, 2), false);
        AssertBackpackItem(battery, 2, 2, false);
        AssertFrame(BackpackItemSize(ruins, battery.InstanceId), 1, 2);
        yield return CloseUsingTab(ruins);
        yield return InteractUsingE(ruins, box);
        AssertOpenSource(ruins, box, box.State);
        yield return null;
        AssertLootGrid(ruins, box);
        AssertLootSlots(box.State, originalSlots);

        // 普通返箱必须落在鼠标格减抓取偏移处，不再使用整视口自动归位。
        yield return BeginDrag(BackpackPoint(ruins, 3, 1));
        Assert.That(ruins.backpackUI.DragGrabOffset, Is.EqualTo(new Vector2Int(1, 1)));
        Vector2 medkitReturnPoint = LootPoint(ruins, 5, 3);
        yield return Pointer(medkitReturnPoint, true);
        Rect medkitReturnPreview = AssertLootReturnPreview(ruins, 4, 2, 2, 2);
        yield return Pointer(medkitReturnPoint, false);
        AssertLootItem(box.State, medkitEntry, medkit, 4, 2, false);
        AssertScreenRect(medkitReturnPreview, ScreenRect(LootItemRect(ruins, medkitEntry.EntryId)));
        originalSlots[medkitEntry.EntryId] = new RectInt(4, 2, 2, 2);
        AssertLootSlots(box.State, originalSlots);
        yield return BeginDrag(EntryPoint(ruins, medkitEntry.EntryId, 1, 1));
        Assert.That(ruins.backpackUI.DragGrabOffset, Is.EqualTo(new Vector2Int(1, 1)));
        yield return Pointer(BackpackPoint(ruins, 3, 1), true);
        AssertPreview(ruins.backpackUI.view.preview, 2, 2);
        yield return Pointer(BackpackPoint(ruins, 3, 1), false);
        AssertBackpackItem(medkit, 2, 0, false);

        int beforeBlank = completedTransfers.Count;
        object[] batteryBeforeBlank = ItemInvariant(battery);
        yield return BeginDrag(BackpackPoint(ruins, 2, 3));
        Assert.That(ruins.backpackUI.DragGrabOffset, Is.EqualTo(new Vector2Int(0, 1)));
        Vector2 blankPoint = LootBlankPoint(ruins);
        yield return Pointer(blankPoint, true);
        Assert.That(ruins.lootUI.view.IsReturnPreviewVisible, Is.False);
        yield return Pointer(blankPoint, false);
        Assert.That(completedTransfers.Count, Is.EqualTo(beforeBlank), "Viewport blank must not submit or find a substitute cell.");
        AssertBackpackItem(battery, 2, 2, false);
        Assert.That(ItemInvariant(battery), Is.EqualTo(batteryBeforeBlank));
        Assert.That(flow.Items.InstanceCount, Is.EqualTo(instanceCount));
        Assert.That(flow.Shelter.CurrentEv, Is.Zero);

        // 从1×2底格抓取，R后偏移夹紧；保存的2×1方向必须能从箱中第二格再次拾回。
        yield return BeginDrag(BackpackPoint(ruins, 2, 3));
        Assert.That(ruins.backpackUI.DragGrabOffset, Is.EqualTo(new Vector2Int(0, 1)));
        Vector2 returnPoint = LootPoint(ruins, 4, 3);
        yield return Pointer(returnPoint, true);
        yield return Press(Key.R);
        Assert.That(ruins.backpackUI.PreviewRotated, Is.True);
        Assert.That(ruins.backpackUI.DragGrabOffset, Is.EqualTo(Vector2Int.zero));
        Rect batteryReturnPreview = AssertLootReturnPreview(ruins, 4, 3, 2, 1);
        yield return Pointer(returnPoint, false);
        Assert.That(battery.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(battery.ContainerId, Is.EqualTo(box.State.SourceId));
        Assert.That(flow.Backpack.TryGetPlacement(battery.InstanceId, out _), Is.False);
        LootEntryState returnedBattery = box.State.Entries.Single(value => value.InstanceId == battery.InstanceId);
        AssertLootItem(box.State, returnedBattery, battery, 4, 3, true);
        AssertFrame(LootItemSize(ruins, returnedBattery.EntryId), 2, 1);
        AssertScreenRect(batteryReturnPreview, ScreenRect(LootItemRect(ruins, returnedBattery.EntryId)));
        AssertLootSlots(box.State, originalSlots);
        AssertLootGrid(ruins, box);
        yield return BeginDrag(EntryPoint(ruins, returnedBattery.EntryId, 1, 0));
        Assert.That(ruins.backpackUI.PreviewRotated, Is.True);
        Assert.That(ruins.backpackUI.DragGrabOffset, Is.EqualTo(new Vector2Int(1, 0)));
        yield return Pointer(BackpackPoint(ruins, 3, 2), true);
        AssertPreview(ruins.backpackUI.view.preview, 2, 1);
        yield return Press(Key.R);
        Assert.That(ruins.backpackUI.PreviewRotated, Is.False);
        Assert.That(ruins.backpackUI.DragGrabOffset, Is.EqualTo(Vector2Int.zero));
        yield return Pointer(BackpackPoint(ruins, 2, 2), true);
        AssertPreview(ruins.backpackUI.view.preview, 1, 2);
        yield return Pointer(BackpackPoint(ruins, 2, 2), false);
        AssertBackpackItem(battery, 2, 2, false);
        Assert.That(returnedBattery.Taken, Is.True);
        Assert.That(flow.Items.InstanceCount, Is.EqualTo(instanceCount));
        AssertLootGrid(ruins, box);

        foreach (Key control in new[] { Key.LeftCtrl, Key.RightCtrl })
        {
            yield return QuickClick(ruins, BackpackPoint(ruins, 2, 2), control, battery);
            AssertLootItem(box.State, returnedBattery, battery, 0, 0, false);
            Assert.That(box.State.Entries.Count(value => value.InstanceId == battery.InstanceId), Is.EqualTo(1));
            yield return QuickClick(ruins, EntryPoint(ruins, returnedBattery.EntryId), control, battery);
            AssertBackpackItem(battery, 2, 2, false);
            Assert.That(returnedBattery.Taken, Is.True);
            AssertBackpackItem(doll, 0, 0, false);
            AssertBackpackItem(medkit, 2, 0, false);
            AssertLootSlots(box.State, originalSlots);
        }

        // 通过实际 UGUI 点击进入确认；切换语言期间不提交，取消后原实例仍在原格。
        Assert.That(flow.Language.CurrentLanguage, Is.EqualTo(GameLanguage.Chinese));
        yield return Click(BackpackPoint(ruins, 0, 0));
        Assert.That(ruins.backpackUI.SelectedInstanceId, Is.EqualTo(doll.InstanceId));
        yield return Click(Center(ruins.transferUI.dropButton.transform));
        Assert.That(ruins.transferUI.IsAwaitingDiscardConfirmation, Is.True);
        Assert.That(ruins.transferUI.discardPanel.activeInHierarchy, Is.True);
        yield return new WaitForSecondsRealtime(0.25f); // Allow the responsive item's 0.2s typography refresh.
        string chineseDiscard = ruins.transferUI.discardMessageText.text;
        AssertReadableLabels(ruins);
        yield return Press(Key.F10);
        Assert.That(flow.Language.CurrentLanguage, Is.EqualTo(GameLanguage.English));
        Assert.That(ruins.transferUI.discardMessageText.text, Is.Not.EqualTo(chineseDiscard));
        AssertLootSlots(box.State, originalSlots);
        AssertLootGrid(ruins, box);
        AssertReadableLabels(ruins);
        yield return Click(Center(ruins.transferUI.cancelDiscardButton.transform));
        Assert.That(ruins.transferUI.IsAwaitingDiscardConfirmation, Is.False);
        AssertBackpackItem(doll, 0, 0, false);
        AssertBackpackItem(medkit, 2, 0, false);
        AssertBackpackItem(battery, 2, 2, false);
        Assert.That(flow.Items.InstanceCount, Is.EqualTo(instanceCount));

        yield return CloseUsingTab(ruins);
        yield return InteractUsingE(ruins, FlowTarget(ruins, GameFlowInteraction.Extraction));
        yield return WaitForStage(GameFlowStage.Shelter);
        Assert.That(flow.Player, Is.SameAs(samePlayer));
        Assert.That(flow.Warehouse.IsManagementAllowed, Is.True);
        Assert.That(flow.SettledRound, Is.EqualTo(1));
        Assert.That(flow.Shelter.CurrentEv, Is.EqualTo(7));
        Assert.That(doll.IsRetained && medkit.IsRetained, Is.True);
        Assert.That(battery.IsRetained, Is.True);
        AssertBackpackItem(doll, 0, 0, false);
        AssertBackpackItem(medkit, 2, 0, false);
        GameFlowSceneBindings home = Bindings(ShelterPath);
        yield return InteractUsingE(home, FlowTarget(home, GameFlowInteraction.Storage));
        Assert.That(home.backpackUI.IsOpen && home.warehouseUI.IsOpen, Is.True);
        yield return new WaitForSecondsRealtime(0.25f);
        Assert.That(flow.Language.CurrentLanguage, Is.EqualTo(GameLanguage.English), "F10 choice survives the real scene transition.");
        AssertReadableLabels(home);
        Assert.That(home.backpackUI.QuickTransferRequested, Is.Not.Null);
        Assert.That(home.warehouseUI.QuickTransferRequested, Is.Not.Null);
        foreach (Key control in new[] { Key.LeftCtrl, Key.RightCtrl })
        {
            yield return QuickClick(home, BackpackPoint(home, 2, 2), control, battery);
            AssertWarehouseItem(battery, 0, 0, false);
            yield return QuickClick(home, WarehousePoint(home, 0, 0), control, battery);
            AssertBackpackItem(battery, 2, 2, false);
            AssertBackpackItem(doll, 0, 0, false);
            AssertBackpackItem(medkit, 2, 0, false);
        }
        // 先存电池，保留原用例的布娃娃最终(0,2)与医疗包(0,0)路线。
        yield return BeginDrag(BackpackPoint(home, 2, 2));
        yield return Pointer(WarehousePoint(home, 8, 1), true);
        AssertPreview(home.warehouseUI.view.preview, 1, 2);
        yield return Pointer(WarehousePoint(home, 8, 1), false);
        AssertWarehouseItem(battery, 8, 1, false);
        AssertFrame(WarehouseItemSize(home, battery.InstanceId), 1, 2);
        yield return Press(Key.F10);
        Assert.That(flow.Language.CurrentLanguage, Is.EqualTo(GameLanguage.Chinese));
        AssertReadableLabels(home);

        // EV7 只通过可见升级按钮扩至 20×24；已有格子和包含间隙的物品外框不可缩小。
        WarehouseView warehouseView = home.warehouseUI.view;
        Assert.That(new[] { flow.Warehouse.Width, flow.Warehouse.Height }, Is.EqualTo(new[] { 20, 20 }));
        float cellBeforeUpgrade = warehouseView.EffectiveCellSize;
        AssertMatchingGridGeometry(home);
        Vector2 medkitFrame = BackpackItemSize(home, medkit.InstanceId);
        AssertFrame(medkitFrame, 2, 2);
        Assert.That(home.shelterView.upgradeButton.interactable, Is.True);
        yield return Click(Center(home.shelterView.upgradeButton.transform));
        Assert.That(new[] { flow.Warehouse.Width, flow.Warehouse.Height }, Is.EqualTo(new[] { 20, 24 }));
        Assert.That(warehouseView.EffectiveCellSize, Is.EqualTo(cellBeforeUpgrade).Within(0.001f));
        Assert.That(flow.Shelter.CurrentEv, Is.EqualTo(7), "The manual upgrade must not spend EV.");
        AssertMatchingGridGeometry(home);
        AssertWarehouseItem(battery, 8, 1, false);
        AssertFrame(WarehouseItemSize(home, battery.InstanceId), 1, 2);
        AssertReadableLabels(home);
        yield return Press(Key.F10);
        Assert.That(flow.Language.CurrentLanguage, Is.EqualTo(GameLanguage.English));
        AssertReadableLabels(home);

        // 滚轮经 InputSystemUIInputModule 到真正 ScrollRect，不设置 normalizedPosition 或直接转移。
        yield return ScrollWarehouse(home, false);
        yield return BeginDrag(BackpackPoint(home, 2, 0));
        Assert.That(home.backpackUI.IsDragging, Is.True);
        yield return Pointer(WarehousePoint(home, 0, 22), true);
        AssertPreview(home.warehouseUI.view.preview, 2, 2);
        yield return Pointer(WarehousePoint(home, 0, 22), false);
        AssertWarehouseItem(medkit, 0, 22, false);
        Assert.That(flow.Backpack.TryGetPlacement(medkit.InstanceId, out _), Is.False);
        AssertSameRenderedSize(medkitFrame, WarehouseItemSize(home, medkit.InstanceId));
        yield return BeginDrag(WarehousePoint(home, 0, 22));
        Assert.That(home.warehouseUI.IsDragging, Is.True);
        yield return Pointer(BackpackPoint(home, 2, 0), true);
        AssertPreview(home.backpackUI.view.preview, 2, 2);
        yield return Pointer(BackpackPoint(home, 2, 0), false);
        AssertBackpackItem(medkit, 2, 0, false);
        Assert.That(flow.Warehouse.TryGetPlacement(medkit.InstanceId, out _), Is.False);
        AssertSameRenderedSize(medkitFrame, BackpackItemSize(home, medkit.InstanceId));
        yield return ScrollWarehouse(home, true);

        yield return BeginDrag(BackpackPoint(home, 1, 2));
        Assert.That(home.backpackUI.IsDragging, Is.True);
        Assert.That(home.backpackUI.DragGrabOffset, Is.EqualTo(new Vector2Int(1, 2)));
        yield return Pointer(WarehousePoint(home, 5, 3), true);
        AssertPreview(home.warehouseUI.view.preview, 2, 3);
        yield return Press(Key.R);
        Assert.That(home.backpackUI.PreviewRotated, Is.True, "One R press rotates the active source exactly once.");
        Assert.That(home.backpackUI.DragGrabOffset, Is.EqualTo(new Vector2Int(1, 1)));
        yield return Pointer(WarehousePoint(home, 5, 2), true);
        AssertPreview(home.warehouseUI.view.preview, 3, 2);
        AssertBackpackItem(doll, 0, 0, false);
        yield return Pointer(WarehousePoint(home, 5, 2), false);
        AssertWarehouseItem(doll, 4, 1, true);
        AssertFrame(WarehouseItemSize(home, doll.InstanceId), 3, 2);
        Assert.That(flow.Backpack.TryGetPlacement(doll.InstanceId, out _), Is.False);

        yield return BeginDrag(WarehousePoint(home, 5, 2));
        Assert.That(home.warehouseUI.IsDragging, Is.True);
        Assert.That(home.warehouseUI.DragGrabOffset, Is.EqualTo(new Vector2Int(1, 1)));
        yield return Pointer(BackpackPoint(home, 1, 3), true);
        AssertPreview(home.backpackUI.view.preview, 3, 2);
        yield return Pointer(BackpackPoint(home, 1, 3), false);
        AssertBackpackItem(doll, 0, 2, true);
        AssertFrame(BackpackItemSize(home, doll.InstanceId), 3, 2);
        Assert.That(flow.Warehouse.TryGetPlacement(doll.InstanceId, out _), Is.False);
        yield return BeginDrag(BackpackPoint(home, 2, 0));
        Assert.That(home.backpackUI.IsDragging, Is.True);
        yield return Pointer(WarehousePoint(home, 0, 0), true);
        yield return Pointer(WarehousePoint(home, 0, 0), false);
        AssertWarehouseItem(medkit, 0, 0, false);
        Assert.That(flow.Backpack.TryGetPlacement(medkit.InstanceId, out _), Is.False);
        Assert.That(flow.Items.InstanceCount, Is.EqualTo(instanceCount));
        Assert.That(flow.Shelter.CurrentEv, Is.EqualTo(7));
        Assert.That(doll.OriginalSpawnPointId, Is.EqualTo(dollOrigin));
        Assert.That(medkit.OriginalSpawnPointId, Is.EqualTo(medkitOrigin));
        Assert.That(battery.OriginalSpawnPointId, Is.EqualTo(batteryOrigin));
        AssertWarehouseItem(battery, 8, 1, false);

        yield return CloseUsingTab(home);
        yield return InteractUsingE(home, FlowTarget(home, GameFlowInteraction.Departure));
        yield return WaitForStage(GameFlowStage.Exploring);
        Assert.That(flow.CurrentRound, Is.EqualTo(2));
        Assert.That(flow.Language.CurrentLanguage, Is.EqualTo(GameLanguage.English));
        Assert.That(new[] { flow.Warehouse.Width, flow.Warehouse.Height }, Is.EqualTo(new[] { 20, 24 }));
        Assert.That(flow.Player, Is.SameAs(samePlayer));
        Assert.That(flow.Warehouse.IsManagementAllowed, Is.False);
        Assert.That(flow.TryOpenStorage(), Is.False);
        AssertBackpackItem(doll, 0, 2, true);
        AssertWarehouseItem(medkit, 0, 0, false);
        Assert.That(flow.Shelter.CurrentEv, Is.EqualTo(7));
        Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        LootBox secondBox = Object.FindObjectsByType<LootBox>(FindObjectsSortMode.None)
            .Single(value => value.gameObject.scene.path == RuinsPath && value.sourceId == "supplies-box");
        Assert.That(secondBox.State.TryGetEntry("doll", out _), Is.False);
        Assert.That(secondBox.State.TryGetEntry("medkit", out _), Is.False);
        AssertWarehouseItem(battery, 8, 1, false);
        Assert.That(secondBox.State.GridWidth, Is.EqualTo(secondBox.gridWidth));
        Assert.That(secondBox.State.GridHeight, Is.EqualTo(secondBox.gridHeight));
        GameFlowSceneBindings secondRuins = Bindings(RuinsPath);
        yield return InteractUsingE(secondRuins, FlowTarget(secondRuins, GameFlowInteraction.Extraction));
        yield return WaitForStage(GameFlowStage.Shelter);
        Assert.That(flow.SettledRound, Is.EqualTo(2));
        Assert.That(flow.Player, Is.SameAs(samePlayer));
        AssertBackpackItem(doll, 0, 2, true);
        AssertWarehouseItem(medkit, 0, 0, false);
        Assert.That(flow.Shelter.CurrentEv, Is.EqualTo(7));
        AssertWarehouseItem(battery, 8, 1, false);

        // 第二轮新场景再次通过真实View点击，检查重新Bind后只有一次提交且原有仓库物不重排。
        GameFlowSceneBindings secondHome = Bindings(ShelterPath);
        yield return InteractUsingE(secondHome, FlowTarget(secondHome, GameFlowInteraction.Storage));
        Assert.That(secondHome, Is.Not.SameAs(home));
        Assert.That(secondHome.backpackUI.QuickTransferRequested, Is.Not.Null);
        Assert.That(secondHome.warehouseUI.QuickTransferRequested, Is.Not.Null);
        int secondRoundInstanceCount = flow.Items.InstanceCount;
        yield return QuickClick(secondHome, WarehousePoint(secondHome, 0, 0), Key.LeftCtrl, medkit);
        AssertBackpackItem(medkit, 0, 0, false);
        yield return QuickClick(secondHome, BackpackPoint(secondHome, 0, 0), Key.RightCtrl, medkit);
        AssertWarehouseItem(medkit, 0, 0, false);
        AssertBackpackItem(doll, 0, 2, true);
        AssertWarehouseItem(battery, 8, 1, false);
        Assert.That(flow.Shelter.CurrentEv, Is.EqualTo(7));
        Assert.That(flow.Items.InstanceCount, Is.EqualTo(secondRoundInstanceCount));
    }

    private IEnumerator LoadBootstrap()
    {
        startedSession = true;
        UnityAction<Scene, LoadSceneMode> activate = (scene, mode) =>
        {
            if (scene.path == BootstrapPath) { SceneManager.SetActiveScene(scene); }
        };
        SceneManager.sceneLoaded += activate;
        try
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(BootstrapPath, new LoadSceneParameters(LoadSceneMode.Additive));
        }
        finally { SceneManager.sceneLoaded -= activate; }
        flow = Object.FindFirstObjectByType<GameFlowManager>();
        Assert.That(flow, Is.Not.Null);
        Assert.That(GameBootstrap.Instance, Is.Not.Null);
        persistentRoot = GameBootstrap.Instance.gameObject;
        yield return WaitForStage(GameFlowStage.Shelter);
        flow.Transfers.Completed += OnTransferCompleted;
    }

    private void OnTransferCompleted(string instanceId) { completedTransfers.Add(instanceId); }

    private IEnumerator WaitForStage(GameFlowStage expected)
    {
        float deadline = Time.realtimeSinceStartup + 30f;
        while (flow.Stage != expected && Time.realtimeSinceStartup < deadline)
        {
            Assert.That(flow.Stage, Is.Not.EqualTo(GameFlowStage.Failed), flow.LastError);
            yield return null;
        }
        Assert.That(flow.Stage, Is.EqualTo(expected), flow.LastError);
        // 阶段完成后的真实首个跟随帧，检查新场景输出与本场景边界；不主动更新Brain。
        yield return null;
        MinimumLoopSceneTests.AssertSceneCamera(flow, Bindings(expected == GameFlowStage.Shelter ? ShelterPath : RuinsPath));
    }

    private static IEnumerator WaitForRevealed(GameFlowSceneBindings bindings, LootEntryState entry)
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (!entry.Revealed && Time.realtimeSinceStartup < deadline)
        {
            Assert.That(bindings.lootUI.IsOpen, Is.True, "The real search session must remain open.");
            yield return null;
        }
        Assert.That(entry.Revealed, Is.True, "Real sequential search did not reveal " + entry.EntryId);
        yield return null;
        Canvas.ForceUpdateCanvases();
    }

    private static GameFlowInteractable FlowTarget(GameFlowSceneBindings bindings, GameFlowInteraction action)
    {
        return bindings.environmentRoot.GetComponentsInChildren<GameFlowInteractable>()
            .Single(target => target.interactionType == action);
    }

    private IEnumerator InteractUsingE(GameFlowSceneBindings bindings, Interactable target)
    {
        Assert.That(bindings.backpackUI.IsOpen || bindings.lootUI.IsOpen, Is.False, "Close the current UI before a new world E gesture.");
        if (bindings.warehouseUI != null) { Assert.That(bindings.warehouseUI.IsOpen, Is.False); }
        GameFlowStage stage = flow.Stage;
        int round = flow.CurrentRound;
        int settled = flow.SettledRound;
        MoveIntoTrigger(bindings, target);
        yield return null;
        yield return null;
        MinimumLoopSceneTests.AssertSceneCamera(flow, bindings);
        Assert.That(flow.Player.interaction.CurrentTarget, Is.SameAs(target));
        Assert.That(flow.Player.interaction.CurrentPromptKey, Is.EqualTo(target.PromptKey));
        Assert.That(bindings.hud.promptText.text, Is.EqualTo(flow.Language.GetText(target.PromptKey)));
        Assert.That(flow.Player.transform.position.z, Is.EqualTo(flow.Player.config.planeZ).Within(0.001f));
        Assert.That(flow.Stage, Is.EqualTo(stage), "Entering the TriggerBox alone cannot perform a flow action.");
        Assert.That(flow.CurrentRound, Is.EqualTo(round));
        Assert.That(flow.SettledRound, Is.EqualTo(settled));
        Assert.That(bindings.backpackUI.IsOpen || bindings.lootUI.IsOpen, Is.False, "Entering alone must not open or take anything.");
        if (bindings.warehouseUI != null) { Assert.That(bindings.warehouseUI.IsOpen, Is.False); }
        yield return Press(Key.E);
    }

    private IEnumerator CloseUsingTab(GameFlowSceneBindings bindings)
    {
        if (bindings.backpackUI.IsOpen) { yield return Press(Key.Tab); }
        Assert.That(bindings.backpackUI.IsOpen || bindings.lootUI.IsOpen, Is.False);
        if (bindings.warehouseUI != null) { Assert.That(bindings.warehouseUI.IsOpen, Is.False); }
        Assert.That(flow.Player.CanAct, Is.True);
    }

    private void MoveIntoTrigger(GameFlowSceneBindings bindings, Interactable target)
    {
        Assert.That(target.TryGetInteractionTrigger(out BoxCollider trigger), Is.True, target.name + " needs its authored TriggerBox.");
        CapsuleCollider body = flow.Player.GetComponent<CapsuleCollider>();
        Assert.That(body, Is.Not.Null);
        Physics.SyncTransforms();
        Bounds range = trigger.bounds;
        float inset = Mathf.Min(0.2f, range.size.x * 0.25f);
        float[] candidateX = { range.min.x - body.bounds.extents.x + inset,
            range.max.x + body.bounds.extents.x - inset, range.center.x };
        foreach (float x in candidateX)
        {
            Vector3 point = bindings.spawnPoint.position;
            point.x = x;
            Assert.That(flow.Player.RelocateWithinRun(point), Is.True);
            Physics.SyncTransforms();
            bool overlaps = Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
                trigger, trigger.transform.position, trigger.transform.rotation, out _, out _);
            if (!overlaps || !flow.Player.interaction.CanReachTarget(target)) { continue; }
            // 搜刮模型需确实留有可见间隔；不通过站进模型或扩大旧球范围制造通过。
            if ((target is LootBox || target is GroundItem) && target.GetComponentsInChildren<Renderer>()
                .Any(renderer => renderer.enabled && renderer.GetComponent<TMP_Text>() == null && renderer.bounds.Intersects(body.bounds))) { continue; }
            flow.Player.interaction.RefreshTarget();
            if (flow.Player.interaction.CurrentTarget == target) { return; }
        }
        Assert.Fail(target.name + " has no reachable test position at the authored trigger edges on the fixed player plane: " + range);
    }

    private void MoveOutsideTrigger(GameFlowSceneBindings bindings, Interactable target)
    {
        Assert.That(target.TryGetInteractionTrigger(out BoxCollider trigger), Is.True);
        CapsuleCollider body = flow.Player.GetComponent<CapsuleCollider>();
        Physics.SyncTransforms();
        Vector3 point = bindings.spawnPoint.position;
        point.x = trigger.bounds.max.x + body.bounds.extents.x + 0.25f;
        Assert.That(flow.Player.RelocateWithinRun(point), Is.True);
        Physics.SyncTransforms();
        Assert.That(Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
            trigger, trigger.transform.position, trigger.transform.rotation, out _, out _), Is.False);
    }

    private IEnumerator AssertOutsideTriggerRejectsE(GameFlowSceneBindings bindings, GameFlowInteractable target)
    {
        GameFlowStage stage = flow.Stage;
        int round = flow.CurrentRound;
        MoveOutsideTrigger(bindings, target);
        yield return null;
        yield return null;
        Assert.That(flow.Player.interaction.CanReachTarget(target), Is.False);
        Assert.That(flow.Player.interaction.CurrentTarget, Is.Null);
        Assert.That(flow.Player.interaction.CurrentPromptKey, Is.Empty);
        Assert.That(bindings.hud.promptText.text, Is.Empty);
        yield return Press(Key.E);
        Assert.That(bindings.backpackUI.IsOpen || bindings.warehouseUI.IsOpen || bindings.lootUI.IsOpen, Is.False);
        Assert.That(flow.Stage, Is.EqualTo(stage));
        Assert.That(flow.CurrentRound, Is.EqualTo(round));
    }

    private void AssertOpenSource(GameFlowSceneBindings bindings, Interactable target, LootSourceState source)
    {
        Assert.That(bindings.lootUI.IsOpen && bindings.backpackUI.IsOpen, Is.True);
        Assert.That(bindings.lootUI.Source, Is.SameAs(source));
        Assert.That(flow.Player.interaction.InputBlocked, Is.True);
        Assert.That(flow.Player.interaction.CurrentTarget, Is.Null);
        Assert.That(flow.Player.interaction.CanReachTarget(target), Is.True, "UI lock must not invalidate the source's spatial reach.");
        Assert.That(bindings.lootUI.IsSessionValid(bindings.lootUI.SessionToken, source), Is.True);
    }

    private IEnumerator Pointer(Vector2 point, bool held)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = (ushort)(held ? 1 : 0) });
        yield return null;
        yield return null;
    }

    private IEnumerator BeginDrag(Vector2 point)
    {
        yield return Pointer(point, false);
        yield return Pointer(point, true);
        yield return Pointer(point + Vector2.right * 20f, true);
    }

    private IEnumerator Click(Vector2 point)
    {
        yield return Pointer(point, false);
        yield return Pointer(point, true);
        yield return Pointer(point, false);
    }

    private IEnumerator QuickClick(GameFlowSceneBindings bindings, Vector2 point, Key control, ItemInstance item)
    {
        // 真实InputSystem排队状态经过UGUI，不调用快捷API或ExecuteEvents伪造提交。
        AssertTopmostItem(bindings, point, item.InstanceId);
        int before = completedTransfers.Count;
        int count = flow.Items.InstanceCount;
        int ev = flow.Shelter.CurrentEv;
        object[] invariant = ItemInvariant(item);
        try
        {
            yield return Pointer(point, false);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(control));
            yield return null;
            yield return null;
            yield return Pointer(point, true);
            yield return Pointer(point, true);
            Assert.That(completedTransfers.Count, Is.EqualTo(before), "Holding Ctrl+left must wait for one completed click.");
            Assert.That(bindings.backpackUI.IsDragging, Is.False);
            if (bindings.warehouseUI != null) { Assert.That(bindings.warehouseUI.IsDragging, Is.False); }
            yield return Pointer(point, false);
            yield return Pointer(point, false);
            Assert.That(completedTransfers.Count, Is.EqualTo(before + 1), "One Ctrl press cycle must commit exactly one item.");
            Assert.That(completedTransfers[before], Is.EqualTo(item.InstanceId));
            Assert.That(bindings.backpackUI.IsDragging, Is.False);
            Assert.That(bindings.backpackUI.IsExternalPreview, Is.False);
            if (bindings.warehouseUI != null) { Assert.That(bindings.warehouseUI.IsDragging, Is.False); }
            Assert.That(flow.Items.TryGetInstance(item.InstanceId, out ItemInstance registered), Is.True);
            Assert.That(registered, Is.SameAs(item));
            Assert.That(ItemInvariant(item), Is.EqualTo(invariant));
            Assert.That(flow.Items.InstanceCount, Is.EqualTo(count));
            Assert.That(flow.Shelter.CurrentEv, Is.EqualTo(ev));
        }
        finally
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(mouse, new MouseState { position = point });
        }
        yield return null;
        yield return null;
        Assert.That(completedTransfers.Count, Is.EqualTo(before + 1), "Modifier release cannot submit again.");
    }

    private static object[] ItemInvariant(ItemInstance item)
    {
        return new object[] { item.Definition, item.DefinitionId, item.UniqueId, item.SourceId,
            item.OriginalSpawnPointId, item.IsRetained, item.UseRound, item.UsesThisRound,
            item.RecoveryRound, item.LastLossReason, item.SpawnClaimed };
    }

    private static void AssertTopmostItem(GameFlowSceneBindings bindings, Vector2 point, string instanceId)
    {
        List<RaycastResult> hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
        Assert.That(hits, Is.Not.Empty);
        GameObject hit = hits[0].gameObject;
        BackpackItemView backpackItem = hit.GetComponentInParent<BackpackItemView>();
        WarehouseItemView warehouseItem = hit.GetComponentInParent<WarehouseItemView>();
        LootItemView lootItem = hit.GetComponentInParent<LootItemView>();
        string hitId = backpackItem != null ? backpackItem.InstanceId : warehouseItem != null ? warehouseItem.InstanceId : null;
        if (lootItem != null && bindings.lootUI.Source.TryGetEntry(lootItem.EntryId, out LootEntryState entry))
        {
            hitId = entry.InstanceId;
        }
        Assert.That(hitId, Is.EqualTo(instanceId), "The quick click must reach the intended actual item View.");
    }

    private IEnumerator ScrollWarehouse(GameFlowSceneBindings bindings, bool toTop)
    {
        WarehouseView view = bindings.warehouseUI.view;
        Vector2 point = Center(view.viewport);
        yield return Pointer(point, false);
        InputSystem.QueueStateEvent(mouse, new MouseState
        {
            position = point,
            scroll = new Vector2(0f, toTop ? 1200f : -1200f)
        });
        yield return null;
        yield return null;
        yield return Pointer(point, false);
        Canvas.ForceUpdateCanvases();
        Assert.That(view.scrollRect.horizontal, Is.False);
        Assert.That(view.scrollRect.verticalNormalizedPosition, Is.EqualTo(toTop ? 1f : 0f).Within(0.01f),
            "Queued wheel events must reach the actual warehouse viewport boundary.");
    }

    private IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }

    private static GameFlowSceneBindings Bindings(string path)
    {
        return Object.FindObjectsByType<GameFlowSceneBindings>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Single(value => value.gameObject.scene.path == path);
    }

    private static Vector2 Center(Transform transform)
    {
        Canvas.ForceUpdateCanvases();
        RectTransform rect = (RectTransform)transform;
        return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
    }

    private static void AssertReadableLabels(GameFlowSceneBindings bindings)
    {
        Canvas.ForceUpdateCanvases();
        int checkedCount = 0;
        foreach (TMP_Text text in bindings.presentationRoot.GetComponentsInChildren<TMP_Text>())
        {
            if (!text.isActiveAndEnabled || string.IsNullOrWhiteSpace(text.text)
                || text.overflowMode == TextOverflowModes.Ellipsis) { continue; }
            // 滚动内容可高于视口；物品缩略名允许省略，完整名称通过详情阅读。
            bool isScrollContent = text.GetComponentsInParent<ScrollRect>().Any(scroll =>
                scroll.content != null && text.transform.IsChildOf(scroll.content));
            if (isScrollContent) { continue; }
            text.ForceMeshUpdate();
            string label = bindings.gameObject.scene.name + "/" + text.transform.parent.name + "/" + text.name;
            Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(text.rectTransform.rect.height + 0.75f),
                label + " clips vertically: " + text.text);
            if (Screen.width <= 1000)
            {
                float renderedFontSize = text.fontSize * Mathf.Abs(text.rectTransform.lossyScale.y);
                Assert.That(renderedFontSize, Is.GreaterThanOrEqualTo(11.95f), label + " must remain at least 12 screen pixels.");
            }
            checkedCount++;
        }
        Assert.That(checkedCount, Is.GreaterThan(5), "The real visible UI labels must be inspected.");
    }

    private static void AssertMatchingGridGeometry(GameFlowSceneBindings bindings)
    {
        Canvas.ForceUpdateCanvases();
        BackpackView backpack = bindings.backpackUI.view;
        WarehouseView warehouse = bindings.warehouseUI.view;
        Assert.That(new[] { backpack.cellSize, backpack.gap, backpack.padding }, Is.EqualTo(new[] { 66f, 4f, 6f }));
        Assert.That(new[] { warehouse.cellSize, warehouse.gap, warehouse.padding }, Is.EqualTo(new[] { 66f, 4f, 6f }));
        Assert.That(warehouse.EffectiveCellSize, Is.EqualTo(66f).Within(0.01f), "A narrow viewport must not shrink the warehouse.");
        AssertGridBackground(backpack.gridRoot, 4, 4);
        AssertGridBackground(warehouse.gridRoot, bindings.warehouseUI.manager.Width, bindings.warehouseUI.manager.Height);
        Assert.That(RenderedSize(warehouse.gridRoot).x, Is.LessThanOrEqualTo(RenderedSize(warehouse.viewport).x + 0.1f));
        Assert.That(warehouse.scrollRect.horizontal, Is.False);
        float backpackScale = Mathf.Abs(backpack.gridRoot.lossyScale.x);
        float warehouseScale = Mathf.Abs(warehouse.gridRoot.lossyScale.x);
        Assert.That(warehouse.EffectiveCellSize * warehouseScale,
            Is.EqualTo(backpack.cellSize * backpackScale).Within(0.05f), "Backpack and warehouse rendered cells must match.");
        Assert.That(warehouse.gap * warehouseScale,
            Is.EqualTo(backpack.gap * backpackScale).Within(0.05f), "Rendered gaps must match as well as cells.");
        foreach (int cells in new[] { 2, 3 })
        {
            float backpackFrame = (cells * backpack.cellSize + (cells - 1) * backpack.gap) * backpackScale;
            float warehouseFrame = (cells * warehouse.EffectiveCellSize + (cells - 1) * warehouse.gap) * warehouseScale;
            Assert.That(warehouseFrame, Is.EqualTo(backpackFrame).Within(0.1f), "Multi-cell frames include their interior gaps.");
        }
    }

    private static void AssertGridBackground(RectTransform root, int width, int height)
    {
        Canvas.ForceUpdateCanvases();
        Assert.That(root, Is.Not.Null);
        Assert.That(root.lossyScale.x, Is.EqualTo(1f).Within(0.001f));
        Assert.That(root.lossyScale.y, Is.EqualTo(1f).Within(0.001f));
        Image[] cells = root.GetComponentsInChildren<Image>().Where(value => value.transform.parent == root
            && value.name.StartsWith("Cell_")).ToArray();
        Assert.That(cells.Length, Is.EqualTo(width * height), "All cells, including empty cells, must exist under " + root.name);
        foreach (Image cell in cells)
        {
            Assert.That(cell.enabled, Is.True);
            Assert.That(cell.color.a, Is.GreaterThan(0f));
            Assert.That(cell.raycastTarget, Is.False, "Background cells must not intercept item drag rays.");
            AssertSameRenderedSize(new Vector2(66, 66), RenderedSize(cell.rectTransform));
        }
        RectTransform first = cells.Single(value => value.name == "Cell_0_0").rectTransform;
        Vector3 firstCorner = first.TransformPoint(new Vector3(first.rect.xMin, first.rect.yMax, 0f));
        Vector3 gridCorner = root.TransformPoint(new Vector3(root.rect.xMin, root.rect.yMax, 0f));
        Assert.That(firstCorner.x - gridCorner.x, Is.EqualTo(6f).Within(0.1f));
        Assert.That(gridCorner.y - firstCorner.y, Is.EqualTo(6f).Within(0.1f));
        if (width > 1)
        {
            RectTransform next = cells.Single(value => value.name == "Cell_1_0").rectTransform;
            Assert.That(Center(next).x - Center(first).x, Is.EqualTo(70f).Within(0.1f));
        }
        if (height > 1)
        {
            RectTransform next = cells.Single(value => value.name == "Cell_0_1").rectTransform;
            Assert.That(Center(first).y - Center(next).y, Is.EqualTo(70f).Within(0.1f));
        }
        AssertSameRenderedSize(new Vector2(width * 66f + (width - 1) * 4f + 12f,
            height * 66f + (height - 1) * 4f + 12f), RenderedSize(root));
    }

    private static void AssertLootGrid(GameFlowSceneBindings bindings, LootBox box)
    {
        LootView view = bindings.lootUI.view;
        Assert.That(bindings.lootUI.Source, Is.SameAs(box.State));
        Assert.That(box.gridWidth, Is.GreaterThan(0));
        Assert.That(box.gridHeight, Is.GreaterThan(0));
        Assert.That(box.State.GridWidth, Is.EqualTo(box.gridWidth));
        Assert.That(box.State.GridHeight, Is.EqualTo(box.gridHeight));
        Assert.That(box.State.LayoutColumns, Is.EqualTo(box.gridWidth));
        Assert.That(box.State.LayoutRows, Is.EqualTo(box.gridHeight));
        Assert.That(new[] { view.cellSize, view.gap, view.padding }, Is.EqualTo(new[] { 66f, 4f, 6f }));
        AssertGridBackground(view.gridRoot, box.gridWidth, box.gridHeight);
        AssertGridBackground(bindings.backpackUI.view.gridRoot, 4, 4);
        AssertSameRenderedSize(RenderedSize(view.gridRoot), RenderedSize(view.contentRoot));
        Image viewportImage = view.GetReturnViewport().GetComponent<Image>();
        Assert.That(viewportImage, Is.Not.Null);
        Assert.That(viewportImage.color.a, Is.EqualTo(0f).Within(0.001f), "The blank viewport must not keep the old opaque fill.");
        Assert.That(viewportImage.raycastTarget, Is.True, "Removing the fill must preserve transparent pointer reception.");
        Assert.That(view.GetReturnViewport().GetComponent<RectMask2D>(), Is.Not.Null);
        foreach (LootEntryState entry in box.State.Entries.Where(value => !value.Taken))
        {
            Assert.That(entry.HasLayout, Is.True);
            Assert.That(entry.LayoutX, Is.GreaterThanOrEqualTo(0));
            Assert.That(entry.LayoutY, Is.GreaterThanOrEqualTo(0));
            Assert.That(entry.LayoutX + entry.LayoutWidth, Is.LessThanOrEqualTo(box.gridWidth));
            Assert.That(entry.LayoutY + entry.LayoutHeight, Is.LessThanOrEqualTo(box.gridHeight));
        }
    }

    private static Dictionary<string, RectInt> LootSlots(LootSourceState source)
    {
        return source.Entries.ToDictionary(entry => entry.EntryId,
            entry => new RectInt(entry.LayoutX, entry.LayoutY, entry.LayoutWidth, entry.LayoutHeight));
    }

    private static void AssertLootSlots(LootSourceState source, Dictionary<string, RectInt> before)
    {
        foreach (KeyValuePair<string, RectInt> pair in before)
        {
            Assert.That(source.TryGetEntry(pair.Key, out LootEntryState entry), Is.True);
            Assert.That(new RectInt(entry.LayoutX, entry.LayoutY, entry.LayoutWidth, entry.LayoutHeight),
                Is.EqualTo(pair.Value), "Picking up, adding a different item, F10 and reopening must not move the original entries.");
        }
    }

    private static Vector2 LootItemSize(GameFlowSceneBindings bindings, string entryId)
    {
        return RenderedSize(LootItemRect(bindings, entryId));
    }

    private static RectTransform LootItemRect(GameFlowSceneBindings bindings, string entryId)
    {
        return (RectTransform)bindings.lootUI.view.contentRoot.GetComponentsInChildren<LootItemView>()
            .Single(value => value.EntryId == entryId).transform;
    }

    private void AssertLootItem(LootSourceState source, LootEntryState entry, ItemInstance item, int x, int y, bool rotated)
    {
        Assert.That(source.TryGetEntry(entry.EntryId, out LootEntryState registeredEntry), Is.True);
        Assert.That(registeredEntry, Is.SameAs(entry));
        Assert.That(flow.Items.TryGetInstance(entry.InstanceId, out ItemInstance registeredItem), Is.True);
        Assert.That(registeredItem, Is.SameAs(item));
        Assert.That(entry.Taken, Is.False);
        Assert.That(entry.Revealed, Is.True);
        Assert.That(entry.LayoutRotated, Is.EqualTo(rotated));
        Assert.That(new[] { entry.LayoutX, entry.LayoutY, entry.LayoutWidth, entry.LayoutHeight },
            Is.EqualTo(new[] { x, y, rotated ? item.Definition.height : item.Definition.width,
                rotated ? item.Definition.width : item.Definition.height }));
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(item.ContainerId, Is.EqualTo(source.SourceId));
        Assert.That(flow.Backpack.TryGetPlacement(item.InstanceId, out _), Is.False);
    }

    private static Rect AssertLootReturnPreview(GameFlowSceneBindings bindings, int x, int y, int width, int height)
    {
        LootView view = bindings.lootUI.view;
        Assert.That(view.IsReturnPreviewVisible, Is.True);
        RectTransform preview = view.ReturnPreviewRect;
        Assert.That(preview, Is.Not.Null);
        Assert.That(preview.anchoredPosition.x, Is.EqualTo(6 + x * 70).Within(0.1f));
        Assert.That(preview.anchoredPosition.y, Is.EqualTo(-6 - y * 70).Within(0.1f));
        AssertFrame(RenderedSize(preview), width, height);
        Image[] edges = preview.GetComponentsInChildren<Image>();
        Assert.That(edges.Length, Is.EqualTo(4));
        foreach (Image edge in edges)
        {
            Assert.That(edge.raycastTarget, Is.False);
            Assert.That(edge.color.g, Is.GreaterThan(edge.color.r), "Legal precise placement must show the valid outline.");
        }
        return ScreenRect(preview);
    }

    private static Rect ScreenRect(RectTransform rect)
    {
        Canvas.ForceUpdateCanvases();
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(null, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(null, corners[2]);
        return new Rect(min, max - min);
    }

    private static void AssertScreenRect(Rect expected, Rect actual)
    {
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.1f));
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.1f));
        AssertSameRenderedSize(expected.size, actual.size);
    }

    private static void AssertFrame(Vector2 actual, int width, int height)
    {
        AssertSameRenderedSize(new Vector2(width * 66f + (width - 1) * 4f, height * 66f + (height - 1) * 4f), actual);
    }

    private static void AssertPreview(Image preview, int width, int height)
    {
        Assert.That(preview, Is.Not.Null);
        Assert.That(preview.gameObject.activeInHierarchy, Is.True);
        Assert.That(preview.raycastTarget, Is.False);
        AssertFrame(RenderedSize(preview.rectTransform), width, height);
    }

    private static Vector2 BackpackItemSize(GameFlowSceneBindings bindings, string instanceId)
    {
        BackpackItemView item = bindings.backpackUI.view.itemRoot.GetComponentsInChildren<BackpackItemView>()
            .Single(value => value.InstanceId == instanceId);
        return RenderedSize((RectTransform)item.transform);
    }

    private static Vector2 WarehouseItemSize(GameFlowSceneBindings bindings, string instanceId)
    {
        WarehouseItemView item = bindings.warehouseUI.view.itemRoot.GetComponentsInChildren<WarehouseItemView>()
            .Single(value => value.InstanceId == instanceId);
        return RenderedSize((RectTransform)item.transform);
    }

    private static Vector2 RenderedSize(RectTransform rect)
    {
        Canvas.ForceUpdateCanvases();
        return new Vector2(rect.rect.width * Mathf.Abs(rect.lossyScale.x), rect.rect.height * Mathf.Abs(rect.lossyScale.y));
    }

    private static void AssertSameRenderedSize(Vector2 expected, Vector2 actual)
    {
        Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.1f), "The actual multi-cell item frame width must remain equal.");
        Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.1f), "The actual multi-cell item frame height must remain equal.");
    }

    private static Vector2 EntryPoint(GameFlowSceneBindings bindings, string entryId, int grabX = 0, int grabY = 0)
    {
        Canvas.ForceUpdateCanvases();
        LootItemView item = bindings.lootUI.view.contentRoot.GetComponentsInChildren<LootItemView>()
            .Single(value => value.EntryId == entryId);
        RectTransform rect = (RectTransform)item.transform;
        float inset = bindings.lootUI.view.cellSize * 0.35f;
        float pitch = bindings.lootUI.view.cellSize + bindings.lootUI.view.gap;
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(
            new Vector3(rect.rect.xMin + inset + grabX * pitch, rect.rect.yMax - inset - grabY * pitch, 0f)));
        Assert.That(RectTransformUtility.RectangleContainsScreenPoint(bindings.lootUI.view.GetReturnViewport(), point, null), Is.True);
        List<RaycastResult> hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
        Assert.That(hits, Is.Not.Empty);
        Assert.That(hits[0].gameObject.transform == item.transform || hits[0].gameObject.transform.IsChildOf(item.transform),
            Is.True, "EntryId must resolve to a visible topmost item, not a background, countdown or hidden list row.");
        return point;
    }

    private static Vector2 LootPoint(GameFlowSceneBindings bindings, int x, int y)
    {
        LootView view = bindings.lootUI.view;
        Vector2 point = CellPoint(view.contentRoot, view.padding, view.cellSize, view.gap, x, y);
        Assert.That(view.TryScreenToCell(point, null, out Vector2Int cell), Is.True);
        Assert.That(cell, Is.EqualTo(new Vector2Int(x, y)));
        return point;
    }

    private static Vector2 LootBlankPoint(GameFlowSceneBindings bindings)
    {
        LootView view = bindings.lootUI.view;
        RectTransform viewport = view.GetReturnViewport();
        Vector2 point = RectTransformUtility.WorldToScreenPoint(null, viewport.TransformPoint(
            new Vector3(viewport.rect.center.x, viewport.rect.yMin + 20f, 0f)));
        Assert.That(RectTransformUtility.RectangleContainsScreenPoint(view.contentRoot, point, null), Is.False);
        Assert.That(view.TryHitReturnViewport(point, null), Is.True);
        Assert.That(view.TryScreenToCell(point, null, out _), Is.False);
        return point;
    }

    private static Vector2 BackpackPoint(GameFlowSceneBindings bindings, int x, int y)
    {
        BackpackView view = bindings.backpackUI.view;
        Vector2 point = CellPoint(view.gridRoot, view.padding, view.cellSize, view.gap, x, y);
        Assert.That(view.TryScreenToCell(point, null, out Vector2Int cell), Is.True);
        Assert.That(cell, Is.EqualTo(new Vector2Int(x, y)));
        return point;
    }

    private static Vector2 WarehousePoint(GameFlowSceneBindings bindings, int x, int y)
    {
        WarehouseView view = bindings.warehouseUI.view;
        Vector2 point = CellPoint(view.gridRoot, view.padding, view.EffectiveCellSize, view.gap, x, y);
        Assert.That(view.TryScreenToCell(point, null, out Vector2Int cell), Is.True);
        Assert.That(cell, Is.EqualTo(new Vector2Int(x, y)));
        return point;
    }

    private static Vector2 CellPoint(RectTransform root, float padding, float size, float gap, int x, int y)
    {
        Canvas.ForceUpdateCanvases();
        return RectTransformUtility.WorldToScreenPoint(null, root.TransformPoint(new Vector3(
            root.rect.xMin + padding + x * (size + gap) + size * 0.35f,
            root.rect.yMax - padding - y * (size + gap) - size * 0.35f, 0f)));
    }

    private void AssertBackpackItem(ItemInstance item, int x, int y, bool rotated)
    {
        Assert.That(flow.Backpack.TryGetCarriedItem(item.InstanceId, out ItemInstance registered), Is.EqualTo(BackpackResult.Success));
        Assert.That(registered, Is.SameAs(item));
        Assert.That(flow.Backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement placement), Is.True);
        Assert.That(new[] { placement.X, placement.Y }, Is.EqualTo(new[] { x, y }));
        Assert.That(placement.Rotated, Is.EqualTo(rotated));
    }

    private void AssertWarehouseItem(ItemInstance item, int x, int y, bool rotated)
    {
        Assert.That(flow.Warehouse.TryGetStoredItem(item.InstanceId, out ItemInstance registered), Is.EqualTo(WarehouseResult.Success));
        Assert.That(registered, Is.SameAs(item));
        Assert.That(flow.Warehouse.TryGetPlacement(item.InstanceId, out WarehousePlacement placement), Is.True);
        Assert.That(new[] { placement.X, placement.Y }, Is.EqualTo(new[] { x, y }));
        Assert.That(placement.Rotated, Is.EqualTo(rotated));
    }
}
#endif
