// Docs-only execute_code method body. Not compiled/run here; never import into Assets.
// Set PlayerFill.Action to Right / Left / Jump / Crouch / Cleanup before each invocation.
string key = "PlayerFill.";
string action = UnityEditor.SessionState.GetString(key + "Action", "Right");
System.Action release = () =>
{
    var device = UnityEngine.InputSystem.InputSystem.GetDeviceById(UnityEditor.SessionState.GetInt(key + "DeviceId", -1));
    if (device != null && device.name == UnityEditor.SessionState.GetString(key + "DeviceName", ""))
    {
        UnityEngine.InputSystem.InputSystem.ResetDevice(device);
        UnityEngine.InputSystem.InputSystem.RemoveDevice(device);
    }
    if (UnityEditor.SessionState.GetBool(key + "Held", false))
    {
        UnityEngine.Time.timeScale = UnityEditor.SessionState.GetFloat(key + "OriginalTimeScale", 1f);
        UnityEngine.Application.runInBackground = UnityEditor.SessionState.GetBool(key + "OriginalBackground", false);
    }
    UnityEditor.SessionState.SetBool(key + "Held", false);
    UnityEditor.SessionState.SetInt(key + "DeviceId", -1);
};
if (action == "Cleanup")
{
    if (UnityEditor.SessionState.GetBool("PlayerFill.CaptureRunning", false))
        throw new System.InvalidOperationException("Wait for screenshot completion before resuming time.");
    UnityEditor.SessionState.SetString(key + "Token", "");
    if (!UnityEditor.SessionState.GetBool(key + "Running", false)) release();
    return new { cleanupRequested = true, running = UnityEditor.SessionState.GetBool(key + "Running", false),
        held = UnityEditor.SessionState.GetBool(key + "Held", false), next = "Wait for both Running and Held=false; active observer cleans itself on next Editor tick." };
}
if (UnityEditor.SessionState.GetBool(key + "Running", false) || UnityEditor.SessionState.GetBool(key + "Held", false))
    throw new System.InvalidOperationException("Run Cleanup and wait for completion before starting another action.");
if (action != "Right" && action != "Left" && action != "Jump" && action != "Crouch")
    throw new System.InvalidOperationException("Action must be Right, Left, Jump, Crouch or Cleanup.");
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
var player = flow != null ? flow.Player : null;
if (!UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isPaused || UnityEngine.Time.timeScale != 1f
    || player == null || !player.CanAct || !player.IsGrounded || player.IsCrouching || player.Body.isKinematic
    || UnityEditor.SessionState.GetBool("PlayerFill.CaptureRunning", false))
    throw new System.InvalidOperationException("Require normal unpaused Play, timeScale1, standing grounded unlocked formal player and no pending capture.");
var lights = player.GetComponentsInChildren<UnityEngine.Light>(true);
if (lights.Length != 1 || lights[0].transform.parent != player.transform)
    throw new System.InvalidOperationException("Expected exactly one player Light directly under the player root.");
int originalLightId = lights[0].GetInstanceID();
var scene = player.gameObject.scene;
var startPosition = player.Position;
System.Func<object> sample = () =>
{
    var lampRows = new System.Collections.Generic.List<object>();
    foreach (var lamp in player.GetComponentsInChildren<UnityEngine.Light>(true))
    {
        var local = lamp.transform.localPosition;
        var world = lamp.transform.position;
        lampRows.Add(new { id = lamp.GetInstanceID(), name = lamp.name, active = lamp.isActiveAndEnabled,
            parent = lamp.transform.parent != null ? lamp.transform.parent.name : "", parentId = lamp.transform.parent != null ? lamp.transform.parent.GetInstanceID() : 0,
            directlyUnderPlayer = lamp.transform.parent == player.transform, localPosition = new[] { local.x, local.y, local.z }, worldPosition = new[] { world.x, world.y, world.z } });
    }
    var p = player.Position;
    return new { frame = UnityEngine.Time.frameCount, time = UnityEngine.Time.time, timeScale = UnityEngine.Time.timeScale,
        scene = player.gameObject.scene.path, playerId = player.GetInstanceID(), position = new[] { p.x, p.y, p.z }, state = player.CurrentState.ToString(),
        facing = player.FacingDirection, grounded = player.IsGrounded, crouching = player.IsCrouching,
        input = new { move = player.inputReader.MoveX, jump = player.inputReader.JumpHeld, crouch = player.inputReader.CrouchHeld },
        lightCount = lampRows.Count, lights = lampRows };
};
object before = sample();
string token = System.Guid.NewGuid().ToString("N");
string output = UnityEditor.SessionState.GetString(key + "Directory", "E:/DD451/Docs/Verification/Rendering/PlayerFillLight/Runtime");
System.IO.Directory.CreateDirectory(output);
string resultFile = System.IO.Path.Combine(output, action + "-" + token.Substring(0, 8) + ".json");
var button = action == "Right" ? UnityEngine.InputSystem.Key.D : action == "Left" ? UnityEngine.InputSystem.Key.A
    : action == "Jump" ? UnityEngine.InputSystem.Key.Space : UnityEngine.InputSystem.Key.C;
var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("PlayerFillProbe" + token);
UnityEditor.SessionState.SetInt(key + "DeviceId", keyboard.deviceId);
UnityEditor.SessionState.SetString(key + "DeviceName", keyboard.name);
UnityEditor.SessionState.SetFloat(key + "OriginalTimeScale", UnityEngine.Time.timeScale);
UnityEditor.SessionState.SetBool(key + "OriginalBackground", UnityEngine.Application.runInBackground);
UnityEditor.SessionState.SetBool(key + "Held", true);
UnityEditor.SessionState.SetString(key + "Token", token);
UnityEditor.SessionState.SetBool(key + "Running", true);
double wallStart = UnityEditor.EditorApplication.timeSinceStartup;
float gameStart = UnityEngine.Time.time;
bool finished = false;
int lastFrame = -1;
UnityEditor.EditorApplication.CallbackFunction observer = null;
System.Action<bool, string> finish = (freeze, reason) =>
{
    if (finished) return;
    finished = true;
    UnityEditor.EditorApplication.update -= observer;
    UnityEditor.SessionState.SetBool(key + "Running", false);
    if (freeze) UnityEngine.Time.timeScale = 0f; else release();
    UnityEditor.SessionState.SetString(key + "Result", (freeze ? "FROZEN: " : "FAILED/CANCELLED: ") + reason);
    var result = new { action = action, reached = freeze, reason = reason, method = "Synthetic InputSystem KeyboardState; natural PlayerInputReader and state-machine Update/FixedUpdate; not native keyboard/mouse",
        observerRemoved = true, inputHeldForScreenshot = freeze, before = before, after = player != null ? sample() : null };
    System.IO.File.WriteAllText(resultFile, Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
};
observer = () =>
{
    try
    {
        if (UnityEditor.SessionState.GetString(key + "Token", "") != token) { finish(false, "Explicit cleanup."); return; }
        if (!UnityEditor.EditorApplication.isPlaying || player == null || !player.CanAct || player.gameObject.scene != scene)
            throw new System.InvalidOperationException("Player/Play/scene changed during short action.");
        if (UnityEditor.EditorApplication.timeSinceStartup - wallStart > 6 || UnityEngine.Time.time - gameStart > 1f)
            throw new System.TimeoutException("Requested state/movement was not naturally reached; no forced pose or relocation.");
        if (lastFrame == UnityEngine.Time.frameCount) return;
        lastFrame = UnityEngine.Time.frameCount;
        float elapsed = UnityEngine.Time.time - gameStart;
        float dx = player.Position.x - startPosition.x;
        float dy = player.Position.y - startPosition.y;
        bool reached = action == "Right" ? elapsed >= .22f && dx > .2f && player.FacingDirection == 1 && player.CurrentState == PlayerState.Move
            : action == "Left" ? elapsed >= .22f && dx < -.2f && player.FacingDirection == -1 && player.CurrentState == PlayerState.Move
            : action == "Jump" ? elapsed >= .08f && dy > .25f && player.CurrentState == PlayerState.Jump && !player.IsGrounded
            : elapsed >= .10f && player.IsCrouching && player.CurrentState == PlayerState.Crouch;
        if (!reached) return;
        var currentLights = player.GetComponentsInChildren<UnityEngine.Light>(true);
        if (currentLights.Length != 1 || currentLights[0].GetInstanceID() != originalLightId || currentLights[0].transform.parent != player.transform)
            throw new System.InvalidOperationException("Player light duplicated, replaced or reparented during action.");
        finish(true, "Requested action naturally reached; timeScale0, virtual key intentionally retained until Cleanup.");
    }
    catch (System.Exception e) { finish(false, e.ToString()); }
};
try
{
    UnityEngine.Application.runInBackground = true;
    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(button));
    UnityEditor.EditorApplication.update += observer;
    UnityEditor.SessionState.SetString(key + "Result", "RUNNING: " + action);
}
catch (System.Exception e) { finish(false, e.ToString()); }
return new { armed = !finished, action = action, resultFile = resultFile, statusKey = key + "Result", cleanup = "Set PlayerFill.Action=Cleanup and execute this same body after screenshot finishes." };
