// C# 6 execute_code METHOD BODY. Docs only; never import into Assets.
// Root first enters Exploring through Bootstrap -> Shelter -> Ruins, Game view 1920x1080.
// Cancel: SessionState.SetString("Stealth09I.ProbeToken", ""); wait for ProbeRunning=false.
// API relocation + synthetic C + Backpack API + temporarily disabled drones; NOT native input.
string key = "StealthIntensity04.";
if (UnityEditor.SessionState.GetBool(key + "ProbeRunning", false))
    throw new System.InvalidOperationException("Another 09I probe is running; cancel its token and await cleanup.");
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
var player = flow != null ? flow.Player : null;
if (!UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isPaused || UnityEngine.Time.timeScale != 1f
    || flow == null || flow.Stage != GameFlowStage.Exploring || player == null || !player.CanAct)
    throw new System.InvalidOperationException("Require unlocked Exploring, unpaused Play and timeScale=1.");
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
        if (effect != null) throw new System.InvalidOperationException("Expected one stealth component in this scene.");
        effect = item;
    }
    foreach (var item in root.GetComponentsInChildren<CoverArea>(false))
        if (UnityEngine.Vector3.Distance(item.transform.position, new UnityEngine.Vector3(-5.5f, 0f, -.5f)) < .25f) cover = item;
    drones.AddRange(root.GetComponentsInChildren<DroneController>(true));
}
if (bindings == null || effect == null || !effect.isActiveAndEnabled || cover == null || !cover.isActiveAndEnabled
    || bindings.backpackUI == null || bindings.backpackUI.IsOpen || effect.cameraBinder != bindings.cameraBinder
    || bindings.cameraBinder.player != player)
    throw new System.InvalidOperationException("Require the authored Cover at (-5.5,0,-.5), bound effect and closed Backpack.");
if (Mathf.Abs(effect.intensity - 0.4f) > 0.0001f || Mathf.Abs(effect.smoothness - 0.45f) > 0.0001f || Mathf.Abs(effect.transitionSeconds - 0.2f) > 0.0001f) throw new System.InvalidOperationException("Unexpected effect parameters");
var cameras = bindings.presentationRoot.GetComponentsInChildren<UnityEngine.Camera>(false);
if (cameras.Length != 1 || cameras[0].targetTexture != null)
    throw new System.InvalidOperationException("Require exactly the formal framebuffer camera.");
var camera = cameras[0];
var cameraData = camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
var volume = effect.GetComponent<UnityEngine.Rendering.Volume>();
var box = cover.GetComponent<UnityEngine.BoxCollider>();
if (cameraData == null || !cameraData.renderPostProcessing || volume == null || box == null
    || (cameraData.volumeLayerMask.value & (1 << volume.gameObject.layer)) == 0)
    throw new System.InvalidOperationException("Actual OutputCamera PP or Volume mask is not ready.");
string token = System.Guid.NewGuid().ToString("N");
string directory = "E:/DD451/Docs/Verification/Enemy/StealthVignette/Intensity04/Probe-" + token.Substring(0, 8);
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
UnityEngine.GameObject temporaryCover = null;
bool originalCoverEnabled = cover.enabled;
bool originalBackground = UnityEngine.Application.runInBackground;
bool finished = false, armed = false, shotRequested = false, shotDone = false;
int phase = 0, phaseFrame = UnityEngine.Time.frameCount, lastRenderedFrame = -1;
double started = UnityEditor.EditorApplication.timeSinceStartup, phaseStarted = started;
string wantedShot = null, pendingPng = null;
object pendingSample = null;
var inside = new UnityEngine.Vector3(-5.5f, .05f, player.config.planeZ);
var outside = new UnityEngine.Vector3(box.bounds.min.x - 1.5f, .05f, player.config.planeZ);
UnityEditor.EditorApplication.CallbackFunction observer = null;
System.Action<UnityEngine.Rendering.ScriptableRenderContext, UnityEngine.Camera> renderObserver = null;
System.Action<bool> keys = crouch => UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,
    crouch ? new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.C)
           : new UnityEngine.InputSystem.LowLevel.KeyboardState());
System.Action<UnityEngine.Vector3, bool> relocate = (position, crouch) =>
{
    if (!player.RelocateWithinRun(position)) throw new System.InvalidOperationException("Real RelocateWithinRun refused.");
    UnityEngine.Physics.SyncTransforms();
    keys(crouch); // Relocate clears requests; requeue C for natural PlayerInputReader Update.
};
System.Action<int> advance = next =>
{
    phase = next; phaseStarted = UnityEditor.EditorApplication.timeSinceStartup;
    phaseFrame = UnityEngine.Time.frameCount; shotRequested = false; shotDone = false;
};
System.Action<string> requestShot = label => { if (!shotRequested) { wantedShot = label; shotRequested = true; } };
System.Func<object> sample = () =>
{
    var p = player.Position;
    var c = camera.transform.position;
    var r = camera.transform.rotation;
    var stack = UnityEngine.Rendering.VolumeManager.instance.stack;
    var vignette = stack.GetComponent<UnityEngine.Rendering.Universal.Vignette>();
    var color = stack.GetComponent<UnityEngine.Rendering.Universal.ColorAdjustments>();
    var bloom = stack.GetComponent<UnityEngine.Rendering.Universal.Bloom>();
    var tone = stack.GetComponent<UnityEngine.Rendering.Universal.Tonemapping>();
    return new { frame = UnityEngine.Time.frameCount, utc = System.DateTime.UtcNow.ToString("o"), phase = phase,
        gameTime = UnityEngine.Time.time, timeScale = UnityEngine.Time.timeScale, stage = flow.Stage.ToString(), scene = scene.path,
        playerId = player.GetInstanceID(), position = new[] { p.x, p.y, p.z }, crouching = player.IsCrouching,
        hidden = CoverArea.IsHidden(player), grounded = player.IsGrounded, inputC = player.inputReader.CrouchHeld,
        backpackOpen = bindings.backpackUI.IsOpen, weight = volume.weight, intensity = effect.intensity,
        health = player.health.CurrentHealth, firstCoverEnabled = cover.enabled, secondCoverExists = temporaryCover != null,
        cameraId = camera.GetInstanceID(), cameraPosition = new[] { c.x, c.y, c.z }, cameraRotation = new[] { r.x, r.y, r.z, r.w },
        cameraPixels = new[] { camera.pixelWidth, camera.pixelHeight }, pp = cameraData.renderPostProcessing,
        effectiveVignette = vignette == null ? -1f : vignette.intensity.value,
        exposure = color == null ? 0f : color.postExposure.value, bloom = bloom == null ? 0f : bloom.intensity.value,
        tone = tone == null ? "missing" : tone.mode.value.ToString() };
};
System.Action<string, string> finish = (status, reason) =>
{
    if (finished) return;
    finished = true;
    try { checks.Add(status + ": " + reason); }
    finally
    {
        UnityEditor.EditorApplication.update -= observer;
        UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= renderObserver;
        // Restore each independent resource even if a scene was unloaded or Play already ended.
        try { if (bindings != null && bindings.backpackUI != null) bindings.backpackUI.Close(); } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
        try { if (cover != null) cover.enabled = originalCoverEnabled; if (temporaryCover != null) { temporaryCover.SetActive(false); UnityEngine.Object.Destroy(temporaryCover); } } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
        try { if (keyboard != null && keyboard.added) { UnityEngine.InputSystem.InputSystem.ResetDevice(keyboard); UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard); } } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
        try { UnityEngine.InputSystem.InputSystem.settings = originalSettings; } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
        foreach (var device in disabledKeyboards)
            try { if (device != null && device.added) UnityEngine.InputSystem.InputSystem.EnableDevice(device); } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
        try { if (settings != null) UnityEngine.Object.Destroy(settings); } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
        foreach (var droneObject in disabledDrones)
            try { if (droneObject != null) droneObject.SetActive(true); } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
        try { if (UnityEditor.EditorApplication.isPlaying) UnityEngine.Application.runInBackground = originalBackground; } catch (System.Exception e) { cleanupErrors.Add(e.Message); }
        UnityEditor.SessionState.SetString(key + "ProbeToken", "");
        UnityEditor.SessionState.SetBool(key + "ProbeRunning", false);
        UnityEditor.SessionState.SetString(key + "ProbeResult", status + ": " + reason);
        var result = new { status = status, reason = reason, checks = checks, captures = captures, trace = trace,
            method = "API relocation + synthetic InputSystem C + real Backpack API; scene drones disabled temporarily; no native keyboard/mouse claim",
            disabledDroneCount = disabledDrones.Count, restoredNativeKeyboardCount = disabledKeyboards.Count,
            timeScaleNeverWritten = true, originalRunInBackground = originalBackground, callbacksRemoved = true,
            cleanupErrors = cleanupErrors, playLeftRunning = UnityEditor.EditorApplication.isPlaying,
            deferred = "Unbind/disable belongs to automated tests; root continues death/return lifecycle separately. Screenshot validity is not visual acceptance." };
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "Result.json"), Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
    }
};
renderObserver = (context, renderedCamera) =>
{
    if (finished || renderedCamera != camera || lastRenderedFrame == UnityEngine.Time.frameCount) return;
    try
    {
        lastRenderedFrame = UnityEngine.Time.frameCount;
        var row = sample(); // Actual formal camera's stack after URP volume update; no Camera.Render/PlayerLoop recursion.
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
            throw new System.InvalidOperationException("Play/time/scene/player changed; stopping and restoring temporary state.");
        if (UnityEditor.EditorApplication.timeSinceStartup - started > 60) throw new System.TimeoutException("60-second probe timeout.");
        if (pendingPng != null)
        {
            if (!System.IO.File.Exists(pendingPng)) return;
            byte[] bytes; try { bytes = System.IO.File.ReadAllBytes(pendingPng); } catch (System.IO.IOException) { return; }
            if (bytes.Length < 32) return;
            int end = bytes.Length - 8;
            if (bytes[end] != 73 || bytes[end + 1] != 69 || bytes[end + 2] != 78 || bytes[end + 3] != 68) return;
            int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            if (bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71 || width != 1920 || height != 1080)
                throw new System.InvalidOperationException("Completed screenshot is not a 1920x1080 PNG.");
            captures.Add(new { path = pendingPng, width = width, height = height, sample = pendingSample });
            pendingPng = null; pendingSample = null; shotDone = true;
        }
        if (wantedShot != null || lastRenderedFrame < phaseFrame + 3 || UnityEditor.EditorApplication.timeSinceStartup - phaseStarted < .65) return;
        bool hidden = CoverArea.IsHidden(player);
        bool full = hidden && player.IsCrouching && volume.weight >= .999f;
        bool clear = !hidden && volume.weight <= .001f;
        switch (phase)
        {
            case 0:
                if (!player.IsCrouching || !clear) return;
                checks.Add("Outside cover: natural C crouch, hidden=false, weight=0.");
                relocate(inside, false); advance(1); break;
            case 1:
                if (player.IsCrouching || !clear) return;
                requestShot("00-InsideStanding-Normal");
                if (shotDone) { checks.Add("Inside standing: hidden=false, weight=0."); keys(true); advance(2); }
                break;
            case 2:
                if (!full) return;
                requestShot("01-InsideCrouching-Stealth");
                if (shotDone) { checks.Add("Inside crouching: hidden=true, weight=1."); keys(false); advance(3); }
                break;
            case 3:
                if (player.IsCrouching || !clear) return;
                requestShot("02-InsideStanding-Exited");
                if (shotDone) { checks.Add("Stand-up restores weight=0 at the same location."); keys(true); advance(4); }
                break;
            case 4:
                if (!full) return;
                relocate(outside, true); advance(5); break;
            case 5:
                if (!player.IsCrouching || !clear) return;
                checks.Add("Leave cover while holding C: crouching remains true, hidden=false, weight=0.");
                relocate(inside, true); advance(6); break;
            case 6:
                if (!full) return;
                temporaryCover = new UnityEngine.GameObject("09I Temporary Overlap Cover");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(temporaryCover, scene);
                temporaryCover.transform.position = cover.transform.position;
                temporaryCover.transform.rotation = cover.transform.rotation;
                temporaryCover.transform.localScale = cover.transform.lossyScale;
                var secondBox = temporaryCover.AddComponent<UnityEngine.BoxCollider>();
                secondBox.center = box.center; secondBox.size = box.size; secondBox.isTrigger = true;
                temporaryCover.AddComponent<CoverArea>();
                UnityEngine.Physics.SyncTransforms();
                if (!CoverArea.IsHidden(player)) throw new System.InvalidOperationException("Overlap creation unexpectedly removed hidden state.");
                checks.Add("Overlap before disabling first Cover: hidden=true, weight=" + volume.weight);
                cover.enabled = false;
                if (!CoverArea.IsHidden(player)) throw new System.InvalidOperationException("Second Cover did not retain hidden state.");
                advance(7); break;
            case 7:
                if (!full) throw new System.InvalidOperationException("Overlap handoff lost hidden state or full vignette weight.");
                checks.Add("Overlap with first Cover disabled: hidden=true, weight=1.");
                cover.enabled = originalCoverEnabled;
                temporaryCover.SetActive(false); UnityEngine.Object.Destroy(temporaryCover); temporaryCover = null;
                if (!bindings.backpackUI.TryOpen()) throw new System.InvalidOperationException("Real Backpack.TryOpen refused.");
                advance(8); break;
            case 8:
                if (!bindings.backpackUI.IsOpen || !full) throw new System.InvalidOperationException("Opening Backpack changed hidden/vignette state.");
                requestShot("03-InsideCrouching-Backpack");
                if (shotDone) { checks.Add("Real Backpack open: crouch/hidden/weight retained."); bindings.backpackUI.Close(); keys(true); advance(9); }
                break;
            case 9:
                if (bindings.backpackUI.IsOpen || !full) throw new System.InvalidOperationException("Backpack close changed hidden/vignette state.");
                checks.Add("Real Backpack close: hidden=true, weight=1; raw per-render trace contains transition edges.");
                finish("PASS", "Behavior phases and four actual Game PNGs complete; root must visually inspect them and continue death/return.");
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
    UnityEngine.Application.runInBackground = true; // Play-only temporary value; no PlayerSettings mutation.
    foreach (var device in new System.Collections.Generic.List<UnityEngine.InputSystem.InputDevice>(UnityEngine.InputSystem.InputSystem.devices))
    {
        if (!(device is UnityEngine.InputSystem.Keyboard) || !device.native || !device.enabled) continue;
        disabledKeyboards.Add(device); UnityEngine.InputSystem.InputSystem.DisableDevice(device);
    }
    keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("Stealth09IProbe-" + token);
    foreach (var drone in drones)
        if (drone != null && drone.gameObject.activeSelf) { disabledDrones.Add(drone.gameObject); drone.gameObject.SetActive(false); }
    UnityEditor.SessionState.SetString(key + "ProbeToken", token);
    UnityEditor.SessionState.SetString(key + "ProbeDirectory", directory);
    UnityEditor.SessionState.SetBool(key + "ProbeRunning", true);
    UnityEditor.SessionState.SetString(key + "ProbeResult", "RUNNING");
    relocate(outside, true);
    UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += renderObserver;
    UnityEditor.EditorApplication.update += observer;
    armed = true;
}
catch (System.Exception e) { finish("FAIL", e.ToString()); }
finally { if (!armed && !finished) finish("FAIL", "Probe setup did not arm; temporary state cleaned."); }
return new { armed = armed && !finished, directory = directory, token = token, resultKey = key + "ProbeResult",
    note = "Asynchronous ordinary Play. Keep Game rendering; all phases timeScale=1. Do not change scene until ProbeRunning=false. Four PNGs and Result.json; native devices/settings/drones restored." };

