// MCP execute_code statements. No BeginRound, forced AI state, or asset mutation.
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
string directory = UnityEditor.SessionState.GetString("MyPrecious.RuinsAcceptance.Directory", "");
if (!UnityEditor.EditorApplication.isPlaying || flow == null || flow.Stage != GameFlowStage.Exploring
    || !System.IO.Directory.Exists(directory)) throw new System.InvalidOperationException("Run01 and wait for formal Exploring first.");
if (UnityEditor.SessionState.GetBool("MyPrecious.RuinsAcceptance.Running", false))
    throw new System.InvalidOperationException("Another acceptance callback is running.");
var player = flow.Player;
var drones = UnityEngine.Object.FindObjectsByType<DroneController>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
var drone = System.Array.Find(drones, d => d.gameObject.scene.path == flow.ruinsScenePath && d.name == "Drone");
int round = UnityEditor.SessionState.GetInt("MyPrecious.RuinsAcceptance.FirstRound", 0);
if (drone == null || !drone.IsRunning || round <= 0 || drone.CurrentRound != round
    || flow.CurrentRound != round || flow.Items.CurrentRound != round || player.health.CurrentRound != round)
    throw new System.InvalidOperationException("Formal entry did not automatically prepare the real user Drone in the common round.");
if (player.GetInstanceID() != UnityEditor.SessionState.GetInt("MyPrecious.RuinsAcceptance.PlayerId", 0))
    throw new System.InvalidOperationException("Formal departure replaced the persistent player.");
var capsule = player.GetComponent<UnityEngine.CapsuleCollider>();
var sphere = drone.GetComponent<UnityEngine.SphereCollider>();
int health = player.health.CurrentHealth;
if (health <= 1 || capsule == null || sphere == null) throw new System.InvalidOperationException("Need a living real player and real body colliders.");
var originalPosition = player.Body.position;
bool originalKinematic = player.Body.isKinematic;
bool originalGravity = player.Body.useGravity;
int phase = 0;
int lastFrame = -1;
float phaseTime = UnityEngine.Time.time;
float patrolTime = -1f;
float alertStarted = 0f;
float maxProgress = 0f;
float maxHoverDrift = 0f;
UnityEngine.Vector3 patrolStart = UnityEngine.Vector3.zero;
UnityEngine.Vector3 hover = UnityEngine.Vector3.zero;
double started = UnityEditor.EditorApplication.timeSinceStartup;
string token = System.Guid.NewGuid().ToString("N");
bool finished = false;
bool halfImageRequested = false;
var csv = new System.Text.StringBuilder("frame,time,phase,flowRound,droneRound,state,progress,visible,health,exploded,x,y,z,speed,blocked\n");
var report = new System.Text.StringBuilder("Real Bootstrap->Shelter->TryDepart->Ruins. API-assisted player positioning only.\n");
var culture = System.Globalization.CultureInfo.InvariantCulture;
System.Action<bool, string> check = (ok, why) => { if (!ok) throw new System.InvalidOperationException(why); };
System.Action<UnityEngine.Vector3> placeCentre = centre =>
{
    var position = centre - (capsule.bounds.center - player.Body.position);
    position.z = player.config.planeZ;
    check(player.RelocateWithinRun(position), "Real player rejected positioning.");
    player.Body.isKinematic = true;
    UnityEngine.Physics.SyncTransforms();
};
System.Func<UnityEngine.Vector3> forward = () =>
{
    var v = drone.transform.TransformDirection(drone.FacingRotation * UnityEngine.Vector3.right);
    return v.x < 0 ? UnityEngine.Vector3.left : UnityEngine.Vector3.right;
};
UnityEditor.EditorApplication.CallbackFunction observer = null;
System.Action<string, string> finish = (status, why) =>
{
    if (finished) return;
    finished = true;
    try
    {
        if (player != null && player.Body != null)
        {
            if (UnityEditor.EditorApplication.isPlaying && player.IsPrepared && !player.IsDead)
                player.RelocateWithinRun(originalPosition);
            player.Body.isKinematic = originalKinematic;
            player.Body.useGravity = originalGravity;
        }
    }
    finally
    {
        UnityEditor.EditorApplication.update -= observer;
        UnityEditor.SessionState.SetBool("MyPrecious.RuinsAcceptance.Running", false);
        UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Result", status + ":02:" + why);
        UnityEngine.Time.timeScale = 1f;
        UnityEngine.Application.runInBackground = false;
    }
    report.AppendLine(status + ": " + why);
    report.AppendLine("Max Alert drift=" + maxHoverDrift.ToString("R", culture));
    report.AppendLine("No direct damage/AI initialization/state/progress calls. Real explosion and health loss remain for next step.");
    System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "02-PatrolFight.csv"), csv.ToString());
    System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "02-PatrolFight.txt"), report.ToString());
};
observer = delegate
{
    try
    {
        if (UnityEditor.SessionState.GetString("MyPrecious.RuinsAcceptance.Token", "") != token) { finish("CANCELLED", "Token cancelled."); return; }
        check(UnityEditor.EditorApplication.isPlaying && flow != null && drone != null && player != null, "Play or runtime objects ended.");
        check(flow.Stage == GameFlowStage.Exploring && flow.CurrentRound == round && drone.CurrentRound == round, "Formal stage/round changed during the probe.");
        check(UnityEditor.EditorApplication.timeSinceStartup - started < 35, "Natural patrol/vision/contact timed out; do not force state or change route.");
        if (lastFrame == UnityEngine.Time.frameCount) return;
        lastFrame = UnityEngine.Time.frameCount;
        bool visible = drone.vision.CanSee(player);
        var p = drone.Body.position;
        csv.AppendFormat(culture, "{0},{1:R},{2},{3},{4},{5},{6:R},{7},{8},{9},{10:R},{11:R},{12:R},{13:R},{14}\n",
            lastFrame, UnityEngine.Time.time, phase, flow.CurrentRound, drone.CurrentRound, drone.CurrentState,
            drone.AlertProgress, visible, player.health.CurrentHealth, drone.HasExploded, p.x, p.y, p.z,
            drone.FlightVelocity.magnitude, drone.IsBlocked);
        if (phase < 3) check(player.health.CurrentHealth == health && !drone.HasExploded, "Damage/explosion occurred before Chase contact.");
        switch (phase)
        {
            case 0:
                if (drone.CurrentState != DroneState.Patrol || drone.FlightVelocity.magnitude < 0.1f) { patrolTime = -1f; break; }
                if (patrolTime < 0f) { patrolTime = UnityEngine.Time.time; patrolStart = p; break; }
                if (UnityEngine.Time.time - patrolTime < 0.3f) break;
                check(UnityEngine.Vector3.Distance(p, patrolStart) > 0.1f, "Formal Drone has a patrol state but did not really move.");
                report.AppendLine("PASS automatic common round and real patrol movement; user route and Z unchanged.");
                placeCentre(drone.vision.transform.position + forward() * 2.5f);
                check(drone.vision.CanSee(player), "The actual scene blocks the requested visible location.");
                phase = 1; phaseTime = UnityEngine.Time.time; lastFrame = UnityEngine.Time.frameCount;
                break;
            case 1:
                check(drone.CurrentState != DroneState.Chase, "Real detection skipped Alert.");
                if (drone.CurrentState != DroneState.Alert) break;
                check(visible && drone.Target == player && drone.AlertProgress < 0.2f, "Expected natural initial Alert.");
                hover = p; alertStarted = UnityEngine.Time.time; phase = 2;
                break;
            case 2:
                if (drone.CurrentState == DroneState.Alert)
                {
                    check(visible, "Player unexpectedly became invisible during accumulation.");
                    maxProgress = UnityEngine.Mathf.Max(maxProgress, drone.AlertProgress);
                    if (!halfImageRequested && drone.AlertProgress >= 0.5f)
                    {
                        halfImageRequested = true;
                        UnityEngine.ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(directory, "02-Ruins-AlertHalf.png"));
                    }
                    maxHoverDrift = UnityEngine.Mathf.Max(maxHoverDrift, UnityEngine.Vector3.Distance(hover, p));
                    check(maxHoverDrift < 0.005f, "Alert body moved instead of hovering.");
                    break;
                }
                check(drone.CurrentState == DroneState.Chase && maxProgress > 0.75f
                    && UnityEngine.Time.time - alertStarted >= drone.config.alertFillSeconds * 0.8f,
                    "Expected natural accumulated Alert followed by Chase.");
                report.AppendLine("PASS real visible Alert accumulated while hovering and naturally entered Chase.");
                placeCentre(drone.vision.transform.position + forward() * 0.15f);
                UnityEngine.Vector3 direction; float distance;
                check(UnityEngine.Physics.ComputePenetration(sphere, sphere.transform.position, sphere.transform.rotation,
                    capsule, capsule.transform.position, capsule.transform.rotation, out direction, out distance), "Real 3D body colliders do not overlap at the unchanged Z planes.");
                check(drone.vision.CanSee(player), "Contact location must still be visible.");
                phase = 3; lastFrame = UnityEngine.Time.frameCount;
                break;
            case 3:
                if (!drone.HasExploded) break;
                check(player.health.CurrentHealth == health - drone.config.contactDamage, "Real contact did not cause exactly the configured single hit.");
                phase = 4; phaseTime = UnityEngine.Time.time;
                break;
            case 4:
                check(player.health.CurrentHealth == health - drone.config.contactDamage && drone.HasExploded,
                    "Duplicate damage or same-round revival occurred.");
                if (UnityEngine.Time.time - phaseTime < 0.3f) break;
                check(!sphere.enabled && !drone.visual.gameObject.activeInHierarchy && !drone.vision.gameObject.activeInHierarchy,
                    "Explosion did not disable collision/model/vision.");
                UnityEditor.SessionState.SetInt("MyPrecious.RuinsAcceptance.ExplodedDroneId", drone.GetInstanceID());
                UnityEditor.SessionState.SetInt("MyPrecious.RuinsAcceptance.AfterHitHealth", player.health.CurrentHealth);
                finish("PASS", "Real contact exploded once; no repeat after0.3 seconds. Next verify same-round formal preparation.");
                break;
        }
    }
    catch (System.Exception e) { finish("FAIL", "phase" + phase + ": " + e.Message); }
};
UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Token", token);
UnityEditor.SessionState.SetBool("MyPrecious.RuinsAcceptance.Running", true);
UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Result", "RUNNING:02");
try
{
    placeCentre(drone.Body.position + UnityEngine.Vector3.up * (drone.vision.radius + 10f));
    check(!drone.vision.CanSee(player), "Initial fixture is not really outside vision.");
    UnityEngine.Application.runInBackground = true;
    UnityEngine.Time.timeScale = 1f;
    UnityEditor.EditorApplication.update += observer;
}
catch (System.Exception e) { finish("FAIL", "Setup: " + e.Message); }
return new { armed = !finished, directory = directory, statusKey = "MyPrecious.RuinsAcceptance.Result" };
