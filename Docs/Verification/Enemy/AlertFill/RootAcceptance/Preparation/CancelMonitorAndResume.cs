// Safe cancellation: the old callback sees the changed token and removes itself on the next Editor update.
UnityEditor.SessionState.SetString("MyPrecious.RootAlertAcceptance.Monitor", "");
UnityEngine.Time.timeScale = 1f;
UnityEditor.SessionState.SetString("MyPrecious.RootAlertAcceptance.Status", "cancelled and resumed by root");
return new
{
    timeScale = UnityEngine.Time.timeScale,
    lastCsv = UnityEditor.SessionState.GetString("MyPrecious.RootAlertAcceptance.LastCsv", ""),
    note = "No game states, progress, timings or assets changed. Any active monitor unsubscribes next Editor update."
};
