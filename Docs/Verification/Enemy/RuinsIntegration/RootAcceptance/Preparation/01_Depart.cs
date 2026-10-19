// MCP execute_code statements. Bootstrap must already have reached Shelter normally.
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
if (!UnityEditor.EditorApplication.isPlaying || flow == null || flow.Stage != GameFlowStage.Shelter)
    throw new System.InvalidOperationException("Start the real Bootstrap and wait for Shelter first.");
if (UnityEngine.Screen.width != 1920 || UnityEngine.Screen.height != 1080)
    throw new System.InvalidOperationException("Use the agreed 1920x1080 Game resolution.");
if (flow.CurrentRound != 0 || flow.SettledRound != 0 || flow.Player == null || !flow.Player.IsPrepared)
    throw new System.InvalidOperationException("This probe requires a fresh formal session, not a test fixture.");
if (UnityEngine.Object.FindObjectsByType<GameBootstrap>(UnityEngine.FindObjectsSortMode.None).Length != 1
    || UnityEngine.Object.FindObjectsByType<PlayerController>(UnityEngine.FindObjectsSortMode.None).Length != 1)
    throw new System.InvalidOperationException("Expected one real Bootstrap and one player.");
if (UnityEditor.SessionState.GetBool("MyPrecious.RuinsAcceptance.Running", false))
    throw new System.InvalidOperationException("Cancel the previous probe and wait for cleanup.");
string directory = "E:/DD451/Docs/Verification/Enemy/RuinsIntegration/RootAcceptance/Run-" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
System.IO.Directory.CreateDirectory(directory);
UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Directory", directory);
UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.ItemId", "");
UnityEditor.SessionState.SetInt("MyPrecious.RuinsAcceptance.PlayerId", flow.Player.GetInstanceID());
UnityEditor.SessionState.SetInt("MyPrecious.RuinsAcceptance.FirstRound", flow.CurrentRound + 1);
UnityEditor.SessionState.SetBool("MyPrecious.RuinsAcceptance.SameRoundPassed", false);
UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Result", "DEPARTING");
System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "01-Departure.txt"),
    "Formal Bootstrap reached Shelter at round0. Public flow.TryDepart is the sole departure trigger.\n");
if (!flow.TryDepart()) throw new System.InvalidOperationException("Formal TryDepart rejected the request: " + flow.LastError);
return new { directory = directory, stage = flow.Stage.ToString(), next = "Wait for Exploring, then execute 02_ObservePatrolAndFight.cs." };
