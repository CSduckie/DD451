using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.SceneTemplate;
using UnityEngine;
using UnityEngine.SceneManagement;

// 放入 Editor 目录。只初始化新场景，不覆盖重新打开的场景或自动保存。
[InitializeOnLoad]
public static class ProjectNightEnvironment
{
    private const string SkyboxPath = "Assets/Art/Materials/Environment/ShelterNightSkybox.mat";

    static ProjectNightEnvironment()
    {
        EditorSceneManager.newSceneCreated += OnNewSceneCreated;
        SceneTemplateService.newSceneTemplateInstantiated += OnTemplateInstantiated;
    }

    private static void OnNewSceneCreated(Scene scene, NewSceneSetup setup, NewSceneMode mode)
    {
        ApplyToNewScene(scene);
    }

    private static void OnTemplateInstantiated(SceneTemplateAsset template, Scene scene,
        SceneAsset sceneAsset, bool additiveLoad)
    {
        ApplyToNewScene(scene);
    }

    private static void ApplyToNewScene(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid()
            || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
        {
            return;
        }

        PrefabStage prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
        if (prefabStage != null && prefabStage.scene == scene) { return; }

        Material skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
        if (skybox == null)
        {
            Debug.LogWarning("新场景黑夜默认值未应用：缺少天空盒 " + SkyboxPath);
            return;
        }

        // RenderSettings 属于当前活动场景；Additive 创建不能改到旧场景。
        Scene previous = SceneManager.GetActiveScene();
        bool switched = previous != scene;
        if (switched && !SceneManager.SetActiveScene(scene)) { return; }
        try
        {
            if (RenderSettings.skybox == skybox) { return; }
            RenderSettings.skybox = skybox;
            EditorSceneManager.MarkSceneDirty(scene);
        }
        finally
        {
            if (switched && previous.IsValid() && previous.isLoaded)
            {
                SceneManager.SetActiveScene(previous);
            }
        }
    }
}
