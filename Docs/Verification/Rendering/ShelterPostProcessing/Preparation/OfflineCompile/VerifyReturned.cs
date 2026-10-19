public static class ShelterPPVerifyReturned { public static object Run() {
// MCP execute_code method body, after actual formal return reaches Shelter.
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
string directory = UnityEditor.SessionState.GetString("ShelterPP.Directory", "");
if (!UnityEditor.EditorApplication.isPlaying || flow == null || flow.Stage != GameFlowStage.Shelter || flow.Player == null
    || !System.IO.Directory.Exists(directory)) throw new System.InvalidOperationException("Wait for real return to Shelter.");
if (flow.Player.GetInstanceID() != UnityEditor.SessionState.GetInt("ShelterPP.PlayerId", 0)
    || flow.CurrentRound != 1 || flow.SettledRound != 1 || flow.Player.health.CurrentRound != 1
    || flow.Player.IsDead || flow.Player.IsSceneTransitionBlocked || flow.Player.gameObject.scene.path != flow.shelterScenePath)
    throw new System.InvalidOperationException("Player identity, round settlement or formal return invariant failed.");
var oldRuins = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(flow.ruinsScenePath);
if (oldRuins.IsValid() && oldRuins.isLoaded) throw new System.InvalidOperationException("Old Ruins still loaded.");
var loaded = new System.Collections.Generic.List<string>();
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) loaded.Add(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).path);
if (loaded.Count != 1 || loaded[0] != flow.shelterScenePath) throw new System.InvalidOperationException("Expected only the formal Shelter scene plus DontDestroyOnLoad objects.");
if (UnityEngine.Object.FindObjectsByType<PlayerController>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Length != 1)
    throw new System.InvalidOperationException("Duplicate player present after return.");
var bindings = System.Array.Find(UnityEngine.Object.FindObjectsByType<GameFlowSceneBindings>(UnityEngine.FindObjectsSortMode.None), b => b.gameObject.scene == flow.Player.gameObject.scene);
string error;
if (bindings == null || !bindings.Validate(out error)) throw new System.InvalidOperationException("Returned bindings invalid.");
var result = new { status = "PASS_FORMAL_BOOTSTRAP_SHELTER_RUINS_SHELTER_API_ASSISTED", playerId = flow.Player.GetInstanceID(), currentRound = flow.CurrentRound,
    settledRound = flow.SettledRound, playerRound = flow.Player.health.CurrentRound, health = flow.Player.health.CurrentHealth, loadedScenes = loaded,
    visualAcceptance = "Pending actual1920x1080 image review and target-camera stack comparison; does not claim physical keyboard/mouse validation." };
System.IO.File.WriteAllText(directory + "/FlowReturn.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
UnityEditor.SessionState.SetString("ShelterPP.CaptureLabel", "04-Shelter-Returned");
return result;

} }