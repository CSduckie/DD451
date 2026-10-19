using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class LootBox : Interactable
{
    public string mapId;
    public string sourceId;
    public LootTableSO lootTable;
    [Header("箱子格子容量（列 × 行）")]
    [Min(1)] public int gridWidth = 6;
    [Min(1)] public int gridHeight = 4;
    public LootRuntimeManager runtimeManager;
    public LootUIController lootUI;
    public LootSourceState State { get; private set; }
    public string SourceKey { get { return mapId + "/" + sourceId; } }
    public override string PromptKey { get { return "loot.interact.box"; } }

    private void Start() { TryBind(); }
    private void OnEnable() { if (runtimeManager != null) { TryBind(); } }
    private void OnDisable() { if (runtimeManager != null) { runtimeManager.Unbind(this, SourceKey); } }

    private void TryBind()
    {
        LootRuntimeManager resolved = LootRuntimeManager.Resolve();
        if (resolved != null) { runtimeManager = resolved; }
        if (runtimeManager == null) { runtimeManager = FindFirstObjectByType<LootRuntimeManager>(); }
        if (runtimeManager != null && !runtimeManager.BindBox(this, out string error)) { Debug.LogWarning(error, this); }
    }

    internal void SetState(LootSourceState state) { State = state; }

    public override bool CanInteract(PlayerInteraction player)
    {
        return runtimeManager != null && runtimeManager.IsExplorationActive && runtimeManager.ActiveMapId == mapId
            && runtimeManager.IsSourceReady(State);
    }

    public override void Interact(PlayerInteraction player)
    {
        if (!CanInteract(player)) { return; }
        if (lootUI == null) { lootUI = FindFirstObjectByType<LootUIController>(); }
        if (lootUI != null) { lootUI.TryOpenSource(State, this, player); }
    }
}
