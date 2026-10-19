// execute_code METHOD BODY. Runtime-only profile clone; never persist this clone.
if (!UnityEditor.EditorApplication.isPlaying || UnityEditor.SessionState.GetBool("ShelterPP.CaptureRunning", false))
    throw new System.InvalidOperationException("Need ordinary Play with no pending capture.");
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
if (flow == null || flow.Stage != GameFlowStage.Shelter) throw new System.InvalidOperationException("Need formal Shelter.");
var scene = flow.Player.gameObject.scene;
var volume = System.Array.Find(UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(UnityEngine.FindObjectsSortMode.None), v => v.gameObject.scene == scene);
var camera = System.Array.Find(UnityEngine.Object.FindObjectsByType<UnityEngine.Camera>(UnityEngine.FindObjectsSortMode.None), c => c.gameObject.scene == scene && c.isActiveAndEnabled);
if (volume == null || camera == null || UnityEditor.AssetDatabase.GetAssetPath(volume.sharedProfile) != "Assets/Scenes/Shelter/Global Volume Profile.asset")
    throw new System.InvalidOperationException("Unexpected Shelter profile/camera.");
var profile = volume.profile;
UnityEngine.Rendering.Universal.ColorAdjustments color;
if (!profile.TryGet(out color)) color = profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>();
color.active = true;
color.postExposure.Override(UnityEditor.SessionState.GetFloat("ShelterPP.Exposure", -.35f));
color.contrast.Override(UnityEditor.SessionState.GetFloat("ShelterPP.Contrast", 12f));
color.colorFilter.Override(UnityEngine.Color.white);
color.hueShift.Override(0f);
color.saturation.Override(0f);
UnityEngine.Rendering.Universal.Tonemapping tone;
if (!profile.TryGet(out tone)) tone = profile.Add<UnityEngine.Rendering.Universal.Tonemapping>();
tone.active = true;
tone.mode.Override(UnityEngine.Rendering.Universal.TonemappingMode.None);
UnityEngine.Rendering.Universal.Vignette vignette;
if (!profile.TryGet(out vignette)) vignette = profile.Add<UnityEngine.Rendering.Universal.Vignette>();
vignette.active = true;
vignette.intensity.Override(0f);
UnityEngine.Rendering.Universal.Bloom bloom;
if (!profile.TryGet(out bloom)) throw new System.InvalidOperationException("Existing Bloom missing.");
// Preserve existing intensity/tint; pin threshold to its previously inherited effective value.
bloom.threshold.Override(1f);
camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing = true;
return new { runtimeOnly = true, exposure = color.postExposure.value, contrast = color.contrast.value, tone = tone.mode.value.ToString(), vignette = vignette.intensity.value, bloom = bloom.intensity.value };
