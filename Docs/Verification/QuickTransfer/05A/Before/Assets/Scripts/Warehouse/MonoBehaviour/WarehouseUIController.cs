using System;
using UnityEngine;
using UnityEngine.InputSystem;

// 仓库只拥有自己的 R 动作和拖拽预览，不接管背包、玩家或 F10。
[DisallowMultipleComponent]
public class WarehouseUIController : MonoBehaviour
{
    public WarehouseManager manager;
    public WarehouseView view;
    public LocalizationManager localization;
    public InputActionAsset inputActions;

    private WarehouseManager boundManager;
    private LocalizationManager boundLocalization;
    private InputAction rotateAction;
    private InputActionAsset boundInputActions;
    private int nextDragToken;
    private int dragToken;
    private ItemInstance dragItem;
    private WarehousePlacement originalPlacement;
    private Vector2Int grabOffset;
    private Vector2 lastPointer;
    private Camera pointerCamera;
    private bool pointerInside;

    // 跨界面由06单一回调协调；仓库仍独立处理原格重排与本次R输入。
    public Func<bool> CanBeginDrag { get; set; }
    public Action<int, Vector2, Camera> DragUpdated { get; set; }
    public Func<int, Vector2, Camera, bool> ExternalDropRequested { get; set; }
    public event Action Closing;
    public event Action DragCancelled;

    public bool IsOpen { get; private set; }
    public bool IsDragging { get { return dragToken != 0; } }
    public int DragToken { get { return dragToken; } }
    public Vector2Int DragGrabOffset { get { return grabOffset; } }
    public bool IsAwaitingAbandonConfirmation { get { return !string.IsNullOrEmpty(PendingAbandonInstanceId); } }
    public string SelectedInstanceId { get; private set; }
    public string PendingAbandonInstanceId { get; private set; }
    public string DragInstanceId { get { return dragItem != null ? dragItem.InstanceId : null; } }
    public string FeedbackKey { get; private set; } = "warehouse.ready";
    public WarehouseResult PreviewResult { get; private set; }
    public int PreviewX { get; private set; }
    public int PreviewY { get; private set; }
    public bool PreviewRotated { get; private set; }

    private void OnEnable()
    {
        Bind(manager, localization);
        if (view != null) { view.SetVisible(false); }
    }

    private void Start()
    {
        // Bootstrap 的 Awake 顺序不固定，Start 再核对存活入口。
        Bind(manager, localization);
    }

    private void LateUpdate()
    {
        // 只在拖拽期间更新坐标；滚动和 CanvasScaler 变化后，即使指针不动也重新换算。
        if (IsDragging) { UpdateDrag(dragToken, lastPointer, pointerCamera); }
    }

    public void Bind(WarehouseManager warehouse, LocalizationManager language)
    {
        WarehouseManager current = Application.isPlaying ? WarehouseManager.Resolve() : warehouse;
        LocalizationManager currentLanguage = LocalizationManager.Resolve(language);
        if (boundManager != current || boundLocalization != currentLanguage)
        {
            Close();
            ReleaseInput();
            UnsubscribeManager();
            boundManager = current;
            boundLocalization = currentLanguage;
            if (isActiveAndEnabled && boundManager != null)
            {
                boundManager.Changed += OnLayoutChanged;
                boundManager.ContextChanged += OnContextChanged;
            }
        }
        manager = current;
        localization = currentLanguage;
        if (view != null) { view.Bind(this, boundManager, boundLocalization); }
        EnableInput();
    }

    public void Open()
    {
        TryOpen();
    }

    public bool TryOpen()
    {
        if (!isActiveAndEnabled) { return false; }
        Bind(manager, localization);
        if (boundManager == null || !boundManager.IsReady || boundLocalization == null || view == null)
        {
            SetFeedback("warehouse.result.not_ready");
            return false;
        }
        if (!boundManager.IsManagementAllowed)
        {
            SetFeedback("warehouse.result.not_in_shelter");
            return false;
        }
        IsOpen = true;
        view.SetVisible(true);
        return true;
    }

    public void Close()
    {
        // 先取消 token 和确认目标，再停用界面，迟到的指针回调就没有提交资格。
        bool wasOpen = IsOpen;
        IsOpen = false;
        if (wasOpen && Closing != null)
        {
            foreach (Action listener in Closing.GetInvocationList())
            {
                try { listener(); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
        }
        CancelDrag();
        CancelAbandon();
        if (view != null) { view.SetVisible(false); }
    }

    public void SelectItem(string instanceId)
    {
        if (!CanInteract() || IsDragging || IsAwaitingAbandonConfirmation
            || boundManager.TryGetStoredItem(instanceId, out _) != WarehouseResult.Success) { return; }
        SelectedInstanceId = instanceId;
        RefreshText();
    }

    public int BeginItemDrag(string instanceId, Vector2 screenPosition, Camera eventCamera)
    {
        if (!CanInteract() || IsDragging || IsAwaitingAbandonConfirmation || (CanBeginDrag != null && !CanBeginDrag())
            || boundManager.TryGetStoredItem(instanceId, out ItemInstance item) != WarehouseResult.Success
            || !boundManager.TryGetPlacement(instanceId, out WarehousePlacement placement)
            || !boundManager.TryClaimInteraction(this)) { return 0; }
        SelectedInstanceId = instanceId;
        dragItem = item;
        originalPlacement = placement;
        PreviewRotated = placement.Rotated;
        grabOffset = view.GetGrabOffset(screenPosition, eventCamera, placement);
        nextDragToken = nextDragToken == int.MaxValue ? 1 : nextDragToken + 1;
        dragToken = nextDragToken;
        // 原摆放始终保留；预览没有调用移除、改归属或写回 SO。
        UpdateDrag(dragToken, screenPosition, eventCamera);
        RefreshText();
        return dragToken;
    }

    public void UpdateDrag(int token, Vector2 screenPosition, Camera eventCamera)
    {
        if (token == 0 || token != dragToken) { return; }
        if (!CanInteract() || !DragSourceIsUnchanged())
        {
            CancelDrag();
            SetFeedback("warehouse.result.wrong_owner");
            return;
        }
        lastPointer = screenPosition;
        pointerCamera = eventCamera;
        pointerInside = view.TryScreenToCell(screenPosition, eventCamera, out Vector2Int cell);
        PreviewX = cell.x - grabOffset.x;
        PreviewY = cell.y - grabOffset.y;
        PreviewResult = pointerInside
            ? boundManager.CanMove(DragInstanceId, PreviewX, PreviewY, PreviewRotated)
            : WarehouseResult.OutOfBounds;
        SetFeedback(pointerInside ? ResultKey(PreviewResult) : "warehouse.outside");
        if (pointerInside) { view.ShowPreview(dragItem, PreviewX, PreviewY, PreviewRotated, PreviewResult == WarehouseResult.Success); }
        else { view.HidePreview(); }
        DragUpdated?.Invoke(token, screenPosition, eventCamera);
    }

    public void RotatePreview()
    {
        if (!CanInteract() || !IsDragging || dragItem == null || !DragSourceIsUnchanged()) { return; }
        if (!dragItem.Definition.canRotate)
        {
            SetFeedback("warehouse.result.rotation_not_allowed");
            return;
        }
        PreviewRotated = !PreviewRotated;
        int width = PreviewRotated ? dragItem.Definition.height : dragItem.Definition.width;
        int height = PreviewRotated ? dragItem.Definition.width : dragItem.Definition.height;
        grabOffset = new Vector2Int(Mathf.Clamp(grabOffset.x, 0, width - 1), Mathf.Clamp(grabOffset.y, 0, height - 1));
        UpdateDrag(dragToken, lastPointer, pointerCamera);
    }

    public void EndDrag(int token, Vector2 screenPosition, Camera eventCamera)
    {
        if (token == 0 || token != dragToken) { return; }
        UpdateDrag(token, screenPosition, eventCamera);
        if (token != dragToken) { return; }
        if (ExternalDropRequested != null && ExternalDropRequested(token, screenPosition, eventCamera))
        {
            string resultKey = FeedbackKey;
            CancelDrag();
            SetFeedback(resultKey);
            return;
        }
        string instanceId = DragInstanceId;
        int x = PreviewX;
        int y = PreviewY;
        bool rotated = PreviewRotated;
        bool canCommit = pointerInside && PreviewResult == WarehouseResult.Success && DragSourceIsUnchanged();
        string failureKey = FeedbackKey;
        CancelDrag();
        if (!canCommit) { SetFeedback(failureKey); return; }
        WarehouseResult result = boundManager.TryMove(instanceId, x, y, rotated);
        SetFeedback(result == WarehouseResult.Success ? "warehouse.moved" : ResultKey(result));
    }

    public void CancelDrag()
    {
        bool hadDrag = IsDragging;
        dragToken = 0;
        dragItem = null;
        originalPlacement = null;
        pointerInside = false;
        if (!IsAwaitingAbandonConfirmation && boundManager != null) { boundManager.ReleaseInteraction(this); }
        if (view != null) { view.HidePreview(); }
        if (hadDrag) { SetFeedback("warehouse.cancelled"); }
        RefreshText();
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
        return CanInteract() && token != 0 && token == dragToken && DragSourceIsUnchanged();
    }

    public void RequestAbandonSelected()
    {
        if (!CanInteract() || IsAwaitingAbandonConfirmation) { return; }
        CancelDrag();
        WarehouseResult result = boundManager.TryGetStoredItem(SelectedInstanceId, out _);
        if (result != WarehouseResult.Success) { SetFeedback(ResultKey(result)); return; }
        if (!boundManager.TryClaimInteraction(this)) { SetFeedback("warehouse.result.busy"); return; }
        // 确认框只保存稳定身份；图标被刷新或换语言不会改变目标。
        PendingAbandonInstanceId = SelectedInstanceId;
        RefreshText();
    }

    public void ConfirmAbandon()
    {
        if (!IsAwaitingAbandonConfirmation) { return; }
        string instanceId = PendingAbandonInstanceId;
        bool allowed = CanInteract() && boundManager.OwnsInteraction(this);
        CancelAbandon();
        if (!allowed) { SetFeedback("warehouse.result.not_in_shelter"); return; }
        // 清空确认资格后同步提交，重复点击不能再次执行同一个确认。
        WarehouseResult result = boundManager.TryAbandon(instanceId);
        if (result == WarehouseResult.Success && SelectedInstanceId == instanceId) { SelectedInstanceId = null; }
        SetFeedback(result == WarehouseResult.Success ? "warehouse.abandoned" : ResultKey(result));
    }

    public void CancelAbandon()
    {
        PendingAbandonInstanceId = null;
        if (!IsDragging && boundManager != null) { boundManager.ReleaseInteraction(this); }
        RefreshText();
    }

    private bool CanInteract()
    {
        return IsOpen && isActiveAndEnabled && view != null && boundManager != null
            && boundManager.IsReady && boundManager.IsManagementAllowed;
    }

    private bool DragSourceIsUnchanged()
    {
        return boundManager != null && boundManager.OwnsInteraction(this) && dragItem != null && originalPlacement != null
            && boundManager.TryGetStoredItem(DragInstanceId, out ItemInstance current) == WarehouseResult.Success
            && ReferenceEquals(current, dragItem)
            && boundManager.TryGetPlacement(DragInstanceId, out WarehousePlacement placement)
            && placement.X == originalPlacement.X && placement.Y == originalPlacement.Y
            && placement.Rotated == originalPlacement.Rotated;
    }

    private void OnLayoutChanged()
    {
        if (IsDragging && !DragSourceIsUnchanged()) { CancelDrag(); }
        if (view != null) { view.RefreshItems(); }
    }

    private void OnContextChanged()
    {
        if (boundManager == null || !boundManager.IsManagementAllowed) { Close(); }
        else
        {
            // 从一个允许整理的场景切到另一个，也不能把旧场景的待提交操作带过去。
            if ((IsDragging || IsAwaitingAbandonConfirmation) && !boundManager.OwnsInteraction(this))
            {
                CancelDrag();
                CancelAbandon();
            }
            RefreshText();
        }
    }

    public void RefreshText()
    {
        if (view != null) { view.RefreshText(); }
    }

    public void SetFeedback(string key)
    {
        if (FeedbackKey == key) { return; }
        FeedbackKey = key;
        RefreshText();
    }

    private void EnableInput()
    {
        if (boundInputActions != inputActions) { ReleaseInput(); }
        if (!isActiveAndEnabled || rotateAction != null || inputActions == null) { return; }
        InputAction source = inputActions.FindAction("Player/RotateItem");
        if (source == null)
        {
            Debug.LogError("仓库缺少 Player/RotateItem 输入动作。", this);
            return;
        }
        boundInputActions = inputActions;
        rotateAction = source.Clone();
        rotateAction.bindingMask = InputBinding.MaskByGroup("Keyboard&Mouse");
        rotateAction.performed += OnRotate;
        rotateAction.Enable();
    }

    private void OnRotate(InputAction.CallbackContext context)
    {
        RotatePreview();
    }

    private void ReleaseInput()
    {
        boundInputActions = null;
        if (rotateAction == null) { return; }
        rotateAction.performed -= OnRotate;
        rotateAction.Dispose();
        rotateAction = null;
    }

    private void UnsubscribeManager()
    {
        if (boundManager != null)
        {
            boundManager.Changed -= OnLayoutChanged;
            boundManager.ContextChanged -= OnContextChanged;
        }
        boundManager = null;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) { CancelDrag(); CancelAbandon(); }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) { CancelDrag(); CancelAbandon(); }
    }

    private void OnDisable()
    {
        Close();
        ReleaseInput();
        UnsubscribeManager();
    }

    private void OnDestroy()
    {
        Close();
        ReleaseInput();
        UnsubscribeManager();
    }

    public static string ResultKey(WarehouseResult result)
    {
        switch (result)
        {
            case WarehouseResult.Success: return "warehouse.result.success";
            case WarehouseResult.NotReady: return "warehouse.result.not_ready";
            case WarehouseResult.InvalidConfiguration: return "warehouse.result.invalid_configuration";
            case WarehouseResult.NotFound: return "warehouse.result.not_found";
            case WarehouseResult.WrongOwner: return "warehouse.result.wrong_owner";
            case WarehouseResult.WrongContainer: return "warehouse.result.wrong_container";
            case WarehouseResult.MissingPlacement: return "warehouse.result.missing_placement";
            case WarehouseResult.DuplicateInstance: return "warehouse.result.duplicate_instance";
            case WarehouseResult.RotationNotAllowed: return "warehouse.result.rotation_not_allowed";
            case WarehouseResult.OutOfBounds: return "warehouse.result.out_of_bounds";
            case WarehouseResult.Overlap: return "warehouse.result.overlap";
            case WarehouseResult.NotInShelter: return "warehouse.result.not_in_shelter";
            case WarehouseResult.LossRejected: return "warehouse.result.loss_rejected";
            default: return "warehouse.result.busy";
        }
    }
}
