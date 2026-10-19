// External MCP execute_code statements, C#6. Run only after009 releases Unity.
// API-assisted real AI sequence; temporary CoverArea is NOT the user's authored grass layout.
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
if (!UnityEditor.EditorApplication.isPlaying || flow == null || flow.Stage != GameFlowStage.Exploring
    || UnityEngine.Screen.width != 1920 || UnityEngine.Screen.height != 1080)
    throw new System.InvalidOperationException("Use the real Bootstrap departure into Ruins, Game1920x1080.");
if (UnityEditor.SessionState.GetBool("MyPrecious.TimedAlertScenario.Running", false))
    throw new System.InvalidOperationException("Cancel the previous sequence and wait for its cleanup.");
var player = flow.Player;
var drone = System.Array.Find(UnityEngine.Object.FindObjectsByType<DroneController>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None),
    d => d.gameObject.scene.path == flow.ruinsScenePath && d.name == "Drone");
if (player == null || drone == null || !drone.IsRunning || drone.CurrentRound != flow.CurrentRound
    || player.health.CurrentRound != flow.CurrentRound || player.IsBackpackOpen || !DroneController.CanTarget(player))
    throw new System.InvalidOperationException("Need the automatically prepared real Drone/player with no open interaction UI.");
var capsule = player.GetComponent<UnityEngine.CapsuleCollider>();
var sphere = drone.GetComponent<UnityEngine.SphereCollider>();
var fan = drone.vision.GetComponent<UnityEngine.MeshRenderer>();
var effect = drone.GetComponent<DroneStatusLight>();
if (capsule == null || sphere == null || fan == null || effect == null || effect.targetLight == null
    || player.health.CurrentHealth <= drone.config.contactDamage)
    throw new System.InvalidOperationException("Actual body/vision/light and a surviving one-hit player are required.");
var light = effect.targetLight;
var originalPosition = player.Body.position;
bool originalKinematic = player.Body.isKinematic, originalGravity = player.Body.useGravity, originalCrouch = player.IsCrouching;
int health = player.health.CurrentHealth, round = flow.CurrentRound, phase = 0, lastFrame = -1;
float phaseTime = UnityEngine.Time.time, firstAlertTime = 0f, hiddenTime = 0f;
UnityEngine.Vector3 hover = drone.Body.position, route = UnityEngine.Vector3.zero;
double started = UnityEditor.EditorApplication.timeSinceStartup;
string token = System.Guid.NewGuid().ToString("N");
string directory = "E:/DD451/Docs/Verification/Enemy/TimedAlert/RootAcceptance/Sequence-" + token.Substring(0, 8);
System.IO.Directory.CreateDirectory(directory);
var csv = new System.Text.StringBuilder("frame,time,phase,state,health,hidden,visible,x,y,z,speed,fan,range,lightR,lightG,lightB,intensity\n");
var report = new System.Text.StringBuilder("API-assisted real formal Drone/player. Temporary runtime CoverArea; NOT authored grass-layout acceptance.\nNo AI state/timer/speed/route edits. FixedUpdate race remains covered by009 tests, not proved by this observer.\n");
var culture = System.Globalization.CultureInfo.InvariantCulture;
UnityEngine.GameObject cover = null;
bool finished = false, firstImage = false, hiddenImage = false, paused = false;
int pauseFrame = 0, captureFrame = -1;
double pauseStarted = 0;
string imagePath = null;
System.Action afterImage = null;
UnityEditor.EditorApplication.CallbackFunction observer = null;
System.Action<bool, string> check = (ok, why) => { if (!ok) throw new System.InvalidOperationException(why); };
System.Action<int> enter = next => { phase = next; phaseTime = UnityEngine.Time.time; lastFrame = UnityEngine.Time.frameCount; };
System.Func<UnityEngine.Vector3> forward = () => drone.vision.transform.right.x < 0 ? UnityEngine.Vector3.left : UnityEngine.Vector3.right;
System.Action<UnityEngine.Vector3> placeCentre = centre =>
{
    var position = centre - (capsule.bounds.center - player.Body.position);
    position.z = player.config.planeZ;
    check(player.RelocateWithinRun(position), "Real player rejected position.");
    player.Body.isKinematic = true;
    UnityEngine.Physics.SyncTransforms();
};
System.Action<bool> crouch = value =>
{
    if (value) { player.SetCrouching(true); player.SetBackpackOpen(true); }
    else { player.SetBackpackOpen(false); player.SetCrouching(false); }
    UnityEngine.Physics.SyncTransforms();
    check(CoverArea.IsHidden(player) == value, "Actual CoverArea hidden condition did not match the requested player posture.");
};
System.Action<bool> checkPresentation = chasing =>
{
    check(drone.vision.gameObject.activeInHierarchy && drone.vision.enabled && drone.vision.RangeCollider.enabled,
        "AI detection was disabled with its presentation.");
    check(fan.enabled != chasing, "Fan Renderer does not match Alert/Chase presentation.");
    var expected = chasing ? effect.chaseColor : effect.searchColor;
    check(UnityEngine.Vector3.Distance(new UnityEngine.Vector3(light.color.r, light.color.g, light.color.b),
        new UnityEngine.Vector3(expected.r, expected.g, expected.b)) < 0.03f, "Actual status light has the wrong state color.");
};
System.Action<string, System.Action> screenshot = (name, continuation) =>
{
    paused = true; pauseFrame = UnityEngine.Time.frameCount; captureFrame = -1;
    pauseStarted = UnityEditor.EditorApplication.timeSinceStartup;
    imagePath = System.IO.Path.Combine(directory, name + ".png"); afterImage = continuation;
    UnityEngine.Time.timeScale = 0f;
};
System.Action<string, string> finish = (status, why) =>
{
    if (finished) return;
    finished = true;
    try
    {
        if (cover != null) { cover.SetActive(false); UnityEngine.Object.Destroy(cover); }
        if (player != null && player.Body != null)
        {
            player.SetBackpackOpen(false);
            if (UnityEditor.EditorApplication.isPlaying && player.IsPrepared && !player.IsDead) player.RelocateWithinRun(originalPosition);
            player.SetCrouching(originalCrouch);
            player.Body.isKinematic = originalKinematic; player.Body.useGravity = originalGravity;
        }
    }
    catch (System.Exception e) { report.AppendLine("Cleanup issue: " + e.Message); }
    finally
    {
        UnityEditor.EditorApplication.update -= observer;
        UnityEngine.Time.timeScale = 1f; UnityEngine.Application.runInBackground = false;
        UnityEditor.SessionState.SetBool("MyPrecious.TimedAlertScenario.Running", false);
        UnityEditor.SessionState.SetString("MyPrecious.TimedAlertScenario.Result", status + ":" + directory);
    }
    report.AppendLine(status + ": " + why);
    report.AppendLine("Player/temporary cover/observer restored; real AI outcome and real health loss remain. Root reviews PNG/CSV and stops Play.");
    System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "Samples.csv"), csv.ToString());
    System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "Result.txt"), report.ToString());
};
observer = delegate
{
    try
    {
        if (UnityEditor.SessionState.GetString("MyPrecious.TimedAlertScenario.Token", "") != token) { finish("CANCELLED", "Token cancelled."); return; }
        check(UnityEditor.EditorApplication.isPlaying && flow != null && drone != null && player != null, "Runtime ended.");
        check(flow.Stage == GameFlowStage.Exploring && flow.CurrentRound == round, "Formal flow changed during sequence.");
        check(UnityEditor.EditorApplication.timeSinceStartup - started < 35, "35-second overall timeout; no forced-state fallback.");
        if (paused)
        {
            check(UnityEditor.EditorApplication.timeSinceStartup - pauseStarted < 6, "Screenshot render/write timeout.");
            if (captureFrame < 0 && UnityEngine.Time.frameCount >= pauseFrame + 2)
            { UnityEngine.ScreenCapture.CaptureScreenshot(imagePath); captureFrame = UnityEngine.Time.frameCount; }
            else if (captureFrame >= 0 && UnityEngine.Time.frameCount >= captureFrame + 2 && System.IO.File.Exists(imagePath)
                && new System.IO.FileInfo(imagePath).Length > 128)
            {
                report.AppendLine("Screenshot: " + imagePath); paused = false;
                var continuation = afterImage; afterImage = null; continuation();
                if (!finished) UnityEngine.Time.timeScale = 1f;
            }
            return;
        }
        if (lastFrame == UnityEngine.Time.frameCount) return;
        lastFrame = UnityEngine.Time.frameCount;
        bool hidden = CoverArea.IsHidden(player), visible = drone.vision.CanSee(player);
        var p = drone.Body.position; var color = light.color;
        csv.AppendFormat(culture, "{0},{1:R},{2},{3},{4},{5},{6},{7:R},{8:R},{9:R},{10:R},{11},{12},{13:R},{14:R},{15:R},{16:R}\n",
            lastFrame, UnityEngine.Time.time, phase, drone.CurrentState, player.health.CurrentHealth, hidden, visible,
            p.x, p.y, p.z, drone.FlightVelocity.magnitude, fan.enabled, drone.vision.RangeCollider.enabled,
            color.r, color.g, color.b, light.intensity);
        if (phase < 10) check(player.health.CurrentHealth == health && !drone.HasExploded, "Unexpected damage/explosion before final Chase contact.");
        switch (phase)
        {
            case 0:
                if (drone.CurrentState != DroneState.Patrol || UnityEngine.Mathf.Abs(drone.vision.transform.right.x) < 0.98f) break;
                check(drone.TryGetPatrolPoint(out route), "Real patrol target is missing.");
                placeCentre(drone.vision.transform.position + forward() * 2.5f);
                cover.transform.position = capsule.bounds.center;
                UnityEngine.Physics.SyncTransforms();
                crouch(false);
                check(drone.vision.CanSee(player), "Standing fixture is obstructed or outside real vision.");
                enter(1); break;
            case 1:
                check(drone.CurrentState != DroneState.Chase, "First discovery skipped Alert.");
                if (drone.CurrentState != DroneState.Alert) break;
                firstAlertTime = UnityEngine.Time.time; hover = p; enter(2); break;
            case 2:
                check(visible && !hidden, "Standing target lost real visibility during first alert.");
                if (drone.CurrentState == DroneState.Alert)
                {
                    check(UnityEngine.Vector3.Distance(p, hover) <= 0.005f && drone.FlightVelocity.sqrMagnitude < 0.0001f, "First Alert moved.");
                    if (!firstImage && UnityEngine.Time.time - firstAlertTime >= 0.2f)
                    { firstImage = true; screenshot("01-FirstAlert-0p2s", () => checkPresentation(false)); }
                    check(UnityEngine.Time.time - firstAlertTime < 0.8f, "First Alert exceeds the new0.5-second rule.");
                    break;
                }
                check(drone.CurrentState == DroneState.Chase && UnityEngine.Time.time - firstAlertTime >= 0.35f
                    && UnityEngine.Time.time - firstAlertTime <= 0.75f && firstImage, "First Alert did not naturally last about0.5 seconds.");
                report.AppendLine("PASS first standing-in-cover discovery: orange stationary Alert about0.5 seconds, then Chase.");
                // Pause before the next advancing game frame; capture red Chase before introducing hiding.
                screenshot("02-Chase-DetectionStillActive", () =>
                {
                    checkPresentation(true); check(drone.vision.CanSee(player), "Renderer hiding broke CanSee.");
                    crouch(true); hover = drone.Body.position; hiddenTime = UnityEngine.Time.time; enter(3);
                });
                break;
            case 3:
                check(hidden && !visible, "Actual crouched cover condition was lost.");
                if (drone.CurrentState != DroneState.Alert) { check(UnityEngine.Time.time - hiddenTime < 0.15f, "Hidden Chase did not stop promptly."); break; }
                enter(4); break;
            case 4:
                check(hidden && !visible, "Continuous hiding must remain real.");
                if (drone.CurrentState == DroneState.Alert)
                {
                    check(UnityEngine.Vector3.Distance(p, hover) <= 0.005f && drone.FlightVelocity.sqrMagnitude < 0.0001f, "Cover Alert kept chasing the hidden player.");
                    checkPresentation(false);
                    if (!hiddenImage && UnityEngine.Time.time - hiddenTime >= 1f)
                    { hiddenImage = true; screenshot("03-CoverAlert-1s", () => checkPresentation(false)); }
                    check(UnityEngine.Time.time - hiddenTime < 2.25f, "Persistent Hidden is restarting the2-second timer.");
                    break;
                }
                check(drone.CurrentState == DroneState.Patrol && UnityEngine.Time.time - hiddenTime >= 1.8f
                    && UnityEngine.Time.time - hiddenTime <= 2.25f && hiddenImage, "Cover Alert did not naturally return to Patrol after2 seconds.");
                UnityEngine.Vector3 restoredRoute;
                check(drone.TryGetPatrolPoint(out restoredRoute) && UnityEngine.Vector3.Distance(route, restoredRoute) < 0.001f, "Cover Alert changed the patrol target/index.");
                report.AppendLine("PASS continuous actual Hidden:2-second stationary orange Alert then original Patrol target.");
                crouch(false); check(drone.vision.CanSee(player), "Standing target is no longer truly visible after Patrol resumed.");
                enter(5); break;
            case 5:
                check(drone.CurrentState != DroneState.Chase, "Fresh Patrol discovery skipped its0.5-second Alert.");
                if (drone.CurrentState != DroneState.Alert) break;
                firstAlertTime = UnityEngine.Time.time; enter(6); break;
            case 6:
                if (drone.CurrentState == DroneState.Alert) { check(UnityEngine.Time.time - firstAlertTime < 0.8f, "Second initial Alert timed out."); break; }
                check(drone.CurrentState == DroneState.Chase && UnityEngine.Time.time - firstAlertTime >= 0.35f
                    && UnityEngine.Time.time - firstAlertTime <= 0.75f, "Second fresh discovery did not use0.5-second Alert.");
                crouch(true); hover = p; hiddenTime = UnityEngine.Time.time; enter(7); break;
            case 7:
                check(hidden && !visible, "Second actual cover hide failed.");
                if (drone.CurrentState != DroneState.Alert) { check(UnityEngine.Time.time - hiddenTime < 0.15f, "Second hidden Chase did not stop promptly."); break; }
                enter(8); break;
            case 8:
                check(drone.CurrentState == DroneState.Alert && hidden && !visible
                    && UnityEngine.Vector3.Distance(p, hover) <= 0.005f, "Second cover Alert did not remain stationary/hidden.");
                if (UnityEngine.Time.time - hiddenTime < 0.6f) break;
                crouch(false); check(drone.vision.CanSee(player), "Reappearance is not actually visible.");
                enter(9); break;
            case 9:
                check(visible && !hidden, "Reappearance lost actual visibility.");
                check(UnityEngine.Time.time - phaseTime <= 0.15f, "Cover reappearance waited instead of immediately resuming Chase.");
                if (drone.CurrentState != DroneState.Chase) break;
                report.AppendLine("PASS reappearance0.6 seconds into cover Alert immediately resumed Chase.");
                placeCentre(drone.vision.transform.position + forward() * 0.15f);
                crouch(false);
                UnityEngine.Vector3 direction; float penetration;
                check(UnityEngine.Physics.ComputePenetration(sphere, sphere.transform.position, sphere.transform.rotation,
                    capsule, capsule.transform.position, capsule.transform.rotation, out direction, out penetration), "Final real3D body overlap failed at the unchanged Z planes.");
                check(drone.vision.CanSee(player), "Final contact became invisible before the Chase contact test.");
                enter(10); break;
            case 10:
                if (!drone.HasExploded) { check(UnityEngine.Time.time - phaseTime < 1f, "True Chase contact did not explode."); break; }
                check(player.health.CurrentHealth == health - drone.config.contactDamage, "Actual contact did not deal one configured hit.");
                enter(11); break;
            case 11:
                check(drone.HasExploded && player.health.CurrentHealth == health - drone.config.contactDamage, "Repeated damage or unexpected revival.");
                if (UnityEngine.Time.time - phaseTime < 0.3f) break;
                check(!sphere.enabled && !drone.visual.gameObject.activeInHierarchy && !drone.vision.gameObject.activeInHierarchy, "Explosion left collision/model/vision active.");
                UnityEditor.SessionState.SetInt("MyPrecious.TimedAlertScenario.ExplodedDroneId", drone.GetInstanceID());
                UnityEditor.SessionState.SetInt("MyPrecious.TimedAlertScenario.Round", round);
                UnityEditor.SessionState.SetInt("MyPrecious.TimedAlertScenario.AfterHitHealth", player.health.CurrentHealth);
                finish("PASS", "Natural timed-alert/cover/reappearance/contact sequence completed; root still reviews screenshots, breathing samples and remaining targeted tests.");
                break;
        }
    }
    catch (System.Exception e) { finish("FAIL", "Phase" + phase + ": " + e.Message); }
};
UnityEditor.SessionState.SetString("MyPrecious.TimedAlertScenario.Token", token);
UnityEditor.SessionState.SetBool("MyPrecious.TimedAlertScenario.Running", true);
UnityEditor.SessionState.SetString("MyPrecious.TimedAlertScenario.Result", "RUNNING:" + directory);
try
{
    cover = new UnityEngine.GameObject("RootTimedAlert_TemporaryCover");
    cover.hideFlags = UnityEngine.HideFlags.DontSave;
    var box = cover.AddComponent<UnityEngine.BoxCollider>(); box.isTrigger = true; box.size = new UnityEngine.Vector3(2f, 3f, 2f);
    cover.AddComponent<CoverArea>();
    player.SetBackpackOpen(false); player.SetCrouching(false);
    placeCentre(drone.Body.position + UnityEngine.Vector3.up * (drone.vision.radius + 10f));
    check(!drone.vision.CanSee(player), "Initial out-of-range fixture is visible.");
    UnityEngine.Time.timeScale = 1f; UnityEngine.Application.runInBackground = true;
    UnityEditor.EditorApplication.update += observer;
}
catch (System.Exception e) { finish("FAIL", "Setup: " + e.Message); }
return new { armed = !finished, output = directory, resultKey = "MyPrecious.TimedAlertScenario.Result" };
