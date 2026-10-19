// External Unity MCP execute_code statements; NEVER place this file in Assets.
// Requires the two owners' completed changes, EnemyTest already in Play, and Game at1920x1080.
// Uses real observation/state/physics. Only runtime player positioning, temporary obstruction,
// one other drone's activation, and runtime waypoint configuration are assisted by this probe.
if (!UnityEditor.EditorApplication.isPlaying || UnityEngine.Screen.width != 1920 || UnityEngine.Screen.height != 1080)
    throw new System.InvalidOperationException("Run EnemyTest in Play with the Game resolution at1920x1080 first.");
string runningKey = "MyPrecious.RootAlertScenario.Running";
string tokenKey = "MyPrecious.RootAlertScenario.Token";
if (UnityEditor.SessionState.GetBool(runningKey, false))
    throw new System.InvalidOperationException("Another scenario owns the observer. Cancel its token and let it clean up first.");
var driver = UnityEngine.Object.FindFirstObjectByType<EnemyTestDriver>();
if (driver == null || !driver.IsReady) throw new System.InvalidOperationException("The real EnemyTest driver is not ready.");
var drone = System.Array.Find(driver.drones, d => d != null && d.name == "Drone_B");
var other = System.Array.Find(driver.drones, d => d != null && d.name == "Drone_A");
var player = driver.player;
if (drone == null || !drone.IsRunning || drone.vision == null || !DroneController.CanTarget(player))
    throw new System.InvalidOperationException("Live Drone_B, its real vision and an eligible player are required.");
if (drone.patrolPoints == null || drone.patrolPoints.Length < 2 || drone.patrolPoints[0] == null || drone.patrolPoints[1] == null)
    throw new System.InvalidOperationException("Drone_B needs at least two authored patrol waypoints.");
if (UnityEngine.Mathf.Abs(drone.config.alertFillSeconds - 2f) > 0.001f
    || UnityEngine.Mathf.Abs(drone.config.alertDecaySeconds - 2f) > 0.001f || drone.config.contactDamage != 1)
    throw new System.InvalidOperationException("This scenario verifies the authored2s/2s timings and1 damage, without changing config.");
if (player.health.CurrentHealth <= 1) throw new System.InvalidOperationException("Use a fresh living player with at least2 health.");

string token = System.Guid.NewGuid().ToString("N");
string output = "E:/DD451/Docs/Verification/Enemy/AlertFill/RootAcceptance/Scenario-" + token.Substring(0, 8);
System.IO.Directory.CreateDirectory(output);
var culture = System.Globalization.CultureInfo.InvariantCulture;
var csv = new System.Text.StringBuilder("frame,time,phase,state,progress,visible,health,exploded,x,y,z,speed,targetX,targetY,targetZ\n");
var report = new System.Text.StringBuilder("ROOT API-ASSISTED SCENE ACCEPTANCE\nResolution1920x1080; timings2s fill/2s decay; real EnemyTest Drone_B/player.\n");
report.AppendLine("Limit: API positioning/frozen player and a temporary wall isolate conditions. No AI state/progress changes or asset saves. Native keyboard movement is not tested. Screenshots still require root visual review.");
var capsule = player.GetComponent<UnityEngine.CapsuleCollider>();
var sphere = drone.GetComponent<UnityEngine.SphereCollider>();
var originalPoints = (UnityEngine.Transform[])drone.patrolPoints.Clone();
var firstPoint = originalPoints[0];
var secondPoint = originalPoints[1];
var firstPosition = firstPoint.position;
var secondPosition = secondPoint.position;
var playerPosition = player.Body.position;
bool playerKinematic = player.Body.isKinematic;
bool playerGravity = player.Body.useGravity;
bool otherActive = other != null && other.gameObject.activeSelf;
int initialHealth = player.health.CurrentHealth;
UnityEngine.GameObject wall = null;
UnityEngine.Material wallMaterial = null;
UnityEngine.Vector3 routeBefore = UnityEngine.Vector3.zero;
UnityEngine.Vector3 hoverPosition = UnityEngine.Vector3.zero;
UnityEngine.Vector3 motionStart = UnityEngine.Vector3.zero;
UnityEngine.Vector3 motionTarget = UnityEngine.Vector3.zero;
UnityEngine.Vector3 savedVisiblePosition = UnityEngine.Vector3.zero;
UnityEngine.Vector3 hiddenLastKnown = UnityEngine.Vector3.zero;
float resumeProgress = 0f;
float maximumHoverDrift = 0f;
float secondAlertStarted = 0f;
float phaseStarted = UnityEngine.Time.time;
double started = UnityEditor.EditorApplication.timeSinceStartup;
double phaseRealStarted = started;
int phase = 0;
int lastFrame = -1;
bool finished = false;
bool screenshotPaused = false;
int pauseEditorTicks = 0;
int pauseFrame = -1;
int captureFrame = -1;
double pauseStarted = 0;
string pendingImage = null;
System.Action afterImage = null;
UnityEditor.EditorApplication.CallbackFunction observer = null;
System.Action<bool, string> require = (condition, message) =>
{
    if (!condition) throw new System.InvalidOperationException(message);
};
System.Action<string> pass = message => report.AppendLine("PASS: " + message);
System.Action<int> enter = next =>
{
    phase = next;
    lastFrame = UnityEngine.Time.frameCount;
    phaseStarted = UnityEngine.Time.time;
    phaseRealStarted = UnityEditor.EditorApplication.timeSinceStartup;
};
System.Func<UnityEngine.Vector3> forward = () =>
{
    var value = drone.transform.TransformDirection(drone.FacingRotation * UnityEngine.Vector3.right);
    value.y = 0f;
    value.z = 0f;
    return value.normalized;
};
System.Action<UnityEngine.Vector3> placeRoot = position =>
{
    require(player.RelocateWithinRun(position), "Real player RelocateWithinRun rejected the fixture position.");
    player.Body.isKinematic = true;
    UnityEngine.Physics.SyncTransforms();
};
System.Action<UnityEngine.Vector3> placeCentre = centre =>
{
    var offset = capsule.bounds.center - player.Body.position;
    var position = centre - offset;
    position.z = player.config.planeZ;
    placeRoot(position);
};
System.Action placeVisible = () =>
{
    placeCentre(drone.vision.transform.position + forward() * 2.5f);
    require(drone.vision.CanSee(player), "Actual scene geometry blocks the requested visible fixture. Do not bypass vision.");
};
System.Action placeOutside = () =>
{
    placeCentre(drone.vision.transform.position + forward() * (drone.vision.radius + 3f));
    require(!drone.vision.CanSee(player), "The out-of-range fixture must really be invisible.");
};
System.Action verifyContact = () =>
{
    UnityEngine.Vector3 direction;
    float distance;
    require(UnityEngine.Physics.ComputePenetration(sphere, sphere.transform.position, sphere.transform.rotation,
        capsule, capsule.transform.position, capsule.transform.rotation, out direction, out distance),
        "The actual drone sphere and player capsule must overlap for the contact check.");
};
System.Action<string, System.Action> takeImage = (name, continuation) =>
{
    pendingImage = System.IO.Path.Combine(output, name + ".png");
    afterImage = continuation;
    screenshotPaused = true;
    pauseEditorTicks = 0;
    pauseFrame = UnityEngine.Time.frameCount;
    captureFrame = -1;
    pauseStarted = UnityEditor.EditorApplication.timeSinceStartup;
    UnityEngine.Time.timeScale = 0f;
    System.IO.File.WriteAllText(System.IO.Path.Combine(output, "Samples.csv"), csv.ToString());
};
System.Action<string, string> finish = (status, message) =>
{
    if (finished) return;
    finished = true;
    try
    {
        if (wall != null) { wall.SetActive(false); UnityEngine.Object.Destroy(wall); }
        if (wallMaterial != null) UnityEngine.Object.Destroy(wallMaterial);
        if (firstPoint != null) firstPoint.position = firstPosition;
        if (secondPoint != null) secondPoint.position = secondPosition;
        if (drone != null) drone.patrolPoints = originalPoints;
        if (player != null && player.Body != null)
        {
            if (UnityEditor.EditorApplication.isPlaying && player.isActiveAndEnabled && player.IsPrepared && !player.IsDead)
                player.RelocateWithinRun(playerPosition);
            player.Body.isKinematic = playerKinematic;
            player.Body.useGravity = playerGravity;
        }
        if (other != null) other.gameObject.SetActive(otherActive);
    }
    catch (System.Exception cleanupError)
    {
        report.AppendLine("CLEANUP ISSUE: " + cleanupError.Message);
    }
    finally
    {
        UnityEngine.Time.timeScale = 1f;
        UnityEngine.Application.runInBackground = false;
        UnityEditor.EditorApplication.update -= observer;
        UnityEditor.SessionState.SetBool(runningKey, false);
        UnityEditor.SessionState.SetString("MyPrecious.RootAlertScenario.Result", status + ":" + output);
    }
    report.AppendLine(status + ": " + message);
    report.AppendLine("Maximum measured Alert body drift=" + maximumHoverDrift.ToString("R", culture));
    report.AppendLine("Ended in Play for root review; Time.timeScale=1, runInBackground=false, observer removed, temporary wall/waypoint/player-body changes restored. The tested drone's real explosion and player's one health loss are intentionally not reset.");
    try
    {
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "Samples.csv"), csv.ToString());
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "Result.txt"), report.ToString());
    }
    catch (System.Exception fileError)
    {
        UnityEditor.SessionState.SetString("MyPrecious.RootAlertScenario.Result", "EVIDENCE_WRITE_FAILED:" + fileError.Message);
        UnityEngine.Debug.LogError("Root acceptance evidence could not be saved: " + fileError.Message);
    }
};
observer = delegate
{
    try
    {
        if (UnityEditor.SessionState.GetString(tokenKey, "") != token) { finish("CANCELLED", "Root cancelled the token."); return; }
        require(UnityEditor.EditorApplication.isPlaying && driver != null && drone != null && player != null,
            "Play ended or a required runtime object disappeared.");
        require(UnityEditor.EditorApplication.timeSinceStartup - started < 120.0, "Scenario exceeded120 real seconds.");
        if (screenshotPaused)
        {
            pauseEditorTicks++;
            require(UnityEditor.EditorApplication.timeSinceStartup - pauseStarted < 10.0,
                "Screenshot/render did not complete within10 seconds; resumed safely.");
            if (captureFrame < 0 && pauseEditorTicks >= 2 && UnityEngine.Time.frameCount >= pauseFrame + 2)
            {
                UnityEngine.ScreenCapture.CaptureScreenshot(pendingImage);
                captureFrame = UnityEngine.Time.frameCount;
            }
            else if (captureFrame >= 0 && UnityEngine.Time.frameCount >= captureFrame + 2
                && System.IO.File.Exists(pendingImage) && new System.IO.FileInfo(pendingImage).Length > 128)
            {
                report.AppendLine("SCREENSHOT: " + pendingImage);
                screenshotPaused = false;
                UnityEngine.Time.timeScale = 1f;
                var continuation = afterImage;
                afterImage = null;
                continuation();
            }
            return;
        }
        require(UnityEditor.EditorApplication.timeSinceStartup - phaseRealStarted < (phase == 0 ? 30.0 : 8.0),
            "Phase" + phase + " timed out. No forced state/progress fallback is allowed.");
        if (lastFrame == UnityEngine.Time.frameCount) return;
        lastFrame = UnityEngine.Time.frameCount;
        UnityEngine.Vector3 target;
        require(drone.TryGetPatrolPoint(out target), "Configured patrol target disappeared.");
        bool visible = drone.vision.CanSee(player);
        var position = drone.Body.position;
        csv.AppendFormat(culture, "{0},{1:R},{2},{3},{4:R},{5},{6},{7},{8:R},{9:R},{10:R},{11:R},{12:R},{13:R},{14:R}\n",
            lastFrame, UnityEngine.Time.time, phase, drone.CurrentState, drone.AlertProgress, visible,
            player.health.CurrentHealth, drone.HasExploded, position.x, position.y, position.z,
            drone.FlightVelocity.magnitude, target.x, target.y, target.z);
        if ((phase >= 2 && phase <= 5 || phase >= 11 && phase <= 13) && drone.CurrentState == DroneState.Alert)
        {
            maximumHoverDrift = UnityEngine.Mathf.Max(maximumHoverDrift, UnityEngine.Vector3.Distance(position, hoverPosition));
            require(maximumHoverDrift <= 0.005f && drone.FlightVelocity.sqrMagnitude <= 0.0001f,
                "Alert must stay at its captured location with zero commanded flight velocity.");
        }
        require(player.health.CurrentHealth == (phase >= 16 ? initialHealth - 1 : initialHealth)
            || phase == 15, "Unexpected health change before valid Chase contact or after its single hit.");
        switch (phase)
        {
            case 0:
                if (drone.CurrentState != DroneState.Patrol || UnityEngine.Vector3.Distance(target, secondPosition) > 0.01f
                    || UnityEngine.Vector3.Distance(position, target) < 1.75f || drone.FacingDirection != 1
                    || drone.vision.transform.right.x < 0.98f || drone.FlightVelocity.x < 0.1f) break;
                routeBefore = target;
                pass("Actual patrol is travelling toward the second configured waypoint.");
                placeVisible();
                enter(1);
                break;
            case 1:
                require(drone.CurrentState != DroneState.Chase, "Discovery skipped observable Alert.");
                if (drone.CurrentState != DroneState.Alert) break;
                hoverPosition = position;
                require(visible && drone.Target == player && drone.AlertProgress < 0.2f, "Natural discovery must expose an initially unfilled Alert.");
                pass("Actual ObservePlayer entered Alert without a forced state.");
                enter(2);
                break;
            case 2:
                require(drone.CurrentState == DroneState.Alert && visible, "Visibility must remain real during initial accumulation.");
                if (drone.AlertProgress < 0.5f) break;
                pass("Visible Alert accumulated to half while the body stayed still.");
                takeImage("01-AlertHalf", () =>
                {
                    hiddenLastKnown = drone.LastKnownPosition;
                    wall = UnityEngine.GameObject.CreatePrimitive(UnityEngine.PrimitiveType.Cube);
                    wall.name = "RootAlertAcceptance_TemporaryWall";
                    wall.hideFlags = UnityEngine.HideFlags.DontSave;
                    wall.transform.position = (drone.vision.transform.position + capsule.bounds.center) * 0.5f;
                    wall.transform.localScale = new UnityEngine.Vector3(0.3f, 3f, 3f);
                    wall.layer = drone.gameObject.layer;
                    var shader = UnityEngine.Shader.Find("Universal Render Pipeline/Lit");
                    if (shader != null)
                    {
                        wallMaterial = new UnityEngine.Material(shader);
                        wallMaterial.hideFlags = UnityEngine.HideFlags.DontSave;
                        wallMaterial.color = new UnityEngine.Color(0.35f, 0.38f, 0.4f);
                        wall.GetComponent<UnityEngine.Renderer>().sharedMaterial = wallMaterial;
                    }
                    UnityEngine.Physics.SyncTransforms();
                    require(!drone.vision.CanSee(player), "Temporary wall must actually obstruct detection.");
                    enter(3);
                });
                break;
            case 3:
                require(drone.CurrentState == DroneState.Alert && !visible && drone.Target == null,
                    "Occluded target must decay inside Alert, not snap to another state.");
                require(UnityEngine.Vector3.Distance(drone.LastKnownPosition, hiddenLastKnown) < 0.001f,
                    "Hidden target coordinates must not update the last-seen snapshot.");
                if (drone.AlertProgress > 0.25f) break;
                require(drone.AlertProgress > 0.1f, "Decay checkpoint was skipped or snapped to zero.");
                pass("Actual wall occlusion reduced half-fill to about25% without moving the drone.");
                takeImage("02-OccludedQuarter", () =>
                {
                    resumeProgress = drone.AlertProgress;
                    wall.SetActive(false);
                    UnityEngine.Physics.SyncTransforms();
                    require(drone.vision.CanSee(player), "Removing only the test wall must restore visibility.");
                    enter(4);
                });
                break;
            case 4:
                require(drone.CurrentState == DroneState.Alert && visible && drone.AlertProgress >= resumeProgress - 0.001f,
                    "Reacquisition must retain the remainder instead of starting again from zero.");
                if (drone.AlertProgress < 0.5f) break;
                pass("Reacquisition resumed from the remaining quarter and reached half again.");
                takeImage("03-ResumedHalf", () => { placeOutside(); enter(5); });
                break;
            case 5:
                require(!visible && drone.CurrentState != DroneState.Chase, "Out-of-range loss cannot keep accumulating.");
                if (drone.CurrentState != DroneState.Patrol) break;
                require(drone.AlertProgress == 0f && UnityEngine.Vector3.Distance(target, routeBefore) < 0.001f,
                    "Decay to zero must preserve the original second-waypoint target.");
                pass("Decay to zero returned to Patrol with the same second-waypoint target.");
                motionStart = position; motionTarget = target; enter(6);
                break;
            case 6:
                if (UnityEngine.Time.time - phaseStarted < 0.2f) break;
                require(UnityEngine.Vector3.Dot(position - motionStart, (motionTarget - motionStart).normalized) > 0.05f,
                    "Resumed Patrol did not actually move toward the preserved route target.");
                secondPoint.position = secondPosition + new UnityEngine.Vector3(2f, 1f, 0f);
                drone.TryGetPatrolPoint(out target);
                require(UnityEngine.Vector3.Distance(target, secondPoint.position) < 0.001f, "Runtime waypoint edit was not consumed.");
                motionStart = position; motionTarget = target; enter(7);
                break;
            case 7:
                if (UnityEngine.Time.time - phaseStarted < 0.25f) break;
                require(UnityEngine.Vector3.Dot(position - motionStart, (motionTarget - motionStart).normalized) > 0.05f,
                    "Waypoint position changed in data but actual movement did not follow it.");
                pass("Moving the current waypoint at runtime changed both the public target and actual travel.");
                var swapped = (UnityEngine.Transform[])originalPoints.Clone();
                swapped[0] = secondPoint; swapped[1] = firstPoint; drone.patrolPoints = swapped;
                drone.TryGetPatrolPoint(out target);
                require(UnityEngine.Vector3.Distance(target, firstPosition) < 0.001f, "Runtime array order was not consumed at the unchanged index.");
                motionStart = position; motionTarget = target; enter(8);
                break;
            case 8:
                if (UnityEngine.Time.time - phaseStarted < 0.25f) break;
                require(UnityEngine.Vector3.Dot(position - motionStart, (motionTarget - motionStart).normalized) > 0.05f,
                    "Swapping the waypoint array changed data but not actual travel.");
                pass("Changing waypoint array order changed the current target and actual movement.");
                firstPoint.position = firstPosition; secondPoint.position = secondPosition; drone.patrolPoints = originalPoints;
                enter(9);
                break;
            case 9:
                if (UnityEngine.Time.time - phaseStarted < 0.4f || drone.vision.transform.right.x < 0.98f) break;
                placeVisible(); enter(10);
                break;
            case 10:
                require(drone.CurrentState != DroneState.Chase, "Second discovery skipped Alert.");
                if (drone.CurrentState != DroneState.Alert) break;
                hoverPosition = position; secondAlertStarted = UnityEngine.Time.time; enter(11);
                break;
            case 11:
                require(drone.CurrentState == DroneState.Alert && visible, "Second Alert lost visibility before the controlled contact pulse.");
                if (drone.AlertProgress < 0.5f) break;
                savedVisiblePosition = player.Body.position;
                placeCentre(drone.vision.transform.position + forward() * 0.15f);
                verifyContact();
                require(drone.vision.CanSee(player), "Contact fixture must remain ahead of the real vision origin.");
                enter(12);
                break;
            case 12:
                require(drone.CurrentState == DroneState.Alert && !drone.HasExploded,
                    "Actual Alert contact must not explode or skip the remaining warning.");
                if (UnityEngine.Time.time - phaseStarted < 0.12f) break;
                pass("Real body overlap ran for at least0.12 game seconds in Alert without damage or explosion.");
                placeRoot(savedVisiblePosition);
                require(drone.vision.CanSee(player), "Restoring the visible player position must remain visible.");
                enter(13);
                break;
            case 13:
                if (drone.CurrentState != DroneState.Chase) { require(drone.CurrentState == DroneState.Alert, "Expected continued Alert before full charge."); break; }
                require(UnityEngine.Time.time - secondAlertStarted >= 1.8f && !drone.HasExploded,
                    "Chase began too early or contact remained unintentionally active.");
                pass("A second continuous real Alert reached natural Chase at the authored timing; no forced ChangeState.");
                takeImage("04-NaturalChase", () => { motionStart = drone.Body.position; enter(14); });
                break;
            case 14:
                require(drone.CurrentState == DroneState.Chase && !drone.HasExploded, "Chase must stay active before the final contact fixture.");
                if (UnityEngine.Time.time - phaseStarted < 0.15f) break;
                require(UnityEngine.Vector3.Distance(position, motionStart) > 0.1f, "Natural Chase did not start moving.");
                pass("Natural Chase moved for at least0.15 game seconds before contact.");
                placeCentre(drone.vision.transform.position + forward() * 0.15f);
                verifyContact();
                require(drone.vision.CanSee(player), "Final contact must not first become invisible and legitimately switch to Search.");
                enter(15);
                break;
            case 15:
                if (!drone.HasExploded) break;
                require(player.health.CurrentHealth == initialHealth - 1, "Real Chase contact must deal exactly one health point.");
                pass("Actual physics contact in Chase caused one explosion and one health loss.");
                enter(16);
                break;
            case 16:
                if (UnityEngine.Time.time - phaseStarted < 0.3f) break;
                require(drone.HasExploded && !drone.visual.gameObject.activeInHierarchy && !drone.vision.gameObject.activeInHierarchy,
                    "Explosion must leave the body model and vision hidden.");
                var lightEffect = drone.GetComponent<DroneStatusLight>();
                require(lightEffect != null && lightEffect.targetLight != null
                    && (!lightEffect.targetLight.gameObject.activeInHierarchy || !lightEffect.targetLight.enabled || lightEffect.targetLight.intensity <= 0f),
                    "No status light may keep illuminating after explosion.");
                pass("Another0.3 game seconds produced no duplicate damage; model, fan and light stayed hidden.");
                takeImage("05-ExplosionCleanup", () => finish("PASS", "All requested API-assisted runtime checkpoints passed. Root must still inspect saved screenshots."));
                break;
        }
    }
    catch (System.Exception exception)
    {
        finish("FAIL", "Phase" + phase + ": " + exception.Message);
    }
};
UnityEditor.SessionState.SetString(tokenKey, token);
UnityEditor.SessionState.SetBool(runningKey, true);
UnityEditor.SessionState.SetString("MyPrecious.RootAlertScenario.Result", "RUNNING:" + output);
try
{
    if (other != null) other.gameObject.SetActive(false);
    placeCentre(drone.Body.position + UnityEngine.Vector3.left * 20f);
    require(!drone.vision.CanSee(player), "Initial off-screen fixture is not actually outside vision.");
    UnityEngine.Application.runInBackground = true;
    UnityEngine.Time.timeScale = 1f;
    UnityEditor.EditorApplication.update += observer;
}
catch (System.Exception exception)
{
    finish("FAIL", "Initialization: " + exception.Message);
}
return new { armed = !finished, outputDirectory = output, cancelBySettingSessionStateTokenToEmpty = tokenKey,
    note = "Single Editor observer, real scene AI/physics, no forced state/progress. Final result and CSV are written on completion. Root performs visual review and stops Play." };
