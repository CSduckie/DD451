using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 仅生成05资源，不Apply共享Prefab，不改Build Settings；已有资源保留GUID和人工调参。
public static class WarehouseTestSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Tests/Warehouse/WarehouseTest.unity";
    private const string WarehouseModule = "Assets/Prefabs/Warehouse/WarehouseModule.prefab";
    private const string ShelterModule = "Assets/Prefabs/Shelter/ShelterModule.prefab";
    private const string WarehousePanel = "Assets/Prefabs/UI/Warehouse/WarehousePanel.prefab";
    private const string ShelterPanel = "Assets/Prefabs/UI/Shelter/ShelterPanel.prefab";
    private const string CellPath = "Assets/Prefabs/UI/Warehouse/WarehouseCell.prefab";
    private const string ItemPath = "Assets/Prefabs/UI/Warehouse/WarehouseItem.prefab";
    private const string ConfirmPath = "Assets/Prefabs/UI/Warehouse/AbandonConfirmPanel.prefab";
    private const string InputPath = "Assets/Input/GameInputActions.inputactions";
    private static TMP_FontAsset font;

    [MenuItem("Tools/MyPrecious/Create Warehouse Test Assets")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { throw new InvalidOperationException("Stop Play before creating WarehouseTest."); }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty) { throw new InvalidOperationException("Preserve the dirty scene before creating WarehouseTest."); }
        }
        foreach (string folder in new[] { "Assets/Data/Warehouse", "Assets/Data/ShelterUpgrades", "Assets/Data/Localization/Warehouse", "Assets/Data/Localization/Shelter",
            "Assets/Prefabs/Warehouse", "Assets/Prefabs/Shelter", "Assets/Prefabs/UI/Warehouse", "Assets/Prefabs/UI/Shelter", "Assets/Scenes/Tests/Warehouse" })
        {
            Directory.CreateDirectory(folder);
        }
        AssetDatabase.Refresh();
        font = Required<TMP_FontAsset>("Assets/Art/Fonts/Localization/NotoSansCJKsc-Regular SDF.asset");
        WarehouseConfigSO config = CreateConfig<WarehouseConfigSO>("Assets/Data/Warehouse/WarehouseConfig.asset");
        ShelterUpgradeSO upgrade = CreateConfig<ShelterUpgradeSO>("Assets/Data/ShelterUpgrades/FirstWarehouseUpgrade.asset");
        LocalizationTableSO warehouseText = CreateTable("Assets/Data/Localization/Warehouse/WarehouseText.asset", true);
        LocalizationTableSO shelterText = CreateTable("Assets/Data/Localization/Shelter/ShelterText.asset", false);
        CreateModules(config, upgrade);
        CreateCellAndItem();
        CreateConfirmation();
        CreateWarehousePanel();
        CreateShelterPanel();
        if (!File.Exists(ScenePath)) { CreateScene(warehouseText, shelterText); }
        AssetDatabase.SaveAssets();
        Debug.Log("WarehouseTest assets are ready: " + ScenePath);
    }

    private static T CreateConfig<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) { return asset; }
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static LocalizationTableSO CreateTable(string path, bool warehouse)
    {
        LocalizationTableSO table = AssetDatabase.LoadAssetAtPath<LocalizationTableSO>(path);
        if (table != null) { return table; }
        table = ScriptableObject.CreateInstance<LocalizationTableSO>();
        if (warehouse) { WarehouseTextEntries.Fill(table); }
        else
        {
            Add(table, "shelter.title", "庇护所", "Shelter");
            Add(table, "shelter.free_upgrade", "免费升级", "Upgrade for free");
            Add(table, "shelter.open_warehouse", "打开仓库", "Open warehouse");
            Add(table, "shelter.ev", "当前 EV：{0} / 门槛 {1}", "Current EV: {0} / Required {1}");
            Add(table, "shelter.current_size", "当前仓库：{0} × {1}（{2}格）", "Current: {0} × {1} ({2} cells)");
            Add(table, "shelter.target_size", "升级目标：{0} × {1}（{2}格）", "Upgrade: {0} × {1} ({2} cells)");
            Add(table, "shelter.explanation", "手动免费向下扩容，列数不变。不消耗EV或物品，激活后本次运行内永久保留。", "Add rows downward for free; the column count stays unchanged. No EV or items are spent. The upgrade lasts for this run.");
            Add(table, "shelter.upgraded", "升级完成：{0} × {1}（{2}格）→ {3} × {4}（{5}格）。EV不消耗。", "Expanded: {0} × {1} ({2} cells) → {3} × {4} ({5} cells). No EV spent.");
            Add(table, "shelter.result.eligible", "已符合免费升级资格", "Eligible for the free upgrade");
            Add(table, "shelter.result.not_ready", "庇护所模块尚未准备好", "Shelter is not ready");
            Add(table, "shelter.result.not_in_shelter", "当前场景不允许庇护所整理", "Shelter management is unavailable here");
            Add(table, "shelter.result.already_activated", "已升级；EV下降不会缩仓", "Already upgraded; lower EV will not shrink storage");
            Add(table, "shelter.result.insufficient_ev", "EV不足", "Not enough EV");
            Add(table, "shelter.result.invalid_configuration", "升级配置无效：列数必须不变，目标行数必须增加", "Invalid upgrade: columns must stay unchanged and target rows must increase");
            Add(table, "shelter.result.busy", "请先结束拖拽或遗弃确认", "Finish the drag or abandonment confirmation first");
            Add(table, "shelter.test_note", "05验证场景｜布娃娃已入仓，已保有医疗包仍在背包（仅01数据）。初始EV=7；真实转移与撤离流程待06/08接入。", "05 test scene | Doll is stored; retained medkit stays in backpack (01 data only). Initial EV = 7. Real transfers and extraction await 06/08.");
        }
        AssetDatabase.CreateAsset(table, path);
        return table;
    }

    private static void Add(LocalizationTableSO table, string key, string chinese, string english)
    {
        table.entries.Add(new LocalizationEntry { key = key, chinese = chinese, english = english });
    }

    private static void CreateModules(WarehouseConfigSO config, ShelterUpgradeSO upgrade)
    {
        if (!File.Exists(WarehouseModule))
        {
            GameObject root = new GameObject("WarehouseModule");
            root.AddComponent<WarehouseManager>().config = config;
            PrefabUtility.SaveAsPrefabAsset(root, WarehouseModule);
            Object.DestroyImmediate(root);
        }
        if (!File.Exists(ShelterModule))
        {
            GameObject root = new GameObject("ShelterModule");
            root.AddComponent<ShelterManager>().firstUpgrade = upgrade;
            PrefabUtility.SaveAsPrefabAsset(root, ShelterModule);
            Object.DestroyImmediate(root);
        }
    }

    private static void CreateCellAndItem()
    {
        if (!File.Exists(CellPath))
        {
            GameObject cell = Rect(null, "WarehouseCell", 0, 0, 52, 52).gameObject;
            Image image = cell.AddComponent<Image>();
            image.color = new Color(0.15f, 0.2f, 0.25f);
            image.raycastTarget = false;
            PrefabUtility.SaveAsPrefabAsset(cell, CellPath);
            Object.DestroyImmediate(cell);
        }
        if (!File.Exists(ItemPath))
        {
            GameObject item = Rect(null, "WarehouseItem", 0, 0, 100, 150).gameObject;
            WarehouseItemView view = item.AddComponent<WarehouseItemView>();
            view.background = item.AddComponent<Image>();
            view.background.color = new Color(0.22f, 0.48f, 0.47f);
            view.nameText = Text(item.transform, "Name", 20, 6, 6, 88, 85);
            Stretch(view.nameText.rectTransform, 6, 6, 6, 30);
            view.nameText.enableAutoSizing = true;
            view.nameText.fontSizeMin = 13;
            view.nameText.fontSizeMax = 20;
            view.sizeText = Text(item.transform, "Size", 15, 6, 112, 88, 26);
            view.sizeText.rectTransform.anchorMin = Vector2.zero;
            view.sizeText.rectTransform.anchorMax = new Vector2(1, 0);
            view.sizeText.rectTransform.anchoredPosition = new Vector2(6, 30);
            view.sizeText.rectTransform.sizeDelta = new Vector2(-12, 26);
            PrefabUtility.SaveAsPrefabAsset(item, ItemPath);
            Object.DestroyImmediate(item);
        }
    }

    private static void CreateConfirmation()
    {
        if (File.Exists(ConfirmPath)) { return; }
        GameObject root = Rect(null, "AbandonConfirmPanel", 0, 0, 920, 600).gameObject;
        Image shade = root.AddComponent<Image>();
        shade.color = new Color(0.01f, 0.02f, 0.03f, 0.94f);
        Text(root.transform, "Title", 30, 70, 80, 780, 48);
        Text(root.transform, "Message", 24, 70, 155, 780, 230);
        Button(root.transform, "Confirm", 70, 430, 360, 64, out _);
        Button(root.transform, "Cancel", 490, 430, 360, 64, out _);
        PrefabUtility.SaveAsPrefabAsset(root, ConfirmPath);
        Object.DestroyImmediate(root);
    }

    private static void CreateWarehousePanel()
    {
        if (File.Exists(WarehousePanel)) { return; }
        GameObject root = Rect(null, "WarehousePanel", 20, 100, 920, 600).gameObject;
        root.AddComponent<Image>().color = new Color(0.055f, 0.075f, 0.105f, 0.98f);
        WarehouseView view = root.AddComponent<WarehouseView>();
        view.panelRoot = root;
        view.titleText = Text(root.transform, "Title", 28, 18, 12, 300, 38);
        view.sizeText = Text(root.transform, "Capacity", 20, 18, 50, 600, 34);
        view.closeButton = Button(root.transform, "Close", 770, 16, 130, 46, out view.closeText);
        RectTransform scrollRoot = Rect(root.transform, "Scroll", 18, 96, 600, 412);
        view.scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
        scrollRoot.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.12f, 0.16f);
        RectTransform viewport = Rect(scrollRoot, "Viewport", 0, 0, 578, 390);
        viewport.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.13f, 0.17f);
        viewport.gameObject.AddComponent<RectMask2D>();
        view.viewport = viewport;
        view.gridRoot = Rect(viewport, "Content", 0, 0, 1113, 1113);
        view.itemRoot = Rect(view.gridRoot, "Items", 0, 0, 1113, 1113);
        Stretch(view.itemRoot, 0, 0, 0, 0);
        view.scrollRect.viewport = viewport;
        view.scrollRect.content = view.gridRoot;
        view.scrollRect.movementType = ScrollRect.MovementType.Clamped;
        view.scrollRect.inertia = false;
        view.scrollRect.scrollSensitivity = 32;
        view.scrollRect.horizontalScrollbar = Scrollbar(scrollRoot, "Horizontal", 0, 393, 578, 19, false);
        view.scrollRect.verticalScrollbar = Scrollbar(scrollRoot, "Vertical", 581, 0, 19, 390, true);
        view.scrollRect.horizontalNormalizedPosition = 0;
        view.scrollRect.verticalNormalizedPosition = 1;
        view.cellPrefab = Required<GameObject>(CellPath).GetComponent<Image>();
        view.itemPrefab = Required<GameObject>(ItemPath).GetComponent<WarehouseItemView>();
        view.preview = Rect(view.itemRoot, "Preview", 0, 0, 52, 52).gameObject.AddComponent<Image>();
        view.preview.color = new Color(0.3f, 0.9f, 0.6f, 0.45f);
        view.preview.raycastTarget = false;
        view.preview.gameObject.SetActive(false);
        view.detailsText = Text(root.transform, "Details", 20, 640, 100, 260, 250);
        view.detailsText.enableAutoSizing = true;
        view.detailsText.fontSizeMin = 15;
        view.detailsText.fontSizeMax = 20;
        view.abandonButton = Button(root.transform, "Abandon", 640, 365, 260, 58, out view.abandonText);
        view.cancelDragButton = Button(root.transform, "CancelDrag", 640, 436, 260, 58, out view.cancelDragText);
        view.hintText = Text(root.transform, "Controls", 17, 18, 514, 882, 48);
        view.feedbackText = Text(root.transform, "Feedback", 18, 18, 568, 882, 28);
        GameObject confirmation = Instantiate(ConfirmPath, root.transform);
        Stretch(confirmation.GetComponent<RectTransform>(), 0, 0, 0, 0);
        view.confirmPanel = confirmation;
        view.confirmTitleText = confirmation.transform.Find("Title").GetComponent<TMP_Text>();
        view.confirmMessageText = confirmation.transform.Find("Message").GetComponent<TMP_Text>();
        view.confirmButton = confirmation.transform.Find("Confirm").GetComponent<Button>();
        view.cancelButton = confirmation.transform.Find("Cancel").GetComponent<Button>();
        view.confirmButtonText = view.confirmButton.GetComponentInChildren<TMP_Text>();
        view.cancelButtonText = view.cancelButton.GetComponentInChildren<TMP_Text>();
        confirmation.SetActive(false);
        ApplyWarehouseLayout(root);
        PrefabUtility.SaveAsPrefabAsset(root, WarehousePanel);
        Object.DestroyImmediate(root);
    }

    private static void CreateShelterPanel()
    {
        if (File.Exists(ShelterPanel)) { return; }
        GameObject root = Rect(null, "ShelterPanel", 960, 100, 300, 600).gameObject;
        root.AddComponent<Image>().color = new Color(0.08f, 0.13f, 0.16f, 0.98f);
        ShelterView view = root.AddComponent<ShelterView>();
        view.panelRoot = root;
        view.titleText = Text(root.transform, "Title", 30, 20, 18, 260, 42);
        view.evText = Text(root.transform, "EV", 23, 20, 80, 260, 56);
        view.currentSizeText = Text(root.transform, "CurrentSize", 19, 20, 145, 260, 54);
        view.targetSizeText = Text(root.transform, "TargetSize", 19, 20, 204, 260, 54);
        view.statusText = Text(root.transform, "Status", 20, 20, 268, 260, 70);
        view.upgradeButton = Button(root.transform, "Upgrade", 20, 347, 260, 54, out view.upgradeText);
        view.openWarehouseButton = Button(root.transform, "OpenWarehouse", 20, 412, 260, 48, out view.openWarehouseText);
        view.resultText = Text(root.transform, "Result", 17, 20, 480, 260, 100);
        ApplyShelterLayout(root);
        PrefabUtility.SaveAsPrefabAsset(root, ShelterPanel);
        Object.DestroyImmediate(root);
    }

    private static void CreateScene(LocalizationTableSO warehouseText, LocalizationTableSO shelterText)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        GameObject bootstrap = Instantiate("Assets/Prefabs/Bootstrap/GameBootstrap.prefab");
        ItemManager items = bootstrap.GetComponentInChildren<ItemManager>(true);
        if (items == null)
        {
            items = Instantiate("Assets/Prefabs/Items/ItemModule.prefab", bootstrap.transform).GetComponent<ItemManager>();
        }
        LocalizationManager language = bootstrap.GetComponentInChildren<LocalizationManager>(true);
        if (language == null)
        {
            language = Instantiate("Assets/Prefabs/Localization/LocalizationModule.prefab", bootstrap.transform).GetComponent<LocalizationManager>();
        }
        if (!language.tables.Contains(warehouseText)) { language.tables.Add(warehouseText); }
        if (!language.tables.Contains(shelterText)) { language.tables.Add(shelterText); }
        WarehouseManager warehouse = Instantiate(WarehouseModule, bootstrap.transform).GetComponent<WarehouseManager>();
        warehouse.itemManager = items;
        ShelterManager shelter = Instantiate(ShelterModule, bootstrap.transform).GetComponent<ShelterManager>();
        shelter.itemManager = items;
        shelter.warehouseManager = warehouse;
        foreach (Object component in new Object[] { language, warehouse, shelter }) { PrefabUtility.RecordPrefabInstancePropertyModifications(component); }
        GameObject camera = new GameObject("Main Camera", typeof(Camera), typeof(UniversalAdditionalCameraData));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0, 0, -10);
        camera.GetComponent<Camera>().clearFlags = CameraClearFlags.Skybox;
        camera.GetComponent<Camera>().backgroundColor = new Color(0.025f, 0.035f, 0.055f);
        new GameObject("Directional Light", typeof(Light)).GetComponent<Light>().type = LightType.Directional;
        GameObject canvasObject = new GameObject("WarehouseCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        TMP_Text note = Text(canvas.transform, "TestNote", 18, 20, 10, 1240, 48);
        Localized(note, "shelter.test_note");
        TMP_Text hint = Text(canvas.transform, "LanguageHint", 18, 20, 62, 1240, 30);
        Localized(hint, "common.language.toggle_hint");
        note.gameObject.SetActive(false);
        hint.gameObject.SetActive(false);
        WarehouseView warehouseView = Instantiate(WarehousePanel, canvas.transform).GetComponent<WarehouseView>();
        ShelterView shelterView = Instantiate(ShelterPanel, canvas.transform).GetComponent<ShelterView>();
        ApplyScenePresentation(canvas, warehouseView, shelterView);
        WarehouseUIController ui = new GameObject("WarehouseUIController").AddComponent<WarehouseUIController>();
        ui.manager = warehouse;
        ui.view = warehouseView;
        ui.localization = language;
        ui.inputActions = Required<InputActionAsset>(InputPath);
        shelterView.manager = shelter;
        shelterView.localization = language;
        shelterView.warehouseUI = ui;
        PrefabUtility.RecordPrefabInstancePropertyModifications(shelterView);
        GameObject events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        InputSystemUIInputModule input = events.GetComponent<InputSystemUIInputModule>();
        input.actionsAsset = Required<InputActionAsset>(InputPath);
        input.point = Action("Point");
        input.leftClick = Action("Click");
        input.rightClick = Action("RightClick");
        input.middleClick = Action("MiddleClick");
        input.scrollWheel = Action("ScrollWheel");
        input.move = Action("Navigate");
        input.submit = Action("Submit");
        input.cancel = Action("Cancel");
        WarehouseTestDriver driver = new GameObject("WarehouseTestDriver").AddComponent<WarehouseTestDriver>();
        driver.warehouseUI = ui;
        driver.shelterView = shelterView;
        driver.itemLibrary = Required<ItemLibrarySO>("Assets/Data/Items/ItemLibrary.asset");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.CloseScene(scene, true);
        if (previous.IsValid()) { SceneManager.SetActiveScene(previous); }
    }

    // 只更新现有测试场景的显示，不回写共享Prefab、语言表或业务资源。
    [MenuItem("Tools/MyPrecious/Apply Warehouse Vertical Layout")]
    public static void ApplyVerticalLayout()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { throw new InvalidOperationException("Stop Play before updating Warehouse UI."); }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty) { throw new InvalidOperationException("Save the dirty scene before updating Warehouse UI."); }
        }
        if (!File.Exists(ScenePath)) { throw new InvalidOperationException("Missing scene: " + ScenePath); }
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) { scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); }
        try
        {
            WarehouseView warehouseView = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<WarehouseView>(true)).Single();
            Canvas canvas = warehouseView.GetComponentInParent<Canvas>(true);
            ShelterView shelterView = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<ShelterView>(true)).Single();
            ApplyScenePresentation(canvas, warehouseView, shelterView);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) { throw new InvalidOperationException("Unable to save: " + ScenePath); }
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) { SceneManager.SetActiveScene(previous); }
            if (opened) { EditorSceneManager.CloseScene(scene, true); }
        }
    }

    private static void ApplyScenePresentation(Canvas canvas, WarehouseView warehouse, ShelterView shelter)
    {
        if (canvas == null || warehouse == null || shelter == null)
        {
            throw new InvalidOperationException("WarehouseTest display references are incomplete.");
        }
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) { throw new InvalidOperationException("WarehouseTest CanvasScaler is missing."); }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;
        EditorUtility.SetDirty(scaler);
        ApplyWarehouseLayout(warehouse.gameObject);
        ApplyShelterLayout(shelter.gameObject);
        RecordPresentation(warehouse.gameObject);
        RecordPresentation(shelter.gameObject);
        foreach (string name in new[] { "TestNote", "LanguageHint" })
        {
            Transform note = canvas.transform.Find(name);
            if (note != null) { note.gameObject.SetActive(false); }
        }
    }

    private static void RecordPresentation(GameObject root)
    {
        foreach (Component component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null) { continue; }
            EditorUtility.SetDirty(component);
            if (PrefabUtility.IsPartOfPrefabInstance(component))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
            if (component is Transform && PrefabUtility.IsPartOfPrefabInstance(component.gameObject))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(component.gameObject);
            }
        }
    }

    private static void ApplyWarehouseLayout(GameObject root)
    {
        SetRect(root.transform, 276, 20, 1488, 720);
        WarehouseView view = root.GetComponent<WarehouseView>();
        view.cellSize = 66f;
        view.gap = 4f;
        view.padding = 6f;
        view.gridRoot.localScale = Vector3.one;
        view.itemRoot.localScale = Vector3.one;
        view.gridRoot.anchoredPosition = Vector2.zero;
        SetRect(view.titleText.transform, 18, 12, 220, 48);
        view.titleText.fontSize = 30;
        SetRect(view.sizeText.transform, 300, 20, 1000, 38);
        view.sizeText.fontSize = 24;
        SetRect(view.closeButton.transform, 1344, 12, 124, 44);
        SetRect(view.scrollRect.transform, 18, 74, 1452, 440);
        SetRect(view.viewport, 0, 0, 1430, 440);
        view.scrollRect.horizontal = false;
        view.scrollRect.vertical = true;
        if (view.scrollRect.horizontalScrollbar != null)
        {
            view.scrollRect.horizontalScrollbar.gameObject.SetActive(false);
        }
        SetRect(view.scrollRect.verticalScrollbar.transform, 1434, 0, 18, 440);
        view.scrollRect.horizontalNormalizedPosition = 0;
        Transform detailsRoot = root.transform.Find("DetailsScroll");
        if (detailsRoot == null)
        {
            detailsRoot = Rect(root.transform, "DetailsScroll", 18, 530, 1076, 84);
            detailsRoot.gameObject.AddComponent<Image>().color = new Color(0.08f, 0.12f, 0.16f);
            detailsRoot.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = detailsRoot.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = (RectTransform)detailsRoot;
            scroll.content = view.detailsText.rectTransform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = 30;
            view.detailsText.transform.SetParent(detailsRoot, false);
            ContentSizeFitter fit = view.detailsText.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
        SetRect(detailsRoot, 18, 530, 1076, 84);
        ScrollRect detailsScroll = detailsRoot.GetComponent<ScrollRect>();
        if (detailsScroll.verticalScrollbar == null)
        {
            detailsScroll.verticalScrollbar = Scrollbar(detailsRoot, "Vertical", 1060, 0, 16, 84, true);
        }
        SetRect(detailsScroll.verticalScrollbar.transform, 1060, 0, 16, 84);
        SetRect(view.detailsText.transform, 6, 0, 1048, 84);
        view.detailsText.enableAutoSizing = false;
        view.detailsText.fontSize = 18;
        view.detailsText.margin = new Vector4(0, 4, 0, 4);
        SetRect(view.abandonButton.transform, 1114, 530, 354, 40);
        SetRect(view.cancelDragButton.transform, 1114, 578, 354, 36);
        SetRect(view.hintText.transform, 18, 632, 1452, 52);
        view.hintText.fontSize = 18;
        SetRect(view.feedbackText.transform, 18, 688, 1452, 28);
        view.feedbackText.fontSize = 18;
        Stretch(view.confirmPanel.GetComponent<RectTransform>(), 0, 0, 0, 0);
        SetRect(view.confirmTitleText.transform, 24, 20, 1440, 48);
        SetRect(view.confirmMessageText.transform, 24, 84, 1440, 500);
        view.confirmMessageText.fontSize = 24;
        view.confirmMessageText.enableAutoSizing = false;
        SetRect(view.confirmButton.transform, 24, 620, 710, 64);
        SetRect(view.cancelButton.transform, 754, 620, 710, 64);
        view.confirmPanel.transform.SetAsLastSibling();
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) { continue; }
            Stretch(label.rectTransform, 6, 2, 6, 2);
            label.enableAutoSizing = false;
            label.fontSize = button == view.closeButton || button == view.confirmButton || button == view.cancelButton ? 22 : 18;
            label.textWrappingMode = TextWrappingModes.NoWrap;
        }
    }

    private static void ApplyShelterLayout(GameObject root)
    {
        SetRect(root.transform, 20, 20, 236, 720);
        ShelterView view = root.GetComponent<ShelterView>();
        SetRect(view.titleText.transform, 18, 20, 200, 46);
        SetRect(view.evText.transform, 18, 90, 200, 64);
        view.evText.fontSize = 20;
        SetRect(view.currentSizeText.transform, 18, 164, 200, 60);
        SetRect(view.targetSizeText.transform, 18, 230, 200, 60);
        SetRect(view.statusText.transform, 18, 300, 200, 104);
        view.statusText.fontSize = 18;
        SetRect(view.upgradeButton.transform, 18, 412, 200, 54);
        SetRect(view.openWarehouseButton.transform, 18, 480, 200, 48);
        SetRect(view.resultText.transform, 18, 544, 200, 160);
        view.resultText.fontSize = 16;
        foreach (Button button in root.GetComponentsInChildren<Button>(true))
        {
            TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
            if (label == null) { continue; }
            Stretch(label.rectTransform, 6, 2, 6, 2);
            label.enableAutoSizing = false;
            label.fontSize = 18;
            label.textWrappingMode = TextWrappingModes.NoWrap;
        }
    }

    private static void SetRect(Transform target, float x, float y, float width, float height)
    {
        RectTransform rect = (RectTransform)target;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }

    private static InputActionReference Action(string name)
    {
        return AssetDatabase.LoadAllAssetsAtPath(InputPath).OfType<InputActionReference>()
            .First(reference => reference.action.actionMap.name == "UI" && reference.action.name == name);
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
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
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

    private static Button Button(Transform parent, string name, float x, float y, float width, float height, out TMP_Text label)
    {
        GameObject root = Rect(parent, name, x, y, width, height).gameObject;
        Image background = root.AddComponent<Image>();
        background.color = new Color(0.19f, 0.34f, 0.4f);
        Button button = root.AddComponent<Button>();
        button.targetGraphic = background;
        label = Text(root.transform, "Label", 20, 6, 4, width - 12, height - 8);
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 15;
        label.fontSizeMax = 20;
        return button;
    }

    private static Scrollbar Scrollbar(Transform parent, string name, float x, float y, float width, float height, bool vertical)
    {
        GameObject root = Rect(parent, name, x, y, width, height).gameObject;
        root.AddComponent<Image>().color = new Color(0.07f, 0.09f, 0.12f);
        Scrollbar bar = root.AddComponent<Scrollbar>();
        RectTransform handle = Rect(root.transform, "Handle", 0, 0, width, height);
        Stretch(handle, 0, 0, 0, 0);
        Image image = handle.gameObject.AddComponent<Image>();
        image.color = new Color(0.39f, 0.58f, 0.63f);
        bar.targetGraphic = image;
        bar.handleRect = handle;
        bar.direction = vertical ? UnityEngine.UI.Scrollbar.Direction.BottomToTop : UnityEngine.UI.Scrollbar.Direction.LeftToRight;
        bar.size = 0.35f;
        return bar;
    }

    private static void Localized(TMP_Text text, string key)
    {
        LocalizedText localized = text.gameObject.AddComponent<LocalizedText>();
        localized.targetText = text;
        localized.key = key;
    }
}
