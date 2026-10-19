using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Unity.Cinemachine;

// 一次性白盒搭建；已有场景拒绝覆盖，不触碰共享Prefab与正式场景。
public static class PlayerTestSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/Tests/Player/PlayerTest.unity";

    [MenuItem("Tools/MyPrecious/Create Player Test Assets")]
    public static void Build()
    {
        if (File.Exists(ScenePath)) { Debug.Log("PlayerTest已存在，请直接打开，不覆盖已有调整。"); return; }
        foreach (string path in new[] { "Assets/Data/Player", "Assets/Prefabs/Player", "Assets/Art/Materials/Player", "Assets/Scenes/Tests/Player" })
        {
            Directory.CreateDirectory(path);
        }
        AssetDatabase.Refresh();
        PlayerConfigSO config = ScriptableObject.CreateInstance<PlayerConfigSO>();
        AssetDatabase.CreateAsset(config, "Assets/Data/Player/PlayerConfig.asset");
        Material teal = MakeMaterial("PlayerTeal", new Color(0.15f, 0.7f, 0.68f));
        Material grey = MakeMaterial("WhiteboxGround", new Color(0.25f, 0.31f, 0.39f));
        Material amber = MakeMaterial("TestTarget", new Color(0.95f, 0.68f, 0.22f));
        Material red = MakeMaterial("TestDamage", new Color(0.9f, 0.2f, 0.24f));
        PhysicsMaterial frictionless = new PhysicsMaterial("PlayerFrictionless");
        frictionless.staticFriction = 0f;
        frictionless.dynamicFriction = 0f;
        frictionless.frictionCombine = PhysicsMaterialCombine.Minimum;
        AssetDatabase.CreateAsset(frictionless, "Assets/Art/Materials/Player/PlayerFrictionless.physicMaterial");
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            GameObject playerObject = new GameObject("Player");
            Rigidbody body = playerObject.AddComponent<Rigidbody>();
            body.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
            CapsuleCollider capsule = playerObject.AddComponent<CapsuleCollider>();
            capsule.center = Vector3.up;
            capsule.height = 2f;
            capsule.radius = 0.4f;
            capsule.sharedMaterial = frictionless;
            PlayerController player = playerObject.AddComponent<PlayerController>();
            player.config = config;
            player.inputReader = playerObject.GetComponent<PlayerInputReader>();
            player.inputReader.inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/Input/GameInputActions.inputactions");
            player.health = playerObject.GetComponent<PlayerHealth>();
            player.health.config = config;
            player.interaction = playerObject.GetComponent<PlayerInteraction>();
            GameObject visual = Shape("CapsulePlaceholder", PrimitiveType.Capsule, Vector3.up, new Vector3(0.8f, 1f, 0.8f), teal);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(playerObject.transform, false);
            player.visual = visual.transform;
            GameObject marker = Shape("FacingMarker", PrimitiveType.Cube, new Vector3(0.5f, 0.35f, 0f), new Vector3(0.4f, 0.12f, 0.3f), amber);
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            marker.transform.SetParent(visual.transform, false);
            player.cameraTarget = new GameObject("CameraTarget").transform;
            player.cameraTarget.SetParent(playerObject.transform, false);
            player.cameraTarget.localPosition = new Vector3(0f, 1.3f, 0f);
            GameObject playerPrefab = PrefabUtility.SaveAsPrefabAsset(playerObject, "Assets/Prefabs/Player/Player.prefab");
            Object.DestroyImmediate(playerObject);

            GameObject cameraRoot = new GameObject("PlayerCamera");
            GameObject output = new GameObject("OutputCamera", typeof(Camera), typeof(AudioListener), typeof(UniversalAdditionalCameraData));
            output.tag = "MainCamera";
            output.transform.SetParent(cameraRoot.transform, false);
            output.transform.position = new Vector3(-4f, 2.5f, -12f);
            Camera camera = output.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(0.07f, 0.1f, 0.16f);
            CinemachineBrain brain = output.AddComponent<CinemachineBrain>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
            GameObject virtualObject = new GameObject("SideFollow");
            virtualObject.transform.SetParent(cameraRoot.transform, false);
            CinemachineCamera follow = virtualObject.AddComponent<CinemachineCamera>();
            follow.Lens.OrthographicSize = 5f;
            follow.Lens.NearClipPlane = 0.1f;
            follow.Lens.FarClipPlane = 100f;
            CinemachinePositionComposer composer = virtualObject.AddComponent<CinemachinePositionComposer>();
            composer.CameraDistance = 12f;
            composer.Damping = new Vector3(0.25f, 0.2f, 0f);
            composer.Composition.ScreenPosition = new Vector2(0f, -0.12f);
            composer.Composition.DeadZone.Enabled = false;
            CinemachineConfiner3D confiner = virtualObject.AddComponent<CinemachineConfiner3D>();
            confiner.SlowingDistance = 1f;
            PlayerCameraTargetBinder binder = cameraRoot.AddComponent<PlayerCameraTargetBinder>();
            binder.followCamera = follow;
            binder.confiner = confiner;
            GameObject cameraPrefab = PrefabUtility.SaveAsPrefabAsset(cameraRoot, "Assets/Prefabs/Player/PlayerCamera.prefab");
            Object.DestroyImmediate(cameraRoot);

            playerObject = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            playerObject.transform.position = new Vector3(-4f, 0.05f, 0f);
            player = playerObject.GetComponent<PlayerController>();
            cameraRoot = (GameObject)PrefabUtility.InstantiatePrefab(cameraPrefab);
            binder = cameraRoot.GetComponent<PlayerCameraTargetBinder>();
            // 只为新建白盒场景创建独立相机位置范围，不能把场景引用写入共享Prefab。
            GameObject boundsObject = new GameObject("CameraBounds");
            boundsObject.transform.position = new Vector3(0f, 3f, -12f);
            BoxCollider cameraBounds = boundsObject.AddComponent<BoxCollider>();
            cameraBounds.isTrigger = true;
            cameraBounds.size = new Vector3(26f, 8f, 6f);
            binder.confiner.BoundingVolume = cameraBounds;
            binder.player = player;
            binder.followCamera.Follow = player.cameraTarget;
            PrefabUtility.RecordPrefabInstancePropertyModifications(binder);
            PrefabUtility.RecordPrefabInstancePropertyModifications(binder.followCamera);
            PrefabUtility.RecordPrefabInstancePropertyModifications(binder.confiner);
            GameObject bootstrap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Bootstrap/GameBootstrap.prefab"));
            GameObject module = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Items/ItemModule.prefab"));
            module.transform.SetParent(bootstrap.transform, false);
            Transform spawn = new GameObject("RoundSpawn").transform;
            spawn.position = player.transform.position;
            PlayerTestDriver driver = new GameObject("PlayerTestPanel").AddComponent<PlayerTestDriver>();
            driver.player = player;
            driver.itemManager = module.GetComponent<ItemManager>();
            driver.spawnPoint = spawn;
            // 03的模块独占F10；重建白盒场景时沿用同一预制体和打包字体。
            GameObject languagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Localization/LocalizationModule.prefab");
            if (languagePrefab != null) { PrefabUtility.InstantiatePrefab(languagePrefab, bootstrap.transform); }
            driver.panelFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/Fonts/Localization/NotoSansCJKsc-Regular.otf");

            Shape("Ground3D", PrimitiveType.Cube, new Vector3(0f, -0.5f, 0f), new Vector3(36f, 1f, 3f), grey);
            Shape("JumpPlatform", PrimitiveType.Cube, new Vector3(3f, 0.65f, 0f), new Vector3(3f, 1.3f, 2f), grey);
            Shape("SolidWall", PrimitiveType.Cube, new Vector3(9f, 2f, 0f), new Vector3(0.6f, 4f, 3f), grey);
            InteractionTarget("InteractTarget", new Vector3(-1f, 0.5f, 0f), Vector3.one,
                new Vector3(4f, 3f, 2f), amber);
            InteractionTarget("BehindWallTarget", new Vector3(10f, 1f, 0f), Vector3.one,
                new Vector3(4f, 3f, 2f), amber);
            InteractionTarget("BackgroundDecoration_NotInPlane", new Vector3(-2f, 1f, 4f), new Vector3(1f, 2f, 1f),
                new Vector3(4f, 3f, 1f), grey);
            GameObject damage = Shape("TestDamageTrigger", PrimitiveType.Cube, new Vector3(6.5f, 0.15f, 0f), new Vector3(1f, 0.3f, 1.5f), red);
            damage.GetComponent<Collider>().isTrigger = true;
            damage.AddComponent<PlayerTestDamageSource>();
            Light light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
            RenderSettings.ambientLight = new Color(0.5f, 0.55f, 0.65f);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
        }
    }

    private static void InteractionTarget(string name, Vector3 position, Vector3 visualScale, Vector3 rangeSize, Material material)
    {
        GameObject root = new GameObject(name);
        root.transform.position = position;
        // Visual与TriggerBox同级，调整模型缩放不改变范围，实体Collider仍只负责模型阻挡。
        GameObject visual = Shape("Visual", PrimitiveType.Cube, Vector3.zero, visualScale, material);
        visual.transform.SetParent(root.transform, false);
        PlayerTestInteractable target = root.AddComponent<PlayerTestInteractable>();
        target.ConfigureTestTrigger(Vector3.zero, rangeSize);
    }

    private static Material MakeMaterial(string name, Color color)
    {
        Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.name = name;
        material.color = color;
        AssetDatabase.CreateAsset(material, "Assets/Art/Materials/Player/" + name + ".mat");
        return material;
    }

    private static GameObject Shape(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        GameObject shape = GameObject.CreatePrimitive(type);
        shape.name = name;
        shape.transform.position = position;
        shape.transform.localScale = scale;
        shape.GetComponent<Renderer>().sharedMaterial = material;
        return shape;
    }
}
