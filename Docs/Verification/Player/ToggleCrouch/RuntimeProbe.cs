// C# 6 execute_code METHOD BODY. Docs only; never import into Assets.
// Root enters Bootstrap -> Shelter -> Ruins first, with Game rendering at 1920x1080.
// Cancel: SessionState.SetString("ToggleCrouch.ProbeToken", ""); await ProbeRunning=false.
// API relocation + synthetic InputSystem keyboard. This is not native-keyboard verification.
string key = "ToggleCrouch.";
if (UnityEditor.SessionState.GetBool(key + "ProbeRunning", false))
    throw new System.InvalidOperationException("Toggle crouch probe is already running.");
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
var player = flow != null ? flow.Player : null;
if (!UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isPaused || UnityEngine.Time.timeScale != 1f
    || flow == null || flow.Stage != GameFlowStage.Exploring || player == null || !player.CanAct
    || player.config.crouchInputMode != CrouchInputMode.Toggle)
    throw new System.InvalidOperationException("Require unlocked Exploring, unpaused Play, timeScale=1, default Toggle.");
var scene = player.gameObject.scene;
GameFlowSceneBindings bindings = null;
PlayerStealthVignette effect = null;
CoverArea cover = null;
var drones = new System.Collections.Generic.List<DroneController>();
foreach (var root in scene.GetRootGameObjects())
{
    foreach (var item in root.GetComponentsInChildren<GameFlowSceneBindings>(true)) bindings = item;
    foreach (var item in root.GetComponentsInChildren<PlayerStealthVignette>(true))
    {
        if (effect != null) throw new System.InvalidOperationException("Expected one stealth effect.");
        effect = item;
    }
    foreach (var item in root.GetComponentsInChildren<CoverArea>(false))
        if (UnityEngine.Vector3.Distance(item.transform.position, new UnityEngine.Vector3(-5.5f, 0f, -.5f)) < .25f) cover = item;
    drones.AddRange(root.GetComponentsInChildren<DroneController>(true));
}
if (bindings == null || effect == null || !effect.isActiveAndEnabled || cover == null
    || bindings.backpackUI == null || bindings.backpackUI.IsOpen || effect.cameraBinder != bindings.cameraBinder
    || bindings.cameraBinder.player != player)
    throw new System.InvalidOperationException("Require authored Cover (-5.5,0,-.5), bound effect and closed Backpack.");
var cameras = bindings.presentationRoot.GetComponentsInChildren<UnityEngine.Camera>(false);
if (cameras.Length != 1 || cameras[0].targetTexture != null)
    throw new System.InvalidOperationException("Require exactly the formal framebuffer camera.");
var camera = cameras[0];
var cameraData = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
var volume = effect.GetComponent<UnityEngine.Rendering.Volume>();
if (cameraData == null || !cameraData.renderPostProcessing || volume == null
    || (cameraData.volumeLayerMask.value & (1 << volume.gameObject.layer)) == 0
    || UnityEngine.Mathf.Abs(effect.intensity - .4f) > .0001f)
    throw new System.InvalidOperationException("Require formal camera postprocessing and authored intensity 0.4.");

string token = System.Guid.NewGuid().ToString("N");
string directory = "E:/DD451/Docs/Verification/Player/ToggleCrouch/Probe-" + token.Substring(0, 8);
System.IO.Directory.CreateDirectory(directory);
var trace = new System.Collections.Generic.List<object>();
var captures = new System.Collections.Generic.List<object>();
var checks = new System.Collections.Generic.List<string>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var disabledKeyboards = new System.Collections.Generic.List<UnityEngine.InputSystem.InputDevice>();
var disabledDrones = new System.Collections.Generic.List<UnityEngine.GameObject>();
var originalSettings = UnityEngine.InputSystem.InputSystem.settings;
UnityEngine.InputSystem.InputSettings settings = null;
UnityEngine.InputSystem.Keyboard keyboard = null;
bool originalBackground = UnityEngine.Application.runInBackground;
var originalPosition = player.Position;
bool finished = false, armed = false, shotRequested = false, shotDone = false, secondReleased = false;
int phase = 0, phaseFrame = UnityEngine.Time.frameCount, lastRenderedFrame = -1;
float effectiveVignette = -1f;
double started = UnityEditor.EditorApplication.timeSinceStartup, phaseStarted = started;
string wantedShot = null, pendingPng = null;
object pendingSample = null;
UnityEditor.EditorApplication.CallbackFunction observer = null;
System.Action<UnityEngine.Rendering.ScriptableRenderContext, UnityEngine.Camera> renderObserver = null;
System.Action<bool> keys = pressed => UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,
    pressed ? new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.C)
            : new UnityEngine.InputSystem.LowLevel.KeyboardState());
System.Action<int> advance = next =>
{
    phase = next; phaseStarted = UnityEditor.EditorApplication.timeSinceStartup;
    phaseFrame = UnityEngine.Time.frameCount; shotRequested = false; shotDone = false;
};
System.Action<string> requestShot = label => { if (!shotRequested) { wantedShot = label; shotRequested = true; } };
System.Action<string, string> finish = (status, reason) =>
{
    if (finished) return;
    finished = true;
    UnityEditor.EditorApplication.update -= observer;
    UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= renderObserver;
    try { if (keyboard != null && keyboard.added) { UnityEngine.InputSystem.InputSystem.ResetDevice(keyboard); UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard); } } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
    try { if (UnityEditor.EditorApplication.isPlaying && player != null && !player.IsDead && player.gameObject.scene == scene) player.RelocateWithinRun(originalPosition); } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
    try { UnityEngine.InputSystem.InputSystem.settings = originalSettings; } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
    foreach (var device in disabledKeyboards)
        try { if (device != null && device.added) UnityEngine.InputSystem.InputSystem.EnableDevice(device); } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
    try { if (settings != null) UnityEngine.Object.Destroy(settings); } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
    foreach (var droneObject in disabledDrones)
        try { if (droneObject != null) droneObject.SetActive(true); } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
    try { UnityEngine.Application.runInBackground = originalBackground; } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
    UnityEditor.SessionState.SetString(key + "ProbeToken", "");
    UnityEditor.SessionState.SetBool(key + "ProbeRunning", false);
    UnityEditor.SessionState.SetString(key + "ProbeResult", status + ": " + reason);
    var result = new { status = status, reason = reason, checks = checks, captures = captures, trace = trace,
        method = "API relocation + synthetic C press/release through formal InputReader; drones temporarily disabled; no native input claim",
        disabledDroneCount = disabledDrones.Count, restoredKeyboardCount = disabledKeyboards.Count,
        timeScaleNeverWritten = true, callbacksRemoved = true, cleanupErrors = cleanupErrors };
    System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "Result.json"), Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
};
renderObserver = (context, renderedCamera) =>
{
    if (finished || renderedCamera != camera || lastRenderedFrame == UnityEngine.Time.frameCount) return;
    try
    {
        lastRenderedFrame = UnityEngine.Time.frameCount;
        var vignette = UnityEngine.Rendering.VolumeManager.instance.stack.GetComponent<UnityEngine.Rendering.Universal.Vignette>();
        effectiveVignette = vignette == null ? -1f : vignette.intensity.value;
        var capsule = player.GetComponent<UnityEngine.CapsuleCollider>();
        var row = new { utc = System.DateTime.UtcNow.ToString("o"), frame = UnityEngine.Time.frameCount,
            phase = phase, scene = scene.path, stage = flow.Stage.ToString(), timeScale = UnityEngine.Time.timeScale,
            playerId = player.GetInstanceID(), inputMode = player.config.crouchInputMode.ToString(),
            rawC = player.inputReader.CrouchHeld, crouching = player.IsCrouching, grounded = player.IsGrounded,
            hidden = CoverArea.IsHidden(player), height = capsule.height, centerY = capsule.center.y,
            weight = volume.weight, authoredIntensity = effect.intensity, effectiveVignette = effectiveVignette,
            cameraPixels = new[] { camera.pixelWidth, camera.pixelHeight } };
        trace.Add(row);
        if (wantedShot == null) return;
        if (camera.pixelWidth != 1920 || camera.pixelHeight != 1080 || UnityEngine.Screen.width != 1920 || UnityEngine.Screen.height != 1080)
            throw new System.InvalidOperationException("Actual rendered Game must be 1920x1080.");
        pendingSample = row;
        pendingPng = System.IO.Path.Combine(directory, wantedShot + "-1920x1080.png");
        wantedShot = null;
        UnityEngine.ScreenCapture.CaptureScreenshot(pendingPng, 1);
    }
    catch (System.Exception e) { finish("FAIL", e.ToString()); }
};
observer = () =>
{
    try
    {
        if (UnityEditor.SessionState.GetString(key + "ProbeToken", "") != token) { finish("CANCELLED", "Token cleared."); return; }
        if (!UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isPaused || UnityEngine.Time.timeScale != 1f
            || player == null || player.IsDead || flow.Stage != GameFlowStage.Exploring || player.gameObject.scene != scene)
            throw new System.InvalidOperationException("Play/time/scene/player changed; restore temporary state.");
        if (UnityEditor.EditorApplication.timeSinceStartup - started > 45) throw new System.TimeoutException("45-second probe timeout, phase=" + phase);
        if (pendingPng != null)
        {
            if (!System.IO.File.Exists(pendingPng)) return;
            byte[] bytes; try { bytes = System.IO.File.ReadAllBytes(pendingPng); } catch (System.IO.IOException) { return; }
            if (bytes.Length < 32) return;
            int end = bytes.Length - 8;
            if (bytes[end] != 73 || bytes[end + 1] != 69 || bytes[end + 2] != 78 || bytes[end + 3] != 68) return;
            int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            if (bytes[0] != 137 || bytes[1] != 80 || width != 1920 || height != 1080)
                throw new System.InvalidOperationException("Screenshot is not a complete 1920x1080 PNG.");
            captures.Add(new { path = pendingPng, width = width, height = height, sample = pendingSample });
            pendingPng = null; pendingSample = null; shotDone = true;
        }
        if (wantedShot != null || lastRenderedFrame < phaseFrame + 3 || UnityEditor.EditorApplication.timeSinceStartup - phaseStarted < .65) return;
        bool held = player.inputReader.CrouchHeld;
        bool full = player.IsCrouching && CoverArea.IsHidden(player) && volume.weight >= .999f && UnityEngine.Mathf.Abs(effectiveVignette - .4f) < .001f;
        bool clear = !player.IsCrouching && !CoverArea.IsHidden(player) && volume.weight <= .001f && effectiveVignette <= .001f;
        switch (phase)
        {
            case 0:
                if (held || !clear || !player.IsGrounded) return;
                requestShot("00-Standing-BeforeTap");
                if (shotDone) { checks.Add("Standing in grass: raw C=false, hidden=false, vignette=0."); keys(true); advance(1); }
                break;
            case 1:
                if (!held || !full) return;
                requestShot("01-FirstPress-Crouching");
                if (shotDone) { checks.Add("First C press held >0.65s: one crouch toggle, hidden=true, effective vignette=0.4."); keys(false); advance(2); }
                break;
            case 2:
                if (held || !full) return;
                requestShot("02-Released-StillCrouching");
                if (shotDone) { checks.Add("C released >0.65s: still crouching and hidden, effective vignette remains 0.4."); keys(true); advance(3); }
                break;
            case 3:
                if (!secondReleased)
                {
                    if (!held || !clear) return;
                    keys(false); secondReleased = true; phaseStarted = UnityEditor.EditorApplication.timeSinceStartup;
                    phaseFrame = UnityEngine.Time.frameCount; return;
                }
                if (held || !clear) return;
                requestShot("03-SecondTap-StandingRestored");
                if (shotDone) { checks.Add("Second C press/release: standing, hidden=false, vignette=0."); finish("PASS", "Four phases complete; inspect the actual Game PNGs for visual acceptance."); }
                break;
        }
    }
    catch (System.Exception e) { finish("FAIL", e.ToString()); }
};
try
{
    settings = UnityEngine.Object.Instantiate(originalSettings);
    settings.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
    settings.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
    settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
    UnityEngine.InputSystem.InputSystem.settings = settings;
    UnityEngine.Application.runInBackground = true;
    foreach (var device in new System.Collections.Generic.List<UnityEngine.InputSystem.InputDevice>(UnityEngine.InputSystem.InputSystem.devices))
    {
        if (!(device is UnityEngine.InputSystem.Keyboard) || !device.enabled) continue;
        disabledKeyboards.Add(device); UnityEngine.InputSystem.InputSystem.DisableDevice(device);
    }
    keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("ToggleCrouchProbe-" + token);
    foreach (var drone in drones)
        if (drone != null && drone.gameObject.activeSelf) { disabledDrones.Add(drone.gameObject); drone.gameObject.SetActive(false); }
    UnityEditor.SessionState.SetString(key + "ProbeToken", token);
    UnityEditor.SessionState.SetString(key + "ProbeDirectory", directory);
    UnityEditor.SessionState.SetBool(key + "ProbeRunning", true);
    UnityEditor.SessionState.SetString(key + "ProbeResult", "RUNNING");
    if (!player.RelocateWithinRun(new UnityEngine.Vector3(-5.5f, .05f, player.config.planeZ)))
        throw new System.InvalidOperationException("Formal relocation refused.");
    UnityEngine.Physics.SyncTransforms();
    keys(false);
    UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += renderObserver;
    UnityEditor.EditorApplication.update += observer;
    armed = true;
}
catch (System.Exception e) { finish("FAIL", e.ToString()); }
finally { if (!armed && !finished) finish("FAIL", "Setup did not arm; cleaned temporary resources."); }
return new { armed = armed && !finished, directory = directory, token = token, resultKey = key + "ProbeResult",
    note = "Keep ordinary Game rendering. 4 PNGs + Result.json. Never changes timeScale; cleans callbacks/devices/settings/drones." };
