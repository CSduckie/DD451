// Unity MCP execute_code C# 6 method body. Run only after real Bootstrap reached Shelter.
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
if (!UnityEditor.EditorApplication.isPlaying || flow == null || flow.Stage != GameFlowStage.Shelter)
    throw new System.InvalidOperationException("Start Bootstrap in ordinary Play and wait for Shelter first.");
if (UnityEngine.Screen.width != 1920 || UnityEngine.Screen.height != 1080 || UnityEngine.Time.timeScale != 1f)
    throw new System.InvalidOperationException("Require 1920x1080 and timeScale 1 for measured timing.");
if (flow.CurrentRound != 0 || flow.SettledRound != 0 || flow.Player == null || !flow.Player.IsPrepared)
    throw new System.InvalidOperationException("Use a fresh formal session; do not initialize enemy rounds manually.");
if (UnityEngine.Object.FindObjectsByType<GameBootstrap>(UnityEngine.FindObjectsSortMode.None).Length != 1
    || UnityEngine.Object.FindObjectsByType<PlayerController>(UnityEngine.FindObjectsSortMode.None).Length != 1)
    throw new System.InvalidOperationException("Expected exactly one formal Bootstrap and persistent player.");
if (UnityEditor.SessionState.GetBool("09F.Probe.Running", false))
    throw new System.InvalidOperationException("Cancel the previous probe and wait for its cleanup.");
string directory = "E:/DD451/Docs/Verification/Enemy/TimedAlert/Integration/OrdinaryPlay/Run-"
    + System.Guid.NewGuid().ToString("N").Substring(0, 8);
System.IO.Directory.CreateDirectory(directory);
UnityEditor.SessionState.SetString("09F.Probe.Directory", directory);
UnityEditor.SessionState.SetInt("09F.Probe.PlayerId", flow.Player.GetInstanceID());
UnityEditor.SessionState.SetInt("09F.Probe.Round", flow.CurrentRound + 1);
UnityEditor.SessionState.SetString("09F.Probe.Result", "DEPARTING");
System.IO.File.WriteAllText(directory + "/01-Departure.txt",
    "Actual Bootstrap reached Shelter at round0; public flow.TryDepart is the sole departure trigger.\n"
    + "1920x1080, timeScale1. Later positioning/crouch assistance uses player APIs; no native keyboard claim.\n");
if (!flow.TryDepart()) throw new System.InvalidOperationException("Formal departure rejected: " + flow.LastError);
return new { directory = directory, stage = flow.Stage.ToString(), next = "Wait for Exploring, then execute 02_NaturalTimedAlert.cs." };
