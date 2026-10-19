return new
{
    running = UnityEditor.SessionState.GetBool("09F.Probe.Running", false),
    phase = UnityEditor.SessionState.GetString("09F.Probe.Phase", ""),
    result = UnityEditor.SessionState.GetString("09F.Probe.Result", ""),
    directory = UnityEditor.SessionState.GetString("09F.Probe.Directory", "")
};
