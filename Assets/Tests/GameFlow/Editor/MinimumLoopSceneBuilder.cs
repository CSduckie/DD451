using System;
using System.Collections.Generic;
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
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 正式场景搭建器只复用真实模块；不复制测试场景，也不携带发物/切轮Driver。
public static class MinimumLoopSceneBuilder
{
    public const string BootstrapPath = "Assets/Scenes/Bootstrap.unity";
    public const string ShelterPath = "Assets/Scenes/Shelter.unity";
    public const string RuinsPath = "Assets/Scenes/Ruins.unity";
    private const string ModulePath = "Assets/Prefabs/GameFlow/GameFlowModule.prefab";
    private const string HudPath = "Assets/Prefabs/UI/GameFlow/GameFlowHUD.prefab";
    private const string TextPath = "Assets/Data/Localization/GameFlow/GameFlowText.asset";
    private const string TablePath = "Assets/Data/LootTables/MinimumLoopLoot.asset";
    private const string InputPath = "Assets/Input/GameInputActions.inputactions";
    private static TMP_FontAsset font;

    [MenuItem("Tools/MyPrecious/Update Minimum Loop Responsive Layout")]
    public static void UpdateResponsiveLayouts()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { throw new InvalidOperationException("请停止Play后更新布局接线。"); }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty) { throw new InvalidOperationException("请先保存当前场景。"); }
        }
        Scene previous = SceneManager.GetActiveScene();
        GameObject module = PrefabUtility.LoadPrefabContents(ModulePath);
        try
        {
            ConfigureFallbackHud(module.GetComponent<GameFlowManager>().bootstrapHUD);
            PrefabUtility.SaveAsPrefabAsset(module, ModulePath);
        }
        finally { PrefabUtility.UnloadPrefabContents(module); }
        foreach (string path in new[] { ShelterPath, RuinsPath })
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); }
            try
            {
                GameFlowSceneBindings bindings = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<GameFlowSceneBindings>(true)).Single();
                ConfigureResponsiveLayout(bindings);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) { throw new InvalidOperationException("无法保存：" + path); }
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) { SceneManager.SetActiveScene(previous); }
                if (opened) { EditorSceneManager.CloseScene(scene, true); }
            }
        }
    }

    private static void ConfigureResponsiveLayout(GameFlowSceneBindings bindings)
    {
        GameFlowResponsiveLayout layout = bindings.GetComponent<GameFlowResponsiveLayout>();
        if (layout == null) { layout = bindings.gameObject.AddComponent<GameFlowResponsiveLayout>(); }
        layout.canvas = bindings.backpackUI.view.GetComponentInParent<Canvas>(true);
        layout.backpackView = bindings.backpackUI.view;
        layout.warehouseView = bindings.warehouseUI != null ? bindings.warehouseUI.view : null;
        layout.shelterView = bindings.shelterView;
        layout.lootView = bindings.lootUI.view;
        layout.hud = bindings.hud;
        ConfigureDesignCanvas(layout.canvas);
        if (layout.hud != null) { ConfigureDesignCanvas(layout.hud.GetComponentInParent<Canvas>(true)); }
        if (layout.lootView.gridRoot == null || layout.lootView.cellPrefab == null
            || layout.lootView.itemPrefab == null || layout.lootView.itemPrefab.countdownText == null)
        {
            throw new InvalidOperationException("正式箱子缺少06A背景格或倒计时引用，请先完成06A资源升级。");
        }
        SetRect(layout.lootView.transform, 1412, 132, 484, 808);
        Record(layout.lootView.transform);
        EditorUtility.SetDirty(layout);
    }

    private static void ConfigureDesignCanvas(Canvas canvas)
    {
        if (canvas == null) { return; }
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;
        EditorUtility.SetDirty(scaler);
        Record(scaler);
    }

    private static void ConfigureFallbackHud(GameFlowHUD hud)
    {
        GameFlowResponsiveLayout layout = hud.GetComponent<GameFlowResponsiveLayout>();
        if (layout == null) { layout = hud.gameObject.AddComponent<GameFlowResponsiveLayout>(); }
        layout.hud = hud;
        EditorUtility.SetDirty(layout);
    }

    [MenuItem("Tools/MyPrecious/Create Minimum Loop Assets")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { throw new InvalidOperationException("请停止Play后搭建正式场景。"); }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (SceneManager.GetSceneAt(i).isDirty) { throw new InvalidOperationException("存在未保存场景；正式搭建器不会覆盖或自动保存它。"); }
        }
        ValidateDependencies();
        foreach (string folder in new[] { "Assets/Prefabs/GameFlow", "Assets/Prefabs/UI/GameFlow", "Assets/Data/Localization/GameFlow", "Assets/Data/LootTables" })
        {
            Directory.CreateDirectory(folder);
        }
        AssetDatabase.Refresh();
        font = Required<TMP_FontAsset>("Assets/Art/Fonts/Localization/NotoSansCJKsc-Regular SDF.asset");
        CreateTextTable();
        CreateLootTable();
        CreateHudPrefab();
        CreateInteractionPrefab("ShelterStoragePoint", GameFlowInteraction.Storage, "flow.label.storage");
        CreateInteractionPrefab("DeparturePoint", GameFlowInteraction.Departure, "flow.label.departure");
        CreateInteractionPrefab("ExtractionPoint", GameFlowInteraction.Extraction, "flow.label.extraction");
        CreateModulePrefab();
        if (!File.Exists(ShelterPath)) { CreateGameplayScene(true); }
        if (!File.Exists(RuinsPath)) { CreateGameplayScene(false); }
        if (!File.Exists(BootstrapPath)) { CreateBootstrapScene(); }
        // 保留其他场景与启用标记；正式入口固定第一位。
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(BootstrapPath, true),
            new EditorBuildSettingsScene(ShelterPath, true),
            new EditorBuildSettingsScene(RuinsPath, true)
        };
        scenes.AddRange(EditorBuildSettings.scenes.Where(scene => scene.path != BootstrapPath && scene.path != ShelterPath && scene.path != RuinsPath));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("MinimumLoop正式资产已就绪；已有场景与Prefab保留，不覆盖手工修改。入口：" + BootstrapPath);
    }

    private static void ValidateDependencies()
    {
        foreach (string path in new[]
        {
            "Assets/Prefabs/Bootstrap/GameBootstrap.prefab", "Assets/Prefabs/Items/ItemModule.prefab",
            "Assets/Prefabs/Backpack/BackpackModule.prefab", "Assets/Prefabs/Warehouse/WarehouseModule.prefab",
            "Assets/Prefabs/Shelter/ShelterModule.prefab", "Assets/Prefabs/Localization/LocalizationModule.prefab",
            "Assets/Prefabs/Player/Player.prefab", "Assets/Prefabs/Player/PlayerCamera.prefab",
            "Assets/Prefabs/UI/Backpack/BackpackPanel.prefab", "Assets/Prefabs/UI/Warehouse/WarehousePanel.prefab",
            "Assets/Prefabs/UI/Shelter/ShelterPanel.prefab", "Assets/Prefabs/Loot/LootModule.prefab",
            "Assets/Prefabs/Loot/LootBox.prefab", "Assets/Prefabs/Loot/GroundItem.prefab", "Assets/Prefabs/UI/Loot/LootPanel.prefab"
        }) { Required<GameObject>(path); }
        Required<LocalizationTableSO>("Assets/Data/Localization/Loot/LootText.asset");
        Required<ItemLibrarySO>("Assets/Data/Items/ItemLibrary.asset");
        Required<InputActionAsset>(InputPath);
    }

    private static void CreateTextTable()
    {
        if (AssetDatabase.LoadAssetAtPath<LocalizationTableSO>(TextPath) != null) { return; }
        LocalizationTableSO table = ScriptableObject.CreateInstance<LocalizationTableSO>();
        Add(table, "flow.interact.storage", "E  整理背包与仓库", "E  Open backpack and storage");
        Add(table, "flow.interact.departure", "E  出发前往废墟", "E  Depart for the ruins");
        Add(table, "flow.interact.extraction", "E  立即撤离并返回庇护所", "E  Extract and return to shelter");
        Add(table, "flow.label.storage", "仓库", "STORAGE");
        Add(table, "flow.label.departure", "出发 →", "DEPART →");
        Add(table, "flow.label.extraction", "撤离 →", "EXTRACT →");
        Add(table, "flow.label.loot", "搜索物资", "SEARCH SUPPLIES");
        Add(table, "flow.label.ground", "地面物品", "GROUND ITEM");
        Add(table, "flow.stage.booting", "正在准备庇护所", "Preparing shelter");
        Add(table, "flow.stage.shelter", "庇护所 · 自由整理", "Shelter · Prepare for departure");
        Add(table, "flow.stage.departing", "出发 · 正在准备废墟", "Departing · Preparing the ruins");
        Add(table, "flow.stage.exploring", "废墟 · 向右搜刮并撤离", "Ruins · Search and extract to the right");
        Add(table, "flow.stage.returning", "撤离 · 正在返回庇护所", "Extracting · Returning to shelter");
        Add(table, "flow.stage.deathreturning", "死亡 · 正在返回庇护所", "Defeated · Returning to shelter");
        Add(table, "flow.deathreturning", "正在结算丢失物品并返回庇护所。", "Settling lost items and returning to shelter.");
        Add(table, "flow.deathreturned", "已返回庇护所。背包物品已丢失，仓库物品已保留。", "Back at shelter. Backpack items were lost. Stored items are safe.");
        Add(table, "flow.stage.failed", "流程暂时中断", "The journey is interrupted");
        Add(table, "flow.health", "生命 {0}/{1}", "Health {0}/{1}");
        Add(table, "flow.controls", "A/D 移动  ·  空格 跳跃  ·  C 蹲伏  ·  E 交互  ·  Tab 背包  ·  R 旋转  ·  F10 中英切换", "A/D Move  ·  Space Jump  ·  C Crouch  ·  E Interact  ·  Tab Backpack  ·  R Rotate  ·  F10 Language");
        Add(table, "flow.retry", "重试", "Retry");
        Add(table, "flow.loading", "正在准备场景，请稍候。", "Preparing the scene. Please wait.");
        Add(table, "flow.booting", "正在进入庇护所。", "Entering the shelter.");
        Add(table, "flow.departing", "正在开始新一轮探索。", "Starting a new exploration round.");
        Add(table, "flow.returning", "正在确认带回与遗留物品。", "Checking carried items and items left behind.");
        Add(table, "flow.shelter", "前往仓库整理，或向右出发。", "Visit storage or depart to the right.");
        Add(table, "flow.exploring", "搜索物资，再到路线另一端互动撤离。", "Search supplies and interact at the far end to extract.");
        Add(table, "flow.ready", "准备就绪。", "Ready.");
        Add(table, "flow.shelter_ready", "前往仓库整理，或向右出发。", "Visit storage or depart to the right.");
        Add(table, "flow.exploration_ready", "搜索物资，再到路线另一端互动撤离。", "Search supplies and interact at the far end to extract.");
        Add(table, "flow.returned", "已安全返回；物品可留在背包或自由存取。", "Returned safely. Keep items in your backpack or move them to storage.");
        Add(table, "flow.failed", "准备未完成，当前物品与进度已保留。修正配置后可重试。", "Preparation failed. Items and progress are preserved. Correct the configuration and retry.");
        Add(table, "flow.error.dependencies", "系统准备未完成，请重试。", "The game is not ready yet. Please retry.");
        Add(table, "flow.error.scene", "目标地点暂时不可用，请重试。", "The destination is currently unavailable. Please retry.");
        Add(table, "flow.error.sources", "物资尚未准备好，本轮进度已保留，请重试。", "The supplies are not ready yet. This round's progress is preserved. Please retry.");
        Add(table, "flow.error.player", "玩家无法继续当前行程，请检查当前状态后重试。", "The player cannot continue this journey. Check the current state and retry.");
        Add(table, "flow.error.settlement", "物品核对未完成，进度已保留，请重试。", "The item check is incomplete. Your progress is preserved. Please retry.");
        Add(table, "flow.error.unload", "上一地点尚未关闭，请重试。", "The previous location has not closed yet. Please retry.");
        Add(table, "flow.busy", "当前操作尚未完成。", "The current operation is still in progress.");
        Add(table, "flow.player_unavailable", "玩家当前无法执行此操作。", "The player cannot perform this action now.");
        Add(table, "flow.storage_unavailable", "仓库暂时无法打开。", "Storage cannot be opened right now.");
        AssetDatabase.CreateAsset(table, TextPath);
    }

    private static void Add(LocalizationTableSO table, string key, string chinese, string english)
    {
        table.entries.Add(new LocalizationEntry { key = key, chinese = chinese, english = english });
    }

    private static void CreateLootTable()
    {
        if (AssetDatabase.LoadAssetAtPath<LootTableSO>(TablePath) != null) { return; }
        ItemLibrarySO library = Required<ItemLibrarySO>("Assets/Data/Items/ItemLibrary.asset");
        LootTableSO table = ScriptableObject.CreateInstance<LootTableSO>();
        table.entries.Add(new LootTableEntry { entryId = "doll", item = library.FindDefinition("doll"), useDefaultProbability = false, overrideProbability = 1f });
        table.entries.Add(new LootTableEntry { entryId = "medkit", item = library.FindDefinition("medkit"), useDefaultProbability = false, overrideProbability = 1f });
        table.entries.Add(new LootTableEntry { entryId = "food", item = library.FindDefinition("food"), useDefaultProbability = false, overrideProbability = 0.5f });
        if (!table.TryValidate(out string error)) { throw new InvalidOperationException(error); }
        AssetDatabase.CreateAsset(table, TablePath);
    }

    private static void CreateHudPrefab()
    {
        if (File.Exists(HudPath)) { return; }
        Canvas canvas = CreateCanvas("GameFlowHUD", null, 10);
        GameFlowHUD hud = canvas.gameObject.AddComponent<GameFlowHUD>();
        hud.stageText = Text(canvas.transform, "Stage", 28, 24, 18, 1190, 42);
        hud.healthText = Text(canvas.transform, "Health", 26, 1380, 18, 310, 42);
        hud.healthText.alignment = TextAlignmentOptions.Right;
        hud.hintText = Text(canvas.transform, "Controls", 20, 24, 65, 1660, 36);
        hud.feedbackText = Text(canvas.transform, "Feedback", 20, 24, 108, 1450, 40);
        hud.retryButton = Button(canvas.transform, "Retry", 1535, 106, 160, 42, out hud.retryText);
        hud.promptText = Text(canvas.transform, "InteractionPrompt", 26, 0, 0, 1260, 48);
        RectTransform prompt = hud.promptText.rectTransform;
        prompt.anchorMin = prompt.anchorMax = prompt.pivot = new Vector2(0.5f, 0f);
        prompt.anchoredPosition = new Vector2(0f, 36f);
        hud.promptText.alignment = TextAlignmentOptions.Center;
        PrefabUtility.SaveAsPrefabAsset(canvas.gameObject, HudPath);
        Object.DestroyImmediate(canvas.gameObject);
    }

    private static void CreateInteractionPrefab(string name, GameFlowInteraction action, string labelKey)
    {
        string path = "Assets/Prefabs/GameFlow/" + name + ".prefab";
        if (File.Exists(path)) { return; }
        GameObject root = Shape(name, Vector3.zero, Vector3.one, "TestTarget", null);
        root.GetComponent<BoxCollider>().size = new Vector3(1.2f, 1.4f, 0.6f);
        root.GetComponent<BoxCollider>().isTrigger = true;
        GameFlowInteractable interactable = root.AddComponent<GameFlowInteractable>();
        interactable.interactionType = action;
        ConfigureInteractionTrigger(interactable);
        Label(root.transform, labelKey, new Vector3(0, 1.35f, -0.15f));
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
    }

    // 只补缺少的显式范围；现有模型、实体Collider和策划已配置的范围均保留。
    public static void ConfigureInteractionTrigger(GameFlowInteractable target)
    {
        if (target.interactionTrigger != null) { return; }
        Transform existing = target.transform.Find("TriggerBox");
        if (existing != null) { throw new InvalidOperationException("未接线的TriggerBox需要先人工核对：" + target.name); }
        GameObject range = new GameObject("TriggerBox");
        range.transform.SetParent(target.transform, false);
        range.layer = target.gameObject.layer;
        BoxCollider trigger = range.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(3.6f, 2.4f, 2.4f);
        target.interactionTrigger = trigger;
        EditorUtility.SetDirty(target);
    }

    private static void CreateModulePrefab()
    {
        if (File.Exists(ModulePath)) { return; }
        GameObject root = new GameObject("GameFlowModule");
        GameFlowManager manager = root.AddComponent<GameFlowManager>();
        manager.transfers = root.AddComponent<ItemTransferCoordinator>();
        manager.playerPrefab = Required<GameObject>("Assets/Prefabs/Player/Player.prefab").GetComponent<PlayerController>();
        manager.shelterScenePath = ShelterPath;
        manager.ruinsScenePath = RuinsPath;
        manager.bootstrapPresentationRoot = new GameObject("BootstrapPresentation");
        manager.bootstrapPresentationRoot.transform.SetParent(root.transform, false);
        manager.bootstrapHUD = Instantiate(HudPath, manager.bootstrapPresentationRoot.transform).GetComponent<GameFlowHUD>();
        ConfigureFallbackHud(manager.bootstrapHUD);
        CreateEventSystem(manager.bootstrapPresentationRoot.transform);
        GameObject camera = new GameObject("BootstrapLoadingCamera", typeof(Camera), typeof(UniversalAdditionalCameraData), typeof(AudioListener));
        camera.transform.SetParent(manager.bootstrapPresentationRoot.transform, false);
        camera.transform.localPosition = new Vector3(0, 0, -10);
        camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
        camera.GetComponent<Camera>().backgroundColor = new Color(0.045f, 0.065f, 0.09f);
        PrefabUtility.SaveAsPrefabAsset(root, ModulePath);
        Object.DestroyImmediate(root);
    }

    private static void CreateBootstrapScene()
    {
        CreateScene(BootstrapPath, scene =>
        {
            GameObject root = Instantiate("Assets/Prefabs/Bootstrap/GameBootstrap.prefab");
            // 00入口已内置物品和语言模块，先复用，避免两份身份数据与F10监听。
            ItemManager items = root.GetComponentInChildren<ItemManager>(true);
            if (items == null) { items = Instantiate("Assets/Prefabs/Items/ItemModule.prefab", root.transform).GetComponent<ItemManager>(); }
            BackpackManager backpack = Instantiate("Assets/Prefabs/Backpack/BackpackModule.prefab", root.transform).GetComponent<BackpackManager>();
            WarehouseManager warehouse = Instantiate("Assets/Prefabs/Warehouse/WarehouseModule.prefab", root.transform).GetComponent<WarehouseManager>();
            ShelterManager shelter = Instantiate("Assets/Prefabs/Shelter/ShelterModule.prefab", root.transform).GetComponent<ShelterManager>();
            LocalizationManager language = root.GetComponentInChildren<LocalizationManager>(true);
            if (language == null) { language = Instantiate("Assets/Prefabs/Localization/LocalizationModule.prefab", root.transform).GetComponent<LocalizationManager>(); }
            LootRuntimeManager loot = Instantiate("Assets/Prefabs/Loot/LootModule.prefab", root.transform).GetComponent<LootRuntimeManager>();
            GameFlowManager flow = Instantiate(ModulePath, root.transform).GetComponent<GameFlowManager>();
            backpack.itemManager = items;
            warehouse.itemManager = items;
            shelter.itemManager = items;
            shelter.warehouseManager = warehouse;
            loot.itemManager = items;
            language.itemLibrary = items.itemLibrary;
            // 所有语言表在场景序列化时接齐；运行中Initialize后不追加表。
            foreach (string relative in new[] { "Backpack/BackpackText", "Warehouse/WarehouseText", "Shelter/ShelterText", "Loot/LootText", "GameFlow/GameFlowText" })
            {
                LocalizationTableSO table = Required<LocalizationTableSO>("Assets/Data/Localization/" + relative + ".asset");
                if (!language.tables.Contains(table)) { language.tables.Add(table); }
            }
            flow.items = items;
            flow.backpack = backpack;
            flow.warehouse = warehouse;
            flow.shelter = shelter;
            flow.loot = loot;
            flow.language = language;
            flow.transfers.itemManager = items;
            flow.transfers.backpackManager = backpack;
            flow.transfers.warehouseManager = warehouse;
            flow.transfers.shelterManager = shelter;
            flow.transfers.lootRuntime = loot;
            foreach (Object component in new Object[] { backpack, warehouse, shelter, language, loot, flow, flow.transfers })
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        });
    }

    private static void CreateGameplayScene(bool isShelter)
    {
        CreateScene(isShelter ? ShelterPath : RuinsPath, scene =>
        {
            GameFlowSceneBindings bindings = new GameObject("GameFlowSceneBindings").AddComponent<GameFlowSceneBindings>();
            bindings.isShelter = isShelter;
            bindings.mapId = isShelter ? "shelter" : "ruins01";
            bindings.environmentRoot = new GameObject("Environment3D");
            bindings.spawnPoint = new GameObject("PlayerSpawn").transform;
            bindings.spawnPoint.position = new Vector3(isShelter ? -8f : -12f, 0.05f, 0);
            Shape("ContinuousGround3D", new Vector3(0, -0.5f, 0), new Vector3(36, 1, 4), "WhiteboxGround", bindings.environmentRoot.transform);
            Shape("LeftBoundary3D", new Vector3(-17.5f, 2, 0), new Vector3(0.5f, 4, 4), "WhiteboxGround", bindings.environmentRoot.transform);
            Shape("RightBoundary3D", new Vector3(17.5f, 2, 0), new Vector3(0.5f, 4, 4), "WhiteboxGround", bindings.environmentRoot.transform);
            Light light = new GameObject("Directional Light").AddComponent<Light>();
            light.transform.SetParent(bindings.environmentRoot.transform, false);
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.transform.rotation = Quaternion.Euler(45, -25, 0);
            RenderSettings.ambientLight = new Color(0.5f, 0.55f, 0.65f);
            bindings.presentationRoot = new GameObject("ScenePresentation");
            bindings.cameraBinder = Instantiate("Assets/Prefabs/Player/PlayerCamera.prefab", bindings.presentationRoot.transform).GetComponent<PlayerCameraTargetBinder>();
            CreateCameraBounds(bindings.cameraBinder, bindings.environmentRoot.transform,
                isShelter ? new Vector3(0f, 3f, -12f) : new Vector3(0f, 4f, -12f),
                isShelter ? new Vector3(16.8f, 1f, 2f) : new Vector3(15.8f, 3f, 2f));
            bindings.hud = Instantiate(HudPath, bindings.presentationRoot.transform).GetComponent<GameFlowHUD>();
            CreateEventSystem(bindings.presentationRoot.transform);
            bindings.businessRoot = new GameObject("BusinessUI");
            bindings.businessRoot.transform.SetParent(bindings.presentationRoot.transform, false);
            CreateBusinessUI(bindings);
            if (isShelter)
            {
                PlaceInteraction("ShelterStoragePoint", new Vector3(-5, 0.7f, 0), bindings.environmentRoot.transform);
                PlaceInteraction("DeparturePoint", new Vector3(9, 0.7f, 0), bindings.environmentRoot.transform);
            }
            else
            {
                PlaceInteraction("ExtractionPoint", new Vector3(14, 0.7f, 0), bindings.environmentRoot.transform);
                Shape("OptionalJumpPlatform3D", new Vector3(6, 0.2f, 0), new Vector3(2, 0.4f, 2), "WhiteboxGround", bindings.environmentRoot.transform);
                LootBox box = Instantiate("Assets/Prefabs/Loot/LootBox.prefab", bindings.environmentRoot.transform).GetComponent<LootBox>();
                box.name = "SuppliesBox";
                box.transform.position = new Vector3(-7, 0.43f, 0);
                box.mapId = bindings.mapId;
                box.sourceId = "supplies-box";
                box.lootTable = Required<LootTableSO>(TablePath);
                box.gridWidth = 6;
                box.gridHeight = 4;
                box.lootUI = bindings.lootUI;
                Record(box, box.transform);
                Label(bindings.environmentRoot.transform, "flow.label.loot", new Vector3(-7, 2.1f, -0.15f));
                GroundItem ground = Instantiate("Assets/Prefabs/Loot/GroundItem.prefab", bindings.environmentRoot.transform).GetComponent<GroundItem>();
                ground.name = "BatteryGroundOrigin";
                ground.transform.position = new Vector3(0, 0.2f, 0);
                ground.mapId = bindings.mapId;
                ground.sourceId = "battery-ground";
                ground.entryId = "battery";
                ground.initialItem = Required<ItemLibrarySO>("Assets/Data/Items/ItemLibrary.asset").FindDefinition("battery");
                ground.lootUI = bindings.lootUI;
                Record(ground, ground.transform);
                Label(bindings.environmentRoot.transform, "flow.label.ground", new Vector3(0, 1.6f, -0.15f));
            }
            // 编辑态显示正式镜头与后处理；运行时由Bindings.Awake先关闭，Activate再开启。
            bindings.businessRoot.SetActive(false);
            bindings.hud.gameObject.SetActive(false);
            bindings.presentationRoot.GetComponentInChildren<EventSystem>(true).gameObject.SetActive(false);
        });
    }

    // 只供新场景搭建；现有场景由集成阶段逐项接线，不重建用户环境。
    public static BoxCollider CreateCameraBounds(PlayerCameraTargetBinder binder, Transform parent, Vector3 center, Vector3 size)
    {
        CinemachineConfiner3D confiner = binder.followCamera.GetComponent<CinemachineConfiner3D>();
        if (confiner == null) { confiner = binder.followCamera.gameObject.AddComponent<CinemachineConfiner3D>(); }
        GameObject range = new GameObject("CameraBounds");
        range.transform.SetParent(parent, false);
        range.transform.position = center;
        BoxCollider bounds = range.AddComponent<BoxCollider>();
        bounds.isTrigger = true;
        bounds.size = size;
        confiner.BoundingVolume = bounds;
        confiner.SlowingDistance = 0.5f;
        binder.confiner = confiner;
        Record(binder, confiner);
        return bounds;
    }

    private static void CreateBusinessUI(GameFlowSceneBindings bindings)
    {
        Canvas canvas = CreateCanvas("InventoryCanvas", bindings.businessRoot.transform, 0);
        BackpackView backpackView = Instantiate("Assets/Prefabs/UI/Backpack/BackpackPanel.prefab", canvas.transform).GetComponent<BackpackView>();
        Transform preview = backpackView.transform.Find("LootContainerPreview");
        if (preview != null) { preview.gameObject.SetActive(false); Record(preview.gameObject); }
        Transform inventory = backpackView.transform.Find("PlayerInventory");
        SetRect(inventory, 24, 160, 360, 680);
        Transform inset = inventory.Find("Inset");
        SetRect(inset, 3, 3, 354, 674);
        Record(inventory, inset);
        BackpackUIController backpack = new GameObject("BackpackController").AddComponent<BackpackUIController>();
        backpack.transform.SetParent(bindings.businessRoot.transform, false);
        backpack.view = backpackView;
        backpack.inputActions = Required<InputActionAsset>(InputPath);
        bindings.backpackUI = backpack;
        if (bindings.isShelter)
        {
            WarehouseView warehouseView = Instantiate("Assets/Prefabs/UI/Warehouse/WarehousePanel.prefab", canvas.transform).GetComponent<WarehouseView>();
            // 正式1920界面给66边长的20列仓库留宽度，内部布局由GameFlowResponsiveLayout统一设置。
            SetRect(warehouseView.transform, 408, 160, 1488, 720);
            SetRect(warehouseView.hintText.transform, 18, 628, 948, 55);
            warehouseView.hintText.fontSize = 18;
            SetRect(warehouseView.feedbackText.transform, 18, 686, 948, 26);
            warehouseView.feedbackText.fontSize = 16;
            Record(warehouseView.transform, warehouseView.hintText, warehouseView.hintText.rectTransform,
                warehouseView.feedbackText, warehouseView.feedbackText.rectTransform);
            WarehouseUIController warehouse = new GameObject("WarehouseController").AddComponent<WarehouseUIController>();
            warehouse.transform.SetParent(bindings.businessRoot.transform, false);
            warehouse.view = warehouseView;
            warehouse.inputActions = Required<InputActionAsset>(InputPath);
            bindings.warehouseUI = warehouse;
            ShelterView shelterView = Instantiate("Assets/Prefabs/UI/Shelter/ShelterPanel.prefab", canvas.transform).GetComponent<ShelterView>();
            SetRect(shelterView.transform, 24, 904, 1872, 152);
            shelterView.warehouseUI = warehouse;
            // 仓库始终与背包成对打开；整理面板内已有完整仓库，不再暴露单开仓库按钮。
            if (shelterView.openWarehouseButton != null) { shelterView.openWarehouseButton.gameObject.SetActive(false); Record(shelterView.openWarehouseButton.gameObject); }
            Record(shelterView, shelterView.transform);
            bindings.shelterView = shelterView;
        }
        LootView lootView = Instantiate("Assets/Prefabs/UI/Loot/LootPanel.prefab", canvas.transform).GetComponent<LootView>();
        SetRect(lootView.transform, 1412, 132, 484, 808);
        Record(lootView.transform);
        LootUIController loot = new GameObject("LootController").AddComponent<LootUIController>();
        loot.transform.SetParent(bindings.businessRoot.transform, false);
        loot.backpack = backpack;
        loot.view = lootView;
        bindings.lootUI = loot;
        ItemTransferUIBridge bridge = new GameObject("TransferUIBridge").AddComponent<ItemTransferUIBridge>();
        bridge.transform.SetParent(bindings.businessRoot.transform, false);
        bridge.backpack = backpack;
        bridge.warehouse = bindings.warehouseUI;
        bridge.loot = loot;
        loot.transfers = bridge;
        BackpackDiscardUIBuilder.Attach(bridge, font, 522f);
        SetRect(backpackView.hintText.transform, 20, 564, 320, 96);
        backpackView.hintText.fontSize = 18;
        Record(backpackView.hintText, backpackView.hintText.rectTransform);
        bindings.transferUI = bridge;
        ConfigureResponsiveLayout(bindings);
    }

    private static void CreateScene(string path, System.Action<Scene> fill)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene created = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(created);
        try
        {
            fill(created);
            if (!EditorSceneManager.SaveScene(created, path)) { throw new InvalidOperationException("无法保存：" + path); }
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) { SceneManager.SetActiveScene(previous); }
            EditorSceneManager.CloseScene(created, true);
        }
    }

    private static void PlaceInteraction(string prefab, Vector3 position, Transform parent)
    {
        GameObject target = Instantiate("Assets/Prefabs/GameFlow/" + prefab + ".prefab", parent);
        target.transform.position = position;
        Record(target.transform);
    }

    private static Canvas CreateCanvas(string name, Transform parent, int order)
    {
        Canvas canvas = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
        if (parent != null) { canvas.transform.SetParent(parent, false); }
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        // 当前开发基准固定为1920×1080。
        scaler.matchWidthOrHeight = 0f;
        return canvas;
    }

    private static void CreateEventSystem(Transform parent)
    {
        InputSystemUIInputModule input = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule)).GetComponent<InputSystemUIInputModule>();
        input.transform.SetParent(parent, false);
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
        return AssetDatabase.LoadAllAssetsAtPath(InputPath).OfType<InputActionReference>()
            .First(reference => reference.action.actionMap.name == "UI" && reference.action.name == name);
    }

    private static void Label(Transform parent, string key, Vector3 position)
    {
        TextMeshPro label = new GameObject("WorldLabel").AddComponent<TextMeshPro>();
        label.transform.SetParent(parent, false);
        label.transform.localPosition = position;
        label.rectTransform.sizeDelta = new Vector2(6, 0.8f);
        label.font = font;
        label.fontSize = 2.6f;
        label.alignment = TextAlignmentOptions.Center;
        LocalizedText text = label.gameObject.AddComponent<LocalizedText>();
        text.targetText = label;
        text.key = key;
    }

    private static GameObject Shape(string name, Vector3 position, Vector3 scale, string material, Transform parent)
    {
        GameObject shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shape.name = name;
        if (parent != null) { shape.transform.SetParent(parent, false); }
        shape.transform.localPosition = position;
        shape.transform.localScale = scale;
        shape.GetComponent<Renderer>().sharedMaterial = Required<Material>("Assets/Art/Materials/Player/" + material + ".mat");
        return shape;
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        if (parent != null) { rect.SetParent(parent, false); }
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
        text.color = new Color(0.93f, 0.97f, 1);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private static Button Button(Transform parent, string name, float x, float y, float width, float height, out TMP_Text label)
    {
        GameObject root = Rect(parent, name, x, y, width, height).gameObject;
        Image image = root.AddComponent<Image>();
        image.color = new Color(0.18f, 0.33f, 0.41f);
        Button button = root.AddComponent<Button>();
        button.targetGraphic = image;
        label = Text(root.transform, "Label", 18, 6, 4, width - 12, height - 8);
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }

    private static GameObject Instantiate(string path, Transform parent = null)
    {
        return (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>(path), parent);
    }

    private static T Required<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) { throw new InvalidOperationException("缺少真实依赖资源：" + path + "。先完成对应模块资源生成。"); }
        return asset;
    }

    private static void Record(params Object[] objects)
    {
        foreach (Object target in objects) { PrefabUtility.RecordPrefabInstancePropertyModifications(target); }
    }
}
