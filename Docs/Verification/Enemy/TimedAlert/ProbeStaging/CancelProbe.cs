// Active callback checks this token even while game time is paused, then restores its own changes.
UnityEditor.SessionState.SetString("09F.Probe.Token", "");
return new { cancellationRequested = true, waitForRunningFalse = "09F.Probe.Running" };
