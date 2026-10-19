#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class BackpackIntegrationTests
{
    private const string ScenePath = "Assets/Scenes/Tests/Backpack/BackpackTest.unity";
    private readonly List<GameObject> ownedObjects = new List<GameObject>();
    private readonly List<ItemDataSO> ownedDefinitions = new List<ItemDataSO>();
    private Keyboard keyboard;
    private Mouse mouse;
    private InputSettings originalInputSettings;
    private InputSettings testInputSettings;
    private bool originalRunInBackground;
    private bool loadedScene;
    private BackpackTestDriver driver;
    private BackpackUIController controller;
    private BackpackManager manager;

    [SetUp]
    public void RequireCleanTestScene()
    {
        if (Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
        {
            Assert.Ignore("Run Backpack PlayMode tests from the Test Runner clean test scene.");
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
        foreach (ItemDataSO definition in ownedDefinitions)
        {
            if (definition != null) { Object.Destroy(definition); }
        }
        ownedDefinitions.Clear();
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
    public IEnumerator SharedModulesResolveBackpackTextAndF10RefreshesUiWithoutChangingItems()
    {
        yield return LoadScene();
        GameBootstrap bootstrap = GameBootstrap.Instance;
        ItemManager[] itemManagers = bootstrap.GetComponentsInChildren<ItemManager>(true);
        LocalizationManager[] languages = bootstrap.GetComponentsInChildren<LocalizationManager>(true);
        Assert.That(itemManagers, Has.Length.EqualTo(1), "The scene must reuse the Bootstrap item module.");
        Assert.That(languages, Has.Length.EqualTo(1), "The Backpack table must belong to the sole active language module.");
        LocalizationManager language = languages[0];
        Assert.That(LocalizationManager.Instance, Is.SameAs(language));
        Assert.That(driver.itemManager, Is.SameAs(itemManagers[0]));
        Assert.That(manager.itemManager, Is.SameAs(itemManagers[0]));
        Assert.That(manager.Items, Is.SameAs(itemManagers[0]));
        Assert.That(manager.Items.InstanceCount, Is.EqualTo(5));

        LocalizationTableSO table = AssetDatabase.LoadAssetAtPath<LocalizationTableSO>(
            "Assets/Data/Localization/Backpack/BackpackText.asset");
        Assert.That(table, Is.Not.Null);
        Assert.That(language.tables.Count(candidate => candidate == table), Is.EqualTo(1));
        string[] reportedKeys =
        {
            "backpack.test.scope", "backpack.test.instructions", "backpack.title", "backpack.close",
            "backpack.controls", "backpack.language_hint", "backpack.loot.title", "backpack.loot.disconnected",
            "backpack.ready", "backpack.select_hint"
        };
        Dictionary<string, LocalizationEntry> expected = reportedKeys.ToDictionary(key => key,
            key => table.entries.Single(entry => entry.key == key));
        LocalizedText[] labels = SceneManager.GetSceneByPath(ScenePath).GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<LocalizedText>(true)).ToArray();
        Assert.That(labels.Select(label => label.key), Does.Contain("backpack.test.scope"));
        Assert.That(labels.Select(label => label.key), Does.Contain("backpack.test.instructions"));
        string original = Snapshot();
        AssertBackpackText(GameLanguage.Chinese);

        yield return Press(Key.Tab);
        Assert.That(controller.IsOpen, Is.True);
        AssertBackpackText(GameLanguage.Chinese);
        yield return Press(Key.F10);
        AssertBackpackText(GameLanguage.English);
        Assert.That(Snapshot(), Is.EqualTo(original));

        // Scope is hidden while the backpack is open; reopening the hints must show the current language immediately.
        yield return Press(Key.Tab);
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(labels.Single(label => label.key == "backpack.test.scope").isActiveAndEnabled, Is.True);
        AssertBackpackText(GameLanguage.English);
        yield return Press(Key.Tab);
        Assert.That(controller.IsOpen, Is.True);
        yield return Press(Key.F10);
        AssertBackpackText(GameLanguage.Chinese);
        Assert.That(Snapshot(), Is.EqualTo(original), "Language changes and reopening UI must preserve all sample items.");
        LogAssert.NoUnexpectedReceived();

        void AssertBackpackText(GameLanguage expectedLanguage)
        {
            Assert.That(language.CurrentLanguage, Is.EqualTo(expectedLanguage));
            foreach (KeyValuePair<string, LocalizationEntry> pair in expected)
            {
                string translation = expectedLanguage == GameLanguage.Chinese ? pair.Value.chinese : pair.Value.english;
                Assert.That(language.GetText(pair.Key), Is.EqualTo(translation), pair.Key);
            }
            if (controller.IsOpen)
            {
                LocalizationEntry title = expected["backpack.title"];
                Assert.That(controller.view.titleText.text,
                    Is.EqualTo(expectedLanguage == GameLanguage.Chinese ? title.chinese : title.english));
            }
            foreach (LocalizedText label in labels.Where(label => label.isActiveAndEnabled))
            {
                Assert.That(label.localizationManager, Is.SameAs(language), label.key);
                Assert.That(label.targetText.text, Is.EqualTo(language.GetText(label.key)), label.key);
            }
        }
    }

    [UnityTest]
    public IEnumerator QueuedPointerDragKeyboardRotationAndF10PreservePreviewUntilRelease()
    {
        yield return LoadScene();
        Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        Assert.That(Object.FindFirstObjectByType<PlayerTestDriver>(), Is.Null);
        Assert.That(manager.Items.InstanceCount, Is.EqualTo(5));
        Assert.That(manager.Items.GetInstances().Sum(item => item.Definition.width * item.Definition.height), Is.EqualTo(20));
        Assert.That(manager.GetPlacements().Sum(placement => placement.Width * placement.Height), Is.EqualTo(14));
        ItemInstance battery = FindItem("battery");
        string original = Snapshot();
        yield return Press(Key.Tab);
        Assert.That(controller.IsOpen, Is.True);
        Assert.That(driver.player.IsBackpackOpen, Is.True);
        AssertPanelsFitGameView();
        Vector2 start = CellPoint(2, 2);
        yield return SetPointer(start, false);
        yield return SetPointer(start, true);
        yield return SetPointer(start + Vector2.right * 20f, true);
        Assert.That(controller.IsDragging, Is.True, "Real EventSystem pointer events must start the drag.");
        Assert.That(controller.DragInstanceId, Is.EqualTo(battery.InstanceId));
        yield return Press(Key.R);
        Assert.That(controller.PreviewRotated, Is.True);
        int previewX = controller.PreviewX;
        int previewY = controller.PreviewY;
        yield return Press(Key.F10);
        Assert.That(LocalizationManager.Instance.CurrentLanguage, Is.EqualTo(GameLanguage.English));
        Assert.That(controller.IsDragging, Is.True);
        Assert.That(controller.PreviewX, Is.EqualTo(previewX));
        Assert.That(controller.PreviewY, Is.EqualTo(previewY));
        Assert.That(controller.PreviewRotated, Is.True);
        Assert.That(controller.view.detailsText.text, Does.Contain("Battery"));
        Assert.That(Snapshot(), Is.EqualTo(original), "Preview and language changes must not mutate real inventory.");
        yield return SetPointer(CellPoint(3, 2), true);
        Assert.That(controller.PreviewResult, Is.EqualTo(BackpackResult.OutOfBounds));
        yield return SetPointer(CellPoint(3, 2), false);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(Snapshot(), Is.EqualTo(original));

        // 第二次从电池的下半格抓取，目标仍按左上角记录，不能跳到鼠标所在格。
        Vector2 lowerCell = CellPoint(2, 3);
        yield return SetPointer(lowerCell, false);
        yield return SetPointer(lowerCell, true);
        yield return SetPointer(lowerCell + Vector2.right * 20f, true);
        Assert.That(controller.PreviewX, Is.EqualTo(2));
        Assert.That(controller.PreviewY, Is.EqualTo(2));
        yield return SetPointer(CellPoint(3, 3), true);
        yield return SetPointer(CellPoint(3, 3), false);
        Assert.That(manager.TryGetPlacement(battery.InstanceId, out BackpackPlacement moved), Is.True);
        Assert.That(moved.X, Is.EqualTo(3));
        Assert.That(moved.Y, Is.EqualTo(2));
        Assert.That(moved.Rotated, Is.False);
        yield return Press(Key.Tab);
        Assert.That(controller.IsOpen, Is.False, "The controller remains active outside the hidden panel.");
        yield return Press(Key.Tab);
        Assert.That(controller.IsOpen, Is.True);
        Assert.That(LocalizationManager.Instance.CurrentLanguage, Is.EqualTo(GameLanguage.English));
    }

    [UnityTest]
    public IEnumerator StandaloneBackpackHidesDisconnectedSourceAndConfirmsDiscardWithoutLootRuntime()
    {
        yield return LoadScene();
        yield return Press(Key.Tab);
        Assert.That(controller.view.transform.Find("LootContainerPreview").gameObject.activeInHierarchy, Is.False);
        ItemTransferUIBridge bridge = Object.FindFirstObjectByType<ItemTransferUIBridge>();
        Assert.That(bridge, Is.Not.Null);
        Assert.That(bridge.coordinator.Initialize(), Is.True);
        Assert.That(bridge.coordinator.lootRuntime, Is.Null);
        ItemInstance item = FindItem("battery");
        int count = driver.itemManager.InstanceCount;
        yield return SetPointer(CellPoint(2, 2), false);
        yield return SetPointer(CellPoint(2, 2), true);
        yield return SetPointer(CellPoint(2, 2), false);
        Assert.That(controller.SelectedInstanceId, Is.EqualTo(item.InstanceId));
        Canvas.ForceUpdateCanvases();
        Vector2 request = RectTransformUtility.WorldToScreenPoint(null,
            bridge.dropButton.transform.TransformPoint(((RectTransform)bridge.dropButton.transform).rect.center));
        yield return SetPointer(request, false);
        yield return SetPointer(request, true);
        yield return SetPointer(request, false);
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.True);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
        Vector2 confirm = RectTransformUtility.WorldToScreenPoint(null,
            bridge.confirmDiscardButton.transform.TransformPoint(((RectTransform)bridge.confirmDiscardButton.transform).rect.center));
        yield return SetPointer(confirm, false);
        yield return SetPointer(confirm, true);
        yield return SetPointer(confirm, false);
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.False);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Lost));
        Assert.That(manager.TryGetPlacement(item.InstanceId, out _), Is.False);
        Assert.That(driver.itemManager.InstanceCount, Is.EqualTo(count));
    }

    [UnityTest]
    public IEnumerator DamageCancelsBeforeLateReleaseAndDeathLeavesItemsForFlowCoordinator()
    {
        yield return LoadScene();
        yield return Press(Key.Tab);
        ItemInstance battery = FindItem("battery");
        string original = Snapshot();
        int token = controller.BeginItemDrag(battery.InstanceId, CellPoint(2, 2), null);
        controller.RotatePreview();
        Assert.That(token, Is.Not.Zero);
        yield return Press(Key.H);
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(driver.player.IsBackpackOpen, Is.False);
        Assert.That(driver.player.CanAct, Is.True);
        controller.EndDrag(token, CellPoint(2, 3), null);
        Assert.That(Snapshot(), Is.EqualTo(original));
        yield return Press(Key.Tab);
        int newToken = controller.BeginItemDrag(battery.InstanceId, CellPoint(2, 2), null);
        Assert.That(newToken, Is.Not.EqualTo(token));
        controller.EndDrag(token, CellPoint(3, 2), null);
        Assert.That(controller.IsDragging, Is.True, "A late event must not commit or cancel a newer preview.");
        Assert.That(Snapshot(), Is.EqualTo(original));
        yield return new WaitForSeconds(driver.player.config.damageInvulnerabilitySeconds + 0.05f);
        yield return Press(Key.K);
        Assert.That(driver.player.IsDead, Is.True);
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(driver.player.CanAct, Is.False);
        controller.EndDrag(newToken, CellPoint(3, 2), null);
        controller.CancelDrag();
        controller.Close();
        Assert.That(Snapshot(), Is.EqualTo(original), "04 must retain all carried identity and placement records after death.");
        Assert.That(controller.TryOpen(), Is.False);
    }

    [UnityTest]
    public IEnumerator RightClickUsesExactlyOneRealItemAndFullHealthDoesNotFreeCells()
    {
        yield return LoadScene();
        ItemInstance foodA = ItemAt(2, 0);
        ItemInstance foodB = ItemAt(0, 2);
        ItemInstance medkit = FindItem("medkit");
        yield return Press(Key.Tab);
        string fullHealth = Snapshot();
        yield return RightClick(CellPoint(2, 0));
        Assert.That(controller.FeedbackKey, Is.EqualTo("items.use.full_health"));
        yield return RightClick(CellPoint(0, 0));
        Assert.That(controller.FeedbackKey, Is.EqualTo("items.use.full_health"));
        Assert.That(Snapshot(), Is.EqualTo(fullHealth));
        yield return Press(Key.H);
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(driver.player.health.MaxHealth - 1),
            "Queued H must apply one real point of damage before food use.");
        Assert.That(controller.IsOpen, Is.False, "Accepted nonlethal damage must close the backpack.");
        yield return Press(Key.Tab);
        Assert.That(controller.IsOpen, Is.True, "Tab must reopen the backpack before right-click use.");
        yield return RightClick(CellPoint(2, 0));
        Assert.That(foodA.Owner, Is.EqualTo(ItemOwner.Consumed),
            "Feedback=" + controller.FeedbackKey + "; HP=" + driver.player.health.CurrentHealth
            + "; selected=" + controller.SelectedInstanceId + "; expected=" + foodA.InstanceId);
        Assert.That(foodA.UsesThisRound, Is.EqualTo(1));
        Assert.That(manager.TryGetPlacement(foodA.InstanceId, out _), Is.False);
        Assert.That(foodB.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(foodB.UsesThisRound, Is.Zero);
        Assert.That(manager.GetPlacements().Count, Is.EqualTo(3));
        yield return new WaitForSeconds(driver.player.config.damageInvulnerabilitySeconds + 0.05f);
        yield return Press(Key.H);
        yield return Press(Key.Tab);
        yield return RightClick(CellPoint(0, 0));
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        Assert.That(medkit.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(manager.TryGetPlacement(medkit.InstanceId, out _), Is.True);
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(driver.player.health.MaxHealth));
        yield return new WaitForSeconds(driver.player.config.damageInvulnerabilitySeconds + 0.05f);
        yield return Press(Key.H);
        yield return Press(Key.Tab);
        yield return RightClick(CellPoint(0, 0));
        Assert.That(controller.FeedbackKey, Is.EqualTo("items.use.round_limit_reached"));
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        int token = controller.BeginItemDrag(foodB.InstanceId, CellPoint(0, 2), null);
        yield return RightClick(CellPoint(0, 2));
        Assert.That(token, Is.Not.Zero);
        Assert.That(foodB.Owner, Is.EqualTo(ItemOwner.Backpack), "Right click cannot consume the item being previewed.");
        Assert.That(foodB.UsesThisRound, Is.Zero);
    }

    [UnityTest]
    public IEnumerator ReloadKeepsPlacementsConsumedFoodMedkitUseAndLanguageWhileCancellingPreview()
    {
        yield return LoadScene();
        ItemInstance food = ItemAt(2, 0);
        ItemInstance medkit = FindItem("medkit");
        ItemInstance battery = FindItem("battery");
        driver.DamageOne();
        Assert.That(controller.TryOpen(), Is.True);
        controller.TryUseHoveredItem(food.InstanceId);
        yield return new WaitForSeconds(driver.player.config.damageInvulnerabilitySeconds + 0.05f);
        driver.DamageOne();
        Assert.That(controller.TryOpen(), Is.True);
        controller.TryUseHoveredItem(medkit.InstanceId);
        Assert.That(manager.TryMove(battery.InstanceId, 2, 3, true), Is.EqualTo(BackpackResult.Success));
        yield return Press(Key.F10);
        Assert.That(controller.BeginItemDrag(battery.InstanceId, CellPoint(2, 3), null), Is.Not.Zero);
        string beforeReload = Snapshot();
        BackpackManager originalManager = manager;
        ItemManager originalItems = manager.Items;
        LocalizationManager originalLanguage = LocalizationManager.Instance;
        yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByPath(ScenePath));
        yield return LoadScene();
        Assert.That(manager, Is.SameAs(originalManager));
        Assert.That(manager.Items, Is.SameAs(originalItems));
        Assert.That(LocalizationManager.Instance, Is.SameAs(originalLanguage));
        Assert.That(LocalizationManager.Instance.CurrentLanguage, Is.EqualTo(GameLanguage.English));
        Assert.That(Snapshot(), Is.EqualTo(beforeReload));
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Consumed));
        Assert.That(medkit.UsesThisRound, Is.EqualTo(1));
        Assert.That(manager.Items.InstanceCount, Is.EqualTo(5));
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(driver.player.IsPrepared, Is.True);
        Assert.That(driver.player.health.CurrentRound, Is.EqualTo(originalItems.CurrentRound));
        Assert.That(Object.FindObjectsByType<BackpackManager>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator ExternalPreviewAndShelterContextNeverTransferOwnershipOrLeaveOldPlayerLocked()
    {
        yield return LoadScene();
        string original = Snapshot();
        yield return Press(Key.V);
        Assert.That(controller.IsExternalPreview, Is.True);
        Assert.That(driver.ExternalItem.Owner, Is.EqualTo(ItemOwner.World));
        yield return Press(Key.F10);
        yield return Press(Key.Escape);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(Snapshot(), Is.EqualTo(original));
        PlayerController oldPlayer = driver.player;
        GameObject fresh = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab"));
        ownedObjects.Add(fresh);
        PlayerController unprepared = fresh.GetComponent<PlayerController>();
        controller.BindPlayer(unprepared);
        Assert.That(oldPlayer.IsBackpackOpen, Is.False);
        Assert.That(unprepared.IsPrepared, Is.False);
        controller.SetShelterContext(false);
        Assert.That(controller.TryOpen(), Is.False);
        controller.SetShelterContext(true);
        Assert.That(controller.TryOpen(), Is.True);
        Assert.That(unprepared.IsBackpackOpen, Is.True);
        controller.enabled = false;
        Assert.That(unprepared.IsBackpackOpen, Is.False);
        Assert.That(Snapshot(), Is.EqualTo(original));
    }

    [UnityTest]
    public IEnumerator DestroyingBoundExplorationPlayerClosesAndCancelsWithoutChangingItems()
    {
        yield return LoadScene();
        yield return Press(Key.Tab);
        ItemInstance battery = FindItem("battery");
        Assert.That(controller.BeginItemDrag(battery.InstanceId, CellPoint(2, 2), null), Is.Not.Zero);
        string original = Snapshot();
        Object.Destroy(driver.player.gameObject);
        yield return null;
        yield return null;
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(Snapshot(), Is.EqualTo(original));
    }

    [UnityTest]
    public IEnumerator QueuedCloseButtonAndSimulatedFocusLossCancelPreviewAndIgnoreLateRelease()
    {
        yield return LoadScene();
        yield return Press(Key.Tab);
        ItemInstance battery = FindItem("battery");
        int token = controller.BeginItemDrag(battery.InstanceId, CellPoint(2, 2), null);
        string original = Snapshot();
        RectTransform close = controller.view.closeButton.GetComponent<RectTransform>();
        Vector2 closePoint = RectTransformUtility.WorldToScreenPoint(null, close.TransformPoint(close.rect.center));
        yield return SetPointer(closePoint, false);
        yield return SetPointer(closePoint, true);
        yield return SetPointer(closePoint, false);
        Assert.That(controller.IsOpen, Is.False, "A real EventSystem left click must execute the close button.");
        Assert.That(controller.IsDragging, Is.False);
        controller.EndDrag(token, CellPoint(3, 2), null);
        controller.Close();
        controller.Close();
        controller.CancelDrag();
        controller.CancelDrag();
        Assert.That(Snapshot(), Is.EqualTo(original));
        yield return Press(Key.Tab);
        int nextToken = controller.BeginItemDrag(battery.InstanceId, CellPoint(2, 2), null);
        Assert.That(nextToken, Is.Not.Zero);
        // 这里只模拟Unity生命周期通知；真实操作系统焦点切换需另外人工复验。
        controller.SendMessage("OnApplicationFocus", false, SendMessageOptions.RequireReceiver);
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(driver.player.IsBackpackOpen, Is.False);
        Assert.That(driver.player.CanAct, Is.True);
        controller.EndDrag(nextToken, CellPoint(3, 2), null);
        Assert.That(Snapshot(), Is.EqualTo(original));
    }

    [UnityTest]
    public IEnumerator OpenBackpackPreservesCrouchAndAllowsWorldTimeAndGravity()
    {
        yield return LoadScene();
        yield return new WaitForSeconds(0.2f);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.C));
        yield return null;
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        Assert.That(driver.player.IsCrouching, Is.True);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.C, Key.Tab));
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        Assert.That(controller.IsOpen, Is.True);
        Assert.That(driver.player.IsCrouching, Is.True);
        Vector3 position = driver.player.transform.position;
        Vector3 moving = driver.movingObject.position;
        float seconds = driver.ElapsedSeconds;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D, Key.Space));
        yield return new WaitForSeconds(0.15f);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        Assert.That(driver.player.IsCrouching, Is.True);
        Assert.That(driver.player.transform.position.x, Is.EqualTo(position.x).Within(0.03f));
        Assert.That(driver.player.transform.position.y, Is.EqualTo(position.y).Within(0.03f));
        Assert.That(driver.ElapsedSeconds, Is.GreaterThan(seconds));
        Assert.That(driver.movingObject.position, Is.Not.EqualTo(moving));
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(driver.player.Body.isKinematic, Is.False);
        yield return Press(Key.Tab);
        yield return null;
        Assert.That(driver.player.IsCrouching, Is.True, "The default toggle mode keeps crouch after closing the backpack.");
        yield return Press(Key.C);
        yield return new WaitForFixedUpdate();
        Assert.That(driver.player.IsCrouching, Is.False, "A second C press must stand before the jump/gravity check.");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
        yield return null;
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        Assert.That(controller.TryOpen(), Is.True);
        float airborneY = driver.player.transform.position.y;
        yield return new WaitForSeconds(0.15f);
        Assert.That(driver.player.transform.position.y, Is.Not.EqualTo(airborneY).Within(0.001f));
    }

    [UnityTest]
    public IEnumerator QueuedLeftAndRightCtrlClickRequestOnceAfterReleaseWithoutHeldRepeats()
    {
        yield return LoadScene();
        yield return Press(Key.Tab);
        ItemInstance battery = FindItem("battery");
        string before = Snapshot();
        List<string> requests = new List<string>();
        // 这里只验证04输入交接；spy不搬运物品，不把本用例作为06跨容器提交通过。
        controller.QuickTransferRequested = id => { requests.Add(id); return true; };
        Vector2 point = CellPoint(2, 2);
        foreach (Key control in new[] { Key.LeftCtrl, Key.RightCtrl })
        {
            int previousCount = requests.Count;
            yield return SetQuickTransferKeys(control);
            yield return SetPointer(point, false);
            yield return SetPointer(point, true);
            for (int i = 0; i < 5; i++) { yield return null; }
            Assert.That(requests.Count, Is.EqualTo(previousCount), "Holding Ctrl and left mouse must not submit before release.");
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(Snapshot(), Is.EqualTo(before));
            if (control == Key.RightCtrl)
            {
                // 修饰键在PointerDown捕获；先松Ctrl不能把本次点击改成普通选择。
                yield return SetQuickTransferKeys();
            }
            yield return SetPointer(point, false);
            Assert.That(requests.Count, Is.EqualTo(previousCount + 1));
            Assert.That(requests.Last(), Is.EqualTo(battery.InstanceId));
            for (int i = 0; i < 5; i++) { yield return null; }
            Assert.That(requests.Count, Is.EqualTo(previousCount + 1), "A held modifier or idle frames cannot repeat one mouse click.");
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(Snapshot(), Is.EqualTo(before));
            yield return SetQuickTransferKeys();
        }
    }

    [UnityTest]
    public IEnumerator QueuedRejectedCtrlGestureBlocksDragAndRightUseAndKeepsItsSource()
    {
        yield return LoadScene();
        driver.DamageOne();
        yield return Press(Key.Tab);
        ItemInstance food = ItemAt(2, 0);
        int health = driver.player.health.CurrentHealth;
        Assert.That(health, Is.LessThan(driver.player.health.MaxHealth));
        int requests = 0;
        controller.QuickTransferRequested = id =>
        {
            Assert.That(id, Is.EqualTo(food.InstanceId));
            requests++;
            return false;
        };
        string before = Snapshot();
        Vector2 point = CellPoint(2, 0);
        yield return SetQuickTransferKeys(Key.LeftCtrl);
        yield return SetPointer(point, false);
        yield return SetPointer(point, true);
        // 超过UGUI拖动阈值，但仍在同一件食物里，快捷按压不得退回普通拖拽。
        Vector2 movedPoint = point + Vector2.right * 20f;
        yield return SetPointer(movedPoint, true);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(controller.BeginItemDrag(food.InstanceId, point, null), Is.Zero);
        Assert.That(requests, Is.Zero);
        // 保留左键按下，再实际按/松右键；现有RightClick helper会先松左键，不能用于这里。
        InputSystem.QueueStateEvent(mouse, new MouseState { position = movedPoint, buttons = 3 });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = movedPoint, buttons = 1 });
        yield return null;
        yield return null;
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(food.UsesThisRound, Is.Zero);
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health));
        Assert.That(requests, Is.Zero);
        Assert.That(Snapshot(), Is.EqualTo(before));
        yield return SetPointer(movedPoint, false);
        Assert.That(requests, Is.Zero, "Crossing the drag threshold cancels the quick press, even if UGUI emits Click on the same item.");
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(Snapshot(), Is.EqualTo(before));

        // 重新完整单击，失败回调也消费一次手势，不能退回拖拽或使用。
        yield return SetPointer(movedPoint, true);
        yield return SetPointer(movedPoint, false);
        Assert.That(requests, Is.EqualTo(1));
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health));
        Assert.That(Snapshot(), Is.EqualTo(before));
        for (int i = 0; i < 4; i++) { yield return null; }
        Assert.That(requests, Is.EqualTo(1));
        yield return SetQuickTransferKeys();

        // 下一次普通拖拽仍能正常开始，失败快捷操作不能遗留输入锁。
        Vector2 batteryPoint = CellPoint(2, 2);
        yield return SetPointer(batteryPoint, false);
        yield return SetPointer(batteryPoint, true);
        yield return SetPointer(batteryPoint + Vector2.right * 20f, true);
        Assert.That(controller.IsDragging, Is.True);
        yield return SetPointer(CellPoint(3, 2), true);
        yield return SetPointer(CellPoint(3, 2), false);
        Assert.That(manager.TryGetPlacement(FindItem("battery").InstanceId, out BackpackPlacement moved), Is.True);
        Assert.That(moved.X, Is.EqualTo(3));
        Assert.That(moved.Y, Is.EqualTo(2));
        Assert.That(requests, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator QueuedSameFrameCtrlLeftAndRightPressCannotUseAndRightUseRecoversAfterRelease()
    {
        yield return LoadScene();
        driver.DamageOne();
        yield return Press(Key.Tab);
        ItemInstance food = ItemAt(2, 0);
        int health = driver.player.health.CurrentHealth;
        Assert.That(health, Is.LessThan(driver.player.health.MaxHealth));
        string before = Snapshot();
        int requests = 0;
        controller.QuickTransferRequested = id =>
        {
            Assert.That(id, Is.EqualTo(food.InstanceId));
            requests++;
            return false;
        };
        Vector2 point = CellPoint(2, 0);
        yield return SetQuickTransferKeys(Key.RightCtrl);
        yield return SetPointer(point, false);
        // 同一MouseState里同时按左右键，覆盖UseItem早于UGUI PointerDown的顺序。
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 3 });
        yield return null;
        yield return null;
        Assert.That(requests, Is.Zero);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health));
        Assert.That(Snapshot(), Is.EqualTo(before));
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = 1 });
        yield return null;
        yield return null;
        Assert.That(requests, Is.Zero);
        yield return SetPointer(point, false);
        // InputSystem共享点击资格；先松右键会取消这次混合手势的左键Click。
        Assert.That(requests, Is.Zero);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(controller.view.DragImage == null || !controller.view.DragImage.IsVisible, Is.True);
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health));
        Assert.That(Snapshot(), Is.EqualTo(before));
        // 保持Ctrl，另一次完整左键单击仍必须且只能交接一次。
        yield return SetPointer(point, true);
        yield return SetPointer(point, false);
        Assert.That(requests, Is.EqualTo(1));
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(controller.view.DragImage == null || !controller.view.DragImage.IsVisible, Is.True);
        Assert.That(Snapshot(), Is.EqualTo(before));
        yield return SetQuickTransferKeys();
        yield return RightClick(point);
        Assert.That(requests, Is.EqualTo(1), "A new ordinary right click must not repeat the earlier quick request.");
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Consumed));
        Assert.That(food.UsesThisRound, Is.EqualTo(1));
        Assert.That(manager.TryGetPlacement(food.InstanceId, out _), Is.False);
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(driver.player.health.MaxHealth));
        Assert.That(controller.IsDragging, Is.False);
    }

    [UnityTest]
    public IEnumerator QueuedOrdinaryPointerDownAndDragNeverBecomeQuickAfterCtrlIsAdded()
    {
        yield return LoadScene();
        yield return Press(Key.Tab);
        ItemInstance battery = FindItem("battery");
        int requests = 0;
        controller.QuickTransferRequested = _ => { requests++; return true; };
        string before = Snapshot();
        Vector2 point = CellPoint(2, 2);
        yield return SetQuickTransferKeys();
        yield return SetPointer(point, false);
        yield return SetPointer(point, true);
        yield return SetQuickTransferKeys(Key.LeftCtrl);
        yield return SetPointer(point, false);
        Assert.That(requests, Is.Zero, "Ctrl pressed after ordinary PointerDown must remain an ordinary selection.");
        Assert.That(controller.SelectedInstanceId, Is.EqualTo(battery.InstanceId));
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(Snapshot(), Is.EqualTo(before));

        yield return SetQuickTransferKeys();
        yield return SetPointer(point, true);
        yield return SetPointer(point + Vector2.right * 20f, true);
        Assert.That(controller.IsDragging, Is.True);
        int token = controller.DragToken;
        yield return SetQuickTransferKeys(Key.RightCtrl);
        Assert.That(controller.DragToken, Is.EqualTo(token));
        yield return SetPointer(CellPoint(3, 2), true);
        yield return SetPointer(CellPoint(3, 2), false);
        Assert.That(requests, Is.Zero, "Adding Ctrl to an existing normal drag cannot request a quick transfer.");
        Assert.That(manager.TryGetPlacement(battery.InstanceId, out BackpackPlacement placement), Is.True);
        Assert.That(placement.X, Is.EqualTo(3));
        Assert.That(placement.Y, Is.EqualTo(2));
        Assert.That(placement.Rotated, Is.False);
        Assert.That(battery.Owner, Is.EqualTo(ItemOwner.Backpack));
        yield return SetQuickTransferKeys();
    }

    [UnityTest]
    public IEnumerator QueuedQuickPressCancelledByDamageCloseOrDisableRejectsOldReleaseAfterReopen()
    {
        yield return LoadScene();
        int requests = 0;
        controller.QuickTransferRequested = _ => { requests++; return true; };
        ItemInstance battery = FindItem("battery");
        string before = Snapshot();
        foreach (string interruption in new[] { "damage", "Tab close", "controller disable" })
        {
            Assert.That(controller.IsOpen || controller.TryOpen(), Is.True);
            int previousCount = requests;
            Vector2 point = CellPoint(2, 2);
            yield return SetQuickTransferKeys(Key.LeftCtrl);
            yield return SetPointer(point, false);
            yield return SetPointer(point, true);
            Assert.That(requests, Is.EqualTo(previousCount));
            if (interruption == "damage")
            {
                int health = driver.player.health.CurrentHealth;
                yield return SetQuickTransferKeys(Key.LeftCtrl, Key.H);
                Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health - 1));
                yield return SetQuickTransferKeys(Key.LeftCtrl);
            }
            else if (interruption == "Tab close")
            {
                yield return SetQuickTransferKeys(Key.LeftCtrl, Key.Tab);
                yield return SetQuickTransferKeys(Key.LeftCtrl);
            }
            else
            {
                // 控制器停用是生命周期夹具；按下和迟到松手仍使用真实排队UGUI路径。
                controller.enabled = false;
                yield return null;
                controller.enabled = true;
                yield return null;
            }
            Assert.That(controller.IsOpen, Is.False, interruption);
            Assert.That(controller.IsDragging, Is.False, interruption);
            Assert.That(mouse.leftButton.isPressed, Is.True);
            Assert.That(Snapshot(), Is.EqualTo(before));
            Assert.That(controller.TryOpen(), Is.True);
            yield return null;
            yield return SetPointer(point, false);
            Assert.That(requests, Is.EqualTo(previousCount), interruption + ": an old release must not use the reopened view.");
            Assert.That(battery.Owner, Is.EqualTo(ItemOwner.Backpack));
            Assert.That(Snapshot(), Is.EqualTo(before));
            yield return SetPointer(point, true);
            yield return SetPointer(point, false);
            Assert.That(requests, Is.EqualTo(previousCount + 1), interruption + ": a fresh quick click must still work.");
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(Snapshot(), Is.EqualTo(before));
            yield return SetQuickTransferKeys();
        }
    }

    [UnityTest]
    public IEnumerator QuickTransferApiRespectsRealDiscardGuardTransitionDeathAndReentry()
    {
        yield return LoadScene();
        ItemInstance battery = FindItem("battery");
        string before = Snapshot();
        int requests = 0;
        controller.QuickTransferRequested = _ => { requests++; return true; };
        Assert.That(controller.TryQuickTransfer(battery.InstanceId), Is.False, "A closed panel cannot submit an API request.");
        Assert.That(controller.TryOpen(), Is.True);
        Assert.That(controller.TryQuickTransfer("missing-instance"), Is.False);
        Assert.That(controller.TryQuickTransfer(driver.ExternalItem.InstanceId), Is.False, "A world item is not a carried backpack item.");
        int drag = controller.BeginItemDrag(battery.InstanceId, CellPoint(2, 2), null);
        Assert.That(drag, Is.Not.Zero);
        Assert.That(controller.TryQuickTransfer(battery.InstanceId), Is.False);
        controller.CancelDrag();
        ItemTransferUIBridge bridge = Object.FindFirstObjectByType<ItemTransferUIBridge>();
        Assert.That(bridge, Is.Not.Null);
        controller.SelectItem(battery.InstanceId);
        bridge.DropSelected();
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.True);
        Assert.That(controller.TryQuickTransfer(battery.InstanceId), Is.False, "Reuse the actual 06 discard gate, rather than a fake confirmation flag.");
        bridge.CancelDiscard(bridge.DiscardToken);
        Assert.That(requests, Is.Zero);

        // 闸门同步关闭并重开会改变会话，返回true也不能把旧请求交给06。
        System.Func<bool> originalGuard = controller.CanBeginDrag;
        bool reopenedInsideGuard = false;
        controller.CanBeginDrag = () =>
        {
            controller.Close();
            reopenedInsideGuard = controller.TryOpen();
            return true;
        };
        Assert.That(controller.TryQuickTransfer(battery.InstanceId), Is.False);
        Assert.That(reopenedInsideGuard, Is.True);
        Assert.That(controller.IsOpen, Is.True);
        Assert.That(requests, Is.Zero, "A session changed by CanBeginDrag cannot reach QuickTransferRequested.");
        Assert.That(Snapshot(), Is.EqualTo(before));
        controller.CanBeginDrag = originalGuard;
        controller.QuickTransferRequested = null;
        Assert.That(controller.TryQuickTransfer(battery.InstanceId), Is.False, "04 must safely reject an unbound transfer callback.");

        bool nestedAccepted = true;
        controller.QuickTransferRequested = id =>
        {
            requests++;
            nestedAccepted = controller.TryQuickTransfer(id);
            return true;
        };
        Assert.That(controller.TryQuickTransfer(battery.InstanceId), Is.True);
        Assert.That(nestedAccepted, Is.False);
        Assert.That(requests, Is.EqualTo(1), "Callback reentry cannot invoke the transfer callback twice.");
        controller.enabled = false;
        Assert.That(controller.TryQuickTransfer(battery.InstanceId), Is.False);
        controller.enabled = true;
        Assert.That(controller.TryOpen(), Is.True);
        driver.player.SetSceneTransitionBlocked(true);
        Assert.That(controller.TryQuickTransfer(battery.InstanceId), Is.False);
        yield return null;
        Assert.That(controller.IsOpen, Is.False);
        driver.player.SetSceneTransitionBlocked(false);
        Assert.That(controller.TryOpen(), Is.True);
        bool acceptedWhileClosing = true;
        System.Action checkClosing = () => acceptedWhileClosing = controller.TryQuickTransfer(battery.InstanceId);
        controller.Closing += checkClosing;
        controller.Close();
        controller.Closing -= checkClosing;
        Assert.That(acceptedWhileClosing, Is.False);
        Assert.That(controller.TryOpen(), Is.True);
        Assert.That(driver.player.health.TakeDamage(driver.player.health.MaxHealth), Is.True);
        Assert.That(driver.player.IsDead, Is.True);
        Assert.That(controller.TryQuickTransfer(battery.InstanceId), Is.False);
        Assert.That(requests, Is.EqualTo(1));
        Assert.That(Snapshot(), Is.EqualTo(before), "These API guards and spy requests must never alter 01 identity or 04 placement.");
    }

    [UnityTest]
    public IEnumerator ExternalPreviewOverloadPreservesLegacyDefaultsAndRotatedGrabBeforeFirstUpdate()
    {
        yield return LoadScene();
        string legacySnapshot = Snapshot();
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(controller.BeginExternalPreview(driver.ExternalItem.InstanceId), Is.True);
        Assert.That(controller.IsOpen, Is.True, "The single argument entry still opens the backpack.");
        Assert.That(controller.PreviewRotated, Is.False);
        Assert.That(controller.DragGrabOffset, Is.EqualTo(Vector2Int.zero));
        controller.CancelDrag();
        Assert.That(Snapshot(), Is.EqualTo(legacySnapshot));

        // API来源夹具：只把真实电池移出04占格，不冒充06箱子条目或鼠标拾回验收。
        ItemInstance battery = MoveBatteryToExternalPreviewSource();
        string before = Snapshot();
        yield return SetPointer(CellPoint(3, 2), false);
        System.Action<int, Vector2, Camera> originalUpdated = controller.DragUpdated;
        int firstToken = 0;
        bool firstRotated = false;
        Vector2Int firstOffset = Vector2Int.zero;
        Vector2Int firstCell = Vector2Int.zero;
        controller.DragUpdated = (token, position, camera) =>
        {
            if (firstToken == 0)
            {
                firstToken = token;
                firstRotated = controller.PreviewRotated;
                firstOffset = controller.DragGrabOffset;
                firstCell = new Vector2Int(controller.PreviewX, controller.PreviewY);
            }
            originalUpdated?.Invoke(token, position, camera);
        };
        try
        {
            Assert.That(controller.BeginExternalPreview(battery.InstanceId, true, new Vector2Int(1, 0)), Is.True);
            Assert.That(firstToken, Is.EqualTo(controller.DragToken).And.Not.Zero);
            Assert.That(firstRotated, Is.True, "The first DragUpdated must already see the source orientation.");
            Assert.That(firstOffset, Is.EqualTo(new Vector2Int(1, 0)));
            Assert.That(firstCell, Is.EqualTo(new Vector2Int(2, 2)), "Mouse cell (3,2) minus the grabbed cell (1,0).");
            Assert.That(controller.PreviewResult, Is.EqualTo(BackpackResult.Success));
            Assert.That(controller.view.preview.rectTransform.sizeDelta,
                Is.EqualTo(new Vector2(controller.view.cellSize * 2 + controller.view.gap, controller.view.cellSize)));
            Assert.That(manager.Items.TryGetInstance(battery.InstanceId, out ItemInstance same), Is.True);
            Assert.That(same, Is.SameAs(battery));
            Assert.That(Snapshot(), Is.EqualTo(before));

            // R仍走04已有InputAction；2×1转回1×2时抓取x被夹回0。
            yield return Press(Key.R);
            Assert.That(controller.PreviewRotated, Is.False);
            Assert.That(controller.DragGrabOffset, Is.EqualTo(Vector2Int.zero));
            Assert.That(new Vector2Int(controller.PreviewX, controller.PreviewY), Is.EqualTo(new Vector2Int(3, 2)));
            Assert.That(controller.PreviewResult, Is.EqualTo(BackpackResult.Success));
            controller.CancelDrag();
            Assert.That(controller.BeginExternalPreview(battery.InstanceId, true,
                new Vector2Int(int.MaxValue, int.MinValue)), Is.True);
            Assert.That(controller.DragGrabOffset, Is.EqualTo(new Vector2Int(1, 0)));
            controller.CancelDrag();
            Assert.That(controller.BeginExternalPreview(battery.InstanceId, false,
                new Vector2Int(int.MinValue, int.MaxValue)), Is.True);
            Assert.That(controller.DragGrabOffset, Is.EqualTo(new Vector2Int(0, 1)));
            controller.CancelDrag();
            Assert.That(Snapshot(), Is.EqualTo(before));
        }
        finally
        {
            controller.DragUpdated = originalUpdated;
            controller.CancelDrag();
        }
    }

    [UnityTest]
    public IEnumerator ExternalPreviewOverloadRejectsInvalidDefinitionRotationAndGateWithoutPartialDrag()
    {
        yield return LoadScene();
        Assert.That(controller.TryOpen(), Is.True);
        ItemDataSO definition = Object.Instantiate(FindItem("battery").Definition);
        ownedDefinitions.Add(definition);
        definition.definitionId = "external-preview-validation";
        definition.isUnique = false;
        definition.uniqueId = string.Empty;
        definition.canRotate = false;
        Assert.That(manager.Items.TryCreateInstance(definition, ItemOwner.Container, "preview-source",
            "preview-source", "preview-origin", out ItemInstance source), Is.True);
        string before = Snapshot();
        string selected = controller.SelectedInstanceId;
        int updates = 0;
        int changes = 0;
        System.Action<int, Vector2, Camera> originalUpdated = controller.DragUpdated;
        System.Func<bool> originalGuard = controller.CanBeginDrag;
        System.Action changed = () => changes++;
        controller.DragUpdated = (_, __, ___) => updates++;
        manager.Changed += changed;
        try
        {
            Assert.That(controller.BeginExternalPreview(source.InstanceId, true, Vector2Int.one), Is.False);
            AssertNoPartialPreview();
            foreach (Vector2Int invalid in new[] { new Vector2Int(0, 2), new Vector2Int(-1, 2),
                new Vector2Int(1, 0), new Vector2Int(1, -1) })
            {
                definition.width = invalid.x;
                definition.height = invalid.y;
                Assert.That(controller.BeginExternalPreview(source.InstanceId, false, Vector2Int.zero), Is.False);
                AssertNoPartialPreview();
                Assert.That(definition.width, Is.EqualTo(invalid.x));
                Assert.That(definition.height, Is.EqualTo(invalid.y));
            }
            definition.width = 1;
            definition.height = 2;
            controller.CanBeginDrag = () => false;
            Assert.That(controller.BeginExternalPreview(source.InstanceId, false, Vector2Int.zero), Is.False);
            AssertNoPartialPreview();
            controller.CanBeginDrag = () => { controller.Close(); return true; };
            Assert.That(controller.BeginExternalPreview(source.InstanceId, false, Vector2Int.zero), Is.False);
            AssertNoPartialPreview();
            controller.CanBeginDrag = originalGuard;
            Assert.That(controller.BeginExternalPreview(source.InstanceId, false, Vector2Int.zero), Is.True,
                "A valid unrotated fixed item remains supported after rejected requests.");
            controller.CancelDrag();
            Assert.That(updates, Is.EqualTo(1));
            Assert.That(Snapshot(), Is.EqualTo(before));

            // 销毁的运行时克隆模拟失效定义；不写磁盘资产、不反射改01实例。
            Object.Destroy(definition);
            yield return null;
            before = Snapshot();
            selected = controller.SelectedInstanceId;
            updates = 0;
            Assert.That(source.Definition == null, Is.True);
            Assert.That(controller.BeginExternalPreview(source.InstanceId, false, Vector2Int.zero), Is.False);
            AssertNoPartialPreview();
        }
        finally
        {
            controller.DragUpdated = originalUpdated;
            controller.CanBeginDrag = originalGuard;
            manager.Changed -= changed;
            controller.CancelDrag();
        }

        void AssertNoPartialPreview()
        {
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(controller.IsExternalPreview, Is.False);
            Assert.That(controller.DragToken, Is.Zero);
            Assert.That(controller.DragInstanceId, Is.Null);
            Assert.That(controller.SelectedInstanceId, Is.EqualTo(selected));
            Assert.That(controller.view.preview.gameObject.activeSelf, Is.False);
            Assert.That(updates, Is.Zero);
            Assert.That(changes, Is.Zero);
            Assert.That(Snapshot(), Is.EqualTo(before));
            Assert.That(manager.Items.TryGetInstance(source.InstanceId, out ItemInstance same), Is.True);
            Assert.That(same, Is.SameAs(source));
            Assert.That(manager.TryGetPlacement(source.InstanceId, out _), Is.False);
        }
    }

    [UnityTest]
    public IEnumerator ExternalPreviewOverloadDamageAndFirstUpdateCancellationCannotCommitOrCancelNewToken()
    {
        yield return LoadScene();
        Assert.That(controller.TryOpen(), Is.True);
        ItemInstance battery = MoveBatteryToExternalPreviewSource();
        yield return SetPointer(CellPoint(3, 2), false);
        string before = Snapshot();
        Assert.That(controller.BeginExternalPreview(battery.InstanceId, true, new Vector2Int(1, 0)), Is.True);
        Assert.That(controller.PreviewResult, Is.EqualTo(BackpackResult.Success));
        int oldToken = controller.DragToken;
        Assert.That(controller.BeginExternalPreview(driver.ExternalItem.InstanceId, false, Vector2Int.zero), Is.False);
        Assert.That(controller.IsDragValid(oldToken), Is.True, "A rejected second request cannot overwrite the active preview.");
        Assert.That(controller.DragGrabOffset, Is.EqualTo(new Vector2Int(1, 0)));
        int health = driver.player.health.CurrentHealth;
        driver.DamageOne();
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health - 1));
        Assert.That(controller.IsOpen, Is.False);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(controller.BeginExternalPreview(battery.InstanceId, false, new Vector2Int(0, 1)), Is.True);
        int nextToken = controller.DragToken;
        Assert.That(nextToken, Is.Not.EqualTo(oldToken));
        controller.EndDrag(oldToken, CellPoint(3, 2), null);
        controller.CancelDrag(oldToken);
        Assert.That(controller.IsDragValid(nextToken), Is.True);
        controller.CancelDrag(nextToken);
        controller.CancelDrag(nextToken);
        controller.EndDrag(nextToken, CellPoint(3, 2), null);
        Assert.That(Snapshot(), Is.EqualTo(before));

        System.Action<int, Vector2, Camera> originalUpdated = controller.DragUpdated;
        try
        {
            controller.DragUpdated = (_, __, ___) => controller.Close();
            Assert.That(controller.BeginExternalPreview(battery.InstanceId, true, Vector2Int.zero), Is.False,
                "A synchronous close in the first update cannot report a started preview.");
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(controller.DragInstanceId, Is.Null);
            Assert.That(Snapshot(), Is.EqualTo(before));

            string sourceContainer = battery.ContainerId;
            controller.DragUpdated = (_, __, ___) =>
                Assert.That(manager.Items.TrySetOwner(battery.InstanceId, ItemOwner.Container, "changed-preview-source"), Is.True);
            Assert.That(controller.BeginExternalPreview(battery.InstanceId, true, Vector2Int.zero), Is.False);
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(battery.ContainerId, Is.EqualTo("changed-preview-source"),
                "04 cancels only its preview; it must not undo another caller's authoritative source change.");
            Assert.That(manager.Items.TrySetOwner(battery.InstanceId, ItemOwner.Container, sourceContainer), Is.True);
            Assert.That(Snapshot(), Is.EqualTo(before));

            controller.DragUpdated = (_, __, ___) => { throw new System.InvalidOperationException("preview-startup-test"); };
            Assert.Throws<System.InvalidOperationException>(() => controller.BeginExternalPreview(battery.InstanceId, true, Vector2Int.zero));
            Assert.That(controller.IsDragging, Is.False, "A throwing callback must not leave its half-started drag active.");
            Assert.That(controller.DragInstanceId, Is.Null);
            Assert.That(Snapshot(), Is.EqualTo(before));

            int replacementToken = 0;
            controller.DragUpdated = (token, position, camera) =>
            {
                controller.CancelDrag(token);
                controller.DragUpdated = originalUpdated;
                Assert.That(controller.BeginExternalPreview(battery.InstanceId, false, Vector2Int.zero), Is.True);
                replacementToken = controller.DragToken;
            };
            Assert.That(controller.BeginExternalPreview(battery.InstanceId, true, Vector2Int.one), Is.False);
            Assert.That(replacementToken, Is.Not.Zero);
            Assert.That(controller.IsDragValid(replacementToken), Is.True,
                "Cleaning up the cancelled startup must not cancel its replacement token.");
            Assert.That(controller.PreviewRotated, Is.False);
            Assert.That(controller.DragGrabOffset, Is.EqualTo(Vector2Int.zero));
            Assert.That(Snapshot(), Is.EqualTo(before));
        }
        finally
        {
            controller.DragUpdated = originalUpdated;
            controller.CancelDrag();
        }
        Assert.That(manager.Items.TryGetInstance(battery.InstanceId, out ItemInstance same), Is.True);
        Assert.That(same, Is.SameAs(battery));
        Assert.That(manager.TryGetPlacement(battery.InstanceId, out _), Is.False);
        Assert.That(Snapshot(), Is.EqualTo(before));
    }

    private ItemInstance MoveBatteryToExternalPreviewSource()
    {
        ItemInstance battery = FindItem("battery");
        Assert.That(manager.TryRemovePlacement(battery.InstanceId, out _), Is.EqualTo(BackpackResult.Success));
        Assert.That(manager.Items.TrySetOwner(battery.InstanceId, ItemOwner.Container, "external-preview-source"), Is.True);
        manager.NotifyChanged();
        return battery;
    }

    [UnityTest]
    public IEnumerator QueuedHoverPreservesSelectionAndDoesNotPassThroughDiscardConfirmation()
    {
        yield return LoadScene();
        yield return Press(Key.Tab);
        BackpackView view = controller.view;
        BackpackItemView battery = FindBackpackItemView(FindItem("battery").InstanceId);
        BackpackItemView food = FindBackpackItemView(ItemAt(2, 0).InstanceId);
        Vector2 outside = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        yield return SetPointer(outside, false);
        Color normalBattery = battery.background.color;
        Color normalFood = food.background.color;
        string before = Snapshot();
        string selectedBeforeHover = controller.SelectedInstanceId;
        Vector2 batteryPoint = ScreenRect((RectTransform)battery.transform).center;
        Vector2 foodPoint = ScreenRect((RectTransform)food.transform).center;

        yield return SetPointer(batteryPoint, false);
        Assert.That(battery.background.color, Is.Not.EqualTo(normalBattery));
        Assert.That(controller.SelectedInstanceId, Is.EqualTo(selectedBeforeHover));
        Color hoveredBattery = battery.background.color;
        yield return SetPointer(ScreenRect(battery.nameText.rectTransform).center, false);
        Assert.That(view.ItemAtScreenPoint(mouse.position.ReadValue(), null), Is.EqualTo(battery.InstanceId));
        Assert.That(battery.background.color, Is.EqualTo(hoveredBattery), "Moving onto the item label must not interrupt hover.");
        yield return ClickPointer(batteryPoint);
        Assert.That(controller.SelectedInstanceId, Is.EqualTo(battery.InstanceId));
        yield return SetPointer(outside, false);
        Color selectedBattery = battery.background.color;
        Assert.That(selectedBattery, Is.Not.EqualTo(normalBattery));
        yield return SetPointer(foodPoint, false);
        Assert.That(food.background.color, Is.Not.EqualTo(normalFood));
        Assert.That(battery.background.color, Is.EqualTo(selectedBattery));
        Assert.That(controller.SelectedInstanceId, Is.EqualTo(battery.InstanceId));
        yield return Press(Key.F10);
        Assert.That(food.nameText.text, Is.EqualTo(LocalizationManager.Instance.GetText(ItemAt(2, 0).Definition.nameKey)));
        Assert.That(controller.SelectedInstanceId, Is.EqualTo(battery.InstanceId));
        Assert.That(Snapshot(), Is.EqualTo(before));
        yield return SetPointer(outside, false);
        Assert.That(food.background.color, Is.EqualTo(normalFood));

        ItemTransferUIBridge bridge = Object.FindFirstObjectByType<ItemTransferUIBridge>();
        Assert.That(bridge, Is.Not.Null);
        yield return ClickPointer(ScreenRect((RectTransform)bridge.dropButton.transform).center);
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.True);
        yield return SetPointer(foodPoint, false);
        Assert.That(food.background.color, Is.EqualTo(normalFood), "The confirmation overlay must not let the covered item hover.");
        Assert.That(controller.SelectedInstanceId, Is.EqualTo(battery.InstanceId));
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(Snapshot(), Is.EqualTo(before));
        yield return ClickPointer(ScreenRect((RectTransform)bridge.cancelDiscardButton.transform).center);
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.False);
        yield return SetPointer(foodPoint, false);
        Assert.That(food.background.color, Is.Not.EqualTo(normalFood));
        yield return Press(Key.Tab);
        Assert.That(controller.IsOpen, Is.False);
        yield return SetPointer(outside, false);
        yield return Press(Key.Tab);
        Assert.That(battery.background.color, Is.EqualTo(selectedBattery));
        Assert.That(food.background.color, Is.EqualTo(normalFood));
        Assert.That(Snapshot(), Is.EqualTo(before));

        // 同一个物品内部起拖并原格松手时，UGUI不会补发Enter，仍应恢复悬停。
        Vector2 innerPoint = new Vector2(ScreenRect((RectTransform)battery.transform).xMin + 18f,
            ScreenRect((RectTransform)battery.transform).yMax - 25f);
        yield return SetPointer(innerPoint, false);
        yield return SetPointer(innerPoint, true);
        yield return SetPointer(innerPoint + Vector2.right * 20f, true);
        Assert.That(controller.IsDragging, Is.True);
        Assert.That(battery.IsHovered, Is.False);
        yield return SetPointer(innerPoint + Vector2.right * 20f, false);
        Assert.That(controller.IsDragging, Is.False);
        Assert.That(battery.IsHovered, Is.True, "No exit/re-enter is needed after dropping within the same item.");
        Assert.That(Snapshot(), Is.EqualTo(before));
    }

    [UnityTest]
    public IEnumerator QueuedDragImageKeepsPixelGrabAcrossGapsOutsideRotationAndLanguageThenRejectsDrop()
    {
        yield return LoadScene();
        yield return Press(Key.Tab);
        BackpackView view = controller.view;
        ItemInstance battery = FindItem("battery");
        BackpackItemView source = FindBackpackItemView(battery.InstanceId);
        RectTransform sourceRect = (RectTransform)source.transform;
        Rect originalRect = ScreenRect(sourceRect);
        Assert.That(originalRect.width, Is.EqualTo(66f).Within(0.1f));
        Assert.That(originalRect.height, Is.EqualTo(136f).Within(0.1f));
        Vector2 pixelGrab = new Vector2(18f, 25f);
        Vector2 press = new Vector2(originalRect.xMin + pixelGrab.x, originalRect.yMax - pixelGrab.y);
        string before = Snapshot();
        int canvasCount = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        Canvas rootCanvas = view.GetComponentInParent<Canvas>().rootCanvas;
        CanvasGroup initialGroup = source.GetComponent<CanvasGroup>();
        float originalAlpha = initialGroup != null ? initialGroup.alpha : 1f;
        int changes = 0;
        System.Action onChanged = () => changes++;
        manager.Changed += onChanged;
        try
        {
            yield return SetPointer(press, false);
            yield return SetPointer(press, true);
            for (int i = 0; i < 4; i++) { yield return null; }
            Assert.That(controller.IsDragging, Is.False, "Holding without movement has no timer-based drag start.");
            Assert.That(view.DragImage == null || !view.DragImage.IsVisible, Is.True);
            Vector2 moved = press + Vector2.right * Mathf.Max(20f, EventSystem.current.pixelDragThreshold + 4f);
            yield return SetPointer(moved, true);
            Assert.That(controller.IsDragging, Is.True, "The real UGUI drag threshold must start the gesture.");
            BackpackDragImage image = view.DragImage;
            Assert.That(image, Is.Not.Null);
            int token = controller.DragToken;
            var owner = image.Owner;
            Assert.That(owner, Is.Not.Null);
            Assert.That(image.Token, Is.EqualTo(token));
            Assert.That(image.SourceRect, Is.SameAs(sourceRect));
            Assert.That(image.RectTransform, Is.Not.SameAs(view.preview.rectTransform));
            Assert.That(image.RectTransform.GetComponentInParent<Canvas>(), Is.SameAs(rootCanvas));
            Assert.That(source.isActiveAndEnabled, Is.True, "Keep the original event receiver active until release.");
            CanvasGroup sourceGroup = source.GetComponent<CanvasGroup>();
            Assert.That(sourceGroup, Is.Not.Null);
            Assert.That(sourceGroup.alpha, Is.LessThan(originalAlpha));

            Vector2 gap = RectTransformUtility.WorldToScreenPoint(null, view.gridRoot.TransformPoint(new Vector3(
                view.gridRoot.rect.xMin + view.padding + 2f * (view.cellSize + view.gap) + view.cellSize + view.gap * 0.5f,
                view.gridRoot.rect.yMax - view.padding - 2f * (view.cellSize + view.gap) - 25f, 0f)));
            Vector2 outside = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Assert.That(RectTransformUtility.RectangleContainsScreenPoint(view.gridRoot, outside, null), Is.False);
            foreach (Vector2 point in new[] { moved + Vector2.right, moved + Vector2.right * 3f, CellPoint(3, 2), gap, outside })
            {
                yield return SetPointer(point, true);
                Assert.That(view.DragImage, Is.SameAs(image));
                Assert.That(image.Owner, Is.SameAs(owner));
                Assert.That(image.Token, Is.EqualTo(token));
                AssertDragImageAt(image, point, pixelGrab, new Vector2(66f, 136f));
                Assert.That(rootCanvas.GetComponentsInChildren<BackpackDragImage>(true).Count(candidate => candidate.IsVisible), Is.EqualTo(1));
                Assert.That(source.isActiveAndEnabled, Is.True);
                Assert.That(Snapshot(), Is.EqualTo(before));
                List<RaycastResult> hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
                Assert.That(hits.Any(hit => hit.gameObject.transform == image.RectTransform
                    || hit.gameObject.transform.IsChildOf(image.RectTransform)), Is.False, "Neither the ghost nor its text may intercept the target raycast.");
                if (point == CellPoint(3, 2))
                {
                    Assert.That(view.TryScreenToCell(point, null, out Vector2Int cell), Is.True);
                    Assert.That(cell, Is.EqualTo(new Vector2Int(3, 2)));
                }
                if (point == gap || point == outside)
                {
                    Assert.That(view.TryScreenToCell(point, null, out _), Is.False);
                    Assert.That(view.preview.gameObject.activeInHierarchy, Is.False);
                }
            }
            yield return Press(Key.R);
            Assert.That(controller.PreviewRotated, Is.True);
            Assert.That(controller.DragGrabOffset, Is.EqualTo(Vector2Int.zero));
            AssertDragImageAt(image, outside, pixelGrab, new Vector2(136f, 66f));
            yield return Press(Key.F10);
            Assert.That(LocalizationManager.Instance.CurrentLanguage, Is.EqualTo(GameLanguage.English));
            Assert.That(view.DragImage, Is.SameAs(image));
            Assert.That(image.Token, Is.EqualTo(token));
            AssertDragImageAt(image, outside, pixelGrab, new Vector2(136f, 66f));
            string ghostText = string.Join("\n", image.RectTransform.GetComponentsInChildren<TMPro.TMP_Text>()
                .Select(label => label.text));
            Assert.That(ghostText, Does.Contain(LocalizationManager.Instance.GetText(battery.Definition.nameKey)));
            Assert.That(Snapshot(), Is.EqualTo(before));
            Assert.That(changes, Is.Zero);

            yield return SetPointer(gap, true);
            AssertDragImageAt(image, gap, pixelGrab, new Vector2(136f, 66f));
            yield return SetPointer(gap, false);
            Assert.That(controller.IsDragging, Is.False);
            Assert.That(image.IsVisible, Is.False);
            Assert.That(view.preview.gameObject.activeInHierarchy, Is.False);
            Assert.That(source.isActiveAndEnabled, Is.True);
            Assert.That(sourceGroup.alpha, Is.EqualTo(originalAlpha).Within(0.001f));
            Assert.That(ScreenRect(sourceRect), Is.EqualTo(originalRect));
            Assert.That(Snapshot(), Is.EqualTo(before), "An invalid release keeps the original owner, cells and rotation.");
            Assert.That(changes, Is.Zero);
            Assert.That(Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(canvasCount));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }
        finally { manager.Changed -= onChanged; }
    }

    private BackpackItemView FindBackpackItemView(string instanceId)
    {
        return controller.view.itemRoot.GetComponentsInChildren<BackpackItemView>()
            .Single(item => item.InstanceId == instanceId);
    }

    private IEnumerator ClickPointer(Vector2 point)
    {
        yield return SetPointer(point, false);
        yield return SetPointer(point, true);
        yield return SetPointer(point, false);
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

    private static void AssertDragImageAt(BackpackDragImage image, Vector2 pointer, Vector2 grab, Vector2 size)
    {
        Assert.That(image.IsVisible, Is.True);
        Rect actual = ScreenRect(image.RectTransform);
        Assert.That(actual.xMin, Is.EqualTo(pointer.x - grab.x).Within(0.5f));
        Assert.That(actual.yMax, Is.EqualTo(pointer.y + grab.y).Within(0.5f));
        Assert.That(actual.width, Is.EqualTo(size.x).Within(0.1f));
        Assert.That(actual.height, Is.EqualTo(size.y).Within(0.1f));
        UnityEngine.UI.Graphic[] graphics = image.RectTransform.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
        Assert.That(graphics, Is.Not.Empty);
        Assert.That(graphics.All(graphic => !graphic.raycastTarget), Is.True);
        Assert.That(graphics.Any(graphic => graphic.isActiveAndEnabled && !graphic.canvasRenderer.cull
            && graphic.canvasRenderer.GetInheritedAlpha() > 0f), Is.True, "A visible ghost needs at least one rendered, nontransparent graphic.");
    }


    private IEnumerator SetQuickTransferKeys(params Key[] keys)
    {
        // 与Press不同，调用者明确保留Ctrl；否则Press结束时会提前释放全部修饰键。
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        yield return null;
        yield return null;
    }

    private void AssertPanelsFitGameView()
    {
        Canvas.ForceUpdateCanvases();
        TestContext.WriteLine("Backpack pointer/UI verification at " + Screen.width + " x " + Screen.height);
        RectTransform left = (RectTransform)controller.view.transform.Find("PlayerInventory");
        RectTransform right = (RectTransform)controller.view.transform.Find("LootContainerPreview");
        Assert.That(left, Is.Not.Null);
        Assert.That(right, Is.Not.Null);
        Vector3[] corners = new Vector3[4];
        foreach (RectTransform panel in new[] { left, right })
        {
            panel.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
            {
                Vector2 point = RectTransformUtility.WorldToScreenPoint(null, corner);
                Assert.That(point.x, Is.InRange(-0.5f, Screen.width + 0.5f), panel.name);
                Assert.That(point.y, Is.InRange(-0.5f, Screen.height + 0.5f), panel.name);
            }
        }
        left.GetWorldCorners(corners);
        float leftEdge = RectTransformUtility.WorldToScreenPoint(null, corners[2]).x;
        right.GetWorldCorners(corners);
        float rightEdge = RectTransformUtility.WorldToScreenPoint(null, corners[0]).x;
        Assert.That(leftEdge, Is.LessThan(rightEdge), "左右面板之间应保留世界视野。");
    }

    private IEnumerator LoadScene()
    {
        loadedScene = true;
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
        yield return null;
        yield return null;
        GameObject bootstrap = GameBootstrap.Instance.gameObject;
        if (!ownedObjects.Contains(bootstrap)) { ownedObjects.Add(bootstrap); }
        driver = Object.FindFirstObjectByType<BackpackTestDriver>();
        Assert.That(driver, Is.Not.Null);
        Assert.That(driver.IsReady, Is.True);
        controller = driver.controller;
        manager = driver.backpackManager;
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

    private IEnumerator RightClick(Vector2 position)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = 2 });
        // 按住多帧，既检验动作只按下触发，也检验UGUI右键回调没有第二次业务。
        for (int i = 0; i < 4; i++) { yield return null; }
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
        yield return null;
        yield return null;
    }

    private Vector2 CellPoint(int x, int y)
    {
        Canvas.ForceUpdateCanvases();
        BackpackView view = controller.view;
        Vector3 local = new Vector3(view.padding + x * (view.cellSize + view.gap) + view.cellSize * 0.35f,
            -view.padding - y * (view.cellSize + view.gap) - view.cellSize * 0.35f, 0f);
        return RectTransformUtility.WorldToScreenPoint(null, view.gridRoot.TransformPoint(local));
    }

    private ItemInstance FindItem(string definition)
    {
        return manager.Items.GetInstances().First(item => item.DefinitionId == definition);
    }

    private ItemInstance ItemAt(int x, int y)
    {
        return manager.GetPlacements().First(placement => placement.X == x && placement.Y == y).Item;
    }

    private string Snapshot()
    {
        IEnumerable<string> items = manager.Items.GetInstances().OrderBy(item => item.InstanceId)
            .Select(item => item.InstanceId + ":" + item.DefinitionId + ":" + item.UniqueId
                + ":" + item.Owner + ":" + item.ContainerId + ":" + item.SourceId + ":" + item.OriginalSpawnPointId
                + ":" + item.UseRound + ":" + item.UsesThisRound + ":" + item.IsRetained
                + ":" + item.RecoveryRound + ":" + item.LastLossReason + ":" + item.SpawnClaimed
                + ":" + (item.Definition != null ? item.Definition.ev.ToString() : "missing-definition"));
        IEnumerable<string> placements = manager.GetPlacements().OrderBy(placement => placement.InstanceId)
            .Select(placement => placement.InstanceId + ":" + placement.X + ":" + placement.Y + ":" + placement.Rotated);
        return manager.Items.CurrentRound + "|" + string.Join(";", items) + "|" + string.Join(";", placements);
    }
}
#endif
