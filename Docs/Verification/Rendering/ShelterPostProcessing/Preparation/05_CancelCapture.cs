// Callback sees the cleared token on its next Editor tick and removes BOTH subscriptions.
UnityEditor.SessionState.SetString("ShelterPP.CaptureToken", "");
return new { cancellationRequested = true, running = UnityEditor.SessionState.GetBool("ShelterPP.CaptureRunning", false),
    next = "Wait until ShelterPP.CaptureRunning=false and inspect CaptureResult before release. No fake clear of running flag." };
