// The active callback restores its own runtime changes on its next editor tick.
UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Token", "");
UnityEngine.Time.timeScale = 1f;
UnityEngine.Application.runInBackground = false;
return new { cancellationRequested = true, waitForRunningFalse = "MyPrecious.RuinsAcceptance.Running" };
