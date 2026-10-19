using System;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 只生成04拥有的资源；共享Prefab始终通过场景实例引用，不把测试接线Apply回共享资源。
public static class BackpackTestSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Tests/Backpack/BackpackTest.unity";
    public const string ModulePath = "Assets/Prefabs/Backpack/BackpackModule.prefab";
    public const string PanelPath = "Assets/Prefabs/UI/Backpack/BackpackPanel.prefab";
    public const string CellPath = "Assets/Prefabs/UI/Backpack/BackpackCell.prefab";
    public const string ItemPath = "Assets/Prefabs/UI/Backpack/BackpackItem.prefab";
    private const string ConfigPath = "Assets/Data/Backpack/BackpackConfig.asset";
    private const string TablePath = "Assets/Data/Localization/Backpack/BackpackText.asset";
    private const string InputPath = "Assets/Input/GameInputActions.inputactions";
    private const string FontPath = "Assets/Art/Fonts/Localization/NotoSansCJKsc-Regular SDF.asset";

    [MenuItem("Tools/MyPrecious/Create Backpack Test Assets")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Create Backpack assets outside Play mode.");
        }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty)
            {
                throw new InvalidOperationException("Save or preserve the current dirty scene before building BackpackTest.");
            }
        }
        foreach (string folder in new[]
        {
            "Assets/Data/Backpack", "Assets/Data/Localization/Backpack", "Assets/Prefabs/Backpack",
            "Assets/Prefabs/UI/Backpack", "Assets/Scenes/Tests/Backpack"
        })
        {
            Directory.CreateDirectory(folder);
        }
        AssetDatabase.Refresh();
        TMP_FontAsset font = Required<TMP_FontAsset>(FontPath);
        BackpackConfigSO config = AssetDatabase.LoadAssetAtPath<BackpackConfigSO>(ConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<BackpackConfigSO>();
            AssetDatabase.CreateAsset(config, ConfigPath);
        }
        LocalizationTableSO table = AssetDatabase.LoadAssetAtPath<LocalizationTableSO>(TablePath);
        if (table == null)
        {
            table = ScriptableObject.CreateInstance<LocalizationTableSO>();
            BackpackTextEntries.Fill(table);
            AssetDatabase.CreateAsset(table, TablePath);
        }
        CreateModule(config);
        CreateCell();
        CreateItem(font);
        CreatePanel(font);
        if (!File.Exists(ScenePath)) { CreateScene(font, table); }
        else { Debug.Log("BackpackTest already exists; existing scene edits and asset GUIDs are preserved."); }
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/MyPrecious/Update Backpack Test Display")]
    public static void UpdateTestDisplay()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Stop Play before updating BackpackTest display.");
        }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty)
            {
                throw new InvalidOperationException("Save the dirty scene before updating BackpackTest display.");
            }
        }
        if (!File.Exists(ScenePath)) { throw new InvalidOperationException("Missing scene: " + ScenePath); }
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) { scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive); }
        try
        {
            BackpackView view = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BackpackView>(true)).Single();
            ApplyScenePresentation(view.GetComponentInParent<Canvas>(true), view);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) { throw new InvalidOperationException("Unable to save: " + ScenePath); }
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) { SceneManager.SetActiveScene(previous); }
            if (opened) { EditorSceneManager.CloseScene(scene, true); }
        }
    }

    // 只保存测试场景的显示覆盖，不重建UI或改动共享资源与业务接线。
    private static void ApplyScenePresentation(Canvas canvas, BackpackView view)
    {
        if (canvas == null || view.gridRoot == null || view.itemRoot == null)
        {
            throw new InvalidOperationException("BackpackTest display references are incomplete.");
        }
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) { throw new InvalidOperationException("BackpackTest CanvasScaler is missing."); }
        RectTransform inventory = view.transform.Find("PlayerInventory") as RectTransform;
        RectTransform inset = inventory != null ? inventory.Find("Inset") as RectTransform : null;
        RectTransform discard = inventory != null ? inventory.Find("DiscardItem") as RectTransform : null;
        if (inventory == null || inset == null || discard == null || view.hintText == null)
        {
            throw new InvalidOperationException("BackpackTest panel display references are incomplete.");
        }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;
        view.cellSize = 66f;
        view.gap = 4f;
        view.padding = 6f;
        view.transform.localScale = Vector3.one;
        view.gridRoot.parent.localScale = Vector3.one;
        foreach (RectTransform root in new[] { view.gridRoot, view.itemRoot })
        {
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(36f, -62f);
            root.sizeDelta = new Vector2(288f, 288f);
            root.localScale = Vector3.one;
        }
        SetDisplayRect(inventory, 24, 110, 360, 680);
        SetDisplayRect(inset, 3, 3, 354, 674);
        SetDisplayRect(discard, 20, 522, 320, 34);
        SetDisplayRect(view.hintText.rectTransform, 20, 564, 320, 96);
        view.hintText.enableAutoSizing = false;
        view.hintText.fontSize = 18;
        foreach (Object target in new Object[]
        {
            scaler, view, view.transform, inventory, inset, discard,
            view.gridRoot, view.itemRoot, view.hintText, view.hintText.rectTransform
        })
        {
            EditorUtility.SetDirty(target);
            if (PrefabUtility.IsPartOfPrefabInstance(target))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            }
        }
    }

    private static void SetDisplayRect(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }

    private static void CreateModule(BackpackConfigSO config)
    {
        if (File.Exists(ModulePath)) { return; }
        GameObject root = new GameObject("BackpackModule");
        try
        {
            BackpackManager manager = root.AddComponent<BackpackManager>();
            manager.config = config;
            manager.containerId = "player-backpack";
            PrefabUtility.SaveAsPrefabAsset(root, ModulePath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void CreateCell()
    {
        if (File.Exists(CellPath)) { return; }
        GameObject root = new GameObject("BackpackCell", typeof(RectTransform), typeof(Image));
        try
        {
            Image image = root.GetComponent<Image>();
            image.color = new Color(0.22f, 0.20f, 0.15f, 1f);
            image.raycastTarget = false;
            PrefabUtility.SaveAsPrefabAsset(root, CellPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void CreateItem(TMP_FontAsset font)
    {
        if (File.Exists(ItemPath)) { return; }
        GameObject root = new GameObject("BackpackItem", typeof(RectTransform), typeof(Image), typeof(BackpackItemView));
        try
        {
            BackpackItemView item = root.GetComponent<BackpackItemView>();
            item.background = root.GetComponent<Image>();
            item.background.color = new Color(0.25f, 0.57f, 0.57f);
            item.nameText = Text(root.transform, "Name", font, 21, 8, 9, 120, 70);
            RectTransform nameRect = item.nameText.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = Vector2.one;
            nameRect.offsetMin = new Vector2(9f, 35f);
            nameRect.offsetMax = new Vector2(-9f, -9f);
            item.nameText.enableAutoSizing = true;
            item.nameText.textWrappingMode = TextWrappingModes.NoWrap;
            item.nameText.fontSizeMin = 12f;
            item.nameText.fontSizeMax = 23f;
            item.sizeText = Text(root.transform, "Size", font, 16, 9, 0, 120, 30);
            RectTransform sizeRect = item.sizeText.rectTransform;
            sizeRect.anchorMin = Vector2.zero;
            sizeRect.anchorMax = new Vector2(1f, 0f);
            sizeRect.pivot = Vector2.zero;
            sizeRect.anchoredPosition = new Vector2(9f, 5f);
            sizeRect.sizeDelta = new Vector2(-18f, 30f);
            PrefabUtility.SaveAsPrefabAsset(root, ItemPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void CreatePanel(TMP_FontAsset font)
    {
        if (File.Exists(PanelPath)) { return; }
        GameObject root = new GameObject("BackpackPanel", typeof(RectTransform), typeof(Image), typeof(BackpackView));
        try
        {
            BuildPanelLayout(root, font);
            PrefabUtility.SaveAsPrefabAsset(root, PanelPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    [MenuItem("Tools/MyPrecious/Rebuild Backpack Panel Layout")]
    public static void RebuildPanelLayout()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Rebuild the Backpack layout outside Play mode.");
        }
        GameObject root = PrefabUtility.LoadPrefabContents(PanelPath);
        try
        {
            // 保留根物体与BackpackView组件的fileID，场景内控制器引用不会失效。
            // 这里只替换本任务自己拥有的UI子物体，不触碰场景、玩家或共享Prefab。
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            }
            BuildPanelLayout(root, Required<TMP_FontAsset>(FontPath));
            PrefabUtility.SaveAsPrefabAsset(root, PanelPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        GameObject cell = PrefabUtility.LoadPrefabContents(CellPath);
        try
        {
            cell.GetComponent<Image>().color = new Color(0.22f, 0.20f, 0.15f, 1f);
            PrefabUtility.SaveAsPrefabAsset(cell, CellPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(cell); }
        LocalizationTableSO table = Required<LocalizationTableSO>(TablePath);
        BackpackTextEntries.ApplySplitLayoutText(table);
        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
    }

    private static void BuildPanelLayout(GameObject root, TMP_FontAsset font)
    {
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        Image rootImage = root.GetComponent<Image>();
        if (rootImage != null) { rootImage.enabled = false; rootImage.raycastTarget = false; }
        BackpackView view = root.GetComponent<BackpackView>();
        view.panelRoot = root;
        view.cellSize = 66f;
        view.gap = 4f;
        view.padding = 6f;
        RectTransform left = PanelFrame(root.transform, "PlayerInventory", 24f, 110f, 360f, 580f);
        view.titleText = Text(left, "Title", font, 26, 20, 14, 210, 40);
        view.titleText.color = new Color(0.92f, 0.85f, 0.65f);
        view.gridRoot = Rect(left, "Grid", 36, 62, 288, 288);
        view.itemRoot = Rect(left, "Items", 36, 62, 288, 288);
        view.cellPrefab = Required<GameObject>(CellPath).GetComponent<Image>();
        view.itemPrefab = Required<GameObject>(ItemPath).GetComponent<BackpackItemView>();
        view.preview = Rect(view.itemRoot, "Preview", 0, 0, 66, 66).gameObject.AddComponent<Image>();
        view.preview.raycastTarget = false;
        view.preview.color = new Color(0.7f, 0.8f, 0.4f, 0.45f);
        view.preview.gameObject.SetActive(false);
        view.detailsText = ScrollDetails(left, font);
        view.feedbackText = Text(left, "Feedback", font, 15, 20, 469, 320, 46);
        view.feedbackText.color = new Color(0.9f, 0.78f, 0.53f);
        view.hintText = Text(left, "Controls", font, 14, 20, 518, 320, 56);
        view.hintText.color = new Color(0.76f, 0.73f, 0.64f);
        RectTransform close = Rect(left, "Close", 248, 17, 92, 36);
        Image closeImage = close.gameObject.AddComponent<Image>();
        closeImage.color = new Color(0.34f, 0.29f, 0.19f);
        view.closeButton = close.gameObject.AddComponent<Button>();
        view.closeButton.targetGraphic = closeImage;
        view.closeText = Text(close, "Label", font, 16, 0, 0, 92, 36);
        view.closeText.alignment = TextAlignmentOptions.Center;
        view.closeText.enableAutoSizing = true;
        view.closeText.fontSizeMin = 12f;
        view.closeText.fontSizeMax = 16f;
        view.closeText.textWrappingMode = TextWrappingModes.NoWrap;

        // 未连接容器时隐藏占位；真实搜刮和仓库面板由各自会话控制显示。
        RectTransform right = PanelFrame(root.transform, "LootContainerPreview", 24f, 110f, 310f, 400f);
        right.gameObject.SetActive(false);
        right.anchorMin = right.anchorMax = new Vector2(1f, 1f);
        right.pivot = new Vector2(1f, 1f);
        right.anchoredPosition = new Vector2(-24f, -110f);
        view.lootTitleText = Text(right, "LootTitle", font, 23, 20, 18, 270, 40);
        view.lootTitleText.color = new Color(0.92f, 0.85f, 0.65f);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Image empty = Rect(right, "DisconnectedCell_" + x + "_" + y,
                    25 + x * 65, 74 + y * 65, 60, 60).gameObject.AddComponent<Image>();
                empty.color = new Color(0.20f, 0.18f, 0.13f, 1f);
                empty.raycastTarget = false;
            }
        }
        view.lootStatusText = Text(right, "LootStatus", font, 17, 20, 340, 270, 53);
        view.lootStatusText.color = new Color(0.77f, 0.73f, 0.62f);
    }

    private static RectTransform PanelFrame(Transform parent, string name, float x, float y, float width, float height)
    {
        RectTransform frame = Rect(parent, name, x, y, width, height);
        Image border = frame.gameObject.AddComponent<Image>();
        border.color = new Color(0.35f, 0.30f, 0.19f, 0.98f);
        Image body = Rect(frame, "Inset", 3, 3, width - 6f, height - 6f).gameObject.AddComponent<Image>();
        body.color = new Color(0.105f, 0.10f, 0.075f, 0.97f);
        body.raycastTarget = false;
        Image line = Rect(frame, "HeaderLine", 16, 57, width - 32f, 2).gameObject.AddComponent<Image>();
        line.color = new Color(0.43f, 0.37f, 0.22f);
        line.raycastTarget = false;
        return frame;
    }

    private static TMP_Text ScrollDetails(Transform parent, TMP_FontAsset font)
    {
        RectTransform scrollRoot = Rect(parent, "ItemDetailsScroll", 20, 362, 320, 100);
        ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;
        RectTransform viewport = Rect(scrollRoot, "Viewport", 0, 0, 307, 100);
        Image surface = viewport.gameObject.AddComponent<Image>();
        surface.color = new Color(0f, 0f, 0f, 0.05f);
        viewport.gameObject.AddComponent<RectMask2D>();
        TMP_Text text = Text(viewport, "Details", font, 18, 0, 0, 307, 100);
        text.rectTransform.anchorMax = new Vector2(1f, 1f);
        text.rectTransform.sizeDelta = new Vector2(0f, 100f);
        ContentSizeFitter fitter = text.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport;
        scroll.content = text.rectTransform;
        RectTransform barRoot = Rect(scrollRoot, "Scrollbar", 312, 0, 8, 100);
        Image track = barRoot.gameObject.AddComponent<Image>();
        track.color = new Color(0.19f, 0.17f, 0.12f);
        Scrollbar bar = barRoot.gameObject.AddComponent<Scrollbar>();
        RectTransform handle = Rect(barRoot, "Handle", 0, 0, 8, 35);
        // Scrollbar会驱动锚点；额外sizeDelta会让滑块比轨道更长。
        handle.sizeDelta = Vector2.zero;
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = new Color(0.52f, 0.45f, 0.29f);
        bar.handleRect = handle;
        bar.targetGraphic = handleImage;
        bar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = bar;
        return text;
    }

    private static void CreateScene(TMP_FontAsset font, LocalizationTableSO table)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            GameObject bootstrap = Instantiate("Assets/Prefabs/Bootstrap/GameBootstrap.prefab");
            // Bootstrap可能已内置共享模块，不能再创建第二份让UI和业务绑定到不同实例。
            ItemManager items = bootstrap.GetComponentInChildren<ItemManager>(true);
            if (items == null)
            {
                items = Instantiate("Assets/Prefabs/Items/ItemModule.prefab", bootstrap.transform).GetComponent<ItemManager>();
            }
            BackpackManager manager = Instantiate(ModulePath, bootstrap.transform).GetComponent<BackpackManager>();
            manager.itemManager = items;
            PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
            LocalizationManager language = bootstrap.GetComponentInChildren<LocalizationManager>(true);
            if (language == null)
            {
                language = Instantiate("Assets/Prefabs/Localization/LocalizationModule.prefab", bootstrap.transform)
                    .GetComponent<LocalizationManager>();
            }
            if (!language.tables.Contains(table)) { language.tables.Add(table); }
            LocalizationTableSO lootText = Required<LocalizationTableSO>("Assets/Data/Localization/Loot/LootText.asset");
            if (!language.tables.Contains(lootText)) { language.tables.Add(lootText); }
            PrefabUtility.RecordPrefabInstancePropertyModifications(language);
            PlayerController player = Instantiate("Assets/Prefabs/Player/Player.prefab").GetComponent<PlayerController>();
            player.transform.position = new Vector3(-4f, 0.1f, 0f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
            PlayerCameraTargetBinder cameraBinder = Instantiate("Assets/Prefabs/Player/PlayerCamera.prefab").GetComponent<PlayerCameraTargetBinder>();
            if (cameraBinder.followCamera == null)
            {
                throw new InvalidOperationException("PlayerCamera缺少SideFollow相机引用。");
            }
            cameraBinder.confiner = cameraBinder.followCamera.GetComponent<CinemachineConfiner3D>();
            if (cameraBinder.confiner == null)
            {
                throw new InvalidOperationException("请先为PlayerCamera的SideFollow接入CinemachineConfiner3D。");
            }
            // 边界只属于本测试场景；不把场景引用或几何Apply到共享相机Prefab。
            BoxCollider cameraBounds = new GameObject("CameraBounds").AddComponent<BoxCollider>();
            cameraBounds.enabled = true;
            cameraBounds.isTrigger = true;
            cameraBounds.center = new Vector3(0f, 4f, -12f);
            cameraBounds.size = new Vector3(8.1f, 3f, 2f);
            cameraBinder.confiner.BoundingVolume = cameraBounds;
            cameraBinder.player = player;
            if (!cameraBinder.ValidateBounds(out string boundsError))
            {
                throw new InvalidOperationException(boundsError);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(cameraBinder.confiner);
            PrefabUtility.RecordPrefabInstancePropertyModifications(cameraBinder);

            Material ground = Required<Material>("Assets/Art/Materials/Player/WhiteboxGround.mat");
            Material amber = Required<Material>("Assets/Art/Materials/Player/TestTarget.mat");
            Material red = Required<Material>("Assets/Art/Materials/Player/TestDamage.mat");
            Shape("Ground3D", new Vector3(0f, -0.5f, 0f), new Vector3(28f, 1f, 4f), ground);
            GameObject moving = Shape("WorldContinuesMoving", new Vector3(-4f, 3.3f, 1f), Vector3.one * 0.7f, amber);
            Object.DestroyImmediate(moving.GetComponent<Collider>());
            GameObject damage = Shape("RealDamageTrigger", new Vector3(3f, 0.2f, 0f), new Vector3(1.5f, 0.4f, 2f), red);
            damage.GetComponent<Collider>().isTrigger = true;
            damage.AddComponent<PlayerTestDamageSource>();
            Light light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(40f, -25f, 0f);
            RenderSettings.ambientLight = new Color(0.5f, 0.55f, 0.65f);

            Canvas canvas = new GameObject("Backpack Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            BackpackView view = Instantiate(PanelPath, canvas.transform).GetComponent<BackpackView>();
            // 监听者独立于被关闭的Panel，因此关闭后Tab仍然可以打开。
            BackpackUIController controller = new GameObject("Backpack Input Controller").AddComponent<BackpackUIController>();
            controller.manager = manager;
            controller.player = player;
            controller.view = view;
            controller.inputActions = Required<InputActionAsset>(InputPath);
            ItemTransferCoordinator transfers = new GameObject("Item Transfer Coordinator").AddComponent<ItemTransferCoordinator>();
            transfers.itemManager = items;
            transfers.backpackManager = manager;
            transfers.player = player;
            ItemTransferUIBridge bridge = new GameObject("Item Transfer UI Bridge").AddComponent<ItemTransferUIBridge>();
            bridge.coordinator = transfers;
            bridge.backpack = controller;
            BackpackDiscardUIBuilder.Attach(bridge, font);
            ApplyScenePresentation(canvas, view);
            BackpackTestDriver driver = new GameObject("Backpack Test Driver").AddComponent<BackpackTestDriver>();
            driver.player = player;
            driver.itemManager = items;
            driver.backpackManager = manager;
            driver.controller = controller;
            driver.movingObject = moving.transform;
            driver.statusText = HudText(canvas.transform, "Status", font, 24, 14, 34);
            driver.contextText = HudText(canvas.transform, "Context", font, 20, 49, 30);
            driver.closedHintsRoot = Rect(canvas.transform, "ClosedPanelHints", 0, 125, 1280, 180).gameObject;
            RectTransform hintsRect = driver.closedHintsRoot.GetComponent<RectTransform>();
            hintsRect.anchorMax = new Vector2(1f, 1f);
            hintsRect.sizeDelta = new Vector2(0f, 180f);
            driver.resultText = HudText(hintsRect, "SampleAndResult", font, 19, 0, 55);
            // 开包后使用包内提示；隐藏测试快捷键行，为丢弃按钮保留顶部空间。
            Localized(HudText(hintsRect, "Instructions", font, 18, -48, 34), "backpack.test.instructions");
            Localized(HudText(hintsRect, "Scope", font, 17, 60, 70), "backpack.test.scope");
            CreateEventSystem();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) { SceneManager.SetActiveScene(previous); }
        }
    }

    private static void CreateEventSystem()
    {
        GameObject root = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        InputSystemUIInputModule input = root.GetComponent<InputSystemUIInputModule>();
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
        // 现有导入器可能为同一actionId保留旧别名子资源；取第一个真实UI动作引用即可。
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
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static TMP_Text Text(Transform parent, string name, TMP_FontAsset font, int fontSize,
        float x, float y, float width, float height)
    {
        TMP_Text text = Rect(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = fontSize;
        text.color = new Color(0.91f, 0.95f, 0.98f);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private static TMP_Text HudText(Transform parent, string name, TMP_FontAsset font, int size, float y, float height)
    {
        TMP_Text text = Text(parent, name, font, size, 24, y, 1232, height);
        text.rectTransform.anchorMax = new Vector2(1f, 1f);
        text.rectTransform.sizeDelta = new Vector2(-48f, height);
        return text;
    }

    private static void Localized(TMP_Text text, string key)
    {
        LocalizedText localized = text.gameObject.AddComponent<LocalizedText>();
        localized.targetText = text;
        localized.key = key;
    }

    private static GameObject Shape(string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shape.name = name;
        shape.transform.position = position;
        shape.transform.localScale = scale;
        shape.GetComponent<Renderer>().sharedMaterial = material;
        return shape;
    }
}
