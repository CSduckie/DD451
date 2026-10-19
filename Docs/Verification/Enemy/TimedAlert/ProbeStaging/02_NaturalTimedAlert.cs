// Unity MCP execute_code C# 6 method body. No AI state/timer/round setter; do not import into Assets.
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
string directory = UnityEditor.SessionState.GetString("09F.Probe.Directory", "");
if (!UnityEditor.EditorApplication.isPlaying || flow == null || flow.Stage != GameFlowStage.Exploring
    || !System.IO.Directory.Exists(directory) || UnityEditor.SessionState.GetBool("09F.Probe.Running", false))
    throw new System.InvalidOperationException("Run01 in fresh Bootstrap Play, wait for Exploring and no other probe.");
if (UnityEngine.Screen.width != 1920 || UnityEngine.Screen.height != 1080 || UnityEngine.Time.timeScale != 1f)
    throw new System.InvalidOperationException("Measured run requires 1920x1080 and timeScale1 throughout.");
var player = flow.Player;
var capsule = player.GetComponent<UnityEngine.CapsuleCollider>();
int round = UnityEditor.SessionState.GetInt("09F.Probe.Round", 0);
if (player.GetInstanceID() != UnityEditor.SessionState.GetInt("09F.Probe.PlayerId", 0)
    || flow.CurrentRound != round || flow.Items.CurrentRound != round || flow.Loot.PreparedRound != round
    || player.health.CurrentRound != round || capsule == null || player.IsDead || player.IsBackpackOpen)
    throw new System.InvalidOperationException("Persistent player/common round or prepared interaction context is invalid.");
var bindings = System.Array.Find(UnityEngine.Object.FindObjectsByType<GameFlowSceneBindings>(UnityEngine.FindObjectsSortMode.None),
    b => b.gameObject.scene.path == flow.ruinsScenePath);
var covers = UnityEngine.Object.FindObjectsByType<CoverArea>(UnityEngine.FindObjectsSortMode.None);
var drones = UnityEngine.Object.FindObjectsByType<DroneController>(UnityEngine.FindObjectsSortMode.None);
CoverArea cover = null;
DroneController drone = null;
float bestPair = float.PositiveInfinity;
foreach (var area in covers)
{
    var box = area.GetComponent<UnityEngine.BoxCollider>();
    if (area.gameObject.scene.path != flow.ruinsScenePath || !area.isActiveAndEnabled || box == null || !box.enabled) continue;
    foreach (var candidate in drones)
    {
        if (candidate.gameObject.scene.path != flow.ruinsScenePath || !candidate.IsRunning
            || candidate.vision == null || candidate.CurrentRound != round || candidate.patrolPoints == null) continue;
        bool hasRoute = System.Array.Exists(candidate.patrolPoints, point => point != null);
        float separation = UnityEngine.Vector3.Distance(candidate.Body.position, box.bounds.center);
        if (hasRoute && separation < bestPair) { bestPair = separation; cover = area; drone = candidate; }
    }
}
if (bindings == null || drone == null || cover == null)
    throw new System.InvalidOperationException("No actual enabled Ruins drone with a route and CoverArea pair was found.");
if (UnityEngine.Mathf.Abs(drone.config.discoveryAlertSeconds - 0.5f) > 0.0001f
    || UnityEngine.Mathf.Abs(drone.config.coverAlertSeconds - 2f) > 0.0001f)
    throw new System.InvalidOperationException("Selected authored drone does not have the agreed 0.5s/2s configuration.");
var volume = cover.GetComponent<UnityEngine.BoxCollider>();
var renderer = drone.vision.GetComponent<UnityEngine.MeshRenderer>();
var effect = drone.GetComponent<DroneStatusLight>();
var light = effect != null ? effect.targetLight : null;
if (renderer == null || light == null) throw new System.InvalidOperationException("Actual vision renderer/status point light is required.");
var originalPosition = player.Body.position;
bool originalKinematic = player.Body.isKinematic;
bool originalGravity = player.Body.useGravity;
bool originalBackground = UnityEngine.Application.runInBackground;
int health = player.health.CurrentHealth;
var coverCentre = volume.bounds.center;
coverCentre.y = volume.bounds.min.y + capsule.bounds.extents.y + 0.02f;
coverCentre.z = player.config.planeZ;
var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
int phase = 0;
int lastFrame = -1;
float phaseStart = UnityEngine.Time.time;
float patrolStartTime = -1f;
float timerPrevious = float.PositiveInfinity;
float observedAlertTime = 0f;
float firstRemaining = 0f;
float hoverDrift = 0f;
float coverDrift = 0f;
float coverLightMin = float.PositiveInfinity;
float coverLightMax = 0f;
bool retriedDiscovery = false;
bool retriedCover = false;
bool finished = false;
var hover = UnityEngine.Vector3.zero;
var patrolStart = UnityEngine.Vector3.zero;
var routeTarget = UnityEngine.Vector3.zero;
var phases = new[] { "wait-patrol", "standing-in-real-cover", "discovery-alert", "first-chase", "request-cover",
    "cover-before-rediscovery", "rediscovery", "rediscovered-chase", "request-cover-again", "full-cover-timeout", "resumed-patrol" };
string token = System.Guid.NewGuid().ToString("N");
double realStart = UnityEditor.EditorApplication.timeSinceStartup;
var culture = System.Globalization.CultureInfo.InvariantCulture;
var properties = new UnityEngine.MaterialPropertyBlock();
var csv = new System.Text.StringBuilder("frame,time,fixedTime,phase,round,state,remaining,coverAlert,crouching,grounded,hidden,canSee,health,exploded,x,y,z,speed,lastX,lastY,lastZ,targetPresent,visionActive,visionEnabled,rangeEnabled,rendererEnabled,lightR,lightG,lightB,intensity,fill\n");
var report = new System.Text.StringBuilder("Formal Bootstrap->Shelter->TryDepart->Ruins at1920x1080/timeScale1.\n"
    + "Public player RelocateWithinRun plus queued C on a probe-owned InputSystem Keyboard; not native keyboard input.\n"
    + "No AI ChangeState/BeginRound/timer writes; no route, cover, config, camera or scene edits.\n");
report.AppendLine("Selected drone=" + drone.name + "#" + drone.GetInstanceID() + "; cover=" + cover.name + "#" + cover.GetInstanceID());
System.Action<bool, string> check = (ok, message) => { if (!ok) throw new System.InvalidOperationException(message); };
System.Action<bool> crouch = held => UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,
    held ? new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.C)
        : new UnityEngine.InputSystem.LowLevel.KeyboardState());
System.Action<UnityEngine.Vector3> placeCentre = centre =>
{
    var position = centre - (capsule.bounds.center - player.Body.position);
    position.z = player.config.planeZ;
    check(player.RelocateWithinRun(position), "Public in-round positioning was rejected.");
    player.Body.isKinematic = true;
    UnityEngine.Physics.SyncTransforms();
};
System.Action<string> screenshot = name => UnityEngine.ScreenCapture.CaptureScreenshot(directory + "/" + name + "-1920.png");
System.Action retryFormal = () =>
{
    float timer = drone.AlertSecondsRemaining;
    var state = drone.CurrentState;
    var position = drone.Body.position;
    var target = drone.Target;
    bool branch = drone.IsCoverAlert;
    string error;
    check(bindings.Activate(flow, player, out error), "Same-round formal Activate: " + error);
    check(bindings.TryBeginExplorationEnemies(round, out error), "Same-round enemy preparation: " + error);
    check(drone.AlertSecondsRemaining == timer && drone.CurrentState == state && drone.IsCoverAlert == branch
        && drone.Body.position == position && drone.Target == target, "Formal same-round retry changed timer/state/body/target.");
    report.AppendLine("PASS synchronous formal rebind preserves " + state + ", cover=" + branch + ", remaining=" + timer.ToString("R", culture));
};
System.Action visibleOutsideCover = () =>
{
    crouch(false);
    var forward = drone.vision.transform.right;
    for (int height = 0; height < 3; height++)
    {
        for (float distance = 2.5f; distance <= 4.5f; distance += 1f)
        {
            var centre = drone.vision.transform.position + forward * distance;
            centre.y += height == 1 ? 1f : height == 2 ? -1f : 0f;
            placeCentre(centre);
            UnityEngine.Vector3 direction;
            float penetration;
            bool inside = UnityEngine.Physics.ComputePenetration(volume, volume.transform.position, volume.transform.rotation,
                capsule, capsule.transform.position, capsule.transform.rotation, out direction, out penetration);
            if (!inside && !CoverArea.IsHidden(player) && drone.vision.CanSee(player)) return;
        }
    }
    throw new System.InvalidOperationException("No unobstructed outside-cover point exists in the current real cone; do not force discovery.");
};
UnityEditor.EditorApplication.CallbackFunction observer = null;
System.Action<string, string> finish = (status, why) =>
{
    if (finished) return;
    finished = true;
    UnityEditor.EditorApplication.update -= observer;
    try
    {
        if (keyboard != null && keyboard.added) UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
        if (player != null && player.Body != null)
        {
            if (UnityEditor.EditorApplication.isPlaying && player.IsPrepared && !player.IsDead) player.RelocateWithinRun(originalPosition);
            player.Body.isKinematic = originalKinematic;
            player.Body.useGravity = originalGravity;
        }
    }
    finally
    {
        UnityEngine.Application.runInBackground = originalBackground;
        UnityEditor.SessionState.SetBool("09F.Probe.Running", false);
        UnityEditor.SessionState.SetString("09F.Probe.Result", status + ": " + why);
    }
    report.AppendLine(status + ": " + why);
    report.AppendLine("Discovery max hover drift=" + hoverDrift.ToString("R", culture) + "; cover max drift=" + coverDrift.ToString("R", culture));
    report.AppendLine("Callback removed, probe keyboard removed, player position/body flags and background setting restored. No native input claim.");
    report.AppendLine("Not covered here: ordinary wall Search, same-physics-step hidden contact, natural explosion/next formal round. Retain automated acceptance for these.");
    System.IO.File.WriteAllText(directory + "/02-TimedAlert.csv", csv.ToString());
    System.IO.File.WriteAllText(directory + "/02-TimedAlert.txt", report.ToString());
};
observer = delegate
{
    try
    {
        if (UnityEditor.SessionState.GetString("09F.Probe.Token", "") != token) { finish("CANCELLED", "Token cancelled."); return; }
        check(UnityEditor.EditorApplication.isPlaying && flow != null && player != null && drone != null && cover != null, "Play/runtime objects ended.");
        check(UnityEditor.EditorApplication.timeSinceStartup - realStart < 90, "Natural route/cover scenario timed out; no forced AI fallback.");
        check(UnityEngine.Time.timeScale == 1f, "Timing run was paused/slowed; discard this measured run.");
        if (lastFrame == UnityEngine.Time.frameCount) return;
        lastFrame = UnityEngine.Time.frameCount;
        check(flow.Stage == GameFlowStage.Exploring && flow.CurrentRound == round && drone.CurrentRound == round, "Formal stage/round changed.");
        check(player.health.CurrentHealth == health && !drone.HasExploded, "Unexpected damage/explosion during non-contact scenario.");
        bool hidden = CoverArea.IsHidden(player);
        bool visible = drone.vision.CanSee(player);
        var p = drone.Body.position;
        var last = drone.LastKnownPosition;
        renderer.GetPropertyBlock(properties);
        var color = light.color;
        csv.AppendFormat(culture, "{0},{1:R},{2:R},{3},{4},{5},{6:R},{7},{8},{9},{10},{11},{12},{13},{14:R},{15:R},{16:R},{17:R},{18:R},{19:R},{20:R},{21},{22},{23},{24},{25},{26:R},{27:R},{28:R},{29:R},{30:R}\n",
            lastFrame, UnityEngine.Time.time, UnityEngine.Time.fixedTime, phases[phase], round, drone.CurrentState,
            drone.AlertSecondsRemaining, drone.IsCoverAlert, player.IsCrouching, player.IsGrounded, hidden, visible,
            player.health.CurrentHealth, drone.HasExploded, p.x, p.y, p.z, drone.FlightVelocity.magnitude,
            last.x, last.y, last.z, drone.Target != null, drone.vision.gameObject.activeInHierarchy, drone.vision.enabled,
            drone.vision.RangeCollider.enabled, renderer.enabled, color.r, color.g, color.b, light.intensity, properties.GetFloat("_FillProgress"));
        UnityEditor.SessionState.SetString("09F.Probe.Phase", phases[phase]);
        check(drone.vision.gameObject.activeInHierarchy && drone.vision.enabled && drone.vision.RangeCollider.enabled,
            "Living drone disabled real vision detection; Chase may hide only its renderer.");
        check(properties.GetFloat("_FillProgress") == 0f, "Legacy gradual red fill remains active.");
        switch (phase)
        {
            case 0:
                if (drone.CurrentState != DroneState.Patrol || drone.FlightVelocity.magnitude < 0.05f) { patrolStartTime = -1f; break; }
                if (patrolStartTime < 0f) { patrolStartTime = UnityEngine.Time.time; patrolStart = p; break; }
                if (UnityEngine.Time.time - patrolStartTime < 0.25f) break;
                check(UnityEngine.Vector3.Distance(p, patrolStart) > 0.03f, "Patrol state did not move on the authored route.");
                // 等原路线把无人机带到草丛前的稳定内圈，避免首次边缘发现后机头回正使目标出界。
                var settledHeading = UnityEngine.Quaternion.Euler(0f, drone.FacingDirection < 0 ? 180f : 0f, 0f);
                if (UnityEngine.Quaternion.Angle(drone.FacingRotation, settledHeading) > 1f) break;
                var rangeForPlacement = drone.vision.RangeCollider;
                var placementOrigin = drone.vision.visionOrigin != null
                    ? drone.vision.visionOrigin.position : drone.vision.transform.position;
                if (!rangeForPlacement.enabled || rangeForPlacement.sharedMesh == null
                    || (rangeForPlacement.ClosestPoint(coverCentre) - coverCentre).sqrMagnitude > 0.000001f
                    || UnityEngine.Vector3.Distance(placementOrigin, coverCentre) >= drone.vision.radius * 0.65f) break;
                placeCentre(coverCentre);
                phase = 1; phaseStart = UnityEngine.Time.time;
                report.AppendLine("PASS shared formal round and actual route motion; player now stands in the existing cover.");
                break;
            case 1:
                check(!hidden && !player.IsCrouching, "Standing in cover incorrectly counts as hidden.");
                check(drone.CurrentState != DroneState.Chase, "Natural detection skipped the discovery Alert.");
                if (drone.CurrentState != DroneState.Alert) break;
                check(player.IsGrounded && visible && !drone.IsCoverAlert && drone.AlertSecondsRemaining > 0.3f,
                    "Need grounded standing player and a naturally sampled initial0.5s Alert.");
                check(drone.TryGetPatrolPoint(out routeTarget), "Authored route unexpectedly has no current point.");
                hover = p; observedAlertTime = UnityEngine.Time.time; firstRemaining = drone.AlertSecondsRemaining;
                timerPrevious = firstRemaining; screenshot("02-Discovery-Orange"); phase = 2;
                break;
            case 2:
                if (drone.CurrentState == DroneState.Alert)
                {
                    check(visible && !drone.IsCoverAlert && renderer.enabled, "Discovery Alert lost real vision or its orange fan.");
                    check(light.color == effect.searchColor, "Discovery Alert light is not the authored orange state color.");
                    check(drone.AlertSecondsRemaining <= timerPrevious + 0.0001f, "Discovery countdown restarted.");
                    timerPrevious = drone.AlertSecondsRemaining;
                    hoverDrift = UnityEngine.Mathf.Max(hoverDrift, UnityEngine.Vector3.Distance(hover, p));
                    check(hoverDrift < 0.02f, "Discovery Alert moved instead of hovering.");
                    if (!retriedDiscovery && drone.AlertSecondsRemaining < 0.35f) { retryFormal(); retriedDiscovery = true; }
                    break;
                }
                check(drone.CurrentState == DroneState.Chase && visible && !renderer.enabled, "Expected Chase with hidden renderer and working CanSee.");
                check(retriedDiscovery && UnityEngine.Mathf.Abs((UnityEngine.Time.time - observedAlertTime) - firstRemaining) < 0.15f,
                    "First discovery did not expire at the measured0.5s countdown or too few frames were sampled.");
                screenshot("03-First-Chase-NoFan"); report.AppendLine("PASS first discovery0.5s hover, orange fan, then natural Chase with renderer hidden and detection active.");
                phase = 3; phaseStart = UnityEngine.Time.time;
                break;
            case 3:
                check(drone.CurrentState == DroneState.Chase && visible && !renderer.enabled, "First Chase did not persist while the standing player stayed visible.");
                check(light.color == effect.chaseColor, "Chase light is not the authored red state color.");
                if (UnityEngine.Time.time - phaseStart < 0.1f) break;
                crouch(true); phase = 4; phaseStart = UnityEngine.Time.time;
                break;
            case 4:
            case 8:
                check(UnityEngine.Time.time - phaseStart < 0.5f, "Queued C did not produce grounded hidden cover Alert promptly.");
                if (!hidden || drone.CurrentState != DroneState.Alert || !drone.IsCoverAlert) break;
                check(!visible && renderer.enabled && drone.AlertSecondsRemaining > 1.7f, "Cover entry must restore orange cone and start a fresh2s countdown.");
                hover = p; observedAlertTime = UnityEngine.Time.time; firstRemaining = drone.AlertSecondsRemaining;
                timerPrevious = firstRemaining;
                screenshot(phase == 4 ? "04-Cover-Orange" : "06-Cover-FullTimeout");
                phase = phase == 4 ? 5 : 9;
                break;
            case 5:
                check(hidden && !visible && drone.CurrentState == DroneState.Alert && drone.IsCoverAlert && renderer.enabled, "Cover Alert ended before rediscovery.");
                check(light.color == effect.searchColor, "Cover Alert light is not orange.");
                check(drone.AlertSecondsRemaining <= timerPrevious + 0.0001f, "Repeated Hidden restarted the cover countdown.");
                timerPrevious = drone.AlertSecondsRemaining;
                coverDrift = UnityEngine.Mathf.Max(coverDrift, UnityEngine.Vector3.Distance(hover, p));
                check(coverDrift < 0.02f, "Hidden target was tracked by moving toward it.");
                if (!retriedCover && UnityEngine.Time.time - observedAlertTime > 0.2f) { retryFormal(); retriedCover = true; }
                if (UnityEngine.Time.time - observedAlertTime < 0.45f) break;
                visibleOutsideCover(); phase = 6; phaseStart = UnityEngine.Time.time;
                break;
            case 6:
                check(!hidden && visible, "Rediscovery point must be outside actual cover and inside real visible geometry.");
                if (drone.CurrentState != DroneState.Chase)
                {
                    check(UnityEngine.Time.time - phaseStart < 0.2f, "Cover rediscovery added another0.5s delay instead of immediate Chase.");
                    break;
                }
                check(!renderer.enabled && retriedCover && !drone.IsCoverAlert, "Rediscovered Chase did not clear cover context/hide only the renderer.");
                screenshot("05-Rediscovered-Chase-NoFan"); report.AppendLine("PASS real cover hides via queued crouch, countdown decreases, formal retry preserves timer, actual rediscovery immediately chases.");
                phase = 7; phaseStart = UnityEngine.Time.time;
                break;
            case 7:
                check(drone.CurrentState == DroneState.Chase && visible && !renderer.enabled, "Rediscovered Chase did not persist.");
                check(light.color == effect.chaseColor, "Rediscovered Chase light is not red.");
                if (UnityEngine.Time.time - phaseStart < 0.1f) break;
                placeCentre(coverCentre); crouch(true); phase = 8; phaseStart = UnityEngine.Time.time;
                break;
            case 9:
                check(hidden && !visible, "Continuous hidden branch lost its real cover condition.");
                if (drone.CurrentState == DroneState.Alert)
                {
                    check(drone.IsCoverAlert && renderer.enabled && drone.AlertSecondsRemaining <= timerPrevious + 0.0001f, "Continuous Hidden reset/changed cover Alert.");
                    check(light.color == effect.searchColor, "Full cover wait stopped using orange light.");
                    coverLightMin = UnityEngine.Mathf.Min(coverLightMin, light.intensity);
                    coverLightMax = UnityEngine.Mathf.Max(coverLightMax, light.intensity);
                    timerPrevious = drone.AlertSecondsRemaining;
                    coverDrift = UnityEngine.Mathf.Max(coverDrift, UnityEngine.Vector3.Distance(hover, p));
                    check(coverDrift < 0.02f, "Cover Alert moved toward the hidden player.");
                    break;
                }
                check(drone.CurrentState == DroneState.Patrol && !drone.IsCoverAlert && drone.AlertSecondsRemaining == 0f,
                    "Continuous hidden cover must return to Patrol after2s, not Search/Chase.");
                check(UnityEngine.Mathf.Abs((UnityEngine.Time.time - observedAlertTime) - firstRemaining) < 0.15f, "Cover Alert did not expire at its measured2s countdown.");
                check(coverLightMax - coverLightMin > 0.01f, "The existing orange status light did not visibly change intensity during the two-second cover wait.");
                UnityEngine.Vector3 currentPoint;
                check(drone.TryGetPatrolPoint(out currentPoint) && UnityEngine.Vector3.Distance(currentPoint, routeTarget) < 0.001f,
                    "Cover timeout reset/advanced the existing patrol route index.");
                patrolStart = p; phase = 10; phaseStart = UnityEngine.Time.time;
                screenshot("07-Returned-Patrol");
                break;
            case 10:
                check(drone.CurrentState == DroneState.Patrol && hidden && renderer.enabled, "Restored patrol did not remain harmless while player stayed hidden.");
                if (UnityEngine.Vector3.Distance(p, patrolStart) < 0.03f)
                {
                    check(UnityEngine.Time.time - phaseStart < drone.config.patrolWaitSeconds + 3f, "Patrol never resumed actual movement.");
                    break;
                }
                report.AppendLine("PASS second continuous Hidden lasts2s without restarting, preserves route target, then actual patrol motion resumes.");
                finish("PASS", "Timed discovery, hidden renderer with real detection, real cover stop/timeout, immediate rediscovery and formal same-round timer preservation.");
                break;
        }
    }
    catch (System.Exception e) { finish("FAIL", phases[phase] + ": " + e.Message); }
};
UnityEditor.SessionState.SetString("09F.Probe.Token", token);
UnityEditor.SessionState.SetInt("09F.Probe.DroneId", drone.GetInstanceID());
UnityEditor.SessionState.SetBool("09F.Probe.Running", true);
UnityEditor.SessionState.SetString("09F.Probe.Result", "RUNNING");
try
{
    crouch(false);
    placeCentre(drone.Body.position + UnityEngine.Vector3.up * 40f);
    UnityEngine.Application.runInBackground = true;
    UnityEditor.EditorApplication.update += observer;
}
catch (System.Exception e) { finish("FAIL", "Setup: " + e.Message); }
return new { armed = !finished, directory = directory, drone = drone.name, cover = cover.name, statusKey = "09F.Probe.Result" };
