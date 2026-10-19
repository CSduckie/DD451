// Real second departure/reload only. Never calls BeginRound or restores the enemy manually.
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
string directory = UnityEditor.SessionState.GetString("MyPrecious.RuinsAcceptance.Directory", "");
string itemId = UnityEditor.SessionState.GetString("MyPrecious.RuinsAcceptance.ItemId", "");
int round = UnityEditor.SessionState.GetInt("MyPrecious.RuinsAcceptance.NextRound", 0);
if (!UnityEditor.EditorApplication.isPlaying || flow == null || flow.Stage != GameFlowStage.Exploring
    || round <= 1 || flow.CurrentRound != round || !System.IO.Directory.Exists(directory)
    || UnityEditor.SessionState.GetBool("MyPrecious.RuinsAcceptance.Running", false))
    throw new System.InvalidOperationException("Wait for05's second real departure to reach Exploring.");
var player = flow.Player;
var drone = System.Array.Find(UnityEngine.Object.FindObjectsByType<DroneController>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None),
    d => d.gameObject.scene.path == flow.ruinsScenePath && d.name == "Drone");
ItemInstance item; BackpackPlacement placement;
if (drone == null || !drone.IsRunning || drone.HasExploded || drone.CurrentRound != round
    || flow.Items.CurrentRound != round || player.health.CurrentRound != round || player.health.CurrentHealth != player.health.MaxHealth
    || !drone.GetComponent<UnityEngine.SphereCollider>().enabled || !drone.visual.gameObject.activeInHierarchy
    || !drone.vision.gameObject.activeInHierarchy || !flow.Items.TryGetInstance(itemId, out item)
    || item.Owner != ItemOwner.Backpack || !item.IsRetained || !flow.Backpack.TryGetPlacement(itemId, out placement))
    throw new System.InvalidOperationException("Second formal round failed automatic enemy/player/item preparation.");
if (player.GetInstanceID() != UnityEditor.SessionState.GetInt("MyPrecious.RuinsAcceptance.PlayerId", 0))
    throw new System.InvalidOperationException("Formal round-trip replaced the original player.");
var originalPosition = player.Body.position;
bool originalKinematic = player.Body.isKinematic;
bool originalGravity = player.Body.useGravity;
var remote = drone.Body.position + UnityEngine.Vector3.up * (drone.vision.radius + 10f);
remote.z = player.config.planeZ;
if (!player.RelocateWithinRun(remote)) throw new System.InvalidOperationException("Real player rejected isolated observation position.");
player.Body.isKinematic = true;
UnityEngine.Physics.SyncTransforms();
float movementStarted = -1f;
UnityEngine.Vector3 movementOrigin = UnityEngine.Vector3.zero;
double started = UnityEditor.EditorApplication.timeSinceStartup;
string token = System.Guid.NewGuid().ToString("N");
UnityEditor.EditorApplication.CallbackFunction observer = null;
System.Action<string, string> finish = (status, why) =>
{
    try
    {
        if (player != null && player.Body != null)
        {
            if (UnityEditor.EditorApplication.isPlaying && player.IsPrepared && !player.IsDead) player.RelocateWithinRun(originalPosition);
            player.Body.isKinematic = originalKinematic; player.Body.useGravity = originalGravity;
        }
    }
    finally
    {
        UnityEditor.EditorApplication.update -= observer;
        UnityEditor.SessionState.SetBool("MyPrecious.RuinsAcceptance.Running", false);
        UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Result", status + ":06:" + why);
        UnityEngine.Time.timeScale = 1f; UnityEngine.Application.runInBackground = false;
    }
    System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "06-NextRound.txt"), status + ": " + why
        + "\nround=" + round + "; real carried item=" + itemId
        + "; current Drone instance=" + (drone != null ? drone.GetInstanceID().ToString() : "destroyed")
        + "; previous exploded instance=" + UnityEditor.SessionState.GetInt("MyPrecious.RuinsAcceptance.ExplodedDroneId", 0));
};
observer = delegate
{
    try
    {
        if (UnityEditor.SessionState.GetString("MyPrecious.RuinsAcceptance.Token", "") != token) { finish("CANCELLED", "Token cancelled."); return; }
        if (!UnityEditor.EditorApplication.isPlaying || flow == null || player == null || drone == null)
            throw new System.InvalidOperationException("Runtime ended.");
        if (UnityEditor.EditorApplication.timeSinceStartup - started > 20) throw new System.InvalidOperationException("Second-round actual patrol movement timed out.");
        if (flow.Stage != GameFlowStage.Exploring || drone.CurrentRound != round || !drone.IsRunning || drone.HasExploded
            || drone.vision.CanSee(player)) throw new System.InvalidOperationException("Unexpected flow/enemy/visibility state during second-round patrol observation.");
        if (drone.CurrentState != DroneState.Patrol || drone.FlightVelocity.magnitude < 0.1f) { movementStarted = -1f; return; }
        if (movementStarted < 0f) { movementStarted = UnityEngine.Time.time; movementOrigin = drone.Body.position; return; }
        if (UnityEngine.Time.time - movementStarted < 0.3f) return;
        if (UnityEngine.Vector3.Distance(movementOrigin, drone.Body.position) <= 0.1f)
            throw new System.InvalidOperationException("Second-round enemy claims Patrol but remains stationary.");
        finish("PASS", "Automatic second-round enemy reset and actual patrol movement; same player, full health, retained same item after real warehouse round-trip.");
    }
    catch (System.Exception e) { finish("FAIL", e.Message); }
};
UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Token", token);
UnityEditor.SessionState.SetBool("MyPrecious.RuinsAcceptance.Running", true);
UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Result", "RUNNING:06");
UnityEngine.Time.timeScale = 1f; UnityEngine.Application.runInBackground = true;
UnityEditor.EditorApplication.update += observer;
return new { armed = true, round = round, statusKey = "MyPrecious.RuinsAcceptance.Result" };
