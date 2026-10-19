// Recorder changes no TimeScale/player/physics settings; only ask its observer to stop.
UnityEditor.SessionState.SetString("MyPrecious.TimedAlertRecorder.Token", "");
return new { cancellationRequested = true, waitForRunningFalse = "MyPrecious.TimedAlertRecorder.Running" };
