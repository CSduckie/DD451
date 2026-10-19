using System;
using System.IO;
using System.Linq;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// 仅生成009自己的新资产；已有玩家、背包、搜刮等Prefab只实例化，不Apply修改。
public static class EnemyTestSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Tests/Enemy/EnemyTest.unity";
    public const string DronePath = "Assets/Prefabs/Enemy/Drone/Drone.prefab";
    public const string CoverPath = "Assets/Prefabs/Enemy/Stealth/CoverArea.prefab";
    public const string ConfigPath = "Assets/Data/Enemy/Drone/DroneConfig.asset";
    public const string TextPath = "Assets/Data/Localization/Enemy/EnemyText.asset";
    private const string VisionMaterialPath = "Assets/Art/Materials/Enemy/DroneVision.mat";
    private const string ModelPath = "Assets/Art/Model/Enemy_Patrol_Drone_A.fbx";
    private const string InputPath = "Assets/Input/GameInputActions.inputactions";
    private const string FontPath = "Assets/Art/Fonts/Localization/NotoSansCJKsc-Regular SDF.asset";
    private static TMP_FontAsset font;

    [MenuItem("Tools/MyPrecious/Create Enemy Test Assets")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
        {
            throw new InvalidOperationException("Stop Play and wait for compilation before building EnemyTest.");
        }
        font = Required<TMP_FontAsset>(FontPath);
        LootBox sourceBox = Required<GameObject>("Assets/Prefabs/Loot/LootBox.prefab").GetComponent<LootBox>();
        BoxCollider sourceTrigger = sourceBox != null ? sourceBox.interactionTrigger : null;
        // 只消费07C升级后的Prefab；不能在009悄悄补范围掩盖共享资产尚未完成。
        if (sourceTrigger == null || !sourceTrigger.isTrigger
            || sourceTrigger.GetComponentInParent<Interactable>(true) != sourceBox)
        {
            throw new InvalidOperationException("EnemyTest requires the 07C-upgraded LootBox prefab with its explicit interactionTrigger.");
        }
        Scene previous = SceneManager.GetActiveScene();
        Scene workspace = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(workspace);
        try
        {
            // Prefab模板也在临时场景创建，避免把当前用户场景标成dirty。
            EnsureFolder(Path.GetDirectoryName(ScenePath));
            LocalizationTableSO text = CreateText();
            BuildDroneAssets();
            BuildCoverAsset();
            if (!File.Exists(ScenePath))
            {
                // Unity不允许在未保存的临时场景旁再次新建场景；先释放自己的模板工作区。
                EditorSceneManager.CloseScene(workspace, true);
                SceneManager.SetActiveScene(previous);
                CreateScene(text);
            }
            else { Debug.Log("EnemyTest already exists; its saved content and GUID were preserved: " + ScenePath); }
        }
        finally
        {
            if (workspace.IsValid() && workspace.isLoaded) { EditorSceneManager.CloseScene(workspace, true); }
            if (previous.IsValid() && previous.isLoaded) { SceneManager.SetActiveScene(previous); }
        }
    }

    private static void BuildDroneAssets()
    {
        EnsureFolder(Path.GetDirectoryName(ConfigPath));
        DroneConfigSO config = AssetDatabase.LoadAssetAtPath<DroneConfigSO>(ConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<DroneConfigSO>();
            config.patrolSpeed = 1.5f;
            config.chaseSpeed = 3f;
            config.arrivalDistance = 0.15f;
            config.patrolWaitSeconds = 1f;
            config.discoveryAlertSeconds = 0.5f;
            config.coverAlertSeconds = 2f;
            config.searchSeconds = 2f;
            config.blockedReturnSeconds = 2f;
            config.contactDamage = 1;
            AssetDatabase.CreateAsset(config, ConfigPath);
        }
        EnsureFolder(Path.GetDirectoryName(VisionMaterialPath));
        Material visionMaterial = AssetDatabase.LoadAssetAtPath<Material>(VisionMaterialPath);
        if (visionMaterial == null)
        {
            Shader shader = Shader.Find("MyPrecious/DroneVision");
            if (shader == null) { throw new InvalidOperationException("MyPrecious/DroneVision shader is required for drone vision."); }
            visionMaterial = new Material(shader);
            visionMaterial.SetColor("_BaseColor", new Color(1f, 0.72f, 0.12f, 0.22f));
            visionMaterial.SetColor("_FillColor", new Color(1f, 0.22f, 0.12f, 0.24f));
            visionMaterial.SetFloat("_FillProgress", 0f);
            visionMaterial.SetFloat("_Cull", (float)CullMode.Back);
            visionMaterial.renderQueue = (int)RenderQueue.Transparent;
            AssetDatabase.CreateAsset(visionMaterial, VisionMaterialPath);
        }
        if (File.Exists(DronePath)) { return; }
        EnsureFolder(Path.GetDirectoryName(DronePath));
        GameObject root = new GameObject("Drone");
        try
        {
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.isKinematic = true;
            body.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            SphereCollider contact = root.AddComponent<SphereCollider>();
            contact.radius = 0.45f;
            DroneController controller = root.AddComponent<DroneController>();
            controller.config = config;
            controller.collisionMask = Physics.DefaultRaycastLayers;
            controller.initialFacing = 1;
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            visual.localRotation = Quaternion.Euler(0, 90, 0);
            GameObject model = Instantiate(ModelPath, visual);
            // 已检查FBX：保留模型自带局部姿态，四桨叶绕各自局部Z轴转。
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) { Object.DestroyImmediate(collider); }
            controller.visual = visual;
            DroneFlightVisual flight = root.AddComponent<DroneFlightVisual>();
            flight.visual = visual;
            string[] names = { "Propeller_FL", "Propeller_FR", "Propeller_RL", "Propeller_RR" };
            flight.propellers = names.Select(name => model.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child.name == name)).ToArray();
            if (flight.propellers.Any(propeller => propeller == null))
            {
                throw new InvalidOperationException("The inspected drone model must contain all four named propeller nodes.");
            }
            controller.flightVisual = flight;
            GameObject visionObject = new GameObject("Vision", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            visionObject.transform.SetParent(root.transform, false);
            MeshRenderer renderer = visionObject.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = visionMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            DroneVision vision = visionObject.AddComponent<DroneVision>();
            vision.radius = 6f;
            vision.angleDegrees = 80f;
            vision.thickness = 2f;
            vision.segments = 32;
            vision.directionDegrees = -12f;
            vision.playerMask = Physics.DefaultRaycastLayers;
            vision.obstructionMask = Physics.DefaultRaycastLayers;
            controller.vision = vision;
            PrefabUtility.SaveAsPrefabAsset(root, DronePath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void BuildCoverAsset()
    {
        if (File.Exists(CoverPath)) { return; }
        EnsureFolder(Path.GetDirectoryName(CoverPath));
        GameObject root = new GameObject("CoverArea", typeof(BoxCollider), typeof(CoverArea));
        try
        {
            BoxCollider trigger = root.GetComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(3f, 2.3f, 2.4f);
            trigger.center = new Vector3(0f, 1.15f, 0f);
            GameObject view = Cube("CoverVisual", new Vector3(0, 0.55f, 0.8f), new Vector3(3f, 1.1f, 0.25f), "TestTarget");
            view.transform.SetParent(root.transform, false);
            // 掩体只有Trigger；与实体墙的遮挡规则分开，站立不会被这块装饰挡住。
            Object.DestroyImmediate(view.GetComponent<Collider>());
            PrefabUtility.SaveAsPrefabAsset(root, CoverPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void CreateScene(LocalizationTableSO text)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            GameObject bootstrap = Instantiate("Assets/Prefabs/Bootstrap/GameBootstrap.prefab");
            ItemManager items = bootstrap.GetComponentInChildren<ItemManager>(true);
            if (items == null) { items = Instantiate("Assets/Prefabs/Items/ItemModule.prefab", bootstrap.transform).GetComponent<ItemManager>(); }
            LocalizationManager language = bootstrap.GetComponentInChildren<LocalizationManager>(true);
            if (language == null) { language = Instantiate("Assets/Prefabs/Localization/LocalizationModule.prefab", bootstrap.transform).GetComponent<LocalizationManager>(); }
            BackpackManager backpack = Instantiate("Assets/Prefabs/Backpack/BackpackModule.prefab", bootstrap.transform).GetComponent<BackpackManager>();
            LootRuntimeManager loot = Instantiate("Assets/Prefabs/Loot/LootModule.prefab", bootstrap.transform).GetComponent<LootRuntimeManager>();
            backpack.itemManager = items;
            loot.itemManager = items;
            foreach (LocalizationTableSO table in new[] { text,
                Required<LocalizationTableSO>("Assets/Data/Localization/Backpack/BackpackText.asset"),
                Required<LocalizationTableSO>("Assets/Data/Localization/Loot/LootText.asset") })
            {
                if (!language.tables.Contains(table)) { language.tables.Add(table); }
            }
            Record(backpack, loot, language);

            PlayerController player = Instantiate("Assets/Prefabs/Player/Player.prefab").GetComponent<PlayerController>();
            player.transform.position = new Vector3(-13f, 0.05f, 0f);
            PlayerCameraTargetBinder camera = Instantiate("Assets/Prefabs/Player/PlayerCamera.prefab").GetComponent<PlayerCameraTargetBinder>();
            camera.player = player;
            // 复用02B的原生边界契约，只为本测试场景提供范围，不改共享相机Prefab或镜头。
            BoxCollider cameraBounds = new GameObject("CameraBounds").AddComponent<BoxCollider>();
            cameraBounds.isTrigger = true;
            cameraBounds.center = new Vector3(0f, 3f, -12f);
            cameraBounds.size = new Vector3(16f, 6f, 4f);
            CinemachineConfiner3D confiner = camera.followCamera.GetComponent<CinemachineConfiner3D>();
            if (confiner == null) { confiner = camera.followCamera.gameObject.AddComponent<CinemachineConfiner3D>(); }
            confiner.BoundingVolume = cameraBounds;
            confiner.SlowingDistance = 1f;
            camera.confiner = confiner;
            if (!camera.ValidateBounds(out string boundsError)) { throw new InvalidOperationException(boundsError); }
            Record(confiner);
            Record(player.transform, camera);
            Cube("Ground3D", new Vector3(0, -0.5f, 0), new Vector3(36, 1, 4), "WhiteboxGround");
            Cube("LeftBoundaryWall", new Vector3(-18f, 2f, 0), new Vector3(0.5f, 4f, 4f), "WhiteboxGround");
            Cube("RightBoundaryWall", new Vector3(18f, 2f, 0), new Vector3(0.5f, 4f, 4f), "WhiteboxGround");
            Cube("SolidSightWall", new Vector3(2f, 1f, 0), new Vector3(0.7f, 2f, 2.5f), "WhiteboxGround");
            Light light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(40f, -25f, 0f);
            RenderSettings.ambientLight = new Color(0.5f, 0.55f, 0.65f);

            PlaceCover("CoverSingle", -9.5f);
            PlaceCover("CoverOverlapA", 6f);
            PlaceCover("CoverOverlapB", 7.5f);
            DroneController first = PlaceDrone("Drone_A", new Vector3(-6, 2.4f, 0), new Vector3(-1, 2.4f, 0), -1);
            DroneController second = PlaceDrone("Drone_B", new Vector3(6, 2.4f, 0), new Vector3(12, 2.4f, 0), 1);

            Canvas canvas = new GameObject("EnemyTest Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0f;
            BackpackView backpackView = Instantiate("Assets/Prefabs/UI/Backpack/BackpackPanel.prefab", canvas.transform).GetComponent<BackpackView>();
            Transform preview = backpackView.transform.Find("LootContainerPreview");
            if (preview != null) { preview.gameObject.SetActive(false); Record(preview.gameObject); }
            Transform inventory = backpackView.transform.Find("PlayerInventory");
            SetRect(inventory, 24, 180, 360, 720);
            Transform inset = inventory.Find("Inset");
            if (inset != null) { SetRect(inset, 3, 3, 354, 714); Record(inset); }
            Record(inventory);
            LootView lootView = Instantiate("Assets/Prefabs/UI/Loot/LootPanel.prefab", canvas.transform).GetComponent<LootView>();
            SetRect(lootView.transform, 1412, 180, 484, 808);
            Record(lootView.transform);
            BackpackUIController backpackUI = new GameObject("Backpack Controller").AddComponent<BackpackUIController>();
            backpackUI.manager = backpack;
            backpackUI.player = player;
            backpackUI.view = backpackView;
            backpackUI.inputActions = Required<InputActionAsset>(InputPath);
            LootUIController lootUI = new GameObject("Loot Controller").AddComponent<LootUIController>();
            lootUI.backpack = backpackUI;
            lootUI.runtime = loot;
            lootUI.localization = language;
            lootUI.view = lootView;
            ItemTransferCoordinator transfers = new GameObject("Transfer Coordinator").AddComponent<ItemTransferCoordinator>();
            transfers.itemManager = items;
            transfers.backpackManager = backpack;
            transfers.lootRuntime = loot;
            transfers.player = player;
            ItemTransferUIBridge bridge = new GameObject("Transfer UI Bridge").AddComponent<ItemTransferUIBridge>();
            bridge.coordinator = transfers;
            bridge.backpack = backpackUI;
            bridge.loot = lootUI;
            lootUI.transfers = bridge;
            LootBox box = Instantiate("Assets/Prefabs/Loot/LootBox.prefab").GetComponent<LootBox>();
            box.name = "EnemyTest_SearchBox";
            box.transform.position = new Vector3(-8.4f, 0.43f, 0);
            box.mapId = "enemy-test";
            box.sourceId = "search-box";
            box.lootTable = Required<LootTableSO>("Assets/Data/LootTables/Guaranteed.asset");
            box.runtimeManager = loot;
            box.lootUI = lootUI;
            Record(box, box.transform);

            EnemyTestDriver driver = new GameObject("Enemy Test Driver").AddComponent<EnemyTestDriver>();
            driver.player = player;
            driver.cameraBinder = camera;
            driver.backpackUI = backpackUI;
            driver.lootUI = lootUI;
            driver.transfers = transfers;
            driver.transferUI = bridge;
            driver.lootBox = box;
            driver.drones = new[] { first, second };
            Localized(Text(canvas.transform, "Title", 24, 18, 1100, 40, 26), "enemy.test.title");
            driver.statusText = Text(canvas.transform, "Status", 24, 62, 1800, 36, 21);
            driver.resultText = Text(canvas.transform, "Result", 24, 104, 1320, 52, 19);
            Localized(Text(canvas.transform, "Instructions", 24, 1000, 1872, 66, 20), "enemy.test.instructions");
            Button next = Button(canvas.transform, "NextEnemyRound", 1452, 16, 444, 40, "enemy.test.next_round");
            Button same = Button(canvas.transform, "RepeatEnemyRound", 1452, 108, 444, 40, "enemy.test.same_round");
            UnityEventTools.AddPersistentListener(next.onClick, driver.BeginNextEnemyRound);
            UnityEventTools.AddPersistentListener(same.onClick, driver.RepeatEnemyRound);
            CreateEventSystem();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) { SceneManager.SetActiveScene(previous); }
        }
    }

    private static DroneController PlaceDrone(string name, Vector3 start, Vector3 end, int facing)
    {
        DroneController drone = Instantiate(DronePath).GetComponent<DroneController>();
        drone.name = name;
        drone.transform.position = start;
        Transform route = new GameObject(name + "_PatrolPoints").transform;
        Transform a = new GameObject("Point_A").transform;
        Transform b = new GameObject("Point_B").transform;
        a.SetParent(route, false);
        b.SetParent(route, false);
        a.position = start;
        b.position = end;
        drone.patrolPoints = new[] { a, b };
        drone.initialFacing = facing;
        Record(drone, drone.transform);
        return drone;
    }

    private static void PlaceCover(string name, float x)
    {
        GameObject cover = Instantiate(CoverPath);
        cover.name = name;
        cover.transform.position = new Vector3(x, 0, 0);
        Record(cover.transform);
    }

    private static LocalizationTableSO CreateText()
    {
        LocalizationTableSO table = AssetDatabase.LoadAssetAtPath<LocalizationTableSO>(TextPath);
        if (table != null) { return table; }
        EnsureFolder(Path.GetDirectoryName(TextPath));
        table = ScriptableObject.CreateInstance<LocalizationTableSO>();
        Add(table, "enemy.test.title", "009 无人机与潜行 · 独立测试", "009 Drone and stealth - isolated test");
        Add(table, "enemy.test.status", "敌人测试轮次 {0}  |  生命 {1}/{2}  |  {3}  |  背包{4}", "Enemy test round {0}  |  HP {1}/{2}  |  {3}  |  Backpack {4}");
        Add(table, "enemy.test.drone_status", "  |  无人机{0}：{1}", "  |  Drone {0}: {1}");
        Add(table, "enemy.test.instructions", "A/D 移动 · 空格跳跃 · C 蹲伏 · Tab 背包 · 箱旁 E 搜索 · F10 中英切换\n黄色矮挡板是蹲伏掩体；实墙独立遮挡。右侧两块掩体重叠；死亡后重新运行本场景。", "A/D move - Space jump - C crouch - Tab backpack - E near box to search - F10 language\nYellow low barriers hide crouching players; the solid wall blocks sight separately. Two covers overlap on the right. Restart Play after death.");
        Add(table, "enemy.test.ready", "使用真实背包与搜索；开包时世界继续运行。敌人轮次按钮不回血、不结算或刷新物品。", "Real backpack and search; the world keeps running while open. Enemy round buttons do not heal, settle or refresh items.");
        Add(table, "enemy.test.next_round", "下一敌人测试轮次", "Next enemy test round");
        Add(table, "enemy.test.same_round", "重复初始化本敌人轮次", "Repeat current enemy round");
        Add(table, "enemy.test.next_round_done", "已调用正式 BeginRound：仅敌人恢复。生命、背包、箱子保持原样。", "Called BeginRound: only enemies reset. Health, backpack and box remain unchanged.");
        Add(table, "enemy.test.same_round_done", "已再次调用同轮 BeginRound；已自爆无人机应保持自爆。", "Called BeginRound with the same round. Exploded drones must remain exploded.");
        Add(table, "enemy.test.failed", "测试初始化失败，请查看 Console；未把失败当成玩法通过。", "Test setup failed. Check Console; this is not a gameplay pass.");
        Add(table, "enemy.test.crouching", "蹲伏", "Crouching");
        Add(table, "enemy.test.standing", "站立", "Standing");
        Add(table, "enemy.test.open", "打开", "open");
        Add(table, "enemy.test.closed", "关闭", "closed");
        Add(table, "enemy.state.none", "未运行", "Inactive");
        Add(table, "enemy.state.patrol", "巡逻", "Patrol");
        Add(table, "enemy.state.chase", "追击", "Chase");
        Add(table, "enemy.state.alert", "警戒", "Alert");
        Add(table, "enemy.state.search", "搜索", "Search");
        Add(table, "enemy.state.explode", "已自爆", "Exploded");
        AssetDatabase.CreateAsset(table, TextPath);
        return table;
    }

    private static void Add(LocalizationTableSO table, string key, string chinese, string english)
    {
        table.entries.Add(new LocalizationEntry { key = key, chinese = chinese, english = english });
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
        return AssetDatabase.LoadAllAssetsAtPath(InputPath).OfType<InputActionReference>()
            .First(reference => reference.action.actionMap.name == "UI" && reference.action.name == name);
    }

    private static GameObject Cube(string name, Vector3 position, Vector3 scale, string material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.position = position;
        cube.transform.localScale = scale;
        cube.GetComponent<Renderer>().sharedMaterial = Required<Material>("Assets/Art/Materials/Player/" + material + ".mat");
        return cube;
    }

    private static TMP_Text Text(Transform parent, string name, float x, float y, float width, float height, int size)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetRect(rect, x, y, width, height);
        TMP_Text text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.color = new Color(0.94f, 0.96f, 0.98f);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }

    private static Button Button(Transform parent, string name, float x, float y, float width, float height, string key)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        SetRect(rect, x, y, width, height);
        Image background = rect.GetComponent<Image>();
        background.color = new Color(0.16f, 0.3f, 0.38f, 0.95f);
        Button button = rect.GetComponent<Button>();
        button.targetGraphic = background;
        TMP_Text label = Text(rect, "Label", 8, 3, width - 16, height - 6, 20);
        label.alignment = TextAlignmentOptions.Center;
        Localized(label, key);
        return button;
    }

    private static void Localized(TMP_Text text, string key)
    {
        LocalizedText localized = text.gameObject.AddComponent<LocalizedText>();
        localized.targetText = text;
        localized.key = key;
    }

    private static void SetRect(Transform target, float x, float y, float width, float height)
    {
        RectTransform rect = (RectTransform)target;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Record(params Object[] targets)
    {
        foreach (Object target in targets) { PrefabUtility.RecordPrefabInstancePropertyModifications(target); }
    }

    private static GameObject Instantiate(string path, Transform parent = null)
    {
        return (GameObject)PrefabUtility.InstantiatePrefab(Required<GameObject>(path), parent);
    }

    private static T Required<T>(string path) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) { throw new InvalidOperationException("Missing required EnemyTest asset: " + path); }
        return asset;
    }

    private static void EnsureFolder(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (AssetDatabase.IsValidFolder(normalized)) { return; }
        string parent = Path.GetDirectoryName(normalized).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(normalized));
    }
}
