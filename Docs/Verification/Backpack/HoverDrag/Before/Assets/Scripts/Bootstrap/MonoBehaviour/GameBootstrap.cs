using UnityEngine;

[DisallowMultipleComponent]
public class GameBootstrap : MonoBehaviour
{
    public static GameBootstrap Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticReference()
    {
        // 每次开始运行先清掉上次的静态引用。
        Instance = null;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Destroy在帧末执行，先禁用重复入口。
            enabled = false;
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (transform.parent != null)
        {
            transform.SetParent(null);
        }
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        // 销毁重复入口时，不能清掉仍在运行的那个入口。
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
