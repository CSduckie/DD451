// Real box interaction/search Update/transfer/extraction. Never create an item or write its owner/layout.
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
string directory = UnityEditor.SessionState.GetString("MyPrecious.RuinsAcceptance.Directory", "");
if (!UnityEditor.EditorApplication.isPlaying || flow == null || flow.Stage != GameFlowStage.Exploring
    || !System.IO.Directory.Exists(directory) || !UnityEditor.SessionState.GetBool("MyPrecious.RuinsAcceptance.SameRoundPassed", false))
    throw new System.InvalidOperationException("Complete01/02 and the actual same-round formal preparation check first.");
if (UnityEditor.SessionState.GetBool("MyPrecious.RuinsAcceptance.Running", false))
    throw new System.InvalidOperationException("Another probe is still running.");
var player = flow.Player;
var bindings = System.Array.Find(UnityEngine.Object.FindObjectsByType<GameFlowSceneBindings>(UnityEngine.FindObjectsSortMode.None),
    b => b.gameObject.scene.path == flow.ruinsScenePath);
var box = System.Array.Find(UnityEngine.Object.FindObjectsByType<LootBox>(UnityEngine.FindObjectsSortMode.None),
    b => b.gameObject.scene.path == flow.ruinsScenePath && b.sourceId == "supplies-box");
var extraction = System.Array.Find(UnityEngine.Object.FindObjectsByType<GameFlowInteractable>(UnityEngine.FindObjectsSortMode.None),
    b => b.gameObject.scene.path == flow.ruinsScenePath && b.interactionType == GameFlowInteraction.Extraction);
if (bindings == null || box == null || extraction == null || box.State == null)
    throw new System.InvalidOperationException("Formal scene references are missing.");
var capsule = player.GetComponent<UnityEngine.CapsuleCollider>();
var originalPosition = player.Body.position;
bool originalKinematic = player.Body.isKinematic;
bool originalGravity = player.Body.useGravity;
bool releasedForTransition = false;
bool finished = false;
string token = System.Guid.NewGuid().ToString("N");
double started = UnityEditor.EditorApplication.timeSinceStartup;
int lastFrame = -1;
var csv = new System.Text.StringBuilder("frame,time,searchEntry,elapsed,revealed,entries,health\n");
var culture = System.Globalization.CultureInfo.InvariantCulture;
System.Action<bool, string> check = (ok, why) => { if (!ok) throw new System.InvalidOperationException(why); };
System.Action<Interactable> enterTrigger = target =>
{
    UnityEngine.BoxCollider trigger;
    check(target.TryGetInteractionTrigger(out trigger), "Real target has no active TriggerBox.");
    var position = trigger.bounds.center - (capsule.bounds.center - player.Body.position);
    position.z = player.config.planeZ;
    check(player.RelocateWithinRun(position), "Player rejected target positioning.");
    player.Body.isKinematic = true;
    UnityEngine.Physics.SyncTransforms();
    check(player.interaction.CanReachTarget(target), "The actual player cannot reach this TriggerBox.");
    player.interaction.RefreshTarget();
    check(player.interaction.CurrentTarget == target, "The real interaction query did not select the intended target.");
};
UnityEditor.EditorApplication.CallbackFunction observer = null;
System.Action<string, string> finish = (status, why) =>
{
    if (finished) return;
    finished = true;
    try
    {
        if (!releasedForTransition && player != null && player.Body != null)
        {
            if (bindings != null) bindings.CloseSessions();
            if (UnityEditor.EditorApplication.isPlaying && player.IsPrepared && !player.IsDead) player.RelocateWithinRun(originalPosition);
            player.Body.isKinematic = originalKinematic;
            player.Body.useGravity = originalGravity;
        }
    }
    finally
    {
        UnityEditor.EditorApplication.update -= observer;
        UnityEditor.SessionState.SetBool("MyPrecious.RuinsAcceptance.Running", false);
        UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Result", status + ":04:" + why);
        UnityEngine.Time.timeScale = 1f;
        UnityEngine.Application.runInBackground = false;
    }
    System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "04-NaturalSearch.csv"), csv.ToString());
    System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "04-LootExtraction.txt"), status + ": " + why);
};
observer = delegate
{
    try
    {
        if (UnityEditor.SessionState.GetString("MyPrecious.RuinsAcceptance.Token", "") != token) { finish("CANCELLED", "Token cancelled."); return; }
        check(UnityEditor.EditorApplication.isPlaying && flow != null && player != null && box != null, "Play or scene ended unexpectedly.");
        check(flow.Stage == GameFlowStage.Exploring, "Formal flow left Exploring before extraction.");
        check(UnityEditor.EditorApplication.timeSinceStartup - started < 35, "Actual search timed out; do not force reveal/ownership.");
        check(bindings.lootUI.IsSessionValid(bindings.lootUI.SessionToken, box.State), "Real source session became invalid.");
        if (lastFrame == UnityEngine.Time.frameCount) return;
        lastFrame = UnityEngine.Time.frameCount;
        int revealed = 0;
        LootEntryState candidate = null;
        foreach (var entry in box.State.Entries)
        {
            if (entry.Revealed) revealed++;
            if (entry.Revealed && !entry.Taken && candidate == null) candidate = entry;
        }
        csv.AppendFormat(culture, "{0},{1:R},{2},{3:R},{4},{5},{6}\n", lastFrame, UnityEngine.Time.time,
            box.State.SearchEntryId, box.State.SearchElapsed, revealed, box.State.Entries.Count, player.health.CurrentHealth);
        if (candidate == null) return;
        ItemInstance item;
        check(flow.Items.TryGetInstance(candidate.InstanceId, out item), "Revealed entry has no real item instance.");
        UnityEngine.Vector2Int cell; bool rotated;
        check(flow.Backpack.TryFindSpaceForExternal(item.InstanceId, false, out cell, out rotated) == BackpackResult.Success,
            "No legal backpack space for the naturally revealed item.");
        int session = bindings.lootUI.SessionToken;
        var result = flow.Transfers.TrySourceToBackpack(box.State, candidate.EntryId, cell.x, cell.y, rotated,
            () => bindings.lootUI.IsSessionValid(session, box.State));
        check(result == ItemTransferResult.Success, "Real pickup transfer rejected: " + result);
        BackpackPlacement carried;
        check(candidate.Taken && item.Owner == ItemOwner.Backpack && item.ContainerId == flow.Backpack.ContainerId
            && flow.Backpack.TryGetPlacement(item.InstanceId, out carried), "Pickup did not commit consistent ownership and placement.");
        UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.ItemId", item.InstanceId);
        UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.ItemDefinition", item.DefinitionId);
        bindings.CloseSessions();
        enterTrigger(extraction);
        check(extraction.CanInteract(player.interaction), "Real extraction interaction is not available.");
        // Release only the probe's freeze BEFORE the formal transition takes its own lock.
        player.Body.isKinematic = originalKinematic;
        player.Body.useGravity = originalGravity;
        releasedForTransition = true;
        check(player.interaction.TryInteract(), "The real player interaction entry rejected extraction.");
        check(flow.Stage == GameFlowStage.Returning || flow.Stage == GameFlowStage.Shelter,
            "Real extraction did not start Returning: " + flow.LastError);
        finish("PASS", "Naturally revealed " + item.DefinitionId + "/" + item.InstanceId
            + " transferred by real coordinator; actual PlayerInteraction.TryInteract started formal return. Wait for Shelter next.");
    }
    catch (System.Exception e) { finish("FAIL", e.Message); }
};
UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Token", token);
UnityEditor.SessionState.SetBool("MyPrecious.RuinsAcceptance.Running", true);
UnityEditor.SessionState.SetString("MyPrecious.RuinsAcceptance.Result", "RUNNING:04");
try
{
    enterTrigger(box);
    check(player.interaction.TryInteract(), "The real player interaction entry rejected the box.");
    check(bindings.lootUI.IsOpen && object.ReferenceEquals(bindings.lootUI.Source, box.State), "Real box interaction did not open its search UI.");
    UnityEngine.Application.runInBackground = true;
    UnityEngine.Time.timeScale = 1f;
    UnityEditor.EditorApplication.update += observer;
}
catch (System.Exception e) { finish("FAIL", "Setup: " + e.Message); }
return new { armed = !finished, directory = directory, statusKey = "MyPrecious.RuinsAcceptance.Result" };
