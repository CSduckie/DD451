// Read-only runtime telemetry. Does not position, initialize or force any AI state/timer.
string label = "first-alert"; // Use a distinct label for each planned scenario.
float duration = 6f;
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
if (!UnityEditor.EditorApplication.isPlaying || flow == null || flow.Player == null)
    throw new System.InvalidOperationException("Use the real Bootstrap session first.");
var drone = System.Array.Find(UnityEngine.Object.FindObjectsByType<DroneController>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None),
    d => d.gameObject.scene.path == flow.ruinsScenePath && d.name == "Drone");
if (drone == null || drone.vision == null) throw new System.InvalidOperationException("Actual formal Drone/vision not found.");
if (UnityEditor.SessionState.GetBool("MyPrecious.TimedAlertRecorder.Running", false))
    throw new System.InvalidOperationException("Cancel the previous recorder first.");
string token = System.Guid.NewGuid().ToString("N");
string directory = "E:/DD451/Docs/Verification/Enemy/TimedAlert/RootAcceptance/" + label + "-" + token.Substring(0, 8);
System.IO.Directory.CreateDirectory(directory);
var player = flow.Player;
var renderer = drone.vision.GetComponent<UnityEngine.MeshRenderer>();
var effect = drone.GetComponent<DroneStatusLight>();
var light = effect != null ? effect.targetLight : null;
var properties = new UnityEngine.MaterialPropertyBlock();
var culture = System.Globalization.CultureInfo.InvariantCulture;
var csv = new System.Text.StringBuilder("frame,time,fixedTime,state,health,hidden,canSee,exploded,x,y,z,speed,lastX,lastY,lastZ,targetX,targetY,targetZ,visionActive,visionEnabled,rangeEnabled,rendererEnabled,lightR,lightG,lightB,intensity,legacyFill\n");
float started = UnityEngine.Time.time;
double realStarted = UnityEditor.EditorApplication.timeSinceStartup;
int lastFrame = -1;
UnityEditor.EditorApplication.CallbackFunction observer = null;
System.Action<string> finish = reason =>
{
    UnityEditor.EditorApplication.update -= observer;
    UnityEditor.SessionState.SetBool("MyPrecious.TimedAlertRecorder.Running", false);
    UnityEditor.SessionState.SetString("MyPrecious.TimedAlertRecorder.Result", reason + ":" + directory);
    System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "Samples.csv"), csv.ToString());
    System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "Result.txt"), reason + "\nTelemetry only; root must evaluate transitions, timing and screenshots. No automatic overall PASS.");
};
observer = delegate
{
    try
    {
        if (UnityEditor.SessionState.GetString("MyPrecious.TimedAlertRecorder.Token", "") != token) { finish("CANCELLED"); return; }
        if (!UnityEditor.EditorApplication.isPlaying || player == null || drone == null) { finish("Runtime ended"); return; }
        if (UnityEditor.EditorApplication.timeSinceStartup - realStarted > 30) { finish("Real-time timeout"); return; }
        if (lastFrame == UnityEngine.Time.frameCount) return;
        lastFrame = UnityEngine.Time.frameCount;
        UnityEngine.Vector3 target;
        drone.TryGetPatrolPoint(out target);
        var p = drone.Body.position;
        var known = drone.LastKnownPosition;
        var color = light != null ? light.color : UnityEngine.Color.clear;
        if (renderer != null) renderer.GetPropertyBlock(properties);
        csv.AppendFormat(culture, "{0},{1:R},{2:R},{3},{4},{5},{6},{7},{8:R},{9:R},{10:R},{11:R},{12:R},{13:R},{14:R},{15:R},{16:R},{17:R},{18},{19},{20},{21},{22:R},{23:R},{24:R},{25:R},{26:R}\n",
            lastFrame, UnityEngine.Time.time, UnityEngine.Time.fixedTime, drone.CurrentState, player.health.CurrentHealth,
            CoverArea.IsHidden(player), drone.vision.CanSee(player), drone.HasExploded, p.x, p.y, p.z,
            drone.FlightVelocity.magnitude, known.x, known.y, known.z, target.x, target.y, target.z,
            drone.vision.gameObject.activeInHierarchy, drone.vision.enabled, drone.vision.RangeCollider != null && drone.vision.RangeCollider.enabled,
            renderer != null && renderer.enabled, color.r, color.g, color.b, light != null ? light.intensity : -1f, properties.GetFloat("_FillProgress"));
        if (UnityEngine.Time.time - started >= duration) finish("CAPTURE_COMPLETE");
    }
    catch (System.Exception e) { finish("ERROR:" + e.Message); }
};
UnityEditor.SessionState.SetString("MyPrecious.TimedAlertRecorder.Token", token);
UnityEditor.SessionState.SetBool("MyPrecious.TimedAlertRecorder.Running", true);
UnityEditor.SessionState.SetString("MyPrecious.TimedAlertRecorder.Result", "RECORDING:" + directory);
UnityEditor.EditorApplication.update += observer;
return new { armed = true, output = directory, note = "Only records; player setup and screenshot review remain root's responsibility." };
