using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// 挂在本场景表现根下的独立Volume上，只把现有隐藏状态映射为暗角。
[DisallowMultipleComponent]
[RequireComponent(typeof(Volume))]
public class PlayerStealthVignette : MonoBehaviour
{
    public PlayerCameraTargetBinder cameraBinder;
    [Range(0f, 1f)] public float intensity = 0.22f;
    [Range(0.01f, 1f)] public float smoothness = 0.45f;
    [Min(0.01f)] public float transitionSeconds = 0.2f;

    private Volume effectVolume;
    private VolumeProfile runtimeProfile;
    private Vignette vignette;

    private void Awake()
    {
        effectVolume = GetComponent<Volume>();
        // 专用运行时Profile仅含Vignette，不修改磁盘共享Profile或其它后处理。
        runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeProfile.name = "Stealth Vignette (Runtime)";
        vignette = runtimeProfile.Add<Vignette>();
        vignette.color.Override(Color.black);
        vignette.center.Override(new Vector2(0.5f, 0.5f));
        vignette.rounded.Override(false);
        effectVolume.profile = runtimeProfile;
        effectVolume.weight = 0f;
    }

    private void LateUpdate()
    {
        PlayerController player = cameraBinder != null ? cameraBinder.player : null;
        if (cameraBinder == null || !cameraBinder.isActiveAndEnabled || player == null
            || !player.isActiveAndEnabled || !player.IsPrepared || player.IsDead
            || player.IsSceneTransitionBlocked || player.gameObject.scene != gameObject.scene)
        {
            // 死亡、解绑和切场景立即清除；普通站起/离草丛才使用渐退。
            effectVolume.weight = 0f;
            return;
        }

        vignette.intensity.Override(intensity);
        vignette.smoothness.Override(smoothness);
        float target = CoverArea.IsHidden(player) ? 1f : 0f;
        effectVolume.weight = Mathf.MoveTowards(effectVolume.weight, target,
            Time.deltaTime / Mathf.Max(0.01f, transitionSeconds));
    }

    private void OnDisable()
    {
        if (effectVolume != null) { effectVolume.weight = 0f; }
    }

    private void OnDestroy()
    {
        if (effectVolume != null) { effectVolume.profile = null; }
        if (vignette != null) { Destroy(vignette); }
        if (runtimeProfile != null) { Destroy(runtimeProfile); }
    }
}
