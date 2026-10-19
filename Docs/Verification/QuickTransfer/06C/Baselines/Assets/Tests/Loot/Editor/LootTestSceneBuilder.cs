using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 只生成06资产。共享Prefab只实例化和保存场景覆盖，不Apply回01—05资源。
public static class LootTestSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Tests/Loot/LootTest.unity";
    private const string ModulePath = "Assets/Prefabs/Loot/LootModule.prefab";
    private const string BoxPath = "Assets/Prefabs/Loot/LootBox.prefab";
    private const string GroundPath = "Assets/Prefabs/Loot/GroundItem.prefab";
    private const string PanelPath = "Assets/Prefabs/UI/Loot/LootPanel.prefab";
    private const string ItemPath = "Assets/Prefabs/UI/Loot/LootItem.prefab";
    private const string TextPath = "Assets/Data/Localization/Loot/LootText.asset";
    private const string FontPath = "Assets/Art/Fonts/Localization/NotoSansCJKsc-Regular SDF.asset";
    private const string InputPath = "Assets/Input/GameInputActions.inputactions";
    private static TMP_FontAsset font;

    [MenuItem("Tools/MyPrecious/Create Loot Test Assets")]
    public static void Build()
    {
        RequireSavedEditMode();
        foreach (string folder in new[] { "Assets/Data/LootTables", "Assets/Data/Localization/Loot", "Assets/Prefabs/Loot", "Assets/Prefabs/UI/Loot", "Assets/Scenes/Tests/Loot" })
        {
            Directory.CreateDirectory(folder);
        }
        AssetDatabase.Refresh();
        font = Required<TMP_FontAsset>(FontPath);
        ItemLibrarySO library = Required<ItemLibrarySO>("Assets/Data/Items/ItemLibrary.asset");
        LocalizationTableSO text = AssetDatabase.LoadAssetAtPath<LocalizationTableSO>(TextPath);
        if (text == null)
        {
            text = ScriptableObject.CreateInstance<LocalizationTableSO>();
            AssetDatabase.CreateAsset(text, TextPath);
        }
        LootTextEntries.Fill(text);
        EditorUtility.SetDirty(text);
        CreateTable("Zero", library, new[] { "food", "battery" }, new[] { 0f, 0f });
        CreateTable("Guaranteed", library, new[] { "battery", "food", "battery" }, new[] { 1f, 1f, 1f });
        CreateTable("Mixed", library, new[] { "food", "battery", "food" }, new[] { 0.25f, 0.5f, 0.75f });
        CreateTable("Empty", library, Array.Empty<string>(), Array.Empty<float>());
        CreateTable("Unique", library, new[] { "doll" }, new[] { 1f });
        CreateWorldPrefabs();
        CreateItemPrefab();
        CreatePanelPrefab();
        CreateModulePrefab();
        if (!File.Exists(ScenePath)) { CreateScene(text, library); }
        AssetDatabase.SaveAssets();
        Debug.Log("LootTest assets ready. Existing assets and scene edits are preserved: " + ScenePath);
    }

    [MenuItem("Tools/MyPrecious/Upgrade Loot Grid UI")]
    public static void UpgradeGridUI()
    {
        RequireSavedEditMode();
        font = Required<TMP_FontAsset>(FontPath);
        LocalizationTableSO text = Required<LocalizationTableSO>(TextPath);
        // 原位更新06自己的UI Prefab；不删除重建资产，不Apply共享Prefab或正式场景。
        GameObject itemRoot = PrefabUtility.LoadPrefabContents(ItemPath);
        try
        {
            ConfigureItemPrefab(itemRoot.GetComponent<LootItemView>());
            PrefabUtility.SaveAsPrefabAsset(itemRoot, ItemPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(itemRoot); }
        GameObject panelRoot = PrefabUtility.LoadPrefabContents(PanelPath);
        try
        {
            ConfigurePanelPrefab(panelRoot.GetComponent<LootView>());
            PrefabUtility.SaveAsPrefabAsset(panelRoot, PanelPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(panelRoot); }
        LootTextEntries.Fill(text);
        EditorUtility.SetDirty(text);
        AssetDatabase.SaveAssets();
        Debug.Log("Loot grid UI upgraded in place. Scene overrides are preserved.");
    }

    [MenuItem("Tools/MyPrecious/Upgrade Loot Test Layout")]
    public static void UpgradeTestScene()
    {
        RequireSavedEditMode();
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedHere = !scene.IsValid() || !scene.isLoaded;
        if (openedHere) { scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); }
        try
        {
            LootTestDriver driver = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<LootTestDriver>(true)).Single();
            ConfigureTestSceneLayout(driver);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) { throw new InvalidOperationException("Could not save the LootTest display upgrade."); }
        }
        finally
        {
            if (openedHere) { EditorSceneManager.CloseScene(scene, true); }
            if (previous.IsValid() && previous.isLoaded) { SceneManager.SetActiveScene(previous); }
        }
        Debug.Log("LootTest display upgraded to 1920 x 1080. Gameplay objects and module references were preserved.");
    }

    private static void ConfigureTestSceneLayout(LootTestDriver driver)
    {
        BackpackView backpack = driver.backpackUI.view;
        WarehouseView warehouse = driver.warehouseUI.view;
        LootView loot = driver.lootUI.view;
        Canvas canvas = backpack.GetComponentInParent<Canvas>();
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;
        backpack.cellSize = 66f;
        backpack.gap = 4f;
        backpack.padding = 6f;
        SetRect(backpack.transform.Find("PlayerInventory"), 24, 132, 360, 580);
        SetRect(backpack.gridRoot, 36, 62, 288, 288);
        SetRect(backpack.itemRoot, 36, 62, 288, 288);
        backpack.gridRoot.localScale = Vector3.one;
        backpack.itemRoot.localScale = Vector3.one;
        FitWarehouseBesideBackpack(warehouse);
        // 原LootTest把Prefab根尺寸保存为场景覆盖，只升级Prefab不会消除旧420×580外框。
        RectTransform lootPanel = (RectTransform)loot.transform;
        lootPanel.anchorMin = lootPanel.anchorMax = lootPanel.pivot = Vector2.one;
        lootPanel.anchoredPosition = new Vector2(-24f, -132f);
        lootPanel.sizeDelta = new Vector2(484f, 808f);
        lootPanel.localScale = Vector3.one;
        loot.cellSize = 66f;
        loot.gap = 4f;
        loot.padding = 6f;
        SetRect(driver.statusText.transform, 24, 10, 1872, 28);
        SetRect(driver.contextText.transform, 24, 42, 1872, 42);
        Transform scope = canvas.transform.Find("Scope");
        if (scope != null) { SetRect(scope, 24, 1032, 1872, 32); }
        foreach (Object component in new Object[] { scaler, backpack, warehouse, loot })
        {
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
        foreach (RectTransform rect in canvas.GetComponentsInChildren<RectTransform>(true))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
        }
    }

    private static void RequireSavedEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { throw new InvalidOperationException("Stop Play before building Loot UI."); }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty) { throw new InvalidOperationException("Save the dirty scene before building Loot UI."); }
        }
    }

    private static void CreateTable(string name, ItemLibrarySO library, string[] definitions, float[] probabilities)
    {
        string path = "Assets/Data/LootTables/" + name + ".asset";
        if (AssetDatabase.LoadAssetAtPath<LootTableSO>(path) != null) { return; }
        LootTableSO table = ScriptableObject.CreateInstance<LootTableSO>();
        for (int i = 0; i < definitions.Length; i++)
        {
            ItemDataSO definition = library.FindDefinition(definitions[i]);
            if (definition == null || !definition.TryValidate(out string error))
            {
                throw new InvalidOperationException("Invalid LootTest definition: " + definitions[i]);
            }
            table.entries.Add(new LootTableEntry { entryId = "slot-" + (i + 1), item = definition, useDefaultProbability = false, overrideProbability = probabilities[i] });
        }
        AssetDatabase.CreateAsset(table, path);
    }

    private static void CreateWorldPrefabs()
    {
        if (!File.Exists(BoxPath))
        {
            GameObject root = Shape("LootBox", Vector3.zero, new Vector3(0.9f, 0.85f, 0.75f), "TestTarget");
            root.GetComponent<Collider>().isTrigger = true;
            LootBox box = root.AddComponent<LootBox>();
            box.mapId = "loot-test";
            box.sourceId = "replace-with-stable-source-id";
            box.lootTable = Required<LootTableSO>("Assets/Data/LootTables/Guaranteed.asset");
            PrefabUtility.SaveAsPrefabAsset(root, BoxPath);
            Object.DestroyImmediate(root);
        }
        if (!File.Exists(GroundPath))
        {
            GameObject root = new GameObject("GroundItem");
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = Vector3.one * 0.36f;
            collider.isTrigger = true;
            GameObject visual = Shape("Visual", Vector3.zero, Vector3.one * 0.36f, "TestTarget");
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            root.AddComponent<GroundItem>();
            PrefabUtility.SaveAsPrefabAsset(root, GroundPath);
            Object.DestroyImmediate(root);
        }
    }

    private static void CreateModulePrefab()
    {
        if (File.Exists(ModulePath)) { return; }
        GameObject root = new GameObject("LootModule");
        root.AddComponent<LootRuntimeManager>().groundItemPrefab = Required<GameObject>(GroundPath).GetComponent<GroundItem>();
        PrefabUtility.SaveAsPrefabAsset(root, ModulePath);
        Object.DestroyImmediate(root);
    }

    private static void CreateItemPrefab()
    {
        if (File.Exists(ItemPath)) { return; }
        GameObject root = Rect(null, "LootItem", 0, 0, 66, 66).gameObject;
        LootItemView item = root.AddComponent<LootItemView>();
        ConfigureItemPrefab(item);
        PrefabUtility.SaveAsPrefabAsset(root, ItemPath);
        Object.DestroyImmediate(root);
    }

    private static void ConfigureItemPrefab(LootItemView item)
    {
        if (item == null) { throw new InvalidOperationException("LootItem prefab needs its LootItemView."); }
        GameObject root = item.gameObject;
        SetRect(root.transform, 0, 0, 66, 66);
        root.transform.localScale = Vector3.one;
        LayoutElement oldLayout = root.GetComponent<LayoutElement>();
        if (oldLayout != null) { Object.DestroyImmediate(oldLayout); }
        if (item.background == null) { item.background = root.GetComponent<Image>(); }
        if (item.background == null) { item.background = root.AddComponent<Image>(); }
        item.background.color = Color.clear;
        item.background.raycastTarget = true;
        if (item.icon == null) { item.icon = Rect(root.transform, "Icon", 6, 6, 54, 54).gameObject.AddComponent<Image>(); }
        StretchInside(item.icon.rectTransform, 6f);
        item.icon.color = new Color(0.72f, 0.64f, 0.45f, 0.16f);
        item.icon.raycastTarget = false;
        item.icon.gameObject.SetActive(false);
        if (item.nameText == null) { item.nameText = Text(root.transform, "Name", 18, 6, 6, 54, 54); }
        StretchInside(item.nameText.rectTransform, 6f);
        item.nameText.font = font;
        item.nameText.fontSize = 18f;
        item.nameText.fontSizeMin = 12f;
        item.nameText.fontSizeMax = 18f;
        item.nameText.enableAutoSizing = true;
        item.nameText.alignment = TextAlignmentOptions.Center;
        item.nameText.textWrappingMode = TextWrappingModes.Normal;
        item.nameText.overflowMode = TextOverflowModes.Ellipsis;
        item.nameText.raycastTarget = false;
        item.nameText.text = string.Empty;
        if (item.descriptionText == null) { item.descriptionText = Text(root.transform, "Description", 15, 0, 0, 66, 28); }
        if (item.statusText == null) { item.statusText = Text(root.transform, "Status", 14, 0, 0, 66, 24); }
        item.descriptionText.text = string.Empty;
        item.descriptionText.raycastTarget = false;
        item.descriptionText.gameObject.SetActive(false);
        item.statusText.text = string.Empty;
        item.statusText.raycastTarget = false;
        item.statusText.gameObject.SetActive(false);
        if (item.countdownText == null) { item.countdownText = Text(root.transform, "Countdown", 22, 0, 0, 58, 28); }
        RectTransform countdown = item.countdownText.rectTransform;
        countdown.anchorMin = new Vector2(0f, 0.5f);
        countdown.anchorMax = new Vector2(1f, 0.5f);
        countdown.pivot = new Vector2(0.5f, 0.5f);
        countdown.anchoredPosition = Vector2.zero;
        countdown.sizeDelta = new Vector2(-8f, 32f);
        item.countdownText.font = font;
        item.countdownText.fontSize = 22f;
        item.countdownText.fontSizeMin = 12f;
        item.countdownText.fontSizeMax = 22f;
        item.countdownText.enableAutoSizing = true;
        item.countdownText.alignment = TextAlignmentOptions.Center;
        item.countdownText.textWrappingMode = TextWrappingModes.NoWrap;
        item.countdownText.raycastTarget = false;
        item.countdownText.text = string.Empty;
        item.countdownText.gameObject.SetActive(false);
        item.outline = new[]
        {
            OutlineEdge(root.transform, "OutlineTop", new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), new Vector2(0, 1)),
            OutlineEdge(root.transform, "OutlineBottom", Vector2.zero, new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 1)),
            OutlineEdge(root.transform, "OutlineLeft", Vector2.zero, new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(1, 0)),
            OutlineEdge(root.transform, "OutlineRight", new Vector2(1, 0), Vector2.one, new Vector2(1, 0.5f), new Vector2(1, 0))
        };
    }

    private static Image OutlineEdge(Transform parent, string name, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size)
    {
        Transform existing = parent.Find(name);
        RectTransform rect = existing != null ? (RectTransform)existing : Rect(parent, name, 0, 0, 0, 0);
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = pivot;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        Image edge = rect.GetComponent<Image>();
        if (edge == null) { edge = rect.gameObject.AddComponent<Image>(); }
        edge.color = new Color(0.57f, 0.59f, 0.60f, 1f);
        edge.raycastTarget = false;
        return edge;
    }

    private static void StretchInside(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.one * inset;
        rect.offsetMax = Vector2.one * -inset;
    }

    private static void CreatePanelPrefab()
    {
        if (File.Exists(PanelPath)) { return; }
        GameObject root = Rect(null, "LootPanel", 0, 132, 484, 808).gameObject;
        LootView view = root.AddComponent<LootView>();
        ConfigurePanelPrefab(view);
        PrefabUtility.SaveAsPrefabAsset(root, PanelPath);
        Object.DestroyImmediate(root);
    }

    private static void ConfigurePanelPrefab(LootView view)
    {
        if (view == null) { throw new InvalidOperationException("LootPanel prefab needs its LootView."); }
        GameObject root = view.gameObject;
        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = Vector2.one;
        rootRect.anchoredPosition = new Vector2(-24f, -132f);
        rootRect.sizeDelta = new Vector2(484f, 808f);
        rootRect.localScale = Vector3.one;
        Image panelBackground = root.GetComponent<Image>();
        if (panelBackground == null) { panelBackground = root.AddComponent<Image>(); }
        panelBackground.color = new Color(0.06f, 0.11f, 0.13f, 0.98f);
        view.panelRoot = root;
        view.cellSize = 66f;
        view.gap = 4f;
        view.padding = 6f;
        if (view.titleText == null) { view.titleText = Text(root.transform, "Title", 26, 20, 14, 264, 40); }
        SetRect(view.titleText.transform, 20, 14, 264, 40);
        if (view.closeButton == null) { view.closeButton = Button(root.transform, "Close", 304, 16, 160, 38, null, out view.closeText); }
        SetRect(view.closeButton.transform, 304, 16, 160, 38);
        if (view.closeText == null) { view.closeText = view.closeButton.GetComponentInChildren<TMP_Text>(true); }
        if (view.closeText != null) { SetRect(view.closeText.transform, 6, 3, 148, 32); }
        if (view.statusText == null) { view.statusText = Text(root.transform, "Status", 18, 20, 64, 428, 56); }
        SetRect(view.statusText.transform, 20, 64, 428, 56);
        if (view.progressText == null) { view.progressText = Text(root.transform, "Progress", 18, 0, 0, 0, 0); }
        // 旧字段保留，06A只在当前物品中央展示倒计时。
        view.progressText.gameObject.SetActive(false);
        if (view.progressBar != null) { view.progressBar.gameObject.SetActive(false); }
        Transform existingScroll = root.transform.Find("EntriesScroll");
        RectTransform scrollRoot = existingScroll != null ? (RectTransform)existingScroll : Rect(root.transform, "EntriesScroll", 20, 132, 428, 584);
        SetRect(scrollRoot, 20, 132, 428, 584);
        ScrollRect scroll = scrollRoot.GetComponent<ScrollRect>();
        if (scroll == null) { scroll = scrollRoot.gameObject.AddComponent<ScrollRect>(); }
        Image target = scrollRoot.GetComponent<Image>();
        if (target == null) { target = scrollRoot.gameObject.AddComponent<Image>(); }
        target.color = new Color(0.025f, 0.055f, 0.065f);
        target.raycastTarget = true;
        if (scrollRoot.GetComponent<RectMask2D>() == null) { scrollRoot.gameObject.AddComponent<RectMask2D>(); }
        if (view.contentRoot == null) { view.contentRoot = Rect(scrollRoot, "Content", 0, 0, 428, 584); }
        SetRect(view.contentRoot, 0, 0, 428, 584);
        if (view.gridRoot == null || !view.gridRoot.IsChildOf(view.contentRoot))
        {
            Transform existingGrid = view.contentRoot.Find("Grid");
            view.gridRoot = existingGrid != null ? (RectTransform)existingGrid : Rect(view.contentRoot, "Grid", 0, 0, 428, 584);
        }
        view.gridRoot.SetParent(view.contentRoot, false);
        SetRect(view.gridRoot, 0, 0, 428, 584);
        view.gridRoot.localScale = Vector3.one;
        view.gridRoot.SetAsFirstSibling();
        Image gridImage = view.gridRoot.GetComponent<Image>();
        if (gridImage != null) { gridImage.raycastTarget = false; }
        if (view.cellPrefab == null || !view.cellPrefab.transform.IsChildOf(view.contentRoot))
        {
            Transform existingCell = view.contentRoot.Find("CellTemplate");
            RectTransform cell = existingCell != null ? (RectTransform)existingCell : Rect(view.contentRoot, "CellTemplate", 6, 6, 66, 66);
            view.cellPrefab = cell.GetComponent<Image>();
            if (view.cellPrefab == null) { view.cellPrefab = cell.gameObject.AddComponent<Image>(); }
        }
        // 模板独立隐藏，运行时背景格放入Grid；物品仍是Content直属子对象。
        view.cellPrefab.transform.SetParent(view.contentRoot, false);
        SetRect(view.cellPrefab.transform, 6, 6, 66, 66);
        view.cellPrefab.transform.localScale = Vector3.one;
        view.cellPrefab.color = new Color(0.22f, 0.20f, 0.15f, 1f);
        view.cellPrefab.raycastTarget = false;
        view.cellPrefab.gameObject.SetActive(false);
        LayoutGroup oldLayout = view.contentRoot.GetComponent<LayoutGroup>();
        if (oldLayout != null) { Object.DestroyImmediate(oldLayout); }
        ContentSizeFitter oldFit = view.contentRoot.GetComponent<ContentSizeFitter>();
        if (oldFit != null) { Object.DestroyImmediate(oldFit); }
        scroll.viewport = scrollRoot;
        scroll.content = view.contentRoot;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.inertia = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 70f;
        scroll.verticalScrollbar = ConfigureScrollbar(root.transform, scroll.verticalScrollbar, false);
        scroll.horizontalScrollbar = ConfigureScrollbar(root.transform, scroll.horizontalScrollbar, true);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        scroll.verticalNormalizedPosition = 1f;
        scroll.horizontalNormalizedPosition = 0f;
        view.viewport = scrollRoot;
        view.returnTarget = target;
        view.itemPrefab = Required<GameObject>(ItemPath).GetComponent<LootItemView>();
        if (view.feedbackText == null) { view.feedbackText = Text(root.transform, "Feedback", 17, 20, 744, 444, 48); }
        SetRect(view.feedbackText.transform, 20, 744, 444, 56);
        foreach (TMP_Text label in view.GetComponentsInChildren<TMP_Text>(true))
        {
            label.font = font;
            label.raycastTarget = false;
        }
    }

    private static Scrollbar ConfigureScrollbar(Transform parent, Scrollbar existing, bool horizontal)
    {
        string name = horizontal ? "EntriesHorizontalScrollbar" : "EntriesVerticalScrollbar";
        Scrollbar bar = existing;
        if (bar == null)
        {
            Transform previous = parent.Find(name);
            RectTransform rect = previous != null ? (RectTransform)previous : Rect(parent, name, 0, 0, 0, 0);
            bar = rect.GetComponent<Scrollbar>();
            if (bar == null) { bar = rect.gameObject.AddComponent<Scrollbar>(); }
        }
        bar.transform.SetParent(parent, false);
        SetRect(bar.transform, horizontal ? 20 : 452, horizontal ? 720 : 132, horizontal ? 428 : 12, horizontal ? 12 : 584);
        Image background = bar.GetComponent<Image>();
        if (background == null) { background = bar.gameObject.AddComponent<Image>(); }
        background.color = new Color(0.14f, 0.20f, 0.22f);
        if (bar.handleRect == null)
        {
            RectTransform handle = Rect(bar.transform, "Handle", 0, 0, 12, 70);
            Image image = handle.gameObject.AddComponent<Image>();
            image.color = new Color(0.48f, 0.57f, 0.59f);
            bar.handleRect = handle;
            bar.targetGraphic = image;
        }
        // Scrollbar用anchor表示手柄长度；额外sizeDelta会使手柄伸出轨道。
        bar.handleRect.anchorMin = Vector2.zero;
        bar.handleRect.anchorMax = Vector2.one;
        bar.handleRect.pivot = new Vector2(0.5f, 0.5f);
        bar.handleRect.offsetMin = Vector2.zero;
        bar.handleRect.offsetMax = Vector2.zero;
        bar.direction = horizontal ? Scrollbar.Direction.LeftToRight : Scrollbar.Direction.BottomToTop;
        bar.numberOfSteps = 0;
        bar.SetValueWithoutNotify(horizontal ? 0f : 1f);
        bar.gameObject.SetActive(!horizontal);
        return bar;
    }

    private static void CreateScene(LocalizationTableSO table, ItemLibrarySO library)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            GameObject bootstrap = Instantiate("Assets/Prefabs/Bootstrap/GameBootstrap.prefab");
            ItemManager items = bootstrap.GetComponentInChildren<ItemManager>(true);
            if (items == null) { items = Instantiate("Assets/Prefabs/Items/ItemModule.prefab", bootstrap.transform).GetComponent<ItemManager>(); }
            BackpackManager backpack = Instantiate("Assets/Prefabs/Backpack/BackpackModule.prefab", bootstrap.transform).GetComponent<BackpackManager>();
            WarehouseManager warehouse = Instantiate("Assets/Prefabs/Warehouse/WarehouseModule.prefab", bootstrap.transform).GetComponent<WarehouseManager>();
            ShelterManager shelter = Instantiate("Assets/Prefabs/Shelter/ShelterModule.prefab", bootstrap.transform).GetComponent<ShelterManager>();
            LocalizationManager language = bootstrap.GetComponentInChildren<LocalizationManager>(true);
            if (language == null) { language = Instantiate("Assets/Prefabs/Localization/LocalizationModule.prefab", bootstrap.transform).GetComponent<LocalizationManager>(); }
            LootRuntimeManager runtime = Instantiate(ModulePath, bootstrap.transform).GetComponent<LootRuntimeManager>();
            backpack.itemManager = items;
            warehouse.itemManager = items;
            shelter.itemManager = items;
            shelter.warehouseManager = warehouse;
            runtime.itemManager = items;
            foreach (string path in new[] { "Backpack/BackpackText", "Warehouse/WarehouseText", "Shelter/ShelterText" })
            {
                LocalizationTableSO existing = Required<LocalizationTableSO>("Assets/Data/Localization/" + path + ".asset");
                if (!language.tables.Contains(existing)) { language.tables.Add(existing); }
            }
            if (!language.tables.Contains(table)) { language.tables.Add(table); }
            foreach (Object component in new Object[] { backpack, warehouse, shelter, language, runtime })
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
            PlayerController player = Instantiate("Assets/Prefabs/Player/Player.prefab").GetComponent<PlayerController>();
            player.transform.position = new Vector3(-8, 0.05f, 0);
            PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
            PlayerCameraTargetBinder camera = Instantiate("Assets/Prefabs/Player/PlayerCamera.prefab").GetComponent<PlayerCameraTargetBinder>();
            camera.player = player;
            PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
            Shape("Ground3D", new Vector3(0, -0.5f, 0), new Vector3(44, 1, 4), "WhiteboxGround");
            Shape("ObstructionWall3D", new Vector3(12, 1.5f, 0), new Vector3(0.5f, 3, 2), "WhiteboxGround");
            GameObject moving = Shape("WorldContinuesMoving", new Vector3(-8, 3.4f, 1), Vector3.one * 0.45f, "TestTarget");
            Object.DestroyImmediate(moving.GetComponent<Collider>());
            GameObject damage = Shape("RealDamageTrigger", new Vector3(10, 0.2f, 0), new Vector3(1.3f, 0.4f, 2), "TestDamage");
            damage.GetComponent<Collider>().isTrigger = true;
            damage.AddComponent<PlayerTestDamageSource>();
            Light light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(40, -25, 0);
            RenderSettings.ambientLight = new Color(0.5f, 0.55f, 0.65f);

            Canvas canvas = new GameObject("Loot Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            // 当前验收基准1920；同屏背包、仓库和箱子都使用66/4/6显示单位。
            scaler.matchWidthOrHeight = 0f;
            BackpackView backpackView = Instantiate("Assets/Prefabs/UI/Backpack/BackpackPanel.prefab", canvas.transform).GetComponent<BackpackView>();
            Transform oldPreview = backpackView.transform.Find("LootContainerPreview");
            if (oldPreview != null) { oldPreview.gameObject.SetActive(false); }
            WarehouseView warehouseView = Instantiate("Assets/Prefabs/UI/Warehouse/WarehousePanel.prefab", canvas.transform).GetComponent<WarehouseView>();
            FitWarehouseBesideBackpack(warehouseView);
            LootView lootView = Instantiate(PanelPath, canvas.transform).GetComponent<LootView>();
            BackpackUIController backpackUI = new GameObject("Backpack Input Controller").AddComponent<BackpackUIController>();
            backpackUI.manager = backpack;
            backpackUI.player = player;
            backpackUI.view = backpackView;
            backpackUI.inputActions = Required<InputActionAsset>(InputPath);
            WarehouseUIController warehouseUI = new GameObject("Warehouse Input Controller").AddComponent<WarehouseUIController>();
            warehouseUI.manager = warehouse;
            warehouseUI.view = warehouseView;
            warehouseUI.localization = language;
            warehouseUI.inputActions = Required<InputActionAsset>(InputPath);
            ItemTransferCoordinator coordinator = new GameObject("Item Transfer Coordinator").AddComponent<ItemTransferCoordinator>();
            coordinator.itemManager = items;
            coordinator.backpackManager = backpack;
            coordinator.warehouseManager = warehouse;
            coordinator.shelterManager = shelter;
            coordinator.lootRuntime = runtime;
            coordinator.player = player;
            LootUIController lootUI = new GameObject("Loot Session Controller").AddComponent<LootUIController>();
            ItemTransferUIBridge bridge = new GameObject("Item Transfer UI Bridge").AddComponent<ItemTransferUIBridge>();
            bridge.coordinator = coordinator;
            bridge.backpack = backpackUI;
            bridge.warehouse = warehouseUI;
            bridge.loot = lootUI;
            // 与顶部测试按钮同排，仍归背包显隐；保留04已经验收的详情、反馈和三行操作提示空间。
            BackpackDiscardUIBuilder.Attach(bridge, font);
            lootUI.backpack = backpackUI;
            lootUI.runtime = runtime;
            lootUI.transfers = bridge;
            lootUI.view = lootView;
            lootUI.localization = language;
            LootTestDriver driver = new GameObject("Loot Test Driver - simulates 08").AddComponent<LootTestDriver>();
            driver.player = player;
            driver.items = items;
            driver.backpack = backpack;
            driver.warehouse = warehouse;
            driver.shelter = shelter;
            driver.runtime = runtime;
            driver.backpackUI = backpackUI;
            driver.warehouseUI = warehouseUI;
            driver.lootUI = lootUI;
            driver.coordinator = coordinator;
            driver.movingObject = moving.transform;
            driver.statusText = Text(canvas.transform, "TestStatus", 19, 24, 10, 1232, 28);
            driver.contextText = Text(canvas.transform, "Context", 18, 24, 42, 1232, 42);
            TMP_Text title = Text(canvas.transform, "Scope", 16, 24, 690, 1232, 26);
            Localized(title, "loot.test.title");
            Button shelterButton = Button(canvas.transform, "SimulateShelter", 410, 86, 148, 34, "loot.test.enter_shelter", out _);
            Button explorationButton = Button(canvas.transform, "SimulateExploration", 568, 86, 148, 34, "loot.test.enter_exploration", out _);
            Button roundButton = Button(canvas.transform, "Simulate08NextRound", 726, 86, 252, 34, "loot.test.next_round", out _);
            Button hurtButton = Button(canvas.transform, "DamageOne", 988, 86, 126, 34, "loot.test.damage", out _);
            Button lethalButton = Button(canvas.transform, "DamageLethal", 1124, 86, 132, 34, "loot.test.lethal", out _);
            UnityEventTools.AddPersistentListener(shelterButton.onClick, driver.EnterShelter);
            UnityEventTools.AddPersistentListener(explorationButton.onClick, driver.EnterExploration);
            UnityEventTools.AddPersistentListener(roundButton.onClick, driver.SimulateNextRound);
            UnityEventTools.AddPersistentListener(hurtButton.onClick, driver.DamageOne);
            UnityEventTools.AddPersistentListener(lethalButton.onClick, driver.DamageLethal);
            string[] tables = { "Guaranteed", "Zero", "Mixed", "Empty", "Unique" };
            string[] sources = { "guaranteed", "zero", "mixed", "empty", "unique-doll" };
            for (int i = 0; i < tables.Length; i++)
            {
                LootBox box = Instantiate(BoxPath).GetComponent<LootBox>();
                box.name = "Box_" + tables[i];
                box.transform.position = new Vector3(-6.8f + i * 3.5f, 0.43f, 0);
                box.sourceId = sources[i];
                box.lootTable = Required<LootTableSO>("Assets/Data/LootTables/" + tables[i] + ".asset");
                box.runtimeManager = runtime;
                box.lootUI = lootUI;
                PrefabUtility.RecordPrefabInstancePropertyModifications(box);
                PrefabUtility.RecordPrefabInstancePropertyModifications(box.transform);
                TMP_Text boxLabel = new GameObject("Label_" + sources[i]).AddComponent<TextMeshPro>();
                boxLabel.font = font;
                boxLabel.fontSize = 2.1f;
                boxLabel.alignment = TextAlignmentOptions.Center;
                boxLabel.rectTransform.sizeDelta = new Vector2(3.3f, 0.6f);
                boxLabel.transform.position = box.transform.position + new Vector3(0, 1.05f, -0.15f);
                Localized(boxLabel, "loot.test.box." + sources[i]);
            }
            CreateGround("ground-battery", library.FindDefinition("battery"), new Vector3(-9.3f, 0.2f, 0), runtime, lootUI);
            CreateGround("medical-origin", library.FindDefinition("medkit"), new Vector3(-11.4f, 0.2f, 0), runtime, lootUI);
            foreach (Object component in new Object[] { backpackView, warehouseView }) { PrefabUtility.RecordPrefabInstancePropertyModifications(component); }
            CreateEventSystem();
            ConfigureTestSceneLayout(driver);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) { SceneManager.SetActiveScene(previous); }
        }
    }

    private static void CreateGround(string id, ItemDataSO item, Vector3 position, LootRuntimeManager runtime, LootUIController ui)
    {
        GroundItem ground = Instantiate(GroundPath).GetComponent<GroundItem>();
        ground.name = "Ground_" + id;
        ground.mapId = "loot-test";
        ground.sourceId = id;
        ground.entryId = "item";
        ground.initialItem = item;
        ground.runtimeManager = runtime;
        ground.lootUI = ui;
        ground.transform.position = position;
        PrefabUtility.RecordPrefabInstancePropertyModifications(ground);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ground.transform);
    }

    private static void FitWarehouseBesideBackpack(WarehouseView view)
    {
        view.cellSize = 66f;
        view.gap = 4f;
        view.padding = 6f;
        SetRect(view.transform, 408, 132, 1488, 720);
        SetRect(view.titleText.transform, 18, 12, 220, 48);
        SetRect(view.sizeText.transform, 300, 20, 1000, 38);
        SetRect(view.closeButton.transform, 1344, 12, 124, 44);
        SetRect(view.scrollRect.transform, 18, 74, 1452, 440);
        SetRect(view.viewport, 0, 0, 1430, 440);
        SetRect(view.scrollRect.verticalScrollbar.transform, 1434, 0, 18, 440);
        view.scrollRect.horizontal = false;
        view.scrollRect.vertical = true;
        if (view.scrollRect.horizontalScrollbar != null) { view.scrollRect.horizontalScrollbar.gameObject.SetActive(false); }
        view.gridRoot.localScale = Vector3.one;
        view.itemRoot.localScale = Vector3.one;
        Transform details = view.transform.Find("DetailsScroll");
        if (details != null)
        {
            SetRect(details, 18, 530, 1076, 84);
            ScrollRect detailScroll = details.GetComponent<ScrollRect>();
            if (detailScroll != null && detailScroll.viewport != null && detailScroll.viewport != details)
            {
                SetRect(detailScroll.viewport, 0, 0, 1060, 84);
            }
            if (detailScroll != null && detailScroll.verticalScrollbar != null)
            {
                SetRect(detailScroll.verticalScrollbar.transform, 1068, 0, 8, 84);
            }
        }
        SetRect(view.detailsText.transform, 6, 0, 1048, 84);
        SetRect(view.abandonButton.transform, 1114, 530, 354, 40);
        SetRect(view.cancelDragButton.transform, 1114, 578, 354, 36);
        SetRect(view.hintText.transform, 18, 632, 1452, 52);
        SetRect(view.feedbackText.transform, 18, 688, 1452, 28);
        if (view.confirmPanel != null)
        {
            SetRect(view.confirmPanel.transform, 0, 0, 1488, 720);
            SetRect(view.confirmTitleText.transform, 24, 20, 1440, 44);
            SetRect(view.confirmMessageText.transform, 24, 84, 1440, 500);
            SetRect(view.confirmButton.transform, 24, 620, 710, 64);
            SetRect(view.cancelButton.transform, 754, 620, 710, 64);
        }
        foreach (RectTransform rect in view.GetComponentsInChildren<RectTransform>(true)) { PrefabUtility.RecordPrefabInstancePropertyModifications(rect); }
    }

    private static void CreateEventSystem()
    {
        InputSystemUIInputModule input = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).GetComponent<InputSystemUIInputModule>();
        input.actionsAsset = Required<InputActionAsset>(InputPath);
        input.point = Action("Point");
        input.leftClick = Action("Click");
        input.rightClick = Action("RightClick");
        input.middleClick = Action("MiddleClick");
        input.scrollWheel = Action("ScrollWheel");
        input.move = Action("Navigate");
        input.submit = Action("Submit");
        input.cancel = Action("Cancel");
    }

    private static InputActionReference Action(string name)
    {
        return AssetDatabase.LoadAllAssetsAtPath(InputPath).OfType<InputActionReference>().First(reference => reference.action.actionMap.name == "UI" && reference.action.name == name);
    }

    private static T Required<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) { throw new InvalidOperationException("Missing required asset: " + path); }
        return asset;
    }

    private static GameObject Instantiate(string path, Transform parent = null)
    {
        return (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>(path), parent);
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetRect(rect, x, y, width, height);
        return rect;
    }

    private static void SetRect(Transform target, float x, float y, float width, float height)
    {
        RectTransform rect = (RectTransform)target;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static TMP_Text Text(Transform parent, string name, int size, float x, float y, float width, float height)
    {
        TMP_Text text = Rect(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.color = new Color(0.92f, 0.96f, 0.98f);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private static Button Button(Transform parent, string name, float x, float y, float width, float height, string key, out TMP_Text label)
    {
        GameObject root = Rect(parent, name, x, y, width, height).gameObject;
        Image image = root.AddComponent<Image>();
        image.color = new Color(0.19f, 0.34f, 0.4f);
        Button button = root.AddComponent<Button>();
        button.targetGraphic = image;
        label = Text(root.transform, "Label", 17, 6, 3, width - 12, height - 6);
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 12;
        label.fontSizeMax = 17;
        if (!string.IsNullOrEmpty(key)) { Localized(label, key); }
        return button;
    }

    private static void Localized(TMP_Text text, string key)
    {
        LocalizedText localized = text.gameObject.AddComponent<LocalizedText>();
        localized.targetText = text;
        localized.key = key;
    }

    private static GameObject Shape(string name, Vector3 position, Vector3 scale, string material)
    {
        GameObject shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shape.name = name;
        shape.transform.position = position;
        shape.transform.localScale = scale;
        shape.GetComponent<Renderer>().sharedMaterial = Required<Material>("Assets/Art/Materials/Player/" + material + ".mat");
        return shape;
    }
}
