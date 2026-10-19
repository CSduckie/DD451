#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class LocalizationIntegrationTests
{
    private const string BootstrapPath = "Assets/Prefabs/Bootstrap/GameBootstrap.prefab";
    private const string ModulePath = "Assets/Prefabs/Localization/LocalizationModule.prefab";
    private const string TextPath = "Assets/Prefabs/UI/Localization/LocalizedText.prefab";
    private const string LocalizationScenePath = "Assets/Scenes/Tests/Localization/LocalizationTest.unity";
    private const string PlayerScenePath = "Assets/Scenes/Tests/Player/PlayerTest.unity";
    private const string ItemsScenePath = "Assets/Scenes/Tests/Items/ItemsTest.unity";
    private readonly List<GameObject> ownedObjects = new List<GameObject>();
    private readonly List<string> ownedScenes = new List<string>();
    private Keyboard keyboard;
    private InputSettings savedInputSettings;
    private InputSettings testInputSettings;
    private bool savedRunInBackground;

    [SetUp]
    public void RequireCleanTestScene()
    {
        if (Object.FindObjectsByType<GameBootstrap>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length != 0
            || IsLoaded(LocalizationScenePath) || IsLoaded(PlayerScenePath) || IsLoaded(ItemsScenePath))
        {
            Assert.Ignore("Run Localization PlayMode tests from the Test Runner's clean test scene.");
        }

        // 沿用玩家测试的临时输入设置，真实队列事件不依赖编辑器当前焦点。
        savedInputSettings = InputSystem.settings;
        savedRunInBackground = Application.runInBackground;
        testInputSettings = Object.Instantiate(savedInputSettings);
        testInputSettings.hideFlags = HideFlags.HideAndDontSave;
        testInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        testInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings = testInputSettings;
        Application.runInBackground = true;
        keyboard = InputSystem.AddDevice<Keyboard>();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (keyboard != null && keyboard.added)
        {
            InputSystem.RemoveDevice(keyboard);
        }
        keyboard = null;
        foreach (GameObject owned in ownedObjects)
        {
            if (owned != null) { Object.Destroy(owned); }
        }
        ownedObjects.Clear();
        yield return null;
        foreach (string path in ownedScenes)
        {
            if (IsLoaded(path))
            {
                yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByPath(path));
            }
        }
        ownedScenes.Clear();
        if (savedInputSettings != null)
        {
            InputSystem.settings = savedInputSettings;
            Application.runInBackground = savedRunInBackground;
            Object.Destroy(testInputSettings);
            testInputSettings = null;
            savedInputSettings = null;
        }
    }

    [UnityTest]
    public IEnumerator RealF10PressHoldAndModuleReenableProduceOneChangePerPress()
    {
        LocalizationManager manager = CreateEntry();
        InputAction sharedAction = manager.toggleLanguageAction.action;
        bool sharedEnabled = sharedAction.enabled;
        int changes = 0;
        manager.LanguageChanged += () => changes++;
        yield return null;
        Assert.AreEqual(GameLanguage.Chinese, manager.CurrentLanguage);

        SetKeys(Key.F10);
        yield return null;
        yield return null;
        Assert.AreEqual(GameLanguage.English, manager.CurrentLanguage);
        for (int i = 0; i < 4; i++)
        {
            SetKeys(Key.F10);
            yield return null;
        }
        Assert.AreEqual(1, changes, "Repeated held keyboard states must not repeat the press action.");
        SetKeys();
        yield return null;
        yield return PressF10();
        Assert.AreEqual(GameLanguage.Chinese, manager.CurrentLanguage);
        Assert.AreEqual(2, changes);

        manager.gameObject.SetActive(false);
        yield return null;
        manager.gameObject.SetActive(true);
        yield return null;
        yield return PressF10();
        Assert.AreEqual(GameLanguage.English, manager.CurrentLanguage);
        Assert.AreEqual(3, changes, "Reenabling the module must dispose the previous action subscription.");
        Assert.AreEqual(sharedEnabled, sharedAction.enabled, "The shared input resource belongs to other callers.");
    }

    [UnityTest]
    public IEnumerator DuplicateBootstrapAndRepeatedUiEnableBindOnlyTheSurvivingManager()
    {
        // 先启用UI，再创建入口，覆盖UI的OnEnable早于Bootstrap.Awake的接线。
        LocalizedText text = CreatePrefab(TextPath).GetComponent<LocalizedText>();
        Assert.IsNotNull(text);
        text.key = "common.language.chinese";
        LocalizationManager manager = CreateEntry();
        manager.SetLanguage(GameLanguage.English);
        yield return null;
        Assert.AreSame(manager, text.localizationManager);
        Assert.AreEqual("Chinese", text.targetText.text);

        GameObject duplicateRoot = CreatePrefab(BootstrapPath);
        LocalizationManager duplicate = CreatePrefab(ModulePath, duplicateRoot.transform).GetComponent<LocalizationManager>();
        text.gameObject.SetActive(false);
        text.localizationManager = duplicate;
        text.gameObject.SetActive(true);
        Assert.AreSame(manager, LocalizationManager.Resolve(duplicate));
        Assert.AreSame(manager, text.localizationManager, "Serialized duplicate references must be replaced immediately.");
        Assert.AreEqual(manager.GetText(text.key), text.targetText.text);
        yield return null;
        Assert.IsTrue(duplicate == null);
        Assert.AreEqual(1, Object.FindObjectsByType<LocalizationManager>(FindObjectsSortMode.None).Length);

        for (int i = 0; i < 3; i++)
        {
            text.gameObject.SetActive(false);
            text.gameObject.SetActive(true);
        }
        int changes = 0;
        manager.LanguageChanged += () => changes++;
        yield return PressF10();
        Assert.AreEqual(GameLanguage.Chinese, manager.CurrentLanguage);
        Assert.AreEqual("中文", text.targetText.text);
        Assert.AreEqual(1, changes);
        text.gameObject.SetActive(false);
        yield return PressF10();
        text.gameObject.SetActive(true);
        Assert.AreEqual("Chinese", text.targetText.text, "A reopened UI must immediately use the current language.");
    }

    [UnityTest]
    public IEnumerator LocalizationSceneReloadKeepsLanguageAndRefreshesStaticDynamicAndItemText()
    {
        yield return LoadScene(LocalizationScenePath);
        LocalizationManager manager = LocalizationManager.Instance;
        Assert.IsNotNull(manager);
        Assert.AreEqual(GameLanguage.Chinese, manager.CurrentLanguage);
        yield return PressF10();
        Assert.AreEqual(GameLanguage.English, manager.CurrentLanguage);
        AssertLocalizationExample(manager);

        yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneByPath(LocalizationScenePath));
        yield return LoadScene(LocalizationScenePath);
        Assert.AreSame(manager, LocalizationManager.Instance);
        Assert.AreEqual(GameLanguage.English, manager.CurrentLanguage);
        Assert.AreEqual(1, Object.FindObjectsByType<LocalizationManager>(FindObjectsSortMode.None).Length);
        AssertLocalizationExample(manager);
        yield return PressF10();
        Assert.AreEqual(GameLanguage.Chinese, manager.CurrentLanguage);
        AssertLocalizationExample(manager);
    }

    [UnityTest]
    public IEnumerator PlayerPanelSavedResultAndBusinessStateSurviveF10WhileUnpreparedLockedAndDead()
    {
        yield return LoadScene(PlayerScenePath);
        PlayerTestDriver driver = Object.FindFirstObjectByType<PlayerTestDriver>();
        Assert.IsNotNull(driver);
        Assert.IsTrue(driver.IsReady);
        LocalizationManager manager = LocalizationManager.Instance;
        Assert.IsNotNull(manager);
        Assert.IsFalse(driver.player.IsPrepared);
        string before = PlayerSnapshot(driver);
        yield return PressF10();
        Assert.AreEqual(GameLanguage.English, manager.CurrentLanguage);
        Assert.AreEqual(before, PlayerSnapshot(driver));

        driver.PrepareNextRound();
        driver.UseMedkit();
        ItemDataSO medkit = driver.itemManager.itemLibrary.FindDefinition("medkit");
        Assert.AreEqual(manager.FormatText("items.use.result", manager.GetText(medkit.nameKey),
            manager.GetText("items.use.full_health")), driver.LastResult);
        before = PlayerSnapshot(driver);
        yield return PressF10();
        Assert.AreEqual(before, PlayerSnapshot(driver));
        Assert.AreEqual(manager.FormatText("items.use.result", manager.GetText(medkit.nameKey),
            manager.GetText("items.use.full_health")), driver.LastResult);

        driver.ToggleBackpack();
        Assert.IsFalse(driver.player.CanAct);
        before = PlayerSnapshot(driver);
        yield return PressF10();
        Assert.AreEqual(GameLanguage.English, manager.CurrentLanguage);
        Assert.AreEqual(before, PlayerSnapshot(driver));

        driver.DamageLethal();
        Assert.IsTrue(driver.player.IsDead);
        before = PlayerSnapshot(driver);
        yield return PressF10();
        Assert.AreEqual(GameLanguage.Chinese, manager.CurrentLanguage);
        Assert.AreEqual(before, PlayerSnapshot(driver));
        Assert.AreEqual(manager.GetText("tests.player.result.lethal_damage"), driver.LastResult);
    }

    [UnityTest]
    public IEnumerator ItemsPanelPreservesFailureConsumedFoodAndNumericResultSnapshotsAcrossLanguageChanges()
    {
        yield return LoadScene(ItemsScenePath);
        ItemsTestDriver driver = Object.FindFirstObjectByType<ItemsTestDriver>();
        Assert.IsNotNull(driver);
        LocalizationManager manager = LocalizationManager.Instance;
        Assert.IsNotNull(manager);
        driver.health.Heal(driver.health.MaxHealth);
        driver.UseMedkit();
        string before = ItemsSnapshot(driver.itemManager) + ":" + driver.health.CurrentHealth;
        yield return PressF10();
        ItemDataSO medkit = driver.itemLibrary.FindDefinition("medkit");
        Assert.AreEqual(manager.FormatText("items.use.result", manager.GetText(medkit.nameKey),
            manager.GetText("items.use.full_health")), driver.LastResult);
        Assert.AreEqual(before, ItemsSnapshot(driver.itemManager) + ":" + driver.health.CurrentHealth);

        driver.DamageOne();
        int reportedHealth = driver.health.CurrentHealth;
        int reportedMax = driver.health.MaxHealth;
        driver.health.Heal(1);
        before = ItemsSnapshot(driver.itemManager) + ":" + driver.health.CurrentHealth;
        yield return PressF10();
        Assert.AreEqual(manager.FormatText("tests.items.result.damage", reportedHealth, reportedMax), driver.LastResult,
            "The result keeps its original numbers even when the current health later changes.");
        Assert.AreEqual(before, ItemsSnapshot(driver.itemManager) + ":" + driver.health.CurrentHealth);

        driver.DamageOne();
        driver.UseFirstFood();
        Assert.AreEqual(1, driver.itemManager.GetInstances().FindAll(item => item.Owner == ItemOwner.Consumed).Count);
        before = ItemsSnapshot(driver.itemManager) + ":" + driver.health.CurrentHealth;
        yield return PressF10();
        Assert.AreEqual(before, ItemsSnapshot(driver.itemManager) + ":" + driver.health.CurrentHealth);
        ItemDataSO food = driver.itemLibrary.FindDefinition("food");
        string label = manager.FormatText("tests.items.variant", manager.GetText(food.nameKey), "A");
        Assert.AreEqual(manager.FormatText("items.use.result", label, manager.GetText("items.use.success")), driver.LastResult);
    }

    [UnityTest]
    public IEnumerator RepeatedInitializationKeepsEnglishAndNewEntryStartsInChinese()
    {
        LocalizationManager original = CreateEntry();
        yield return null;
        original.SetLanguage(GameLanguage.English);
        original.Initialize();
        Assert.AreEqual(GameLanguage.English, original.CurrentLanguage);
        Object.Destroy(GameBootstrap.Instance.gameObject);
        yield return null;
        Assert.IsNull(LocalizationManager.Instance);
        LocalizationManager replacement = CreateEntry();
        yield return null;
        Assert.AreEqual(GameLanguage.Chinese, replacement.CurrentLanguage);
        yield return PressF10();
        Assert.AreEqual(GameLanguage.English, replacement.CurrentLanguage);
    }

    private LocalizationManager CreateEntry()
    {
        GameObject root = CreatePrefab(BootstrapPath);
        return CreatePrefab(ModulePath, root.transform).GetComponent<LocalizationManager>();
    }

    private GameObject CreatePrefab(string path, Transform parent = null)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, path);
        GameObject created = Object.Instantiate(prefab, parent);
        ownedObjects.Add(created);
        return created;
    }

    private IEnumerator LoadScene(string path)
    {
        Assert.IsFalse(IsLoaded(path), "Only scenes loaded by this test may be unloaded.");
        if (!ownedScenes.Contains(path)) { ownedScenes.Add(path); }
        yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
        if (GameBootstrap.Instance != null && !ownedObjects.Contains(GameBootstrap.Instance.gameObject))
        {
            ownedObjects.Add(GameBootstrap.Instance.gameObject);
        }
        yield return null;
    }

    private IEnumerator PressF10()
    {
        SetKeys(Key.F10);
        yield return null;
        yield return null;
        SetKeys();
        yield return null;
    }

    private void SetKeys(params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    }

    private static void AssertLocalizationExample(LocalizationManager manager)
    {
        LocalizationTestDriver example = Object.FindFirstObjectByType<LocalizationTestDriver>();
        Assert.IsNotNull(example);
        Assert.AreSame(manager, example.localizationManager);
        Assert.AreEqual(manager.FormatText("player.health.value", example.sampleCurrent, example.sampleMaximum), example.valueText.text);
        Assert.AreEqual(manager.GetText(example.item.nameKey), example.itemNameText.text);
        Assert.AreEqual(manager.GetText(example.item.descriptionKey), example.itemDescriptionText.text);
        Assert.AreEqual(manager.FormatText("tests.localization.sample_result", manager.GetText(example.item.nameKey),
            manager.GetText("items.use.full_health")), example.resultText.text);
        LocalizedText[] labels = Object.FindObjectsByType<LocalizedText>(FindObjectsSortMode.None);
        Assert.IsNotEmpty(labels);
        foreach (LocalizedText label in labels)
        {
            if (label.isActiveAndEnabled)
            {
                Assert.AreEqual(manager.GetText(label.key), label.targetText.text);
            }
        }
    }

    private static string PlayerSnapshot(PlayerTestDriver driver)
    {
        // 只比较业务数据；真实物理和时间照常继续，不把自然落地当作语言副作用。
        return ItemsSnapshot(driver.itemManager) + $"|HP:{driver.player.health.CurrentHealth}/{driver.player.health.MaxHealth}"
            + $"|Round:{driver.player.health.CurrentRound}|Prepared:{driver.player.IsPrepared}|Dead:{driver.player.IsDead}"
            + $"|Backpack:{driver.player.IsBackpackOpen}|CanAct:{driver.player.CanAct}";
    }

    private static string ItemsSnapshot(ItemManager manager)
    {
        List<string> entries = new List<string>();
        foreach (ItemInstance item in manager.GetInstances())
        {
            entries.Add($"{item.InstanceId}:{item.DefinitionId}:{item.UniqueId}:{item.Owner}:{item.ContainerId}"
                + $":{item.SourceId}:{item.IsRetained}:{item.UseRound}:{item.UsesThisRound}:{item.OriginalSpawnPointId}"
                + $":{item.RecoveryRound}:{item.LastLossReason}:{item.SpawnClaimed}");
        }
        entries.Sort(System.StringComparer.Ordinal);
        return manager.CurrentRound + "|" + manager.InstanceCount + "|" + string.Join(";", entries);
    }

    private static bool IsLoaded(string path)
    {
        Scene scene = SceneManager.GetSceneByPath(path);
        return scene.IsValid() && scene.isLoaded;
    }
}
#endif
