// Adapted to007's current public scene entry. Recheck this signature after007 releases ownership.
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
string directory = UnityEditor.SessionState.GetString("MyPrecious.RuinsAcceptance.Directory", "");
if (!UnityEditor.EditorApplication.isPlaying || flow == null || flow.Stage != GameFlowStage.Exploring
    || !System.IO.Directory.Exists(directory) || UnityEditor.SessionState.GetBool("MyPrecious.RuinsAcceptance.Running", false))
    throw new System.InvalidOperationException("Wait for step02 to finish in real Exploring.");
var bindings = System.Array.Find(UnityEngine.Object.FindObjectsByType<GameFlowSceneBindings>(UnityEngine.FindObjectsSortMode.None),
    b => b.gameObject.scene.path == flow.ruinsScenePath);
var drone = System.Array.Find(UnityEngine.Object.FindObjectsByType<DroneController>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None),
    d => d.GetInstanceID() == UnityEditor.SessionState.GetInt("MyPrecious.RuinsAcceptance.ExplodedDroneId", 0));
if (bindings == null || drone == null || !drone.HasExploded || drone.CurrentRound != flow.CurrentRound)
    throw new System.InvalidOperationException("Need the actual same-round exploded instance from02.");
int round = flow.CurrentRound;
int health = flow.Player.health.CurrentHealth;
var position = drone.Body.position;
string error;
if (!bindings.TryBeginExplorationEnemies(round, out error))
    throw new System.InvalidOperationException("The real formal preparation entry rejected its same-round retry: " + error);
string token = System.Guid.NewGuid().ToString("N");
double started = UnityEditor.EditorApplication.timeSinceStartup;
float gameStarted = UnityEngine.Time.time;
UnityEditor.EditorApplication.CallbackFunction observer = null;
System.Action<string, string> finish = (status, why) =>
{
    UnityEditor.EditorApplication.update -= observer;
    UnityEditor.SessionState.SetBool("MyPrecious.RuinsAcceptance.Running", false);
    UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Result", status + ":03:" + why);
    UnityEditor.SessionState.SetBool("MyPrecious.RuinsAcceptance.SameRoundPassed", status == "PASS");
    UnityEngine.Time.timeScale = 1f;
    UnityEngine.Application.runInBackground = false;
    System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "03-SameRound.txt"), status + ": " + why);
};
observer = delegate
{
    try
    {
        if (UnityEditor.SessionState.GetString("MyPrecious.RuinsAcceptance.Token", "") != token) { finish("CANCELLED", "Token cancelled."); return; }
        if (!UnityEditor.EditorApplication.isPlaying || flow == null || drone == null || flow.Player == null)
            throw new System.InvalidOperationException("Runtime ended.");
        if (UnityEditor.EditorApplication.timeSinceStartup - started > 6) throw new System.InvalidOperationException("Post-retry observation timed out.");
        if (flow.CurrentRound != round || drone.CurrentRound != round || !drone.HasExploded || drone.CurrentState != DroneState.Explode
            || drone.GetComponent<UnityEngine.SphereCollider>().enabled || drone.visual.gameObject.activeInHierarchy
            || drone.vision.gameObject.activeInHierarchy || flow.Player.health.CurrentHealth != health
            || UnityEngine.Vector3.Distance(drone.Body.position, position) > 0.001f)
            throw new System.InvalidOperationException("Same-round formal retry revived, moved or repeated damage from the exploded instance.");
        var effect = drone.GetComponent<DroneStatusLight>();
        if (effect != null && effect.targetLight != null && effect.targetLight.gameObject.activeInHierarchy
            && effect.targetLight.enabled && effect.targetLight.intensity > 0f)
            throw new System.InvalidOperationException("Same-round retry left the exploded light illuminating.");
        if (UnityEngine.Time.time - gameStarted >= 0.3f)
            finish("PASS", "Actual TryBeginExplorationEnemies(currentRound) returned success; after0.3 game seconds the same instance remained exploded, hidden, stationary and harmless.");
    }
    catch (System.Exception e) { finish("FAIL", e.Message); }
};
UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Token", token);
UnityEditor.SessionState.SetBool("MyPrecious.RuinsAcceptance.Running", true);
UnityEditor.SessionState.SetBool("MyPrecious.RuinsAcceptance.SameRoundPassed", false);
UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Result", "RUNNING:03");
UnityEngine.Time.timeScale = 1f;
UnityEngine.Application.runInBackground = true;
UnityEditor.EditorApplication.update += observer;
return new { armed = true, round = round, statusKey = "MyPrecious.RuinsAcceptance.Result" };
