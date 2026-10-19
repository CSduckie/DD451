using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

// 只创建缺少的资源；译文始终在现有SO里编辑，重接线不覆盖场景布局和配置。
public static class LocalizationTestSceneBuilder
{
    public const string ModulePath = "Assets/Prefabs/Localization/LocalizationModule.prefab";
    public const string TextPath = "Assets/Prefabs/UI/Localization/LocalizedText.prefab";
    public const string FontPath = "Assets/Art/Fonts/Localization/NotoSansCJKsc-Regular.otf";
    public const string FontAssetPath = "Assets/Art/Fonts/Localization/NotoSansCJKsc-Regular SDF.asset";
    public const string ScenePath = "Assets/Scenes/Tests/Localization/LocalizationTest.unity";

    [MenuItem("Tools/MyPrecious/Create Localization Test Assets")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("请退出Play后再创建语言测试资源。");
            return;
        }
        foreach (string folder in new[] { "Assets/Prefabs/Localization", "Assets/Prefabs/UI/Localization", "Assets/Scenes/Tests/Localization" })
        {
            Directory.CreateDirectory(folder);
        }
        AssetDatabase.Refresh();
        TMP_FontAsset font = CreateFont();
        CreateModule();
        CreateTextPrefab(font);
        if (!File.Exists(ScenePath)) { CreateScene(font); }
        AssetDatabase.SaveAssets();
    }

    private static TMP_FontAsset CreateFont()
    {
        TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (existing != null) { return existing; }
        Font source = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (source == null) { throw new System.InvalidOperationException("Missing bundled font: " + FontPath); }
        TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source, 48, 6, GlyphRenderMode.SDFAA,
            2048, 2048, AtlasPopulationMode.Dynamic, true);
        font.name = "NotoSansCJKsc-Regular SDF";
        AssetDatabase.CreateAsset(font, FontAssetPath);
        AssetDatabase.AddObjectToAsset(font.material, font);
        foreach (Texture2D atlas in font.atlasTextures) { AssetDatabase.AddObjectToAsset(atlas, font); }
        // 预热当前文案；Dynamic保留源字体，新增文字在构建中也能补入字形。
        string characters = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz []{}:/.,-+";
        foreach (string guid in AssetDatabase.FindAssets("t:LocalizationTableSO"))
        {
            LocalizationTableSO table = AssetDatabase.LoadAssetAtPath<LocalizationTableSO>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (LocalizationEntry entry in table.entries) { characters += entry.chinese + entry.english; }
        }
        ItemLibrarySO library = AssetDatabase.LoadAssetAtPath<ItemLibrarySO>("Assets/Data/Items/ItemLibrary.asset");
        foreach (ItemLibraryEntry entry in library.itemLibraryList)
        {
            characters += entry.itemData.nameZh + entry.itemData.nameEn + entry.itemData.descriptionZh + entry.itemData.descriptionEn;
        }
        font.TryAddCharacters(characters, out string missing);
        if (!string.IsNullOrEmpty(missing.Trim('\n', '\r', '\t')))
        {
            Debug.LogWarning("Localization font missing characters: " + missing, font);
        }
        EditorUtility.SetDirty(font);
        return font;
    }

    private static void CreateModule()
    {
        if (File.Exists(ModulePath)) { return; }
        GameObject root = new GameObject("LocalizationModule");
        try
        {
            LocalizationManager manager = root.AddComponent<LocalizationManager>();
            foreach (string path in new[] { "Common/CommonText", "Player/PlayerText", "Items/ItemMessages", "Tests/TestPanelText" })
            {
                LocalizationTableSO table = AssetDatabase.LoadAssetAtPath<LocalizationTableSO>("Assets/Data/Localization/" + path + ".asset");
                if (table == null) { throw new System.InvalidOperationException("Missing language table: " + path); }
                manager.tables.Add(table);
            }
            manager.itemLibrary = AssetDatabase.LoadAssetAtPath<ItemLibrarySO>("Assets/Data/Items/ItemLibrary.asset");
            manager.toggleLanguageAction = AssetDatabase.LoadAllAssetsAtPath("Assets/Input/GameInputActions.inputactions")
                .OfType<InputActionReference>().First(reference => reference.action.actionMap.name == "Player" && reference.action.name == "ToggleLanguage");
            PrefabUtility.SaveAsPrefabAsset(root, ModulePath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void CreateTextPrefab(TMP_FontAsset font)
    {
        if (File.Exists(TextPath)) { return; }
        GameObject root = new GameObject("LocalizedText", typeof(RectTransform));
        try
        {
            TextMeshProUGUI text = root.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = 28;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = new Vector2(600, 80);
            LocalizedText localized = root.AddComponent<LocalizedText>();
            localized.targetText = text;
            localized.key = "common.language.chinese";
            PrefabUtility.SaveAsPrefabAsset(root, TextPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static void CreateScene(TMP_FontAsset font)
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        try
        {
            GameObject bootstrap = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Bootstrap/GameBootstrap.prefab"));
            WireModule(bootstrap);
            Camera camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(UniversalAdditionalCameraData)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(0.035f, 0.055f, 0.085f);
            Light light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            Canvas canvas = new GameObject("Localization Demo", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            AddText(canvas.transform, "Title", 40, 60, 36, font, "tests.localization.title");
            AddText(canvas.transform, "Instructions", 110, 75, 24, font, "tests.localization.instructions");
            LocalizationTestDriver driver = canvas.gameObject.AddComponent<LocalizationTestDriver>();
            driver.currentLanguageText = AddText(canvas.transform, "Current Language", 195, 45, 26, font);
            driver.valueText = AddText(canvas.transform, "Dynamic Value", 255, 45, 28, font);
            driver.itemNameText = AddText(canvas.transform, "Item Name", 320, 45, 30, font);
            driver.itemDescriptionText = AddText(canvas.transform, "Item Description", 375, 95, 25, font);
            driver.resultText = AddText(canvas.transform, "Result Example", 485, 70, 25, font);
            AddText(canvas.transform, "Scope", 575, 100, 21, font, "tests.localization.scope");
            driver.item = AssetDatabase.LoadAssetAtPath<ItemDataSO>("Assets/Data/Items/Medkit.asset");
            driver.localizationManager = bootstrap.GetComponentInChildren<LocalizationManager>();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
        }
    }

    private static TMP_Text AddText(Transform parent, string name, float y, float height, int size, TMP_FontAsset font, string key = null)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TextPath), parent);
        instance.name = name;
        TMP_Text text = instance.GetComponent<TMP_Text>();
        text.font = font;
        text.fontSize = size;
        text.color = new Color(0.88f, 0.94f, 0.97f);
        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(60, -y);
        rect.sizeDelta = new Vector2(-120, height);
        LocalizedText localized = instance.GetComponent<LocalizedText>();
        if (key == null) { localized.enabled = false; }
        else { localized.key = key; }
        PrefabUtility.RecordPrefabInstancePropertyModifications(text);
        PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
        PrefabUtility.RecordPrefabInstancePropertyModifications(localized);
        return text;
    }

    public static void WireModule(GameObject bootstrap)
    {
        if (bootstrap.GetComponentInChildren<LocalizationManager>(true) == null)
        {
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModulePath), bootstrap.transform);
        }
    }

    public static void WireCurrentTestScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Tests/Player/PlayerTest.unity" && scene.path != "Assets/Scenes/Tests/Items/ItemsTest.unity")
        {
            throw new System.InvalidOperationException("Only existing PlayerTest / ItemsTest may be wired here.");
        }
        GameBootstrap bootstrap = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameBootstrap>(true)).Single();
        WireModule(bootstrap.gameObject);
        Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (PlayerTestDriver driver in root.GetComponentsInChildren<PlayerTestDriver>(true)) { driver.panelFont = font; EditorUtility.SetDirty(driver); }
            foreach (ItemsTestDriver driver in root.GetComponentsInChildren<ItemsTestDriver>(true)) { driver.panelFont = font; EditorUtility.SetDirty(driver); }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
