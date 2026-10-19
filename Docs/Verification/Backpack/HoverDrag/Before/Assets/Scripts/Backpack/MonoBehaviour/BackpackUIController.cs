using System;
using UnityEngine;
using UnityEngine.InputSystem;

// 04只处理背包输入和一次拖拽。物品身份归01，玩家运动和死亡限制归02。
[DisallowMultipleComponent]
public class BackpackUIController : MonoBehaviour
{
    public BackpackManager manager;
    public PlayerController player;
    public BackpackView view;
    public InputActionAsset inputActions;
    [Tooltip("由08/09明确指定。庇护所允许未准备玩家整理；死亡玩家仍不可打开。")]
    public bool shelterContext;

    private BackpackManager boundManager;
    private PlayerController boundPlayer;
    private PlayerHealth boundHealth;
    private LocalizationManager boundLocalization;
    private InputAction toggleAction;
    private InputAction rotateAction;
    private InputAction useAction;
    private InputAction pointAction;
    private int nextDragToken;
    private int dragToken;
    private bool externalPreview;
    private bool pointerInside;
    private BackpackPlacement originalPlacement;
    private ItemInstance dragItem;
    private ItemOwner externalOwner;
    private string externalContainerId;
    private Vector2Int grabOffset;
    private Vector2 lastPointer;
    private Camera pointerCamera;
    private bool closing;
    private BackpackItemView quickPressOwner;
    private bool requestingQuickTransfer;

    // 06只接一个同步提交回调；04不引用06类型，也不向多个监听者广播业务请求。
    public Func<bool> CanBeginDrag { get; set; }
    public Action<int, Vector2, Camera> DragUpdated { get; set; }
    public Func<int, Vector2, Camera, bool> ExternalDropRequested { get; set; }
    public Func<string, bool> QuickTransferRequested { get; set; }
    public event Action Closing;
    public event Action DragCancelled;

    public bool IsOpen { get; private set; }
    public bool IsDragging { get { return dragToken != 0; } }
    public bool IsExternalPreview { get { return IsDragging && externalPreview; } }
    public int DragToken { get { return dragToken; } }
    public Vector2Int DragGrabOffset { get { return grabOffset; } }
    public string SelectedInstanceId { get; private set; }
    public string DragInstanceId { get { return dragItem != null ? dragItem.InstanceId : null; } }
    public string FeedbackKey { get; private set; } = "backpack.ready";
    public object[] FeedbackArguments { get { return Array.Empty<object>(); } }
    public BackpackResult PreviewResult { get; private set; }
    public int PreviewX { get; private set; }
    public int PreviewY { get; private set; }
    public bool PreviewRotated { get; private set; }
    internal int InputSession { get; private set; }

    private bool QuickTransferBlocksInput { get { return quickPressOwner != null || requestingQuickTransfer; } }

    private void OnEnable()
    {
        ResolveBindings();
        EnableInput();
        if (view != null) { view.SetVisible(false); }
    }

    private void Start()
    {
        // 场景中Bootstrap的Awake顺序不固定，Start重新解析实际存活入口。
        ResolveBindings();
        EnableInput();
    }

    private void Update()
    {
        if (boundManager == null || boundManager != BackpackManager.Resolve()
            || boundPlayer != player || (player != null && boundHealth == null)
            || boundLocalization != LocalizationManager.Instance)
        {
            ResolveBindings();
        }
        if (IsOpen && ((!shelterContext && boundPlayer == null)
            || (boundPlayer != null && (!boundPlayer.isActiveAndEnabled || boundPlayer.IsDead
                || boundPlayer.IsSceneTransitionBlocked))))
        {
            Close();
        }
        if (IsDragging && pointAction != null)
        {
            UpdateDrag(dragToken, pointAction.ReadValue<Vector2>(), pointerCamera);
        }
    }

    public void ResolveBindings()
    {
        BackpackManager current = BackpackManager.Resolve();
        if (boundManager != current)
        {
            Close();
            if (boundManager != null) { boundManager.Changed -= OnLayoutChanged; }
            boundManager = current;
            manager = current;
            if (boundManager != null) { boundManager.Changed += OnLayoutChanged; }
        }
        if (boundPlayer != player || (player != null && boundHealth == null)) { BindPlayer(player); }
        boundLocalization = LocalizationManager.Instance;
        if (view != null) { view.Bind(this, boundManager, boundLocalization); }
    }

    public void BindPlayer(PlayerController newPlayer)
    {
        if (boundPlayer == newPlayer && boundHealth != null) { player = newPlayer; return; }
        // 必须先清旧玩家的开包标记，再退订，死亡限制由02独立保留。
        Close();
        UnsubscribePlayer();
        player = newPlayer;
        boundPlayer = newPlayer;
        boundHealth = newPlayer != null ? newPlayer.GetComponent<PlayerHealth>() : null;
        if (boundHealth != null)
        {
            boundHealth.NonLethalDamaged += OnDamaged;
            boundHealth.Died += OnDied;
        }
    }

    public void SetShelterContext(bool shelter)
    {
        Close();
        shelterContext = shelter;
    }

    public bool TryOpen()
    {
        if (!isActiveAndEnabled) { return false; }
        ResolveBindings();
        if (boundManager == null || !boundManager.IsReady || view == null || LocalizationManager.Instance == null)
        {
            SetFeedback("backpack.result.not_ready");
            return false;
        }
        if (closing || (boundPlayer != null && (!boundPlayer.isActiveAndEnabled || boundPlayer.IsDead
                || boundPlayer.IsSceneTransitionBlocked))
            || (!shelterContext && (boundPlayer == null || !boundPlayer.IsPrepared)))
        {
            SetFeedback("backpack.player_unavailable");
            return false;
        }
        IsOpen = true;
        if (boundPlayer != null) { boundPlayer.SetBackpackOpen(true); }
        view.SetVisible(true);
        view.RefreshItems();
        return true;
    }

    public void Close()
    {
        if (closing) { return; }
        closing = true;
        try
        {
            bool wasOpen = IsOpen;
            IsOpen = false;
            InputSession = InputSession == int.MaxValue ? 1 : InputSession + 1;
            quickPressOwner = null;
            // 来源会话在解除控制锁前结束；监听者只清理，不反向调用Close。
            if (wasOpen && Closing != null)
            {
                foreach (Action listener in Closing.GetInvocationList())
                {
                    try { listener(); }
                    catch (Exception exception) { Debug.LogException(exception, this); }
                }
            }
            CancelDrag();
            if (view != null) { view.SetVisible(false); }
            // 解除的只是背包限制，不调用复活、补血、Idle或轮次重置。
            if (boundPlayer != null) { boundPlayer.SetBackpackOpen(false); }
        }
        finally { closing = false; }
    }

    public void SelectItem(string instanceId)
    {
        if (!IsOpen || IsDragging || QuickTransferBlocksInput || (CanBeginDrag != null && !CanBeginDrag()) || boundManager == null
            || boundManager.TryGetCarriedItem(instanceId, out _) != BackpackResult.Success) { return; }
        SelectedInstanceId = instanceId;
        view.RefreshText();
    }

    public int BeginItemDrag(string instanceId, Vector2 screenPosition, Camera eventCamera)
    {
        if (!IsOpen || IsDragging || QuickTransferBlocksInput || (CanBeginDrag != null && !CanBeginDrag()) || boundManager == null
            || boundManager.TryGetCarriedItem(instanceId, out ItemInstance item) != BackpackResult.Success
            || !boundManager.TryGetPlacement(instanceId, out BackpackPlacement placement)) { return 0; }
        SelectedInstanceId = instanceId;
        originalPlacement = placement;
        externalPreview = false;
        dragItem = item;
        PreviewRotated = placement.Rotated;
        grabOffset = Vector2Int.zero;
        if (view.TryScreenToCell(screenPosition, eventCamera, out Vector2Int clickedCell))
        {
            grabOffset = clickedCell - new Vector2Int(placement.X, placement.Y);
        }
        dragToken = NextToken();
        // 不移除真实摆放。受击/非法松手只丢弃预览，原占格始终存在。
        UpdateDrag(dragToken, screenPosition, eventCamera);
        return dragToken;
    }

    public bool BeginExternalPreview(string instanceId)
    {
        return BeginExternalPreview(instanceId, false, Vector2Int.zero);
    }

    public bool BeginExternalPreview(string instanceId, bool rotated, Vector2Int grabOffset)
    {
        if (!isActiveAndEnabled || closing || IsDragging || QuickTransferBlocksInput) { return false; }
        if (!IsOpen && !TryOpen()) { return false; }
        int session = InputSession;
        if (CanBeginDrag != null && !CanBeginDrag()) { return false; }
        if (!IsOpen || closing || !isActiveAndEnabled || session != InputSession
            || IsDragging || QuickTransferBlocksInput || view == null
            || boundManager == null || !boundManager.IsReady || boundManager != BackpackManager.Resolve()
            || boundPlayer != player
            || (boundPlayer != null && (!boundPlayer.isActiveAndEnabled || boundPlayer.IsDead
                || boundPlayer.IsSceneTransitionBlocked))
            || (!shelterContext && (boundPlayer == null || !boundPlayer.IsPrepared))
            || !boundManager.Items.TryGetInstance(instanceId, out ItemInstance item)
            || (item.Owner != ItemOwner.World && item.Owner != ItemOwner.Container && item.Owner != ItemOwner.Storage)
            || boundManager.TryGetPlacement(instanceId, out _)) { return false; }
        ItemDataSO definition = item.Definition;
        if (definition == null || definition.width <= 0 || definition.height <= 0
            || (rotated && !definition.canRotate)) { return false; }
        int width = rotated ? definition.height : definition.width;
        int height = rotated ? definition.width : definition.height;
        // 外部条目可以已旋转；首次预览与之后R旋转都以实际占格夹紧抓取偏移。
        Vector2Int offset = new Vector2Int(Mathf.Clamp(grabOffset.x, 0, width - 1),
            Mathf.Clamp(grabOffset.y, 0, height - 1));
        externalPreview = true;
        originalPlacement = null;
        dragItem = item;
        externalOwner = item.Owner;
        externalContainerId = item.ContainerId;
        SelectedInstanceId = instanceId;
        PreviewRotated = rotated;
        this.grabOffset = offset;
        int startedToken = NextToken();
        dragToken = startedToken;
        try
        {
            UpdateDrag(startedToken, pointAction != null ? pointAction.ReadValue<Vector2>() : Vector2.zero, null);
        }
        catch
        {
            CancelDrag(startedToken);
            throw;
        }
        // 首次通知可能同步关闭、取消或换来源；只清理本次，不能取消后来新建的token。
        if (!IsDragValid(startedToken))
        {
            CancelDrag(startedToken);
            return false;
        }
        return true;
    }

    internal void BeginQuickTransferPress(BackpackItemView itemView)
    {
        if (IsOpen && !closing && quickPressOwner == null) { quickPressOwner = itemView; }
    }

    internal void EndQuickTransferPress(BackpackItemView itemView)
    {
        if (quickPressOwner == itemView) { quickPressOwner = null; }
    }

    public bool TryQuickTransfer(string instanceId)
    {
        if (requestingQuickTransfer || !CanRequestQuickTransfer(instanceId)) { return false; }
        Func<string, bool> request = QuickTransferRequested;
        if (request == null) { return false; }

        requestingQuickTransfer = true;
        try
        {
            int session = InputSession;
            // 复用06提供的确认/另一侧拖拽闸门，不在04寻找目标或修改归属。
            if (CanBeginDrag != null && !CanBeginDrag()) { return false; }
            if (session != InputSession || QuickTransferRequested != request
                || !CanRequestQuickTransfer(instanceId)) { return false; }
            return request(instanceId);
        }
        finally { requestingQuickTransfer = false; }
    }

    private bool CanRequestQuickTransfer(string instanceId)
    {
        return isActiveAndEnabled && IsOpen && !closing && !IsDragging
            && boundManager != null && boundManager == BackpackManager.Resolve() && boundManager.IsReady
            && boundPlayer == player
            && (boundPlayer == null ? shelterContext
                : boundPlayer.isActiveAndEnabled && !boundPlayer.IsDead && !boundPlayer.IsSceneTransitionBlocked
                    && (shelterContext || boundPlayer.IsPrepared))
            && (quickPressOwner == null || quickPressOwner.InstanceId == instanceId)
            && boundManager.TryGetCarriedItem(instanceId, out _) == BackpackResult.Success;
    }

    private int NextToken()
    {
        // 每次开始都使用不同编号；取消后迟到的OnEndDrag不能提交下一次预览。
        nextDragToken = nextDragToken == int.MaxValue ? 1 : nextDragToken + 1;
        return nextDragToken;
    }

    public void UpdateDrag(int token, Vector2 screenPosition, Camera eventCamera)
    {
        if (!IsOpen || token == 0 || token != dragToken || dragItem == null) { return; }
        lastPointer = screenPosition;
        pointerCamera = eventCamera;
        if (!DragSourceIsUnchanged()) { CancelDrag(); SetFeedback("backpack.result.wrong_owner"); return; }
        pointerInside = view.TryScreenToCell(screenPosition, eventCamera, out Vector2Int cell);
        PreviewX = cell.x - grabOffset.x;
        PreviewY = cell.y - grabOffset.y;
        PreviewResult = pointerInside
            ? externalPreview ? boundManager.CanPlaceExternal(DragInstanceId, PreviewX, PreviewY, PreviewRotated)
                : boundManager.CanMove(DragInstanceId, PreviewX, PreviewY, PreviewRotated)
            : BackpackResult.OutOfBounds;
        SetFeedback(!pointerInside ? "backpack.outside" : externalPreview && PreviewResult == BackpackResult.Success
            ? "backpack.dragging_external" : ResultKey(PreviewResult));
        if (pointerInside) { view.ShowPreview(dragItem, PreviewX, PreviewY, PreviewRotated, PreviewResult == BackpackResult.Success); }
        else { view.HidePreview(); }
        DragUpdated?.Invoke(token, screenPosition, eventCamera);
    }

    public void RotatePreview()
    {
        if (!IsOpen || !IsDragging || dragItem == null) { return; }
        if (!dragItem.Definition.canRotate) { SetFeedback("backpack.result.rotation_not_allowed"); return; }
        PreviewRotated = !PreviewRotated;
        int width = PreviewRotated ? dragItem.Definition.height : dragItem.Definition.width;
        int height = PreviewRotated ? dragItem.Definition.width : dragItem.Definition.height;
        grabOffset = new Vector2Int(Mathf.Clamp(grabOffset.x, 0, width - 1), Mathf.Clamp(grabOffset.y, 0, height - 1));
        UpdateDrag(dragToken, lastPointer, pointerCamera);
    }

    public void EndDrag(int token, Vector2 screenPosition, Camera eventCamera)
    {
        if (!IsOpen || token == 0 || token != dragToken) { return; }
        UpdateDrag(token, screenPosition, eventCamera);
        if (token != dragToken) { return; }
        // 回调必须在有效token内提交，不能先取消来源资格再请求转移。
        if (ExternalDropRequested != null && ExternalDropRequested(token, screenPosition, eventCamera))
        {
            string resultKey = FeedbackKey;
            CancelDrag();
            SetFeedback(resultKey);
            return;
        }
        if (externalPreview)
        {
            CancelDrag();
            SetFeedback("backpack.external_pending");
            return;
        }
        string instanceId = DragInstanceId;
        int x = PreviewX;
        int y = PreviewY;
        bool rotated = PreviewRotated;
        bool canCommit = pointerInside && PreviewResult == BackpackResult.Success && DragSourceIsUnchanged();
        string failureKey = FeedbackKey;
        CancelDrag();
        if (!canCommit) { SetFeedback(failureKey); return; }
        // TryMove再次完整校验，成功才一次替换位置/朝向。
        BackpackResult result = boundManager.TryMove(instanceId, x, y, rotated);
        SetFeedback(result == BackpackResult.Success ? "backpack.moved" : ResultKey(result));
    }

    private bool DragSourceIsUnchanged()
    {
        if (boundManager == null || dragItem == null || !boundManager.Items.TryGetInstance(DragInstanceId, out ItemInstance current)
            || !ReferenceEquals(current, dragItem)) { return false; }
        if (externalPreview)
        {
            return current.Owner == externalOwner && current.ContainerId == externalContainerId
                && !boundManager.TryGetPlacement(DragInstanceId, out _);
        }
        return boundManager.TryGetCarriedItem(DragInstanceId, out _) == BackpackResult.Success
            && boundManager.TryGetPlacement(DragInstanceId, out BackpackPlacement placement)
            && originalPlacement != null && placement.X == originalPlacement.X
            && placement.Y == originalPlacement.Y && placement.Rotated == originalPlacement.Rotated;
    }

    public void CancelDrag()
    {
        bool hadDrag = IsDragging;
        dragToken = 0;
        dragItem = null;
        originalPlacement = null;
        externalPreview = false;
        pointerInside = false;
        if (view != null) { view.HidePreview(); }
        if (hadDrag) { SetFeedback("backpack.cancelled"); }
        if (hadDrag && DragCancelled != null)
        {
            foreach (Action listener in DragCancelled.GetInvocationList())
            {
                try { listener(); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
        }
    }

    public void CancelDrag(int token)
    {
        if (token != 0 && token == dragToken) { CancelDrag(); }
    }

    public bool IsDragValid(int token)
    {
        return IsOpen && token != 0 && token == dragToken && DragSourceIsUnchanged();
    }

    public void TryUseHoveredItem(string instanceId)
    {
        if (!IsOpen || IsDragging || QuickTransferBlocksInput || IsQuickTransferChordHeld()
            || (CanBeginDrag != null && !CanBeginDrag())) { return; }
        if (boundHealth == null || boundPlayer == null || !boundPlayer.isActiveAndEnabled
            || !boundPlayer.IsPrepared || boundPlayer.IsDead)
        {
            SetFeedback("backpack.player_unavailable");
            return;
        }
        if (boundManager == null) { SetFeedback("backpack.result.not_ready"); return; }
        SelectedInstanceId = instanceId;
        BackpackResult result = boundManager.TryUseItem(instanceId, boundHealth, out ItemUseResult useResult);
        SetFeedback(result == BackpackResult.Success ? UseResultKey(useResult) : ResultKey(result));
    }

    private bool IsQuickTransferChordHeld()
    {
        // InputAction可能先于UGUI按下回调到达；同帧Ctrl+左右键也不能先使用物品。
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        return keyboard != null && mouse != null && mouse.leftButton.isPressed
            && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
    }

    private void OnLayoutChanged()
    {
        if (IsDragging && !DragSourceIsUnchanged()) { CancelDrag(); }
        if (view != null) { view.RefreshItems(); }
    }

    private void OnDamaged(int amount)
    {
        Close();
    }

    private void OnDied()
    {
        Close();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) { Close(); }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) { Close(); }
    }

    public void SetFeedback(string key)
    {
        FeedbackKey = key;
        if (view != null) { view.RefreshText(); }
    }

    private void EnableInput()
    {
        if (inputActions == null || toggleAction != null) { return; }
        toggleAction = CopyAction("Player/ToggleBackpack");
        rotateAction = CopyAction("Player/RotateItem");
        useAction = CopyAction("Player/UseItem");
        pointAction = CopyAction("UI/Point");
        if (toggleAction != null) { toggleAction.performed += OnToggle; toggleAction.Enable(); }
        if (rotateAction != null) { rotateAction.performed += OnRotate; rotateAction.Enable(); }
        if (useAction != null) { useAction.performed += OnUse; useAction.Enable(); }
        pointAction?.Enable();
    }

    private InputAction CopyAction(string path)
    {
        InputAction source = inputActions.FindAction(path);
        if (source == null) { Debug.LogError("背包缺少输入动作：" + path, this); return null; }
        InputAction copy = source.Clone();
        copy.bindingMask = InputBinding.MaskByGroup("Keyboard&Mouse");
        return copy;
    }

    private void OnToggle(InputAction.CallbackContext context)
    {
        if (IsOpen) { Close(); }
        else { TryOpen(); }
    }

    private void OnRotate(InputAction.CallbackContext context)
    {
        RotatePreview();
    }
    private void OnUse(InputAction.CallbackContext context)
    {
        if (!IsOpen || IsDragging || view == null || pointAction == null) { return; }
        string instanceId = view.ItemAtScreenPoint(pointAction.ReadValue<Vector2>(), null);
        if (!string.IsNullOrEmpty(instanceId)) { TryUseHoveredItem(instanceId); }
    }

    private void UnsubscribePlayer()
    {
        if (boundHealth != null)
        {
            boundHealth.NonLethalDamaged -= OnDamaged;
            boundHealth.Died -= OnDied;
        }
        boundHealth = null;
        boundPlayer = null;
    }

    private void OnDisable()
    {
        Close();
        UnsubscribePlayer();
        if (boundManager != null) { boundManager.Changed -= OnLayoutChanged; }
        boundManager = null;
        if (toggleAction != null) { toggleAction.performed -= OnToggle; }
        if (rotateAction != null) { rotateAction.performed -= OnRotate; }
        if (useAction != null) { useAction.performed -= OnUse; }
        toggleAction?.Dispose();
        rotateAction?.Dispose();
        useAction?.Dispose();
        pointAction?.Dispose();
        toggleAction = rotateAction = useAction = pointAction = null;
    }

    public static string ResultKey(BackpackResult result)
    {
        switch (result)
        {
            case BackpackResult.Success: return "backpack.result.success";
            case BackpackResult.NotReady: return "backpack.result.not_ready";
            case BackpackResult.InvalidConfiguration: return "backpack.result.invalid_configuration";
            case BackpackResult.NotFound: return "backpack.result.not_found";
            case BackpackResult.WrongOwner: return "backpack.result.wrong_owner";
            case BackpackResult.WrongContainer: return "backpack.result.wrong_container";
            case BackpackResult.MissingPlacement: return "backpack.result.missing_placement";
            case BackpackResult.DuplicateInstance: return "backpack.result.duplicate_instance";
            case BackpackResult.RotationNotAllowed: return "backpack.result.rotation_not_allowed";
            case BackpackResult.OutOfBounds: return "backpack.result.out_of_bounds";
            case BackpackResult.Overlap: return "backpack.result.overlap";
            default: return "backpack.result.busy";
        }
    }

    private static string UseResultKey(ItemUseResult result)
    {
        switch (result)
        {
            case ItemUseResult.Success: return "items.use.success";
            case ItemUseResult.NotFound: return "items.use.not_found";
            case ItemUseResult.NotCarried: return "items.use.not_carried";
            case ItemUseResult.NotUsable: return "items.use.not_usable";
            case ItemUseResult.InvalidTarget: return "items.use.invalid_target";
            case ItemUseResult.FullHealth: return "items.use.full_health";
            case ItemUseResult.RoundNotStarted: return "items.use.round_not_started";
            case ItemUseResult.RoundLimitReached: return "items.use.round_limit_reached";
            case ItemUseResult.InvalidConfiguration: return "items.use.invalid_configuration";
            case ItemUseResult.NoHealingApplied: return "items.use.no_healing_applied";
            case ItemUseResult.Busy: return "items.use.busy";
            default: return "items.use.unknown";
        }
    }
}
