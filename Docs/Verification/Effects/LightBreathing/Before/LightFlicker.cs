using System.Collections;
using UnityEngine;

// 参考 2DArpg 的 ShielderSprite 闪烁协程，改为灯具的随机间歇闪灭。
[DisallowMultipleComponent]
public class LightFlicker : MonoBehaviour
{
    [Header("灯光引用")]
    public Light targetLight;

    [Header("每轮闪烁之间的间隔")]
    [Tooltip("X 是最短秒数，Y 是最长秒数。")]
    public Vector2 interval = new Vector2(3f, 8f);

    [Header("每轮连续闪烁")]
    [Tooltip("X 是最少次数，Y 是最多次数。")]
    public Vector2Int flashCount = new Vector2Int(2, 4);
    [Tooltip("每次暗下去的秒数范围。")]
    public Vector2 darkTime = new Vector2(0.05f, 0.15f);
    [Tooltip("两次闪灭之间恢复亮光的秒数范围。")]
    public Vector2 brightTime = new Vector2(0.08f, 0.2f);
    [Tooltip("0 表示完全熄灭，1 表示保持原亮度。")]
    [Range(0f, 1f)] public float darkIntensityMultiplier = 0f;

    private Light activeLight;
    private float originalIntensity;
    private Coroutine flickerCoroutine;

    private void OnEnable()
    {
        if (targetLight == null)
        {
            targetLight = GetComponentInChildren<Light>(true);
        }

        if (targetLight == null)
        {
            Debug.LogWarning("LightFlicker：请指定需要闪烁的灯光。", this);
            return;
        }

        activeLight = targetLight;
        originalIntensity = activeLight.intensity;
        flickerCoroutine = StartCoroutine(Flicker());
    }

    private IEnumerator Flicker()
    {
        while (activeLight != null)
        {
            yield return new WaitForSeconds(RandomDuration(interval));

            int minCount = Mathf.Clamp(flashCount.x, 1, 20);
            int maxCount = Mathf.Clamp(flashCount.y, minCount, 20);
            int count = Random.Range(minCount, maxCount + 1);

            for (int i = 0; i < count; i++)
            {
                if (activeLight == null)
                {
                    yield break;
                }

                activeLight.intensity = originalIntensity * Mathf.Clamp01(darkIntensityMultiplier);
                yield return new WaitForSeconds(RandomDuration(darkTime));

                if (activeLight == null)
                {
                    yield break;
                }

                activeLight.intensity = originalIntensity;
                yield return new WaitForSeconds(RandomDuration(brightTime));
            }
        }
    }

    private float RandomDuration(Vector2 range)
    {
        float min = Mathf.Max(0.02f, range.x);
        float max = Mathf.Max(min, range.y);
        return Random.Range(min, max);
    }

    private void OnDisable()
    {
        if (flickerCoroutine != null)
        {
            StopCoroutine(flickerCoroutine);
            flickerCoroutine = null;
        }

        // 中途禁用也恢复，避免重新启用时把暗下去的亮度当成初始值。
        if (activeLight != null)
        {
            activeLight.intensity = originalIntensity;
        }

        activeLight = null;
    }
}
