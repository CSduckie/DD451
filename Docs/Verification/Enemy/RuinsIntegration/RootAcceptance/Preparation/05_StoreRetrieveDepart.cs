// MCP execute_code statements: same actual carried item, real storage UI and transfer coordinator.
var flow = UnityEngine.Object.FindFirstObjectByType<GameFlowManager>();
string directory = UnityEditor.SessionState.GetString("MyPrecious.RuinsAcceptance.Directory", "");
string id = UnityEditor.SessionState.GetString("MyPrecious.RuinsAcceptance.ItemId", "");
if (!UnityEditor.EditorApplication.isPlaying || flow == null || flow.Stage != GameFlowStage.Shelter
    || string.IsNullOrEmpty(id) || !System.IO.Directory.Exists(directory))
    throw new System.InvalidOperationException("Wait for the real extraction to reach Shelter with its carried item.");
if (flow.CurrentRound != flow.SettledRound || flow.CurrentRound != UnityEditor.SessionState.GetInt("MyPrecious.RuinsAcceptance.FirstRound", 0))
    throw new System.InvalidOperationException("Formal extraction has not settled the first round.");
var home = System.Array.Find(UnityEngine.Object.FindObjectsByType<GameFlowSceneBindings>(UnityEngine.FindObjectsSortMode.None),
    b => b.gameObject.scene.path == flow.shelterScenePath);
ItemInstance item;
if (home == null || !flow.Items.TryGetInstance(id, out item) || item.Owner != ItemOwner.Backpack || !item.IsRetained)
    throw new System.InvalidOperationException("The actual extracted item was not retained in the backpack.");
int count = flow.Items.InstanceCount;
if (!flow.TryOpenStorage()) throw new System.InvalidOperationException("Formal storage UI rejected opening.");
System.Func<bool> valid = () => flow.Stage == GameFlowStage.Shelter && home.backpackUI.IsOpen && home.warehouseUI.IsOpen;
try
{
    UnityEngine.Vector2Int cell; bool rotated;
    if (flow.Warehouse.TryFindSpaceForExternal(id, false, out cell, out rotated) != WarehouseResult.Success)
        throw new System.InvalidOperationException("No legal real warehouse space.");
    var stored = flow.Transfers.TryBackpackToWarehouse(id, cell.x, cell.y, rotated, requestIsValid: valid);
    WarehousePlacement warehousePlacement; BackpackPlacement backpackPlacement;
    if (stored != ItemTransferResult.Success || item.Owner != ItemOwner.Storage
        || item.ContainerId != flow.Warehouse.ContainerId || !flow.Warehouse.TryGetPlacement(id, out warehousePlacement)
        || flow.Backpack.TryGetPlacement(id, out backpackPlacement) || flow.Items.InstanceCount != count)
        throw new System.InvalidOperationException("Actual storage transfer failed/inconsistent: " + stored);
    if (flow.Backpack.TryFindSpaceForExternal(id, false, out cell, out rotated) != BackpackResult.Success)
        throw new System.InvalidOperationException("No legal backpack space for the same stored item.");
    var retrieved = flow.Transfers.TryWarehouseToBackpack(id, cell.x, cell.y, rotated, requestIsValid: valid);
    ItemInstance same;
    if (retrieved != ItemTransferResult.Success || !flow.Items.TryGetInstance(id, out same) || !object.ReferenceEquals(item, same)
        || item.Owner != ItemOwner.Backpack || item.ContainerId != flow.Backpack.ContainerId || !item.IsRetained
        || !flow.Backpack.TryGetPlacement(id, out backpackPlacement) || flow.Warehouse.TryGetPlacement(id, out warehousePlacement)
        || flow.Items.InstanceCount != count)
        throw new System.InvalidOperationException("Actual retrieval failed/inconsistent: " + retrieved);
    System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "05-Storage.txt"),
        "PASS real storage round-trip: " + item.DefinitionId + "/" + id + "; same reference/id, retained, no duplication; instances=" + count);
}
finally { home.CloseSessions(); }
UnityEditor.SessionState.SetInt("MyPrecious.RuinsAcceptance.NextRound", flow.CurrentRound + 1);
if (!flow.TryDepart()) throw new System.InvalidOperationException("Second real departure rejected: " + flow.LastError);
return new { stage = flow.Stage.ToString(), item = id, next = "Wait for Exploring, then execute06_ObserveNextRound.cs." };
