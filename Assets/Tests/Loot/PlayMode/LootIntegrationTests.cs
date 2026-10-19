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

// QueueStateEvent经过真实InputAction、UIInputModule和UGUI；不冒充物理鼠标或双尺寸人工验收。
public partial class LootIntegrationTests
{
    private const string ScenePath = "Assets/Scenes/Tests/Loot/LootTest.unity";
    private Keyboard keyboard;
    private Mouse mouse;
    private InputSettings originalSettings;
    private InputSettings testSettings;
    private bool originalRunInBackground;
    private GameObject persistentRoot;
    private LootTestDriver driver;
    private LootTableSO clonedTable;
    private readonly List<InputDevice> disabledInputDevices = new List<InputDevice>();

    [SetUp]
    public void PrepareInput()
    {
        if (Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
        {
            Assert.Ignore("Run from the Test Runner clean test scene.");
        }
        originalSettings = InputSystem.settings;
        originalRunInBackground = Application.runInBackground;
        foreach (InputDevice device in InputSystem.devices)
        {
            if (device.enabled && (device is Keyboard || device is UnityEngine.InputSystem.Pointer))
            {
                disabledInputDevices.Add(device);
            }
        }
        try
        {
            testSettings = Object.Instantiate(originalSettings);
            testSettings.hideFlags = HideFlags.HideAndDontSave;
            testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = testSettings;
            Application.runInBackground = true;
            // 排队测试只消费本次合成设备，避免桌面实体输入抢占UGUI的同一个鼠标指针。
            foreach (InputDevice device in InputSystem.devices.ToArray())
            {
                if (!device.enabled || (!(device is Keyboard) && !(device is UnityEngine.InputSystem.Pointer))) { continue; }
                InputSystem.DisableDevice(device);
            }
            keyboard = InputSystem.AddDevice<Keyboard>();
            mouse = InputSystem.AddDevice<Mouse>();
        }
        catch
        {
            RestoreTestInput();
            throw;
        }
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        try
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded) { yield return SceneManager.UnloadSceneAsync(scene); }
            if (persistentRoot != null) { Object.Destroy(persistentRoot); }
            if (clonedTable != null) { Object.Destroy(clonedTable); }
            ClearGridTestFixtures();
            yield return null;
        }
        finally { RestoreTestInput(); }
        yield return null;
    }

    private void RestoreTestInput()
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
            testSettings = null;
        }
        finally
        {
            foreach (InputDevice device in disabledInputDevices)
            {
                if (device.added && !device.enabled) { InputSystem.EnableDevice(device); }
            }
            disabledInputDevices.Clear();
        }
    }

    [UnityTest]
    public IEnumerator QueuedPointerTransfersBothWaysAndSingleRPreserveIdentityAndEv()
    {
        yield return LoadScene();
        Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        Assert.That(Object.FindFirstObjectByType<PlayerTestDriver>(), Is.Null);
        yield return Click(ButtonPoint("SimulateShelter"));
        Assert.That(driver.IsShelter, Is.True);
        Assert.That(driver.backpackUI.IsOpen && driver.warehouseUI.IsOpen, Is.True);
        ItemInstance battery = driver.backpack.GetPlacements().First(value => value.Item.DefinitionId == "battery").Item;
        int uses = battery.UsesThisRound;
        bool retained = battery.IsRetained;
        int ev = driver.shelter.CurrentEv;
        string source = battery.SourceId;
        yield return BeginPointerDrag(BackpackPoint(0, 2));
        Assert.That(driver.backpackUI.IsDragging, Is.True);
        Assert.That(driver.warehouseUI.IsDragging, Is.False);
        yield return Pointer(WarehousePoint(6, 0), true);
        yield return Press(Key.R);
        Assert.That(driver.backpackUI.PreviewRotated, Is.True, "Only the active source handles R once.");
        yield return Press(Key.F10);
        Assert.That(driver.backpackUI.IsDragging, Is.True);
        yield return Pointer(WarehousePoint(6, 0), false);
        Assert.That(battery.Owner, Is.EqualTo(ItemOwner.Storage), driver.backpackUI.FeedbackKey);
        Assert.That(driver.warehouse.TryGetPlacement(battery.InstanceId, out WarehousePlacement stored), Is.True);
        Assert.That(stored.Rotated, Is.True);
        Assert.That(driver.backpack.TryGetPlacement(battery.InstanceId, out _), Is.False);

        yield return BeginPointerDrag(WarehousePoint(6, 0));
        Assert.That(driver.warehouseUI.IsDragging, Is.True);
        Assert.That(driver.backpackUI.IsDragging, Is.False);
        yield return Pointer(BackpackPoint(1, 2), true);
        yield return Pointer(BackpackPoint(1, 2), false);
        Assert.That(battery.Owner, Is.EqualTo(ItemOwner.Backpack), driver.warehouseUI.FeedbackKey);
        Assert.That(driver.backpack.TryGetPlacement(battery.InstanceId, out BackpackPlacement returned), Is.True);
        Assert.That(returned.Rotated, Is.True);
        Assert.That(driver.warehouse.TryGetPlacement(battery.InstanceId, out _), Is.False);
        Assert.That(battery.UsesThisRound, Is.EqualTo(uses));
        Assert.That(battery.IsRetained, Is.EqualTo(retained));
        Assert.That(battery.SourceId, Is.EqualTo(source));
        Assert.That(driver.shelter.CurrentEv, Is.EqualTo(ev));
    }

    [UnityTest]
    public IEnumerator QueuedEOpensSearchDespiteClearedCurrentTargetAndHiddenEntryCannotTransfer()
    {
        yield return LoadScene();
        LootBox box = Box("guaranteed");
        AssertTriggerReachWithModelGap(box);
        yield return OpenTargetWithSingleHeldE(box);
        Assert.That(driver.lootUI.IsOpen, Is.True);
        Assert.That(driver.lootUI.Source, Is.SameAs(box.State));
        Assert.That(driver.player.CanAct, Is.False);
        Assert.That(driver.player.interaction.CurrentTarget, Is.Null, "UI lock may clear the target without invalidating spatial reach.");
        Assert.That(driver.player.interaction.InputBlocked, Is.True);
        Assert.That(driver.player.interaction.CanReachTarget(box), Is.True);
        LootEntryState first = box.State.Entries[0];
        Assert.That(first.Revealed, Is.False);
        Assert.That(driver.coordinator.TrySourceToBackpack(box.State, first.EntryId, 1, 2, false, () => true),
            Is.EqualTo(ItemTransferResult.SourceHidden));
        Assert.That(driver.lootUI.BeginItemDrag(first.EntryId, Vector2.zero, null), Is.Zero);
        yield return new WaitForSeconds(0.2f);
        float beforeLanguage = box.State.SearchElapsed;
        Assert.That(beforeLanguage, Is.GreaterThan(0));
        yield return Press(Key.F10);
        Assert.That(driver.lootUI.IsOpen, Is.True);
        Assert.That(box.State.SearchElapsed, Is.GreaterThanOrEqualTo(beforeLanguage));
        Assert.That(driver.lootUI.view.statusText.text, Is.EqualTo("Searching"));
        LootItemView searching = GridItemView(box.State.SearchEntryId);
        Assert.That(searching.countdownText.text, Does.Contain("s"));
        Assert.That(driver.lootUI.view.progressText == null || !driver.lootUI.view.progressText.gameObject.activeInHierarchy, Is.True);
        Assert.That(Time.timeScale, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator RevealedEntryPointerPickupContinuesNextSearchAndDamageCancelsLateRelease()
    {
        yield return LoadScene();
        yield return Press(Key.E);
        LootSourceState source = driver.lootUI.Source;
        yield return WaitForRevealed(source, 0);
        LootEntryState first = source.Entries[0];
        LootEntryState second = source.Entries[1];
        Assert.That(second.Revealed, Is.False);
        string secondId = second.EntryId;
        float secondElapsed = source.SearchElapsed;
        yield return BeginPointerDrag(EntryPoint(first.EntryId));
        Assert.That(driver.backpackUI.IsExternalPreview, Is.True);
        yield return Pointer(BackpackPoint(1, 2), true);
        yield return Pointer(BackpackPoint(1, 2), false);
        Assert.That(first.Taken, Is.True);
        Assert.That(driver.items.TryGetInstance(first.InstanceId, out ItemInstance taken), Is.True);
        Assert.That(taken.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(driver.lootUI.IsOpen, Is.True);
        Assert.That(second.Revealed || source.SearchEntryId == secondId, Is.True);
        if (!second.Revealed) { Assert.That(source.SearchElapsed, Is.GreaterThanOrEqualTo(secondElapsed)); }
        driver.DamageOne();
        yield return null;
        Assert.That(driver.backpackUI.IsOpen, Is.False);
        Assert.That(driver.lootUI.IsOpen, Is.False);
        Assert.That(source.SearchElapsed, Is.Zero);
        Assert.That(first.Revealed && first.Taken, Is.True);
        Assert.That(driver.player.CanAct, Is.True);
        int count = driver.items.InstanceCount;
        yield return Press(Key.E);
        Assert.That(driver.lootUI.Source, Is.SameAs(source));
        Assert.That(driver.items.InstanceCount, Is.EqualTo(count));
        Assert.That(first.Taken, Is.True);
        yield return WaitForRevealed(source, 1);
        yield return BeginPointerDrag(EntryPoint(second.EntryId));
        int token = driver.backpackUI.DragToken;
        Assert.That(token, Is.Not.Zero);
        yield return new WaitForSeconds(driver.player.config.damageInvulnerabilitySeconds + 0.05f);
        driver.DamageLethal();
        yield return null;
        Assert.That(driver.player.IsDead, Is.True);
        Assert.That(driver.lootUI.IsOpen, Is.False);
        Assert.That(driver.backpackUI.IsDragging, Is.False);
        yield return Pointer(BackpackPoint(2, 2), false);
        driver.lootUI.EndItemDrag(token, BackpackPoint(2, 2), null);
        Assert.That(second.Taken, Is.False);
        Assert.That(driver.player.CanAct, Is.False);
        Assert.That(driver.items.GetInstances().Single(item => item.InstanceId == second.InstanceId).Owner, Is.EqualTo(ItemOwner.Container));
    }

    [UnityTest]
    public IEnumerator QueuedDiscardConfirmationCancelsTranslatesAndDestroysOrdinaryItemExactlyOnce()
    {
        yield return LoadScene();
        ItemTransferUIBridge bridge = Object.FindFirstObjectByType<ItemTransferUIBridge>();
        Assert.That(bridge, Is.Not.Null);
        ItemInstance battery = driver.backpack.GetPlacements().First(value => value.Item.DefinitionId == "battery").Item;
        string originalSource = battery.SourceId;
        int instanceCount = driver.items.InstanceCount;
        int completions = 0;
        driver.coordinator.Completed += _ => completions++;
        yield return Press(Key.Tab);
        yield return Click(BackpackPoint(0, 2));
        Assert.That(driver.player.CanAct, Is.False);
        Assert.That(driver.backpackUI.SelectedInstanceId, Is.EqualTo(battery.InstanceId));
        string before = TransferSnapshot();
        yield return Click(ButtonPoint("DiscardItem"));
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.True);
        Assert.That(bridge.discardPanel.activeInHierarchy, Is.True);
        Assert.That(bridge.PendingDiscardInstanceId, Is.EqualTo(battery.InstanceId));
        int cancelledToken = bridge.DiscardToken;
        Assert.That(cancelledToken, Is.Not.Zero);
        AssertDiscardText(bridge, battery, false);
        Assert.That(TransferSnapshot(), Is.EqualTo(before), "Requesting confirmation must not change ownership or placement.");
        yield return Click(ButtonPoint("CancelDiscard"));
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.False);
        Assert.That(bridge.discardPanel.activeInHierarchy, Is.False);
        Assert.That(TransferSnapshot(), Is.EqualTo(before));
        Assert.That(completions, Is.Zero);

        yield return Click(ButtonPoint("DiscardItem"));
        int confirmedToken = bridge.DiscardToken;
        Assert.That(confirmedToken, Is.Not.EqualTo(cancelledToken));
        yield return Press(Key.F10);
        Assert.That(LocalizationManager.Instance.CurrentLanguage, Is.EqualTo(GameLanguage.English));
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.True);
        Assert.That(bridge.DiscardToken, Is.EqualTo(confirmedToken));
        Assert.That(bridge.PendingDiscardInstanceId, Is.EqualTo(battery.InstanceId));
        AssertDiscardText(bridge, battery, false);
        Assert.That(TransferSnapshot(), Is.EqualTo(before), "Changing language only refreshes the pending confirmation text.");
        yield return Click(ButtonPoint("ConfirmDiscard"));
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.False);
        Assert.That(battery.Owner, Is.EqualTo(ItemOwner.Lost), driver.backpackUI.FeedbackKey);
        Assert.That(battery.ContainerId, Is.Empty);
        Assert.That(driver.backpack.TryGetPlacement(battery.InstanceId, out _), Is.False);
        Assert.That(battery.SourceId, Is.EqualTo(originalSource));
        Assert.That(driver.items.TryGetInstance(battery.InstanceId, out ItemInstance recorded), Is.True);
        Assert.That(recorded, Is.SameAs(battery), "Lost retains the 01 identity record; it does not create a replacement instance.");
        Assert.That(driver.items.InstanceCount, Is.EqualTo(instanceCount));
        AssertNoGroundPresentation(battery.InstanceId);
        Assert.That(completions, Is.EqualTo(1));
        string committed = TransferSnapshot();
        bridge.ConfirmDiscard(confirmedToken);
        bridge.ConfirmDiscard(confirmedToken);
        bridge.CancelDiscard(confirmedToken);
        Assert.That(TransferSnapshot(), Is.EqualTo(committed));
        Assert.That(completions, Is.EqualTo(1), "Late or duplicate confirmation must not execute another discard.");
    }

    [UnityTest]
    public IEnumerator RealDamageCancelsDiscardAndOldTokensCannotConfirmOrCancelANewerTarget()
    {
        yield return LoadScene();
        ItemTransferUIBridge bridge = Object.FindFirstObjectByType<ItemTransferUIBridge>();
        Assert.That(bridge, Is.Not.Null);
        yield return Press(Key.Tab);
        ItemInstance battery = driver.backpack.GetPlacements().First(value => value.Item.DefinitionId == "battery").Item;
        ItemInstance food = driver.backpack.GetPlacements().First(value => value.Item.DefinitionId == "food").Item;
        yield return Click(BackpackPoint(0, 2));
        yield return Click(ButtonPoint("DiscardItem"));
        int oldToken = bridge.DiscardToken;
        Assert.That(oldToken, Is.Not.Zero);
        Assert.That(bridge.PendingDiscardInstanceId, Is.EqualTo(battery.InstanceId));
        string before = TransferSnapshot();
        int health = driver.player.health.CurrentHealth;
        yield return Press(Key.H);
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health - 1));
        Assert.That(driver.player.IsDead, Is.False);
        Assert.That(driver.backpackUI.IsOpen, Is.False);
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.False);
        Assert.That(bridge.discardPanel.activeInHierarchy, Is.False);
        Assert.That(driver.player.CanAct, Is.True);
        bridge.ConfirmDiscard(oldToken);
        Assert.That(TransferSnapshot(), Is.EqualTo(before));

        yield return Press(Key.Tab);
        yield return Click(BackpackPoint(2, 0));
        yield return Click(ButtonPoint("DiscardItem"));
        int currentToken = bridge.DiscardToken;
        Assert.That(currentToken, Is.Not.Zero.And.Not.EqualTo(oldToken));
        Assert.That(bridge.PendingDiscardInstanceId, Is.EqualTo(food.InstanceId));
        bridge.CancelDiscard(oldToken);
        bridge.ConfirmDiscard(oldToken);
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.True);
        Assert.That(bridge.DiscardToken, Is.EqualTo(currentToken));
        Assert.That(bridge.PendingDiscardInstanceId, Is.EqualTo(food.InstanceId));
        Assert.That(TransferSnapshot(), Is.EqualTo(before), "A cancelled battery token must not touch the new food confirmation.");
        Assert.That(driver.backpackUI.BeginItemDrag(food.InstanceId, BackpackPoint(2, 0), null), Is.Zero,
            "The pending confirmation must gate direct drag requests as well as block pointer hits.");
        int healthBeforeBlockedUse = driver.player.health.CurrentHealth;
        yield return RightPointer(BackpackPoint(2, 0));
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.True);
        Assert.That(bridge.DiscardToken, Is.EqualTo(currentToken));
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Backpack), "Queued right click must not consume a healing item behind the confirmation mask.");
        Assert.That(food.UsesThisRound, Is.Zero);
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(healthBeforeBlockedUse));
        Assert.That(TransferSnapshot(), Is.EqualTo(before));
        yield return Click(ButtonPoint("ConfirmDiscard"));
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Lost));
        Assert.That(battery.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(driver.backpack.TryGetPlacement(battery.InstanceId, out _), Is.True);
        Assert.That(driver.backpack.TryGetPlacement(food.InstanceId, out _), Is.False);
        AssertNoGroundPresentation(food.InstanceId);

        yield return new WaitForSeconds(driver.player.config.damageInvulnerabilitySeconds + 0.05f);
        yield return Click(BackpackPoint(0, 2));
        yield return Click(ButtonPoint("DiscardItem"));
        int lethalToken = bridge.DiscardToken;
        Assert.That(lethalToken, Is.Not.Zero);
        Assert.That(bridge.PendingDiscardInstanceId, Is.EqualTo(battery.InstanceId));
        string beforeDeath = TransferSnapshot();
        yield return Press(Key.K);
        Assert.That(driver.player.health.CurrentHealth, Is.Zero);
        Assert.That(driver.player.IsDead, Is.True);
        Assert.That(driver.player.CanAct, Is.False);
        Assert.That(driver.backpackUI.IsOpen, Is.False);
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.False);
        Assert.That(bridge.discardPanel.activeInHierarchy, Is.False);
        bridge.ConfirmDiscard(lethalToken);
        bridge.CancelDiscard(lethalToken);
        Assert.That(TransferSnapshot(), Is.EqualTo(beforeDeath), "Death cancels pending discard; 08 still owns eventual carried-item loss settlement.");
        Assert.That(battery.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(driver.backpack.TryGetPlacement(battery.InstanceId, out _), Is.True);
        Assert.That(driver.backpackUI.TryOpen(), Is.False);
        yield return Press(Key.Tab);
        Assert.That(driver.backpackUI.IsOpen, Is.False);
        Assert.That(driver.player.CanAct, Is.False);
    }

    [UnityTest]
    public IEnumerator ReloadKeepsGeneratedSourcesAndDiscardedOrdinaryItemsHaveNoGroundPresentation()
    {
        yield return LoadScene();
        Assert.That(Box("zero").State.Generated, Is.True);
        Assert.That(Box("zero").State.Entries.Count, Is.Zero);
        Assert.That(Box("empty").State.Generated, Is.True);
        Assert.That(Box("empty").State.Entries.Count, Is.Zero);
        Assert.That(Box("guaranteed").State.Entries.Count, Is.EqualTo(3));
        LootRuntimeManager originalRuntime = driver.runtime;
        ItemManager originalItems = driver.items;
        LootSourceState originalSource = Box("guaranteed").State;
        yield return Press(Key.E);
        yield return WaitForRevealed(originalSource, 0);
        ItemInstance discardedItem = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        string originalItemSource = discardedItem.SourceId;
        GroundItem nativeGround = Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None)
            .Single(item => item.sourceId == "ground-battery");
        string nativeInstanceId = nativeGround.InstanceId;
        yield return Click(BackpackPoint(0, 2));
        yield return Click(ButtonPoint("DiscardItem"));
        yield return Click(ButtonPoint("ConfirmDiscard"));
        Assert.That(discardedItem.Owner, Is.EqualTo(ItemOwner.Lost), driver.backpackUI.FeedbackKey);
        AssertNoGroundPresentation(discardedItem.InstanceId);
        yield return Press(Key.F10);
        int count = originalItems.InstanceCount;
        yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByPath(ScenePath));
        yield return LoadScene();
        Assert.That(driver.runtime, Is.SameAs(originalRuntime));
        Assert.That(driver.items, Is.SameAs(originalItems));
        Assert.That(Box("guaranteed").State, Is.SameAs(originalSource));
        Assert.That(originalSource.Entries[0].Revealed, Is.True);
        Assert.That(originalSource.SearchElapsed, Is.Zero);
        Assert.That(originalItems.InstanceCount, Is.EqualTo(count));
        Assert.That(LocalizationManager.Instance.CurrentLanguage, Is.EqualTo(GameLanguage.English));
        Assert.That(originalItems.TryGetInstance(discardedItem.InstanceId, out ItemInstance sameDiscarded), Is.True);
        Assert.That(sameDiscarded, Is.SameAs(discardedItem));
        Assert.That(discardedItem.Owner, Is.EqualTo(ItemOwner.Lost));
        Assert.That(discardedItem.ContainerId, Is.Empty);
        Assert.That(discardedItem.SourceId, Is.EqualTo(originalItemSource));
        Assert.That(driver.backpack.TryGetPlacement(discardedItem.InstanceId, out _), Is.False);
        AssertNoGroundPresentation(discardedItem.InstanceId);
        GroundItem reboundNative = Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None)
            .Single(item => item.sourceId == "ground-battery");
        Assert.That(reboundNative.InstanceId, Is.EqualTo(nativeInstanceId), "Authored ground pickups retain their existing source identity.");
        Assert.That(reboundNative.lootUI, Is.SameAs(driver.lootUI));
        foreach (var group in Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None).Where(item => !string.IsNullOrEmpty(item.InstanceId)).GroupBy(item => item.InstanceId))
        {
            Assert.That(group.Count(), Is.EqualTo(1));
        }
    }

    [UnityTest]
    public IEnumerator QueuedUniqueDiscardReturnsToItsOriginalBoxNextRoundEvenAtZeroProbability()
    {
        yield return LoadScene();
        LootBox unique = Box("unique-doll");
        string id = unique.State.Entries.Single().InstanceId;
        Assert.That(driver.items.TryGetInstance(id, out ItemInstance item), Is.True);
        string uniqueId = item.UniqueId;
        string originalSpawn = item.OriginalSpawnPointId;
        string originalSource = item.SourceId;
        LootEntryState originEntry = unique.State.Entries.Single();
        Vector3 position = new Vector3(unique.transform.position.x - 1.1f,
            driver.player.transform.position.y, driver.player.transform.position.z);
        driver.player.transform.position = position;
        driver.player.Body.position = position;
        Physics.SyncTransforms();
        yield return null;
        yield return Press(Key.E);
        Assert.That(driver.lootUI.Source, Is.SameAs(unique.State), "The unique item must be searched at its actual authored origin box.");
        yield return WaitForRevealed(unique.State, 0);
        yield return BeginPointerDrag(EntryPoint(originEntry.EntryId));
        yield return Press(Key.R);
        Assert.That(driver.backpackUI.PreviewRotated, Is.True);
        yield return Pointer(BackpackPoint(1, 2), true);
        Assert.That(driver.backpackUI.PreviewResult, Is.EqualTo(BackpackResult.Success));
        yield return Pointer(BackpackPoint(1, 2), false);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(originEntry.Taken, Is.True);
        ItemTransferUIBridge bridge = Object.FindFirstObjectByType<ItemTransferUIBridge>();
        yield return Click(BackpackPoint(1, 2));
        yield return Click(ButtonPoint("DiscardItem"));
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.True);
        Assert.That(bridge.PendingDiscardInstanceId, Is.EqualTo(id));
        AssertDiscardText(bridge, item, true);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack), "The unique item remains carried until confirmation.");
        yield return Click(ButtonPoint("ConfirmDiscard"));
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.PendingRecovery));
        Assert.That(item.ContainerId, Is.Empty);
        Assert.That(item.RecoveryRound, Is.EqualTo(driver.items.CurrentRound + 1));
        Assert.That(item.OriginalSpawnPointId, Is.EqualTo(originalSpawn));
        Assert.That(driver.backpack.TryGetPlacement(id, out _), Is.False);
        AssertNoGroundPresentation(id);
        Assert.That(driver.items.TryClaimUniqueSpawn(id, originalSpawn), Is.False, "A discarded unique item cannot be reclaimed in the same round.");
        Assert.That(driver.items.GetRecoverableItems(), Has.No.Member(item));
        Assert.That(driver.runtime.PrepareRound(driver.items.CurrentRound, out string sameRoundError), Is.True, sameRoundError);
        Assert.That(unique.State.Entries.Single(), Is.SameAs(originEntry));
        Assert.That(originEntry.Taken, Is.True);
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.PendingRecovery));
        clonedTable = Object.Instantiate(unique.lootTable);
        clonedTable.entries[0].overrideProbability = 0;
        clonedTable.entries[0].useDefaultProbability = false;
        unique.lootTable = clonedTable;
        driver.SimulateNextRound();
        yield return null;
        Assert.That(driver.IsReady, Is.True);
        Assert.That(driver.items.CurrentRound, Is.EqualTo(2));
        Assert.That(unique.State.Entries.Single().InstanceId, Is.EqualTo(id));
        Assert.That(driver.items.TryGetUniqueInstance(uniqueId, out ItemInstance recovered), Is.True);
        Assert.That(recovered, Is.SameAs(item));
        Assert.That(recovered.Owner, Is.EqualTo(ItemOwner.Container));
        Assert.That(recovered.ContainerId, Is.EqualTo(unique.State.SourceId));
        Assert.That(recovered.OriginalSpawnPointId, Is.EqualTo("loot-test/unique-doll/slot-1"));
        Assert.That(recovered.OriginalSpawnPointId, Is.EqualTo(originalSpawn));
        Assert.That(recovered.SourceId, Is.EqualTo(originalSource));
        Assert.That(driver.items.GetInstances().Count(value => value.UniqueId == uniqueId), Is.EqualTo(1));
        Assert.That(unique.State.Entries[0].Revealed, Is.False);
    }

    [UnityTest]
    public IEnumerator QueuedOldDiscardConfirmReleaseCannotOperateANewerConfirmation()
    {
        yield return HeldDiscardButtonReleaseDoesNotOperateNextConfirmation("ConfirmDiscard");
    }

    [UnityTest]
    public IEnumerator QueuedOldDiscardCancelReleaseCannotDismissANewerConfirmation()
    {
        yield return HeldDiscardButtonReleaseDoesNotOperateNextConfirmation("CancelDiscard");
    }

    private IEnumerator HeldDiscardButtonReleaseDoesNotOperateNextConfirmation(string heldButtonName)
    {
        yield return LoadScene();
        ItemTransferUIBridge bridge = Object.FindFirstObjectByType<ItemTransferUIBridge>();
        Assert.That(bridge, Is.Not.Null);
        ItemInstance battery = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        ItemInstance food = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "food").Item;
        yield return Press(Key.Tab);
        yield return Click(BackpackPoint(0, 2));
        yield return Click(ButtonPoint("DiscardItem"));
        int oldToken = bridge.DiscardToken;
        Assert.That(oldToken, Is.Not.Zero);
        Assert.That(bridge.PendingDiscardInstanceId, Is.EqualTo(battery.InstanceId));
        string before = TransferSnapshot();
        Vector2 heldPoint = ButtonPoint(heldButtonName);
        yield return Pointer(heldPoint, false);
        yield return Pointer(heldPoint, true);
        Assert.That(mouse.leftButton.isPressed, Is.True);
        Assert.That(bridge.DiscardToken, Is.EqualTo(oldToken), "PointerDown alone must not confirm or cancel.");

        int health = driver.player.health.CurrentHealth;
        yield return Press(Key.H);
        Assert.That(driver.player.health.CurrentHealth, Is.EqualTo(health - 1));
        Assert.That(driver.backpackUI.IsOpen, Is.False);
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.False);
        Assert.That(bridge.discardPanel.activeInHierarchy, Is.False);
        Assert.That(mouse.leftButton.isPressed, Is.True, "The original mouse press remains held across real damage.");
        Assert.That(TransferSnapshot(), Is.EqualTo(before));

        // 只用API安排下一确认；保留旧UGUI PointerDown，不能通过新鼠标点击顺手释放它。
        Assert.That(driver.backpackUI.TryOpen(), Is.True);
        driver.backpackUI.SelectItem(food.InstanceId);
        bridge.DropSelected();
        int newToken = bridge.DiscardToken;
        Assert.That(newToken, Is.Not.Zero.And.Not.EqualTo(oldToken));
        Assert.That(bridge.PendingDiscardInstanceId, Is.EqualTo(food.InstanceId));
        Assert.That(bridge.discardPanel.activeInHierarchy, Is.True);
        yield return null;
        Canvas.ForceUpdateCanvases();
        RectTransform reopenedButton = GameObject.Find(heldButtonName).GetComponent<RectTransform>();
        Assert.That(RectTransformUtility.RectangleContainsScreenPoint(reopenedButton, heldPoint, null), Is.True,
            "The old release must hit the reused button to exercise the stale UGUI press path.");
        Assert.That(mouse.leftButton.isPressed, Is.True);

        // 这是真实InputSystem释放，经过UIInputModule/UGUI，不能替换为直接调用旧token方法。
        yield return Pointer(heldPoint, false);
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.True,
            "An old " + heldButtonName + " PointerUp must not act on the new confirmation listener.");
        Assert.That(bridge.DiscardToken, Is.EqualTo(newToken));
        Assert.That(bridge.PendingDiscardInstanceId, Is.EqualTo(food.InstanceId));
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(battery.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(TransferSnapshot(), Is.EqualTo(before));

        // 新的完整PointerDown/PointerUp仍能正常确认，修复不能把新按钮永久锁死。
        yield return Click(ButtonPoint("ConfirmDiscard"));
        Assert.That(bridge.IsAwaitingDiscardConfirmation, Is.False);
        Assert.That(food.Owner, Is.EqualTo(ItemOwner.Lost), driver.backpackUI.FeedbackKey);
        Assert.That(battery.Owner, Is.EqualTo(ItemOwner.Backpack));
        Assert.That(driver.backpack.TryGetPlacement(food.InstanceId, out _), Is.False);
        Assert.That(driver.backpack.TryGetPlacement(battery.InstanceId, out _), Is.True);
        AssertNoGroundPresentation(food.InstanceId);
    }

    private static void AssertDiscardText(ItemTransferUIBridge bridge, ItemInstance item, bool unique)
    {
        LocalizationManager language = LocalizationManager.Instance;
        Assert.That(language.GetText("loot.discard.title"), Does.Not.StartWith("["), "The real bilingual confirmation table must be configured.");
        Assert.That(bridge.discardTitleText.text, Is.EqualTo(language.GetText("loot.discard.title")));
        Assert.That(bridge.discardMessageText.text, Is.EqualTo(language.FormatText(
            unique ? "loot.discard.unique" : "loot.discard.ordinary", language.GetText(item.Definition.nameKey))));
        Assert.That(bridge.discardMessageText.text, Does.Contain(language.GetText(item.Definition.nameKey)));
        Assert.That(bridge.confirmDiscardText.text, Is.EqualTo(language.GetText("loot.discard.confirm")));
        Assert.That(bridge.cancelDiscardText.text, Is.EqualTo(language.GetText("loot.discard.cancel")));
    }

    private static void AssertNoGroundPresentation(string instanceId)
    {
        Assert.That(Object.FindObjectsByType<GroundItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Count(item => item.InstanceId == instanceId), Is.Zero, "Discard must not create a ground source or even a hidden ground presentation.");
    }

    [UnityTest]
    public IEnumerator LeavingExplicitTriggerAndRealWallInterruptSearchWhileUiLockDoesNot()
    {
        yield return LoadScene();
        LootBox box = Box("guaranteed");
        AssertTriggerReachWithModelGap(box);
        yield return Press(Key.E);
        LootSourceState source = driver.lootUI.Source;
        yield return new WaitForSeconds(0.2f);
        Assert.That(driver.lootUI.IsOpen, Is.True);
        Vector3 saved = driver.player.transform.position;
        Vector3 outside = saved;
        outside.x = box.interactionTrigger.bounds.min.x - driver.player.GetComponent<CapsuleCollider>().bounds.extents.x - 0.25f;
        TeleportLootTestPlayer(outside);
        Assert.That(driver.player.interaction.CanReachTarget(box), Is.False);
        yield return null;
        Assert.That(driver.lootUI.IsOpen, Is.False);
        Assert.That(source.SearchElapsed, Is.Zero);
        driver.backpackUI.Close();
        TeleportLootTestPlayer(saved);
        Assert.That(driver.player.interaction.CanReachTarget(box), Is.True, "Re-entry must work before another physics trigger event.");
        yield return null;
        yield return Press(Key.E);
        Assert.That(driver.lootUI.IsOpen, Is.True);
        GameObject wall = GameObject.Find("ObstructionWall3D");
        Vector3 sightOrigin = driver.player.interaction.transform.TransformPoint(driver.player.interaction.queryOffset);
        Vector3 sightTarget = box.interactionPoint != null ? box.interactionPoint.position : box.transform.position;
        wall.transform.position = (sightOrigin + sightTarget) * 0.5f;
        wall.transform.localScale = new Vector3(0.12f, 3f, 2f);
        Physics.SyncTransforms();
        Assert.That(driver.player.interaction.CanReachTarget(box), Is.False, "Being inside the TriggerBox must not bypass a real wall.");
        yield return null;
        Assert.That(driver.lootUI.IsOpen, Is.False);
        Assert.That(source.SearchElapsed, Is.Zero);
    }

    [UnityTest]
    public IEnumerator QueuedLeftAndRightControlTransferOneKnownItemBothWaysWhileSearchContinues()
    {
        yield return LoadScene();
        LootBox box = CreateGridTestBox("quick-search", 6, 4, new Vector3(1, 2, 0), new Vector3(2, 2, 30));
        Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
        yield return null;
        LootSourceState source = box.State;
        LootEntryState known = source.Entries[0];
        LootEntryState unknown = source.Entries[1];
        Assert.That(known.Revealed && !unknown.Revealed, Is.True);
        Assert.That(driver.items.TryGetInstance(known.InstanceId, out ItemInstance item), Is.True);
        string identity = ItemIdentitySnapshot(item);
        string layout = GridLayoutSnapshot(source);
        string background = GridBackgroundSnapshot();
        int instanceCount = driver.items.InstanceCount;
        int completions = 0;
        System.Action<string> onCompleted = _ => completions++;
        driver.coordinator.Completed += onCompleted;
        try
        {
            string beforeThresholdGesture = TransferSnapshot();
            Vector2 knownPoint = EntryPoint(known.EntryId);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
            yield return null;
            yield return Pointer(knownPoint, false);
            yield return Pointer(knownPoint, true);
            yield return Pointer(knownPoint + Vector2.right * 20, true);
            AssertNoActiveTransferDrag();
            yield return Pointer(knownPoint + Vector2.right * 20, false);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            Assert.That(completions, Is.Zero, "Crossing the drag threshold consumes the Ctrl gesture even when released on the same revealed tile.");
            Assert.That(TransferSnapshot(), Is.EqualTo(beforeThresholdGesture));
            Assert.That(known.Taken, Is.False);
            AssertNoActiveTransferDrag();

            foreach (Key control in new[] { Key.LeftCtrl, Key.RightCtrl })
            {
                float elapsed = source.SearchElapsed;
                int before = completions;
                yield return ControlClick(control, EntryPoint(known.EntryId));
                Assert.That(completions, Is.EqualTo(before + 1));
                Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
                Assert.That(known.Taken, Is.True);
                Assert.That(driver.backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement placement), Is.True);
                Assert.That(new Vector2Int(placement.X, placement.Y), Is.EqualTo(new Vector2Int(1, 2)));
                Assert.That(placement.Rotated, Is.False);
                AssertNoActiveTransferDrag();
                AssertReturnFeedback(ItemTransferCoordinator.ResultKey(ItemTransferResult.Success));
                yield return ControlClick(control, BackpackPoint(placement.X, placement.Y));
                Assert.That(completions, Is.EqualTo(before + 2));
                Assert.That(item.Owner, Is.EqualTo(ItemOwner.Container));
                Assert.That(item.ContainerId, Is.EqualTo(source.SourceId));
                Assert.That(source.Entries.Single(entry => entry.InstanceId == item.InstanceId), Is.SameAs(known));
                Assert.That(known.Revealed && !known.Taken, Is.True);
                Assert.That(GridEntryBounds(known), Is.EqualTo(new RectInt(0, 0, 1, 2)));
                Assert.That(known.LayoutRotated, Is.False);
                Assert.That(source.SearchEntryId, Is.EqualTo(unknown.EntryId));
                Assert.That(source.SearchElapsed, Is.GreaterThanOrEqualTo(elapsed));
                AssertUnknownGridTile(GridItemView(unknown.EntryId));
                AssertSingleGridCountdown(source);
                Assert.That(GridLayoutSnapshot(source), Is.EqualTo(layout));
                Assert.That(GridBackgroundSnapshot(), Is.EqualTo(background));
                Assert.That(ItemIdentitySnapshot(item), Is.EqualTo(identity));
                Assert.That(driver.items.InstanceCount, Is.EqualTo(instanceCount));
                AssertNoActiveTransferDrag();
                AssertReturnFeedback(ItemTransferCoordinator.ResultKey(ItemTransferResult.Success));
                yield return Press(Key.F10);
            }
        }
        finally { driver.coordinator.Completed -= onCompleted; }
    }

    [UnityTest]
    public IEnumerator QueuedControlUsesOnlyRotatedBackpackSpaceAndPrecisePickupKeepsBoxRotationAndGrabOffset()
    {
        yield return LoadScene();
        LootBox box = CreateGridTestBox("quick-rotated", 6, 4, new Vector3(2, 3, 0));
        Assert.That(driver.lootUI.TryOpenSource(box.State, box, driver.player.interaction), Is.True);
        yield return null;
        LootEntryState entry = box.State.Entries[0];
        Assert.That(driver.items.TryGetInstance(entry.InstanceId, out ItemInstance item), Is.True);
        string identity = ItemIdentitySnapshot(item);
        string existing = BackpackLayoutExcept(item.InstanceId);
        int count = driver.items.InstanceCount;
        yield return ControlClick(Key.LeftCtrl, EntryPoint(entry.EntryId));
        Assert.That(driver.backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement placement), Is.True);
        Assert.That(new RectInt(placement.X, placement.Y, placement.Width, placement.Height), Is.EqualTo(new RectInt(1, 2, 3, 2)));
        Assert.That(placement.Rotated, Is.True, "Only the rotated footprint fits beside the three existing real items.");
        Assert.That(BackpackLayoutExcept(item.InstanceId), Is.EqualTo(existing));
        yield return ControlClick(Key.RightCtrl, BackpackPoint(1, 2));
        Assert.That(entry.LayoutRotated, Is.True, "Quick return prefers the carried orientation before trying another one.");
        Assert.That(GridEntryBounds(entry), Is.EqualTo(new RectInt(0, 0, 3, 2)));
        Assert.That(item.Owner, Is.EqualTo(ItemOwner.Container));

        // 抓住已旋转3×2物品的右下格；真实按下位置是偏移来源，不能被跨阈值后的鼠标位置替换。
        yield return BeginPointerDrag(GridCellPoint(2, 1));
        Assert.That(driver.backpackUI.PreviewRotated, Is.True);
        Assert.That(driver.backpackUI.DragGrabOffset, Is.EqualTo(new Vector2Int(2, 1)));
        yield return Pointer(BackpackPoint(3, 3), true);
        Assert.That(new Vector2Int(driver.backpackUI.PreviewX, driver.backpackUI.PreviewY), Is.EqualTo(new Vector2Int(1, 2)));
        Assert.That(driver.backpackUI.PreviewResult, Is.EqualTo(BackpackResult.Success));
        yield return Pointer(BackpackPoint(3, 3), false);
        Assert.That(driver.backpack.TryGetPlacement(item.InstanceId, out placement), Is.True);
        Assert.That(placement.Rotated, Is.True);
        Assert.That(new RectInt(placement.X, placement.Y, placement.Width, placement.Height), Is.EqualTo(new RectInt(1, 2, 3, 2)));
        Assert.That(BackpackLayoutExcept(item.InstanceId), Is.EqualTo(existing));
        Assert.That(ItemIdentitySnapshot(item), Is.EqualTo(identity));
        Assert.That(driver.items.InstanceCount, Is.EqualTo(count));
        Assert.That(entry.Taken, Is.True);
        AssertNoActiveTransferDrag();
    }

    [UnityTest]
    public IEnumerator QueuedBothControlKeysTransferBackpackAndWarehouseOnceWithoutRearrangingOtherItems()
    {
        yield return LoadScene();
        driver.EnterShelter();
        yield return null;
        ItemInstance item = driver.backpack.GetPlacements().Single(value => value.Item.DefinitionId == "battery").Item;
        string identity = ItemIdentitySnapshot(item);
        string backpackBefore = BackpackLayoutExcept(item.InstanceId);
        string warehouseBefore = string.Join("|", driver.warehouse.GetPlacements().OrderBy(value => value.InstanceId)
            .Select(value => value.InstanceId + ":" + value.X + ":" + value.Y + ":" + value.Rotated));
        int instances = driver.items.InstanceCount;
        int ev = driver.shelter.CurrentEv;
        int completions = 0;
        System.Action<string> onCompleted = _ => completions++;
        driver.coordinator.Completed += onCompleted;
        try
        {
            foreach (Key control in new[] { Key.LeftCtrl, Key.RightCtrl })
            {
                int before = completions;
                yield return ControlClick(control, BackpackPoint(0, 2));
                Assert.That(item.Owner, Is.EqualTo(ItemOwner.Storage));
                Assert.That(driver.warehouse.TryGetPlacement(item.InstanceId, out WarehousePlacement stored), Is.True);
                Assert.That(new Vector2Int(stored.X, stored.Y), Is.EqualTo(new Vector2Int(2, 0)));
                Assert.That(stored.Rotated, Is.False);
                Assert.That(completions, Is.EqualTo(before + 1));
                AssertNoActiveTransferDrag();
                yield return ControlClick(control, WarehousePoint(stored.X, stored.Y));
                Assert.That(item.Owner, Is.EqualTo(ItemOwner.Backpack));
                Assert.That(driver.backpack.TryGetPlacement(item.InstanceId, out BackpackPlacement carried), Is.True);
                Assert.That(new Vector2Int(carried.X, carried.Y), Is.EqualTo(new Vector2Int(0, 2)));
                Assert.That(carried.Rotated, Is.False);
                Assert.That(completions, Is.EqualTo(before + 2));
                Assert.That(driver.backpackUI.FeedbackKey, Is.EqualTo(ItemTransferCoordinator.ResultKey(ItemTransferResult.Success)));
                Assert.That(driver.warehouseUI.FeedbackKey, Is.EqualTo(driver.backpackUI.FeedbackKey));
                Assert.That(BackpackLayoutExcept(item.InstanceId), Is.EqualTo(backpackBefore));
                Assert.That(string.Join("|", driver.warehouse.GetPlacements().OrderBy(value => value.InstanceId)
                    .Select(value => value.InstanceId + ":" + value.X + ":" + value.Y + ":" + value.Rotated)), Is.EqualTo(warehouseBefore));
                Assert.That(ItemIdentitySnapshot(item), Is.EqualTo(identity));
                Assert.That(driver.items.InstanceCount, Is.EqualTo(instances));
                Assert.That(driver.shelter.CurrentEv, Is.EqualTo(ev));
                Assert.That(driver.warehouse.HasPendingInteraction, Is.False);
                AssertNoActiveTransferDrag();
            }
        }
        finally { driver.coordinator.Completed -= onCompleted; }
    }

    private IEnumerator ControlClick(Key control, Vector2 point)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(control));
        yield return null;
        yield return Pointer(point, false);
        yield return Pointer(point, true);
        AssertNoActiveTransferDrag();
        // 右Ctrl在松手前释放，仍按按下时记录的手势执行。
        if (control == Key.RightCtrl)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
        }
        yield return Pointer(point, false);
        yield return Pointer(point, false);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        AssertNoActiveTransferDrag();
    }

    private void AssertNoActiveTransferDrag()
    {
        Assert.That(driver.backpackUI.IsDragging || driver.warehouseUI.IsDragging || driver.backpackUI.IsExternalPreview, Is.False);
        Assert.That(driver.backpackUI.view.preview.gameObject.activeSelf, Is.False);
        Assert.That(driver.warehouseUI.view.preview.gameObject.activeSelf, Is.False);
        Assert.That(driver.lootUI.view.IsReturnPreviewVisible, Is.False);
    }

    private string BackpackLayoutExcept(string instanceId)
    {
        return string.Join("|", driver.backpack.GetPlacements().Where(value => value.InstanceId != instanceId)
            .OrderBy(value => value.InstanceId).Select(value => value.InstanceId + ":" + value.X + ":" + value.Y + ":" + value.Rotated));
    }

    private void AssertTriggerReachWithModelGap(Interactable target)
    {
        Assert.That(target.TryGetInteractionTrigger(out BoxCollider trigger), Is.True,
            "The authored LootTest scene must be migrated before this test; tests must not add missing asset triggers.");
        CapsuleCollider body = driver.player.GetComponent<CapsuleCollider>();
        Physics.SyncTransforms();
        Assert.That(Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
            trigger, trigger.transform.position, trigger.transform.rotation, out _, out _), Is.True);
        Renderer[] models = target.GetComponentsInChildren<Renderer>();
        Assert.That(models, Is.Not.Empty);
        foreach (Renderer model in models)
        {
            Assert.That(body.bounds.max.x < model.bounds.min.x || body.bounds.min.x > model.bounds.max.x,
                Is.True, "The player's physical body must remain visibly separated from the item model.");
        }
        Assert.That(driver.player.interaction.CanReachTarget(target), Is.True);
    }

    private IEnumerator OpenTargetWithSingleHeldE(Interactable target)
    {
        Assert.That(driver.lootUI.IsOpen, Is.False);
        string before = TransferSnapshot();
        yield return null;
        yield return null;
        Assert.That(driver.lootUI.IsOpen, Is.False, "Entering a TriggerBox alone must not open or take anything.");
        Assert.That(TransferSnapshot(), Is.EqualTo(before));
        driver.player.interaction.RefreshTarget();
        Assert.That(driver.player.interaction.CurrentTarget, Is.SameAs(target));
        Assert.That(driver.player.interaction.CurrentPromptKey, Is.EqualTo(target.PromptKey));
        int instanceCount = driver.items.InstanceCount;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
        yield return null;
        yield return null;
        Assert.That(driver.lootUI.IsOpen, Is.True);
        int session = driver.lootUI.SessionToken;
        Assert.That(session, Is.Not.Zero);
        yield return null;
        yield return null;
        Assert.That(driver.lootUI.SessionToken, Is.EqualTo(session), "A held E must not reopen the source or execute a second action.");
        Assert.That(driver.items.InstanceCount, Is.EqualTo(instanceCount));
        Assert.That(TransferSnapshot(), Is.EqualTo(before), "Opening a source must not automatically transfer its items.");
        Assert.That(driver.player.interaction.InputBlocked, Is.True);
        Assert.That(driver.player.interaction.CanReachTarget(target), Is.True, "UI input blocking must leave the spatial query valid.");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
        Assert.That(driver.lootUI.SessionToken, Is.EqualTo(session));
    }

    private void TeleportLootTestPlayer(Vector3 position)
    {
        driver.player.transform.position = position;
        driver.player.Body.position = position;
        Physics.SyncTransforms();
    }

    private IEnumerator LoadScene()
    {
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
        yield return null;
        yield return null;
        Assert.That(GameBootstrap.Instance, Is.Not.Null);
        persistentRoot = GameBootstrap.Instance.gameObject;
        driver = Object.FindFirstObjectByType<LootTestDriver>();
        Assert.That(driver, Is.Not.Null);
        Assert.That(driver.IsReady, Is.True);
        yield return new WaitForFixedUpdate();
        yield return null;
    }

    private IEnumerator Press(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        yield return null;
    }

    private IEnumerator Pointer(Vector2 point, bool left)
    {
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = (ushort)(left ? 1 : 0) });
        yield return null;
        yield return null;
    }

    private IEnumerator Click(Vector2 point)
    {
        yield return Pointer(point, false);
        yield return Pointer(point, true);
        yield return Pointer(point, false);
    }

    private IEnumerator BeginPointerDrag(Vector2 point)
    {
        yield return Pointer(point, false);
        yield return Pointer(point, true);
        yield return Pointer(point + Vector2.right * 20, true);
    }

    private static IEnumerator WaitForRevealed(LootSourceState source, int index)
    {
        float limit = Time.realtimeSinceStartup + 8;
        while (!source.Entries[index].Revealed && Time.realtimeSinceStartup < limit) { yield return null; }
        Assert.That(source.Entries[index].Revealed, Is.True, "Sequential search did not reveal the expected item.");
    }

    private Vector2 BackpackPoint(int x, int y)
    {
        BackpackView view = driver.backpackUI.view;
        return CellPoint(view.gridRoot, view.padding, view.cellSize, view.gap, x, y);
    }

    private Vector2 WarehousePoint(int x, int y)
    {
        WarehouseView view = driver.warehouseUI.view;
        return CellPoint(view.gridRoot, view.padding, view.EffectiveCellSize, view.gap, x, y);
    }

    private static Vector2 CellPoint(RectTransform root, float padding, float size, float gap, int x, int y)
    {
        Canvas.ForceUpdateCanvases();
        return RectTransformUtility.WorldToScreenPoint(null, root.TransformPoint(new Vector3(
            padding + x * (size + gap) + size * 0.35f, -padding - y * (size + gap) - size * 0.35f, 0)));
    }

    private Vector2 EntryPoint(string entryId)
    {
        Canvas.ForceUpdateCanvases();
        LootItemView item = driver.lootUI.view.contentRoot.GetComponentsInChildren<LootItemView>().Single(value => value.EntryId == entryId);
        return RectTransformUtility.WorldToScreenPoint(null, ((RectTransform)item.transform).TransformPoint(new Vector3(20, -20, 0)));
    }

    private static Vector2 ButtonPoint(string name)
    {
        Canvas.ForceUpdateCanvases();
        RectTransform rect = GameObject.Find(name).GetComponent<RectTransform>();
        return RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
    }

    private static LootBox Box(string sourceId)
    {
        return Object.FindObjectsByType<LootBox>(FindObjectsSortMode.None).Single(box => box.sourceId == sourceId);
    }
}
#endif
