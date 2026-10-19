using UnityEngine;

// 参考LiDar/KeyController的明确拾取入口；E归02，只有数据提交成功才隐藏表现。
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class GroundItem : Interactable
{
    public string mapId;
    public string sourceId;
    public string entryId = "item";
    public ItemDataSO initialItem;
    public LootRuntimeManager runtimeManager;
    public LootUIController lootUI;
    public LootSourceState State { get; private set; }
    public string InstanceId { get; private set; }
    public bool IsRuntimePresentation { get; private set; }
    public string SourceKey { get { return IsRuntimePresentation && State != null ? State.SourceId : mapId + "/" + sourceId; } }
    public override string PromptKey { get { return "loot.interact.ground"; } }

    private void Start() { if (!IsRuntimePresentation) { TryBind(); } }
    private void OnEnable() { if (!IsRuntimePresentation && runtimeManager != null) { TryBind(); } }
    private void OnDisable() { if (runtimeManager != null) { runtimeManager.Unbind(this, SourceKey); } }

    private void TryBind()
    {
        LootRuntimeManager resolved = LootRuntimeManager.Resolve();
        if (resolved != null) { runtimeManager = resolved; }
        if (runtimeManager == null) { runtimeManager = FindFirstObjectByType<LootRuntimeManager>(); }
        if (runtimeManager != null && !runtimeManager.BindGround(this, out string error)) { Debug.LogWarning(error, this); }
    }

    internal void SetState(LootSourceState state) { State = state; }

    internal void PrepareRuntime(LootRuntimeManager manager, LootSourceState state)
    {
        IsRuntimePresentation = true;
        runtimeManager = manager;
        State = state;
        mapId = state.MapId;
        InstanceId = state.Entries[0].InstanceId;
        entryId = state.Entries[0].EntryId;
    }

    internal void BindInstance(LootSourceState state, string instanceId)
    {
        State = state;
        InstanceId = instanceId;
    }

    internal void ShowPresentation()
    {
        foreach (Renderer visual in GetComponentsInChildren<Renderer>(true)) { visual.enabled = true; }
        foreach (Collider collider in GetComponentsInChildren<Collider>(true)) { collider.enabled = true; }
    }

    internal void HidePresentation()
    {
        foreach (Renderer visual in GetComponentsInChildren<Renderer>(true)) { visual.enabled = false; }
        foreach (Collider collider in GetComponentsInChildren<Collider>(true)) { collider.enabled = false; }
        // 场景原点标记保留以便下轮复用；动态丢放表现可销毁，运行记录不删除。
        if (IsRuntimePresentation && Application.isPlaying) { Destroy(gameObject); }
    }

    public override bool CanInteract(PlayerInteraction player)
    {
        return runtimeManager != null && runtimeManager.IsExplorationActive && runtimeManager.ActiveMapId == mapId
            && runtimeManager.IsSourceReady(State) && State.Entries.Count > 0 && !State.Entries[0].Taken;
    }

    public override void Interact(PlayerInteraction player)
    {
        if (!CanInteract(player)) { return; }
        if (lootUI == null) { lootUI = FindFirstObjectByType<LootUIController>(); }
        if (lootUI != null) { lootUI.TryOpenSource(State, this, player); }
    }
}
