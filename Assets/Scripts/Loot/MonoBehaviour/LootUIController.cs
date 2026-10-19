using UnityEngine;

// 只管理一次打开来源的会话。搜索推进不负责生成，也不修改物品归属。
public class LootUIController : MonoBehaviour
{
    public BackpackUIController backpack;
    public LootRuntimeManager runtime;
    public ItemTransferUIBridge transfers;
    public LootView view;
    public LocalizationManager localization;

    private BackpackUIController boundBackpack;
    private LootRuntimeManager boundRuntime;
    private Interactable sourceTarget;
    private PlayerInteraction interaction;
    private PlayerController sessionPlayer;
    private int nextSession;

    public LootSourceState Source { get; private set; }
    public int SessionToken { get; private set; }
    public bool IsOpen { get { return SessionToken != 0 && Source != null; } }
    public string FeedbackKey { get; private set; } = "loot.drag_hint";

    private void OnEnable() { Bind(); }
    private void Start() { Bind(); }

    public void Bind()
    {
        if (Application.isPlaying) { runtime = LootRuntimeManager.Resolve(); }
        if (boundBackpack != backpack || boundRuntime != runtime)
        {
            CloseSource();
            Unsubscribe();
            boundBackpack = backpack;
            boundRuntime = runtime;
            if (boundBackpack != null) { boundBackpack.Closing += OnBackpackClosing; }
            if (boundRuntime != null) { boundRuntime.SourceChanged += OnSourceChanged; }
        }
        localization = LocalizationManager.Resolve(localization);
        if (view != null)
        {
            view.Bind(this, localization);
            view.SetVisible(IsOpen);
        }
    }

    public bool TryOpenSource(LootSourceState source, Interactable target, PlayerInteraction playerInteraction)
    {
        if (!isActiveAndEnabled || source == null || target == null || playerInteraction == null
            || backpack == null || runtime == null || view == null) { return false; }
        Bind();
        PlayerController player = playerInteraction.GetComponentInParent<PlayerController>();
        if (player == null || !player.isActiveAndEnabled || !player.IsPrepared || player.IsDead
            || backpack.player != player || !playerInteraction.CanReachTarget(target)
            || !runtime.IsExplorationActive || runtime.ActiveMapId != source.MapId || !runtime.IsSourceReady(source)
            || !runtime.TryGetBinding(source.SourceId, out Interactable boundTarget) || boundTarget != target)
        {
            SetFeedback("loot.source_invalid");
            return false;
        }
        CloseSource();
        // 先成功取得04控制限制；失败不建立来源或搜索计时。
        if (!backpack.TryOpen()) { return false; }
        Source = source;
        sourceTarget = target;
        interaction = playerInteraction;
        sessionPlayer = player;
        nextSession = nextSession == int.MaxValue ? 1 : nextSession + 1;
        SessionToken = nextSession;
        FeedbackKey = source.IsGround ? "loot.drag_hint" : "loot.return_hint";
        view.SetVisible(true);
        view.RefreshItems();
        AdvanceSearch(0f);
        return true;
    }

    private void Update()
    {
        if (!IsOpen) { return; }
        if (!IsSessionValid(SessionToken, Source))
        {
            CloseSource();
            SetFeedback("loot.unreachable");
            if (backpack != null) { backpack.SetFeedback("loot.unreachable"); }
            return;
        }
        // 世界正常推进；不改timeScale，不依赖开包后已清空的CurrentTarget。
        AdvanceSearch(Time.deltaTime);
    }

    private void AdvanceSearch(float delta)
    {
        if (!IsOpen || runtime == null) { return; }
        bool changed = Source.AdvanceSearch(delta, runtime.itemManager);
        if (changed) { runtime.NotifySourceChanged(Source.SourceId); }
        if (view != null) { view.RefreshProgress(); }
    }

    public bool IsSessionValid(int token, LootSourceState source)
    {
        return isActiveAndEnabled && token != 0 && token == SessionToken && ReferenceEquals(source, Source)
            && backpack != null && backpack.IsOpen && backpack.player == sessionPlayer
            && sessionPlayer != null && sessionPlayer.isActiveAndEnabled && sessionPlayer.IsPrepared && !sessionPlayer.IsDead
            && sourceTarget != null && sourceTarget.isActiveAndEnabled && interaction != null
            && interaction.CanReachTarget(sourceTarget) && runtime != null && runtime.itemManager != null
            && runtime.IsExplorationActive && runtime.ActiveMapId == Source.MapId && runtime.IsSourceReady(Source)
            && runtime.TryGetBinding(Source.SourceId, out Interactable currentTarget) && currentTarget == sourceTarget;
    }

    public int BeginItemDrag(string entryId, Vector2 position, Camera eventCamera)
    {
        return BeginItemDrag(entryId, position, eventCamera, position);
    }

    public int BeginItemDrag(string entryId, Vector2 position, Camera eventCamera, Vector2 pressPosition)
    {
        if (transfers == null || !IsSessionValid(SessionToken, Source)
            || !Source.TryGetEntry(entryId, out LootEntryState entry) || !entry.Revealed || entry.Taken
            || view == null || !view.TryGetItemGrabOffset(entryId, pressPosition, eventCamera, out Vector2Int grabOffset)) { return 0; }
        return transfers.BeginLootDrag(Source, entryId, SessionToken, position, eventCamera, grabOffset, entry.LayoutRotated);
    }

    public bool TryQuickTransfer(string entryId)
    {
        if (transfers == null) { return RejectQuickTransfer("transfer.NotReady"); }
        if (!IsSessionValid(SessionToken, Source)) { return RejectQuickTransfer("transfer.StaleRequest"); }
        if (!Source.TryGetEntry(entryId, out LootEntryState entry) || entry.Taken) { return RejectQuickTransfer("transfer.SourceInvalid"); }
        if (!entry.Revealed) { return RejectQuickTransfer("transfer.SourceHidden"); }
        return transfers.TryQuickSourceToBackpack(Source, entryId, SessionToken);
    }

    private bool RejectQuickTransfer(string key)
    {
        SetFeedback(key);
        if (backpack != null) { backpack.SetFeedback(key); }
        return false;
    }

    public void UpdateItemDrag(int token, Vector2 position, Camera eventCamera)
    {
        if (transfers != null) { transfers.UpdateLootDrag(token, position, eventCamera); }
    }

    public void EndItemDrag(int token, Vector2 position, Camera eventCamera)
    {
        if (transfers != null) { transfers.EndLootDrag(token, position, eventCamera); }
    }

    public void CancelItemDrag(int token)
    {
        if (backpack != null) { backpack.CancelDrag(token); }
    }

    public void CloseSource()
    {
        LootSourceState oldSource = Source;
        SessionToken = 0;
        Source = null;
        sourceTarget = null;
        interaction = null;
        sessionPlayer = null;
        // 普通取消预览只走CancelItemDrag；只有结束会话才清未完成项的计时。
        if (transfers != null) { transfers.CancelLootDrag(); }
        if (oldSource != null) { oldSource.InterruptSearch(); }
        if (view != null) { view.SetVisible(false); }
    }

    public void SetFeedback(string key)
    {
        FeedbackKey = key;
        if (view != null) { view.RefreshText(); }
    }

    private void OnSourceChanged(LootSourceState source)
    {
        if (ReferenceEquals(source, Source) && view != null) { view.RefreshItems(); }
    }

    private void OnBackpackClosing() { CloseSource(); }
    private void OnApplicationFocus(bool focused) { if (!focused) { CloseSource(); } }
    private void OnApplicationPause(bool paused) { if (paused) { CloseSource(); } }

    private void Unsubscribe()
    {
        if (boundBackpack != null) { boundBackpack.Closing -= OnBackpackClosing; }
        if (boundRuntime != null) { boundRuntime.SourceChanged -= OnSourceChanged; }
        boundBackpack = null;
        boundRuntime = null;
    }

    private void OnDisable()
    {
        CloseSource();
        Unsubscribe();
    }
}
