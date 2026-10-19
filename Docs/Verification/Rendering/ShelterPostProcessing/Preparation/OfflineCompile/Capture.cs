public static class ShelterPPCapture { public static object Run() {
// MCP execute_code method body. Async actual ScreenCapture, with per-camera post-render stack.
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
string directory = UnityEditor.SessionState.GetString("ShelterPP.Directory", "");
string label = UnityEditor.SessionState.GetString("ShelterPP.CaptureLabel", "");
if (!UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isPaused || flow == null || flow.Player == null
    || (flow.Stage != GameFlowStage.Shelter && flow.Stage != GameFlowStage.Exploring) || !System.IO.Directory.Exists(directory)
    || string.IsNullOrEmpty(label) || label.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
    throw new System.InvalidOperationException("Require a stable formal scene and initialized safe capture label.");
if (UnityEditor.SessionState.GetBool("ShelterPP.CaptureRunning", false)) throw new System.InvalidOperationException("Capture already running.");
if (UnityEngine.Screen.width != 1920 || UnityEngine.Screen.height != 1080) throw new System.InvalidOperationException("Actual Game must be1920x1080.");
var scene = flow.Player.gameObject.scene;
var bindings = System.Array.Find(UnityEngine.Object.FindObjectsByType<GameFlowSceneBindings>(UnityEngine.FindObjectsSortMode.None), b => b.gameObject.scene == scene);
if (bindings == null) throw new System.InvalidOperationException("No current formal bindings.");
var cameras = bindings.presentationRoot.GetComponentsInChildren<UnityEngine.Camera>(false);
if (cameras.Length != 1 || !cameras[0].isActiveAndEnabled || cameras[0].targetTexture != null)
    throw new System.InvalidOperationException("Require the single formal Game framebuffer camera.");
var camera = cameras[0];
var data = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
if (data == null) throw new System.InvalidOperationException("Formal camera lacks URP additional data.");
string png = System.IO.Path.Combine(directory, label + "-1920x1080.png");
string meta = System.IO.Path.Combine(directory, label + "-metadata.json");
if (System.IO.File.Exists(png) || System.IO.File.Exists(meta)) throw new System.InvalidOperationException("Use a new label; never overwrite evidence.");
var stage = flow.Stage;
bool background = UnityEngine.Application.runInBackground;
bool finished = false;
int rendered = 0;
int requestedFrame = -1;
double started = UnityEditor.EditorApplication.timeSinceStartup;
string token = System.Guid.NewGuid().ToString("N");
object snapshot = null;
System.Action<UnityEngine.Rendering.ScriptableRenderContext, UnityEngine.Camera> renderObserver = null;
UnityEditor.EditorApplication.CallbackFunction fileObserver = null;
System.Action<string, string> finish = (status, reason) =>
{
    if (finished) return;
    finished = true;
    UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= renderObserver;
    UnityEditor.EditorApplication.update -= fileObserver;
    UnityEngine.Application.runInBackground = background;
    UnityEditor.SessionState.SetBool("ShelterPP.CaptureRunning", false);
    UnityEditor.SessionState.SetString("ShelterPP.CaptureResult", status + ": " + reason);
    var result = new { status = status, reason = reason, captureMethod = "ScreenCapture.CaptureScreenshot, supersize1, ordinary Play Game framebuffer", png = png,
        requestedFrame = requestedFrame, utc = System.DateTime.UtcNow.ToString("o"), callbacksRemoved = true, restoredRunInBackground = background, sample = snapshot };
    System.IO.File.WriteAllText(meta, Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
};
renderObserver = (context, renderedCamera) =>
{
    if (finished || renderedCamera != camera || requestedFrame >= 0) return;
    try
    {
        if (++rendered < 3) return; // Allow camera/Cinemachine and grading a few natural rendered frames.
        if (flow == null || flow.Stage != stage || flow.Player == null || flow.Player.gameObject.scene != scene)
            throw new System.InvalidOperationException("Scene changed before capture.");
        var stack = UnityEngine.Rendering.VolumeManager.instance.stack;
        if (stack == null || !stack.isValid) throw new System.InvalidOperationException("No valid stack at target camera endCameraRendering.");
        var ca = stack.GetComponent<UnityEngine.Rendering.Universal.ColorAdjustments>();
        var bloom = stack.GetComponent<UnityEngine.Rendering.Universal.Bloom>();
        var tone = stack.GetComponent<UnityEngine.Rendering.Universal.Tonemapping>();
        var vignette = stack.GetComponent<UnityEngine.Rendering.Universal.Vignette>();
        var wb = stack.GetComponent<UnityEngine.Rendering.Universal.WhiteBalance>();
        var volumes = new System.Collections.Generic.List<object>();
        foreach (var volume in UnityEngine.Object.FindObjectsByType<UnityEngine.Rendering.Volume>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
        {
            string profilePath = UnityEditor.AssetDatabase.GetAssetPath(volume.sharedProfile);
            volumes.Add(new { name = volume.name, scene = volume.gameObject.scene.path, enabled = volume.isActiveAndEnabled,
                global = volume.isGlobal, weight = volume.weight, priority = volume.priority, layer = volume.gameObject.layer,
                inCameraMask = (data.volumeLayerMask.value & (1 << volume.gameObject.layer)) != 0,
                profile = profilePath, profileGuid = string.IsNullOrEmpty(profilePath) ? "" : UnityEditor.AssetDatabase.AssetPathToGUID(profilePath) });
        }
        var position = camera.transform.position;
        var rotation = camera.transform.rotation;
        var pp = flow.Player.Position;
        string skyPath = UnityEditor.AssetDatabase.GetAssetPath(UnityEngine.RenderSettings.skybox);
        snapshot = new { samplePoint = "Target formal camera endCameraRendering, after URP UpdateVolumeFramework; read-only stack", frame = UnityEngine.Time.frameCount,
            stage = flow.Stage.ToString(), round = flow.CurrentRound, settledRound = flow.SettledRound, scene = scene.path,
            screen = new[] { UnityEngine.Screen.width, UnityEngine.Screen.height },
            player = new { id = flow.Player.GetInstanceID(), scene = flow.Player.gameObject.scene.path, position = new[] { pp.x, pp.y, pp.z }, health = flow.Player.health.CurrentHealth },
            camera = new { id = camera.GetInstanceID(), name = camera.name, scene = camera.gameObject.scene.path, pixelWidth = camera.pixelWidth, pixelHeight = camera.pixelHeight,
                position = new[] { position.x, position.y, position.z }, rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w }, fov = camera.fieldOfView,
                orthographic = camera.orthographic, orthographicSize = camera.orthographicSize, clearFlags = camera.clearFlags.ToString(), postProcessing = data.renderPostProcessing,
                volumeMask = data.volumeLayerMask.value, volumeUpdate = UnityEngine.Rendering.Universal.CameraExtensions.GetVolumeFrameworkUpdateMode(camera).ToString(), perFrameVolumeUpdate = data.requiresVolumeFrameworkUpdate,
                cameraVolumeStackIsNull = data.volumeStack == null, binderError = bindings.cameraBinder.LastBoundsError },
            environment = new { skybox = skyPath, skyboxGuid = UnityEditor.AssetDatabase.AssetPathToGUID(skyPath), fog = UnityEngine.RenderSettings.fog,
                ambientIntensity = UnityEngine.RenderSettings.ambientIntensity, reflectionIntensity = UnityEngine.RenderSettings.reflectionIntensity },
            effectiveStack = new { color = ca == null ? null : (object)new { active = ca.IsActive(), exposure = ca.postExposure.value, contrast = ca.contrast.value,
                    saturation = ca.saturation.value, hue = ca.hueShift.value, filter = new[] { ca.colorFilter.value.r, ca.colorFilter.value.g, ca.colorFilter.value.b, ca.colorFilter.value.a } },
                bloom = bloom == null ? null : (object)new { active = bloom.IsActive(), intensity = bloom.intensity.value, threshold = bloom.threshold.value },
                tone = tone == null ? null : (object)new { active = tone.IsActive(), mode = tone.mode.value.ToString() },
                vignette = vignette == null ? null : (object)new { active = vignette.IsActive(), intensity = vignette.intensity.value },
                whiteBalance = wb == null ? null : (object)new { active = wb.IsActive(), temperature = wb.temperature.value, tint = wb.tint.value } }, volumes = volumes };
        if (UnityEngine.Screen.width != 1920 || UnityEngine.Screen.height != 1080 || camera.pixelWidth != 1920 || camera.pixelHeight != 1080)
            throw new System.InvalidOperationException("Actual screen/camera resolution changed.");
        requestedFrame = UnityEngine.Time.frameCount;
        UnityEngine.ScreenCapture.CaptureScreenshot(png, 1);
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= renderObserver;
    }
    catch (System.Exception e) { finish("FAIL", e.ToString()); }
};
fileObserver = () =>
{
    try
    {
        if (UnityEditor.SessionState.GetString("ShelterPP.CaptureToken", "") != token) { finish("CANCELLED", "Capture token cleared."); return; }
        if (!UnityEditor.EditorApplication.isPlaying || flow == null || flow.Stage != stage || camera == null)
            throw new System.InvalidOperationException("Play or stable scene ended while screenshot was pending.");
        if (UnityEditor.EditorApplication.timeSinceStartup - started > 25) throw new System.TimeoutException("No complete1920x1080 PNG within25s; focus Game view and inspect normal rendering.");
        if (requestedFrame < 0 || !System.IO.File.Exists(png)) return;
        byte[] bytes;
        try { bytes = System.IO.File.ReadAllBytes(png); } catch (System.IO.IOException) { return; }
        if (bytes.Length < 32) return;
        if (bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71) throw new System.InvalidOperationException("Screenshot is not PNG.");
        int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        // Complete PNG ends with 0-length IEND chunk and its CRC; wait if asynchronous write is incomplete.
        int end = bytes.Length - 8;
        if (bytes[end] != 73 || bytes[end + 1] != 69 || bytes[end + 2] != 78 || bytes[end + 3] != 68) return;
        if (width != 1920 || height != 1080) throw new System.InvalidOperationException("PNG actual size was " + width + "x" + height);
        finish("PASS", "Complete PNG1920x1080 with target-camera stack snapshot; visual review still required.");
    }
    catch (System.Exception e) { finish("FAIL", e.ToString()); }
};
UnityEditor.SessionState.SetString("ShelterPP.CaptureToken", token);
UnityEditor.SessionState.SetBool("ShelterPP.CaptureRunning", true);
UnityEditor.SessionState.SetString("ShelterPP.CaptureResult", "RUNNING: " + label);
UnityEngine.Application.runInBackground = true;
UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += renderObserver;
UnityEditor.EditorApplication.update += fileObserver;
return new { armed = true, label = label, png = png, resultKey = "ShelterPP.CaptureResult", warning = "Wait for PASS before scene/PP changes; PASS is file validity, not visual acceptance." };

} }