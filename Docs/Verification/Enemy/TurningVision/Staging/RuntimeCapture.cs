// C# 6 execute_code METHOD BODY; Docs draft only, never import into Assets.
// Cancel: SessionState.SetString("09H.Capture.Token", ""); then wait for 09H.Capture.Running=false.
var driver = UnityEngine.Object.FindFirstObjectByType<EnemyTestDriver>();
if (!UnityEditor.EditorApplication.isPlaying || driver == null || !driver.IsReady || driver.drones.Length < 2
    || UnityEditor.SessionState.GetBool("09H.Capture.Running", false))
    throw new System.InvalidOperationException("Need ordinary ready EnemyTest Play and no active 09H capture.");
var player = driver.player;
var drone = driver.drones[0];
var other = driver.drones[1];
if (player == null || drone == null || other == null || !drone.IsRunning || player.IsDead || player.IsBackpackOpen
    || drone.CurrentState != DroneState.Patrol || UnityEngine.Screen.width != 1920 || UnityEngine.Screen.height != 1080
    || UnityEngine.Time.timeScale != 1f || UnityEngine.Mathf.Abs(drone.turnSmoothTime - 0.18f) > 0.0001f
    || UnityEngine.Mathf.Abs(drone.config.discoveryAlertSeconds - 0.5f) > 0.0001f)
    throw new System.InvalidOperationException("Require living Patrol, valid player, original .18 turn/.5 alert, 1920x1080, timeScale1.");
var capsule = player.GetComponent<UnityEngine.CapsuleCollider>();
var sphere = drone.GetComponent<UnityEngine.SphereCollider>();
var vision = drone.vision;
var fan = vision.GetComponent<UnityEngine.MeshRenderer>();
var status = drone.GetComponent<DroneStatusLight>();
if (capsule == null || sphere == null || status == null || status.targetLight == null || driver.cameraBinder == null)
    throw new System.InvalidOperationException("Missing real player/body/vision/status/camera references.");
int oldFacing = drone.FacingDirection;
var oldHeading = UnityEngine.Quaternion.Euler(0f, oldFacing < 0 ? 180f : 0f, 0f);
if (UnityEngine.Quaternion.Angle(oldHeading, drone.FacingRotation) > 5f)
    throw new System.InvalidOperationException("Arm during a settled Patrol leg; do not reset the ongoing turn.");
string directory = System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName,
    "Docs/Verification/Enemy/TurningVision/GameCapture-" + System.DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"));
System.IO.Directory.CreateDirectory(directory);
string token = System.Guid.NewGuid().ToString("N");
var originalPosition = player.Body.position;
var originalVelocity = player.Body.linearVelocity;
var originalAngularVelocity = player.Body.angularVelocity;
bool originalKinematic = player.Body.isKinematic, originalGravity = player.Body.useGravity;
bool originalBackground = UnityEngine.Application.runInBackground;
float originalTimeScale = UnityEngine.Time.timeScale;
int health = player.health.CurrentHealth, round = drone.CurrentRound, phase = 0, frame = -1, attempt = 0;
float alertAt = 0f, firstRemaining = 0f, previousRemaining = 0f;
var alertHeading = UnityEngine.Quaternion.identity;
bool finished = false;
string pending = null;
int captureFrame = 0, captureStage = 0, nextPhase = 0;
long previousLength = -1;
double started = UnityEditor.EditorApplication.timeSinceStartup, pausedAt = 0;
var culture = System.Globalization.CultureInfo.InvariantCulture;
var csv = new System.Text.StringBuilder("phase,frame,time,fixedTime,timeScale,state,cover,remaining,facing,yaw,turnProgress,rangeDistance,canSee,target,renderer,range,vision,bodyX,bodyY,bodyZ,centerX,centerY,centerZ,localX,localY,localZ,originX,originY,originZ,lightR,lightG,lightB,intensity,health,exploded\n");
var report = new System.Text.StringBuilder("09H actual EnemyTest; original Cinemachine/config/.18 turn/route. API player positioning, not native input.\n");
System.Action<bool, string> check = (ok, why) => { if (!ok) throw new System.InvalidOperationException(why); };
System.Action<UnityEngine.Vector3> place = center =>
{
    center.z = player.config.planeZ;
    var position = center - player.transform.TransformVector(capsule.center);
    check(player.RelocateWithinRun(position), "Player in-round relocation rejected.");
    player.Body.isKinematic = true;
    UnityEngine.Physics.SyncTransforms();
};
System.Func<float> clearance = () =>
{
    var scale = drone.transform.lossyScale;
    float radius = sphere.radius * UnityEngine.Mathf.Max(UnityEngine.Mathf.Abs(scale.x), UnityEngine.Mathf.Abs(scale.y), UnityEngine.Mathf.Abs(scale.z));
    var center = drone.Body.position + drone.Body.rotation * UnityEngine.Vector3.Scale(sphere.center, scale);
    return UnityEngine.Vector3.Distance(capsule.ClosestPoint(center), center) - radius;
};
System.Action<string, int> capture = (name, after) =>
{
    string folder = directory + "/Attempt-" + attempt;
    System.IO.Directory.CreateDirectory(folder);
    pending = folder + "/" + name + "-1920.png";
    nextPhase = after; captureStage = 0; captureFrame = UnityEngine.Time.frameCount; previousLength = -1;
    pausedAt = UnityEditor.EditorApplication.timeSinceStartup;
    UnityEngine.Time.timeScale = 0f;
    report.AppendLine("FREEZE " + name + " gameTime=" + UnityEngine.Time.time.ToString("R", culture) + "; phase=" + phase);
};
UnityEditor.EditorApplication.CallbackFunction observer = null;
System.Action<string, string> finish = (result, why) =>
{
    if (finished) return;
    finished = true;
    UnityEditor.EditorApplication.update -= observer;
    try
    {
        UnityEngine.Time.timeScale = originalTimeScale;
        UnityEngine.Application.runInBackground = originalBackground;
        if (player != null && player.Body != null)
        {
            if (UnityEditor.EditorApplication.isPlaying && player.IsPrepared && !player.IsDead)
                player.RelocateWithinRun(originalPosition);
            player.Body.isKinematic = originalKinematic;
            player.Body.useGravity = originalGravity;
            if (!originalKinematic) { player.Body.linearVelocity = originalVelocity; player.Body.angularVelocity = originalAngularVelocity; }
        }
    }
    catch (System.Exception e) { result = "FAIL"; why += "; cleanup=" + e.Message; }
    finally
    {
        UnityEngine.Time.timeScale = originalTimeScale;
        UnityEngine.Application.runInBackground = originalBackground;
        UnityEditor.SessionState.SetBool("09H.Capture.Running", false);
        UnityEditor.SessionState.SetString("09H.Capture.Result", result + ": " + why);
    }
    report.AppendLine(result + ": " + why);
    report.AppendLine("Callback removed; player/body/timeScale/background restored. Drone1 remains runtime-disabled until exiting Play (no scene save). Camera untouched.");
    System.IO.File.WriteAllText(directory + "/Trace.csv", csv.ToString());
    System.IO.File.WriteAllText(directory + "/Result.txt", report.ToString());
};
observer = delegate
{
    try
    {
        if (UnityEditor.SessionState.GetString("09H.Capture.Token", "") != token) { finish("CANCELLED", "Token cancelled."); return; }
        check(UnityEditor.EditorApplication.isPlaying && player != null && drone != null, "Play/runtime objects ended.");
        check(UnityEditor.EditorApplication.timeSinceStartup - started < 45, "45s deadline; no forced AI fallback.");
        check(player.health.CurrentHealth == health && !drone.HasExploded && drone.CurrentRound == round, "Unexpected damage/explosion/round change.");
        if (frame == UnityEngine.Time.frameCount) return;
        frame = UnityEngine.Time.frameCount;
        var center = capsule.bounds.center;
        var local = vision.transform.InverseTransformPoint(center);
        var origin = vision.visionOrigin != null ? vision.visionOrigin.position : vision.transform.position;
        var p = drone.Body.position;
        float turn = UnityEngine.Quaternion.Angle(oldHeading, drone.FacingRotation);
        float distance = UnityEngine.Vector3.Distance(vision.RangeCollider.ClosestPoint(center), center);
        // Save raw pose/geometry before the public perception query (which may refresh its pose).
        object[] row = { phase, frame, UnityEngine.Time.time, UnityEngine.Time.fixedTime, UnityEngine.Time.timeScale,
            drone.CurrentState, drone.IsCoverAlert, drone.AlertSecondsRemaining, drone.FacingDirection, drone.FacingRotation.eulerAngles.y,
            turn, distance, vision.CanSee(player), drone.Target == player, fan.enabled, vision.RangeCollider.enabled,
            vision.enabled && vision.gameObject.activeInHierarchy, p.x, p.y, p.z, center.x, center.y, center.z,
            local.x, local.y, local.z, origin.x, origin.y, origin.z, status.targetLight.color.r, status.targetLight.color.g,
            status.targetLight.color.b, status.targetLight.intensity, player.health.CurrentHealth, drone.HasExploded };
        for (int i = 0; i < row.Length; i++) { if (i > 0) csv.Append(','); var value = row[i] as System.IFormattable; csv.Append(value != null ? value.ToString(null, culture) : row[i].ToString()); }
        csv.AppendLine();
        UnityEditor.SessionState.SetString("09H.Capture.Phase", phase + (pending != null ? " screenshot" : ""));
        if (pending != null)
        {
            check(UnityEngine.Time.timeScale == 0f && UnityEditor.EditorApplication.timeSinceStartup - pausedAt < 8, "Screenshot pause failed/timed out.");
            if (captureStage == 0 && frame >= captureFrame + 2) { UnityEngine.ScreenCapture.CaptureScreenshot(pending); captureStage = 1; return; }
            if (captureStage == 0 || !System.IO.File.Exists(pending)) return;
            long length = new System.IO.FileInfo(pending).Length;
            if (length < 1024 || length != previousLength) { previousLength = length; return; }
            byte[] png;
            try { png = System.IO.File.ReadAllBytes(pending); } catch (System.IO.IOException) { return; }
            int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            check(width == 1920 && height == 1080, "Captured PNG dimensions changed.");
            report.AppendLine("PNG " + pending + "; pausedWallSeconds=" + (UnityEditor.EditorApplication.timeSinceStartup - pausedAt).ToString("F3", culture));
            pending = null; phase = nextPhase; UnityEngine.Time.timeScale = originalTimeScale;
            if (phase == 5) finish("CAPTURED", "Same natural discovery reached Chase; three PNGs need visual inspection. Selected Attempt-" + attempt);
            return;
        }
        check(UnityEngine.Time.timeScale == 1f, "Unexpected external pause/speed change.");
        if (phase == -1)
        {
            place(p + UnityEngine.Vector3.up * 20f);
            var settled = UnityEngine.Quaternion.Euler(0f, drone.FacingDirection < 0 ? 180f : 0f, 0f);
            if (drone.CurrentState == DroneState.Patrol && UnityEngine.Quaternion.Angle(drone.FacingRotation, settled) < 2f)
            { oldFacing = drone.FacingDirection; oldHeading = settled; phase = 0; }
            return;
        }
        if (phase == 0)
        {
            place(new UnityEngine.Vector3(p.x - oldFacing * 2.4f, origin.y, player.config.planeZ));
            check(clearance() > 0.35f, "Following behind violated physical clearance.");
            if (drone.CurrentState != DroneState.Patrol) return;
            if (drone.FacingDirection == oldFacing) return;
            if (turn > 22f)
            {
                phase = -1; place(p + UnityEngine.Vector3.up * 20f);
                report.AppendLine("Missed entry window; park player out of range and wait for next actual reversal."); return;
            }
            check(!vision.CanSee(player), "Behind-player baseline unexpectedly visible.");
            attempt++; capture("01-Turn-Initial", 1); return;
        }
        if (phase == 1)
        {
            check(drone.CurrentState == DroneState.Patrol, "Initial turn left Patrol before player entry.");
            if (turn < 10f) { place(new UnityEngine.Vector3(p.x - oldFacing * 2.4f, origin.y, player.config.planeZ)); return; }
            if (turn > 22f) { phase = -1; place(p + UnityEngine.Vector3.up * 20f); report.AppendLine("Missed 10..22 entry; this initial PNG is not a completed capture set."); return; }
            bool accepted = false;
            foreach (float offset in new[] { 2f, 2.4f })
            {
                place(new UnityEngine.Vector3(p.x + oldFacing * offset, origin.y, player.config.planeZ));
                var c = capsule.bounds.center;
                if (clearance() > 0.35f && !CoverArea.IsHidden(player)
                    && (vision.RangeCollider.ClosestPoint(c) - c).sqrMagnitude <= 0.000001f && vision.CanSee(player)) { accepted = true; break; }
            }
            check(accepted, "No valid unobstructed old-front point inside the real volume; do not widen/force vision.");
            report.AppendLine("Player API entry at yaw=" + drone.FacingRotation.eulerAngles.y.ToString("R", culture) + "; turn=" + turn.ToString("R", culture));
            phase = 2; return;
        }
        check(drone.CurrentState != DroneState.Search, "Player entry was followed by Search, including a missed short Alert.");
        check(clearance() > 0.25f && !CoverArea.IsHidden(player) && vision.CanSee(player), "Entered player lost valid unobstructed non-contact visibility.");
        if (phase == 2)
        {
            if (drone.CurrentState == DroneState.Patrol) return;
            check(drone.CurrentState == DroneState.Alert && !drone.IsCoverAlert && drone.Target == player && drone.AlertSecondsRemaining > 0.3f, "Did not sample natural discovery Alert.");
            alertAt = UnityEngine.Time.time; firstRemaining = previousRemaining = drone.AlertSecondsRemaining; alertHeading = drone.FacingRotation;
            phase = 3; return;
        }
        if (phase == 3)
        {
            check(drone.CurrentState == DroneState.Alert && !drone.IsCoverAlert && drone.Target == player && fan.enabled, "Discovery Alert was interrupted.");
            check(UnityEngine.Quaternion.Angle(alertHeading, drone.FacingRotation) < 0.05f, "Alert continued the old patrol yaw.");
            check(drone.AlertSecondsRemaining <= previousRemaining + 0.0001f, "Alert timer restarted.");
            previousRemaining = drone.AlertSecondsRemaining;
            if (drone.AlertSecondsRemaining > 0.35f) return;
            check(drone.AlertSecondsRemaining > 0.15f && status.targetLight.color == status.searchColor, "Missed orange Alert capture window.");
            capture("02-Turn-Discovered-Alert", 4); return;
        }
        if (phase == 4)
        {
            if (drone.CurrentState == DroneState.Alert)
            { check(UnityEngine.Quaternion.Angle(alertHeading, drone.FacingRotation) < 0.05f, "Alert yaw drifted after capture."); return; }
            check(drone.CurrentState == DroneState.Chase && drone.Target == player && !fan.enabled
                && vision.enabled && vision.gameObject.activeInHierarchy && vision.RangeCollider.enabled
                && status.targetLight.color == status.chaseColor, "Expected red Chase, hidden fan, live detection and same target.");
            check(UnityEngine.Mathf.Abs((UnityEngine.Time.time - alertAt) - firstRemaining) < 0.12f, "Game-time discovery duration disagrees with .5s countdown.");
            report.AppendLine("Natural Alert game seconds=" + (UnityEngine.Time.time - alertAt).ToString("R", culture) + "; firstRemaining=" + firstRemaining.ToString("R", culture));
            capture("03-Turn-Chase", 5);
        }
    }
    catch (System.Exception e) { finish("FAIL", "phase=" + phase + "; " + e.Message); }
};
UnityEditor.SessionState.SetString("09H.Capture.Token", token);
UnityEditor.SessionState.SetString("09H.Capture.Directory", directory);
UnityEditor.SessionState.SetString("09H.Capture.Result", "RUNNING");
UnityEditor.SessionState.SetBool("09H.Capture.Running", true);
try
{
    other.gameObject.SetActive(false); // Runtime only; stopping Play restores the authored scene state.
    UnityEngine.Application.runInBackground = true;
    place(new UnityEngine.Vector3(drone.Body.position.x - oldFacing * 2.4f, vision.transform.position.y, player.config.planeZ));
    UnityEditor.EditorApplication.update += observer;
}
catch (System.Exception e) { finish("FAIL", "Setup: " + e.Message); }
return new { armed = !finished, directory = directory, statusKey = "09H.Capture.Result", cancelKey = "09H.Capture.Token" };