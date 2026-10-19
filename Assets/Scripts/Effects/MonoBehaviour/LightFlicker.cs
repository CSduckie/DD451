using System.Collections;
using UnityEngine;

// 参考 2DArpg 的闪烁协程和 TurretLaserController 正弦变化，组合灯光闪灭与呼吸。
[DisallowMultipleComponent]
public class LightFlicker : MonoBehaviour
{
    [Header("灯光引用")]
    public Light targetLight;

    [Header("呼吸效果")]
    [Tooltip("开启后亮度缓慢起伏，同时保留间歇闪灭。")]
    public bool enableBreathing = true;
    [Tooltip("完成一次由亮到暗再回亮的秒数。")]
    [Min(0.02f)] public float breathingDuration = 4f;
    [Tooltip("呼吸最暗时占原亮度的比例，最亮时恢复原亮度。")]
    [Range(0f, 1f)] public float minBreathingMultiplier = 0.65f;

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
    [Tooltip("闪灭时乘在当前呼吸亮度上：0 完全熄灭，1 不额外变暗。")]
    [Range(0f, 1f)] public float darkIntensityMultiplier = 0f;

    private Light activeLight;
    private float originalIntensity;
    private Coroutine flickerCoroutine;
    private float breathingPhase;
    private bool isDark;

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
        // 每盏灯从不同位置开始呼吸，避免同时变亮变暗。
        breathingPhase = Random.Range(0f, 1f);
        isDark = false;
        flickerCoroutine = StartCoroutine(Flicker());
    }

    private void LateUpdate()
    {
        if (activeLight == null)
        {
            return;
        }

        float breathingMultiplier = 1f;
        if (enableBreathing)
        {
            float duration = Mathf.Max(0.02f, breathingDuration);
            breathingPhase = Mathf.Repeat(breathingPhase + Time.deltaTime / duration, 1f);
            float amount = (Mathf.Sin(breathingPhase * Mathf.PI * 2f) + 1f) * 0.5f;
            breathingMultiplier = Mathf.Lerp(Mathf.Clamp01(minBreathingMultiplier), 1f, amount);
        }

        // 协程只切换暗亮状态；这里统一写亮度，呼吸和闪灭就不会互相覆盖。
        float flickerMultiplier = isDark ? Mathf.Clamp01(darkIntensityMultiplier) : 1f;
        activeLight.intensity = originalIntensity * breathingMultiplier * flickerMultiplier;
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

                isDark = true;
                yield return new WaitForSeconds(RandomDuration(darkTime));

                if (activeLight == null)
                {
                    yield break;
                }

                isDark = false;
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
        isDark = false;
    }
}
