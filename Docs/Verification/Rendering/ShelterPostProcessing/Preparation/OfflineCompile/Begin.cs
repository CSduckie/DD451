public static class ShelterPPBegin { public static object Run() {
// MCP execute_code method body; ordinary formal Bootstrap Play only. Docs, never Assets.
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
if (!UnityEditor.EditorApplication.isPlaying || UnityEditor.EditorApplication.isPaused || flow == null
    || flow.Stage != GameFlowStage.Shelter || flow.Player == null || !flow.Player.IsPrepared
    || flow.Player.IsDead || flow.Player.IsSceneTransitionBlocked)
    throw new System.InvalidOperationException("Wait for formal Bootstrap to reach an unlocked Shelter.");
if (UnityEngine.Screen.width != 1920 || UnityEngine.Screen.height != 1080 || UnityEngine.Time.timeScale != 1f)
    throw new System.InvalidOperationException("Require actual 1920x1080 Game and timeScale=1.");
if (flow.CurrentRound != 0 || flow.SettledRound != 0 || flow.Player.gameObject.scene.path != flow.shelterScenePath)
    throw new System.InvalidOperationException("Require a fresh formal round0 session.");
if (UnityEngine.Object.FindObjectsByType<GameBootstrap>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Length != 1
    || UnityEngine.Object.FindObjectsByType<PlayerController>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Length != 1)
    throw new System.InvalidOperationException("Require one Bootstrap and one actual player.");
if (UnityEditor.SessionState.GetBool("ShelterPP.CaptureRunning", false))
    throw new System.InvalidOperationException("Cancel and clean the existing screenshot observer first.");
string directory = "E:/DD451/Docs/Verification/Rendering/ShelterPostProcessing/Runtime-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
System.IO.Directory.CreateDirectory(directory);
UnityEditor.SessionState.SetString("ShelterPP.Directory", directory);
UnityEditor.SessionState.SetInt("ShelterPP.PlayerId", flow.Player.GetInstanceID());
UnityEditor.SessionState.SetString("ShelterPP.CaptureLabel", "01-Shelter-Before");
UnityEditor.SessionState.SetString("ShelterPP.Target", "Departure");
UnityEditor.SessionState.SetBool("ShelterPP.Invoke", true);
var result = new { status = "FORMAL_BOOTSTRAP_REACHED_SHELTER", playerId = flow.Player.GetInstanceID(), stage = flow.Stage.ToString(), round = flow.CurrentRound,
    playerScene = flow.Player.gameObject.scene.path, directory = directory,
    verificationMethod = "Ordinary Play; later positioning and interaction use public API, not physical keyboard/mouse." };
System.IO.File.WriteAllText(directory + "/00-Begin.json", Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
return result;

} }