using UnityEngine;

// 参考原ARPG的TurretLaserController：每帧用正弦平滑改变表现，不参与AI决策。
[DisallowMultipleComponent]
public class DroneStatusLight : MonoBehaviour
{
    [Header("无人机已有点光源")]
    [Tooltip("指定Visual下已有的Point Light，爆炸时灯随模型一起隐藏。")]
    public Light targetLight;

    [Header("呼吸亮度")]
    [Tooltip("完成一次由暗到亮再变暗的秒数。")]
    [Min(0.1f)] public float breathingDuration = 2f;
    [Tooltip("乘在灯光原强度上的最暗、最亮比例。")]
    [Min(0f)] public float minIntensityMultiplier = 0.35f;
    [Min(0f)] public float maxIntensityMultiplier = 1f;

    [Header("状态颜色")]
    [Tooltip("Alert警戒与Search搜索使用橙色；普通巡逻保留灯光原色。")]
    public Color searchColor = new Color(1f, 0.5f, 0f);
    [Tooltip("Chase追击状态使用红色。")]
    public Color chaseColor = new Color(1f, 0.05f, 0.02f);

    private DroneController drone;
    private Light activeLight;
    private Color originalColor;
    private float originalIntensity;
    private float breathingPhase;

    private void OnEnable()
    {
        CaptureLight();
        breathingPhase = 0f;
    }

    private void CaptureLight()
    {
        // 禁用后保留基准，避免把爆炸时的零亮度记成下一轮的原强度。
        if (activeLight != null) { return; }
        drone = GetComponentInParent<DroneController>();
        if (drone == null || targetLight == null) { return; }
        activeLight = targetLight;
        originalColor = activeLight.color;
        originalIntensity = activeLight.intensity;
    }

    private void LateUpdate()
    {
        CaptureLight();
        if (activeLight == null || drone == null) { return; }
        if (!drone.IsRunning || IsVisualHidden() || !activeLight.enabled)
        {
            RestoreLight();
            return;
        }

        // AI在Update中切状态，灯光在LateUpdate读本帧的最终状态。
        activeLight.color = drone.CurrentState == DroneState.Chase ? chaseColor
            : drone.CurrentState == DroneState.Search || drone.CurrentState == DroneState.Alert ? searchColor : originalColor;
        breathingPhase = Mathf.Repeat(breathingPhase + Time.deltaTime / Mathf.Max(0.1f, breathingDuration), 1f);
        float amount = (Mathf.Sin(breathingPhase * Mathf.PI * 2f) + 1f) * 0.5f;
        float min = Mathf.Max(0f, minIntensityMultiplier);
        float max = Mathf.Max(min, maxIntensityMultiplier);
        activeLight.intensity = originalIntensity * Mathf.Lerp(min, max, amount);
    }

    private bool IsVisualHidden()
    {
        return drone != null && (drone.HasExploded
            || (drone.visual != null && !drone.visual.gameObject.activeSelf));
    }

    private void RestoreLight()
    {
        if (activeLight == null) { return; }
        activeLight.color = originalColor;
        activeLight.intensity = IsVisualHidden() ? 0f : originalIntensity;
        // 不修改Light.enabled，原本关闭的灯不会被表现脚本打开。
    }

    private void OnDisable()
    {
        if (activeLight == null) { return; }
        // 停用表现就还原原灯；爆炸时Visual已隐藏，不会照明。
        // 若下一轮仍停用本组件，重新显示模型后也不会残留零亮度。
        activeLight.color = originalColor;
        activeLight.intensity = originalIntensity;
    }
}
