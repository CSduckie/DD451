using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneEnvironmentRefresh
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        // 关闭 Domain Reload 时仍避免重复订阅。
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        RefreshEnvironment();
    }

    private static void OnActiveSceneChanged(Scene previous, Scene current)
    {
        RefreshEnvironment();
    }

    private static void RefreshEnvironment()
    {
        // 换天空盒后，旧环境探针仍可能是日间数据；按当前场景重新生成。
        if (Application.isPlaying && RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Skybox)
        {
            DynamicGI.UpdateEnvironment();
        }
    }
}
