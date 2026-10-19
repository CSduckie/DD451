// Unity MCP execute_code statements. Edit only these four values for each step.
string label = "half-rise";
string stopState = "Alert";
float stopProgress = 0.5f;
bool rising = true;

var driver = UnityEngine.Object.FindFirstObjectByType<EnemyTestDriver>();
if (!UnityEditor.EditorApplication.isPlaying || driver == null || !driver.IsReady)
    throw new System.InvalidOperationException("EnemyTest must already be running normally.");
var drone = System.Array.Find(driver.drones, d => d != null && d.name == "Drone_B");
if (drone == null || !drone.IsRunning || drone.vision == null)
    throw new System.InvalidOperationException("The real Drone_B is unavailable.");
if (UnityEngine.Time.timeScale != 0f && UnityEngine.Mathf.Abs(UnityEngine.Time.timeScale - 1f) > 0.001f)
    throw new System.InvalidOperationException("This probe expects normal speed or an existing screenshot pause.");
if (UnityEngine.Mathf.Abs(drone.config.alertFillSeconds - 2f) > 0.001f
    || UnityEngine.Mathf.Abs(drone.config.alertDecaySeconds - 2f) > 0.001f)
    throw new System.InvalidOperationException("Use the authored2-second timings; do not shorten this acceptance run.");
var player = driver.player;
string directory = "E:/DD451/Docs/Verification/Enemy/AlertFill/RootAcceptance/Runtime";
System.IO.Directory.CreateDirectory(directory);
string token = System.Guid.NewGuid().ToString("N");
string tokenKey = "MyPrecious.RootAlertAcceptance.Monitor";
UnityEditor.SessionState.SetString(tokenKey, token);
UnityEditor.SessionState.SetString("MyPrecious.RootAlertAcceptance.LastCsv", "");
UnityEditor.SessionState.SetString("MyPrecious.RootAlertAcceptance.Status", "armed:" + label);
string csvPath = System.IO.Path.Combine(directory, label + "-" + token.Substring(0, 8) + ".csv");
var samples = new System.Text.StringBuilder("frame,gameTime,realtime,state,progress,visible,health,exploded,x,y,z,velocity,routeX,routeY,routeZ\n");
var culture = System.Globalization.CultureInfo.InvariantCulture;
double deadline = UnityEditor.EditorApplication.timeSinceStartup + 8.0;
double resumeDeadline = 0.0;
int lastFrame = -1;
bool pausedForCapture = false;
UnityEditor.EditorApplication.CallbackFunction monitor = null;
monitor = delegate
{
    try
    {
    if (UnityEditor.SessionState.GetString(tokenKey, "") != token)
    {
        UnityEditor.EditorApplication.update -= monitor;
        return;
    }
    if (!UnityEditor.EditorApplication.isPlaying || drone == null || player == null)
    {
        UnityEngine.Time.timeScale = 1f;
        UnityEditor.EditorApplication.update -= monitor;
        UnityEditor.SessionState.SetString("MyPrecious.RootAlertAcceptance.Status", "stopped with Play or target");
        return;
    }
    if (pausedForCapture)
    {
        if (UnityEngine.Time.timeScale > 0f || UnityEditor.EditorApplication.timeSinceStartup >= resumeDeadline)
        {
            UnityEngine.Time.timeScale = 1f;
            UnityEditor.EditorApplication.update -= monitor;
            UnityEditor.SessionState.SetString("MyPrecious.RootAlertAcceptance.Status", "resumed:" + label);
        }
        return;
    }
    if (lastFrame == UnityEngine.Time.frameCount) { return; }
    lastFrame = UnityEngine.Time.frameCount;
    UnityEngine.Vector3 route;
    drone.TryGetPatrolPoint(out route);
    var position = drone.Body.position;
    samples.AppendFormat(culture, "{0},{1:R},{2:R},{3},{4:R},{5},{6},{7},{8:R},{9:R},{10:R},{11:R},{12:R},{13:R},{14:R}\n",
        lastFrame, UnityEngine.Time.time, UnityEngine.Time.realtimeSinceStartup,
        drone.CurrentState, drone.AlertProgress, drone.vision.CanSee(player),
        player.health.CurrentHealth, drone.HasExploded, position.x, position.y, position.z,
        drone.FlightVelocity.magnitude, route.x, route.y, route.z);
    bool reached = drone.CurrentState.ToString() == stopState
        && (stopState != "Alert" || (rising ? drone.AlertProgress >= stopProgress : drone.AlertProgress <= stopProgress));
    if (reached)
    {
        UnityEngine.Time.timeScale = 0f;
        pausedForCapture = true;
        resumeDeadline = UnityEditor.EditorApplication.timeSinceStartup + 60.0;
        samples.AppendLine("# paused at requested threshold; capture Game at1920x1080, then restore Time.timeScale=1");
        System.IO.File.WriteAllText(csvPath, samples.ToString());
        UnityEditor.SessionState.SetString("MyPrecious.RootAlertAcceptance.LastCsv", csvPath);
        UnityEditor.SessionState.SetString("MyPrecious.RootAlertAcceptance.Status", "paused:" + label);
        return;
    }
    if (UnityEditor.EditorApplication.timeSinceStartup >= deadline)
    {
        samples.AppendLine("# FAILED: threshold was not reached within8 real seconds");
        System.IO.File.WriteAllText(csvPath, samples.ToString());
        UnityEngine.Time.timeScale = 1f;
        UnityEditor.EditorApplication.update -= monitor;
        UnityEditor.SessionState.SetString("MyPrecious.RootAlertAcceptance.Status", "timeout:" + label);
    }
    }
    catch (System.Exception exception)
    {
        UnityEngine.Time.timeScale = 1f;
        UnityEditor.EditorApplication.update -= monitor;
        UnityEditor.SessionState.SetString("MyPrecious.RootAlertAcceptance.Status", "error:" + exception.Message);
        UnityEngine.Debug.LogException(exception);
    }
};
UnityEditor.EditorApplication.update += monitor;
UnityEngine.Time.timeScale = 1f;
return new { armed = true, csvPath = csvPath, stopState = stopState, stopProgress = stopProgress, rising = rising };
