using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 来源保留自己的token和R动作。另一界面只画占格，不再启动第二份拖拽。
public class ItemTransferUIBridge : MonoBehaviour
{
    public ItemTransferCoordinator coordinator;
    public BackpackUIController backpack;
    public WarehouseUIController warehouse;
    public LootUIController loot;
    public Button dropButton;
    public TMP_Text dropText;
    public GameObject discardPanel;
    public TMP_Text discardTitleText;
    public TMP_Text discardMessageText;
    public TMP_Text confirmDiscardText;
    public TMP_Text cancelDiscardText;
    public Button confirmDiscardButton;
    public Button cancelDiscardButton;

    public bool IsAwaitingDiscardConfirmation { get { return DiscardToken != 0; } }
    public int DiscardToken { get; private set; }
    public string PendingDiscardInstanceId { get; private set; }

    private BackpackUIController boundBackpack;
    private WarehouseUIController boundWarehouse;
    private LocalizationManager language;
    private LootSourceState lootSource;
    private string lootEntryId;
    private int lootSession;
    private int lootDragRevision;
    private LootSourceState returnSource;
    private int returnSession;
    private int returnDragToken;
    private string returnFeedbackKey;
    private int quickRevision;
    private bool submitting;
    private int nextDiscardToken;
    private ItemInstance discardItem;
    private BackpackPlacement discardPlacement;
    private BackpackUIController discardBackpack;
    private BackpackManager discardManager;
    private ItemManager discardItems;
    private PlayerController discardPlayer;
    private ItemTransferCoordinator discardCoordinator;
    private LootRuntimeManager discardRuntime;
    private string discardMapId;
    private int discardRound;
    private int discardSceneHandle;
    private GameObject activeDiscardPanel;
    private Button activeConfirmDiscardButton;
    private Button activeCancelDiscardButton;
    private UnityAction confirmDiscardListener;
    private UnityAction cancelDiscardListener;

    private void OnEnable() { Bind(); }
    private void Start() { Bind(); }

    public void Bind()
    {
        if (IsAwaitingDiscardConfirmation && !IsDiscardRequestValid(DiscardToken)) { ClearDiscardConfirmation(); }
        if (boundBackpack != backpack || boundWarehouse != warehouse)
        {
            Unbind();
            boundBackpack = backpack;
            boundWarehouse = warehouse;
            if (boundBackpack != null)
            {
                boundBackpack.CanBeginDrag = CanBeginBackpackDrag;
                boundBackpack.DragUpdated = OnBackpackDrag;
                boundBackpack.ExternalDropRequested = OnBackpackDrop;
                boundBackpack.QuickTransferRequested = OnBackpackQuickTransfer;
                boundBackpack.Closing += CancelAll;
                boundBackpack.DragCancelled += OnBackpackCancelled;
            }
            if (boundWarehouse != null)
            {
                boundWarehouse.CanBeginDrag = CanBeginWarehouseDrag;
                boundWarehouse.DragUpdated = OnWarehouseDrag;
                boundWarehouse.ExternalDropRequested = OnWarehouseDrop;
                boundWarehouse.QuickTransferRequested = OnWarehouseQuickTransfer;
                boundWarehouse.Closing += CancelAll;
                boundWarehouse.DragCancelled += OnWarehouseCancelled;
            }
        }
        LocalizationManager current = LocalizationManager.Instance;
        if (language != current)
        {
            if (language != null) { language.LanguageChanged -= RefreshText; }
            language = current;
            if (language != null) { language.LanguageChanged += RefreshText; }
        }
        if (dropButton != null)
        {
            dropButton.onClick.RemoveListener(DropSelected);
            dropButton.onClick.AddListener(DropSelected);
        }
        if (!IsAwaitingDiscardConfirmation && discardPanel != null) { discardPanel.SetActive(false); }
        RefreshText();
    }

    private void LateUpdate()
    {
        if (IsAwaitingDiscardConfirmation && !IsDiscardRequestValid(DiscardToken))
        {
            ClearDiscardConfirmation();
            if (backpack != null && backpack.IsOpen) { ShowFeedback("loot.discard.invalid"); }
        }
        if (dropButton != null)
        {
            dropButton.interactable = !submitting && !IsAwaitingDiscardConfirmation && CanRequestDiscard();
        }
    }

    private bool CanBeginBackpackDrag()
    {
        return !submitting && !IsAwaitingDiscardConfirmation
            && (warehouse == null || (!warehouse.IsDragging && !warehouse.IsAwaitingAbandonConfirmation));
    }

    private bool CanBeginWarehouseDrag()
    {
        return !submitting && !IsAwaitingDiscardConfirmation && (backpack == null || !backpack.IsDragging);
    }

    private bool QuickContextIsValid()
    {
        return isActiveAndEnabled && backpack != null && backpack == boundBackpack
            && backpack.isActiveAndEnabled && backpack.IsOpen && !backpack.IsDragging
            && backpack.manager != null && backpack.manager.isActiveAndEnabled && backpack.manager.IsReady
            && !IsAwaitingDiscardConfirmation
            && (backpack.player == null ? backpack.shelterContext
                : backpack.player.isActiveAndEnabled && !backpack.player.IsDead
                    && !backpack.player.IsSceneTransitionBlocked && backpack.player.IsBackpackOpen)
            && (warehouse == null || (!warehouse.IsDragging && !warehouse.IsAwaitingAbandonConfirmation
                && (warehouse.manager == null || !warehouse.manager.HasPendingInteraction
                    || warehouse.manager.OwnsInteraction(this))))
            && gameObject.scene.IsValid() && gameObject.scene.isLoaded;
    }

    private bool PrepareQuickTransfer()
    {
        if (submitting || !QuickContextIsValid()) { ShowResult(ItemTransferResult.Busy); return false; }
        if (coordinator == null || !coordinator.Initialize() || coordinator.backpackManager != backpack.manager
            || coordinator.itemManager != backpack.manager.Items)
        {
            ShowResult(ItemTransferResult.NotReady);
            return false;
        }
        coordinator.player = backpack.player;
        return true;
    }

    private bool HasQuickWarehouseTarget()
    {
        return warehouse != null && warehouse == boundWarehouse && warehouse.isActiveAndEnabled && warehouse.IsOpen
            && warehouse.manager != null && warehouse.manager == coordinator.warehouseManager
            && warehouse.manager.IsManagementAllowed && warehouse.manager.Items == backpack.manager.Items;
    }

    private bool HasQuickBoxTarget()
    {
        return loot != null && loot.Source != null && !loot.Source.IsGround
            && loot.runtime != null && loot.runtime == coordinator.lootRuntime && backpack.player != null
            && loot.IsSessionValid(loot.SessionToken, loot.Source)
            && loot.runtime.TryValidateReturnTarget(loot.Source, backpack.player.interaction);
    }

    private bool QuickRequestIsValid(int revision, int sceneHandle)
    {
        return revision == quickRevision && sceneHandle == SceneManager.GetActiveScene().handle
            && QuickContextIsValid() && coordinator != null && coordinator.isActiveAndEnabled
            && coordinator.backpackManager == backpack.manager && coordinator.itemManager == backpack.manager.Items
            && coordinator.player == backpack.player;
    }

    private bool OnBackpackQuickTransfer(string instanceId)
    {
        if (!PrepareQuickTransfer()) { return false; }
        bool toWarehouse = HasQuickWarehouseTarget();
        bool toBox = HasQuickBoxTarget();
        if (toWarehouse == toBox)
        {
            ShowFeedback(toWarehouse ? "loot.quick_ambiguous" : "loot.quick_no_target");
            return false;
        }
        if (!backpack.manager.TryGetPlacement(instanceId, out BackpackPlacement original))
        {
            ShowResult(ItemTransferResult.SourceInvalid);
            return false;
        }
        int revision = quickRevision;
        int sceneHandle = SceneManager.GetActiveScene().handle;
        ItemTransferResult result;
        submitting = true;
        try
        {
            if (toWarehouse)
            {
                WarehouseManager target = warehouse.manager;
                result = ItemTransferCoordinator.FromWarehouse(target.TryFindSpaceForExternal(instanceId,
                    original.Rotated, out Vector2Int cell, out bool rotated));
                if (result == ItemTransferResult.Success)
                {
                    result = coordinator.TryBackpackToWarehouse(instanceId, cell.x, cell.y, rotated, this,
                        () => QuickRequestIsValid(revision, sceneHandle) && HasQuickWarehouseTarget()
                            && warehouse.manager == target && !HasQuickBoxTarget());
                }
            }
            else
            {
                LootSourceState source = loot.Source;
                int session = loot.SessionToken;
                result = loot.runtime.TryFindReturnedItemPosition(source, original.Item, original.Rotated,
                    out RectInt position, out bool rotated) ? ItemTransferResult.Success : ItemTransferResult.Occupied;
                if (result == ItemTransferResult.Success)
                {
                    result = coordinator.TryBackpackToSource(source, instanceId, position.x, position.y, rotated,
                        () => QuickRequestIsValid(revision, sceneHandle) && !HasQuickWarehouseTarget()
                            && HasQuickBoxTarget() && loot.IsSessionValid(session, source));
                }
            }
            ShowResult(result);
            return result == ItemTransferResult.Success;
        }
        finally { submitting = false; }
    }

    private bool OnWarehouseQuickTransfer(string instanceId)
    {
        if (!PrepareQuickTransfer()) { return false; }
        if (!HasQuickWarehouseTarget() || !warehouse.manager.TryGetPlacement(instanceId, out WarehousePlacement original))
        {
            ShowResult(ItemTransferResult.SourceInvalid);
            return false;
        }
        WarehouseManager source = warehouse.manager;
        int revision = quickRevision;
        int sceneHandle = SceneManager.GetActiveScene().handle;
        submitting = true;
        try
        {
            ItemTransferResult result = ItemTransferCoordinator.FromBackpack(backpack.manager.TryFindSpaceForExternal(
                instanceId, original.Rotated, out Vector2Int cell, out bool rotated));
            if (result == ItemTransferResult.Success)
            {
                result = coordinator.TryWarehouseToBackpack(instanceId, cell.x, cell.y, rotated, this,
                    () => QuickRequestIsValid(revision, sceneHandle) && HasQuickWarehouseTarget() && warehouse.manager == source);
            }
            ShowResult(result);
            return result == ItemTransferResult.Success;
        }
        finally { submitting = false; }
    }

    public bool TryQuickSourceToBackpack(LootSourceState source, string entryId, int session)
    {
        if (!PrepareQuickTransfer()) { return false; }
        if (loot == null || !loot.IsSessionValid(session, source)) { ShowResult(ItemTransferResult.StaleRequest); return false; }
        if (!source.TryGetEntry(entryId, out LootEntryState entry) || entry.Taken)
        {
            ShowResult(ItemTransferResult.SourceInvalid);
            return false;
        }
        if (!entry.Revealed) { ShowResult(ItemTransferResult.SourceHidden); return false; }
        int revision = quickRevision;
        int sceneHandle = SceneManager.GetActiveScene().handle;
        submitting = true;
        try
        {
            ItemTransferResult result = ItemTransferCoordinator.FromBackpack(backpack.manager.TryFindSpaceForExternal(
                entry.InstanceId, entry.LayoutRotated, out Vector2Int cell, out bool rotated));
            if (result == ItemTransferResult.Success)
            {
                result = coordinator.TrySourceToBackpack(source, entryId, cell.x, cell.y, rotated,
                    () => QuickRequestIsValid(revision, sceneHandle) && loot != null && loot.IsSessionValid(session, source));
            }
            ShowResult(result);
            return result == ItemTransferResult.Success;
        }
        finally { submitting = false; }
    }

    public int BeginLootDrag(LootSourceState source, string entryId, int session, Vector2 position,
        Camera eventCamera, Vector2Int grabOffset, bool rotated)
    {
        if (backpack == null || backpack.IsDragging || loot == null || !loot.IsSessionValid(session, source) || !CanBeginBackpackDrag()
            || !source.TryGetEntry(entryId, out LootEntryState entry) || !entry.Revealed || entry.Taken
            || rotated != entry.LayoutRotated) { return 0; }
        BackpackUIController dragBackpack = backpack;
        int revision = ++lootDragRevision;
        lootSource = source;
        lootEntryId = entryId;
        lootSession = session;
        int token = 0;
        bool started = false;
        try
        {
            if (!dragBackpack.BeginExternalPreview(entry.InstanceId, rotated, grabOffset)) { return 0; }
            token = dragBackpack.DragToken;
            if (revision != lootDragRevision || backpack != dragBackpack
                || loot == null || !loot.IsSessionValid(session, source)) { return 0; }
            dragBackpack.UpdateDrag(token, position, eventCamera);
            started = revision == lootDragRevision && backpack == dragBackpack && dragBackpack.IsDragValid(token)
                && loot != null && loot.IsSessionValid(session, source);
            return started ? token : 0;
        }
        finally
        {
            // 首次更新可能同步取消并重启；旧失败只清自己的记录，不能擦掉新拖拽。
            if (!started && revision == lootDragRevision)
            {
                if (token != 0) { dragBackpack.CancelDrag(token); }
                if (revision == lootDragRevision) { ClearLootDrag(); }
            }
        }
    }

    public void UpdateLootDrag(int token, Vector2 position, Camera eventCamera)
    {
        if (lootSource != null && backpack != null) { backpack.UpdateDrag(token, position, eventCamera); }
    }

    public void EndLootDrag(int token, Vector2 position, Camera eventCamera)
    {
        if (lootSource != null && backpack != null) { backpack.EndDrag(token, position, eventCamera); }
    }

    public void CancelLootDrag()
    {
        quickRevision++;
        if (lootSource != null && backpack != null && backpack.IsExternalPreview) { backpack.CancelDrag(); }
        // 关闭来源也撤销从背包放回的那次拖拽，迟到松手不能借用重开的会话。
        if (returnDragToken != 0 && backpack != null) { backpack.CancelDrag(returnDragToken); }
        ClearLootDrag();
        ClearReturnDrag();
        HideLootReturnPreview();
    }

    private void OnBackpackDrag(int token, Vector2 position, Camera eventCamera)
    {
        if (!backpack.IsExternalPreview && loot != null && loot.view != null
            && loot.view.TryHitReturnViewport(position, eventCamera))
        {
            if (returnDragToken != token)
            {
                returnDragToken = token;
                returnSource = loot.Source;
                returnSession = loot.SessionToken;
            }
            ReleaseWarehouseClaim();
            if (warehouse != null && warehouse.view != null && !warehouse.IsDragging) { warehouse.view.HidePreview(); }
            // 释放05占用可能同步关闭界面；返回后不能重新画出已经取消的目标。
            if (!backpack.IsDragValid(token) || loot == null || loot.view == null
                || !loot.view.TryHitReturnViewport(position, eventCamera))
            {
                HideLootReturnPreview();
                return;
            }
            bool valid = loot.IsSessionValid(returnSession, returnSource)
                && loot.runtime != null && backpack.player != null
                && loot.runtime.TryValidateReturnTarget(returnSource, backpack.player.interaction);
            ItemTransferResult placementResult = ItemTransferResult.OutOfBounds;
            loot.view.HideReturnPreview();
            if (valid && loot.view.TryScreenToCell(position, eventCamera, out Vector2Int boxCell)
                && backpack.manager != null && backpack.manager.Items != null
                && backpack.manager.Items.TryGetInstance(backpack.DragInstanceId, out ItemInstance returnedItem))
            {
                Vector2Int destination = boxCell - backpack.DragGrabOffset;
                bool rotated = backpack.PreviewRotated;
                placementResult = loot.runtime.CanPlaceReturnedItem(returnSource, returnedItem,
                    destination.x, destination.y, rotated);
                int width = rotated ? returnedItem.Definition.height : returnedItem.Definition.width;
                int height = rotated ? returnedItem.Definition.width : returnedItem.Definition.height;
                if (placementResult == ItemTransferResult.Success || placementResult == ItemTransferResult.Occupied)
                {
                    loot.view.ShowReturnPreview(new RectInt(destination.x, destination.y, width, height),
                        placementResult == ItemTransferResult.Success);
                }
            }
            returnFeedbackKey = !valid ? "loot.return_unavailable" : placementResult == ItemTransferResult.Success
                ? "loot.return_ready" : ItemTransferCoordinator.ResultKey(placementResult);
            backpack.SetFeedback(returnFeedbackKey);
            loot.SetFeedback(returnFeedbackKey);
            return;
        }
        HideLootReturnPreview();
        if (backpack.IsExternalPreview || warehouse == null || !warehouse.IsOpen || warehouse.view == null
            || !warehouse.view.TryScreenToCell(position, eventCamera, out Vector2Int cell))
        {
            ReleaseWarehouseClaim();
            if (warehouse != null && warehouse.view != null && !warehouse.IsDragging) { warehouse.view.HidePreview(); }
            return;
        }
        if (!backpack.manager.Items.TryGetInstance(backpack.DragInstanceId, out ItemInstance item)) { return; }
        WarehouseUIController targetUI = warehouse;
        WarehouseManager targetManager = targetUI.manager;
        bool claimed = targetManager != null && targetManager.TryClaimInteraction(this);
        // 申请占用会同步通知UI，期间可能已关包。不能在回调返回后补回过期预览或遗留占用。
        if (backpack == null || !backpack.IsDragValid(token) || targetUI == null
            || warehouse != targetUI || !targetUI.IsOpen || targetUI.manager != targetManager)
        {
            if (targetManager != null) { targetManager.ReleaseInteraction(this); }
            if (targetUI != null && targetUI.view != null) { targetUI.view.HidePreview(); }
            return;
        }
        Vector2Int target = cell - backpack.DragGrabOffset;
        WarehouseResult result = claimed
            ? targetManager.CanPlaceExternal(item.InstanceId, target.x, target.y, backpack.PreviewRotated)
            : WarehouseResult.Busy;
        warehouse.view.ShowPreview(item, target.x, target.y, backpack.PreviewRotated, result == WarehouseResult.Success);
        warehouse.SetFeedback(WarehouseUIController.ResultKey(result));
    }

    private void OnWarehouseDrag(int token, Vector2 position, Camera eventCamera)
    {
        if (backpack == null || !backpack.IsOpen || backpack.view == null) { return; }
        if (!warehouse.IsDragValid(token) || !warehouse.manager.Items.TryGetInstance(warehouse.DragInstanceId, out ItemInstance item)) { return; }
        // 仓库继续拥有唯一拖拽/R输入；04只提供共用的纯显示影像。
        backpack.view.BeginDragImage(warehouse, token, item, warehouse.view.GetItemRect(item.InstanceId),
            warehouse.DragPressPosition, eventCamera, warehouse.PreviewRotated, warehouse.DragGrabOffset,
            warehouse.view.EffectiveCellSize, warehouse.view.gap);
        backpack.view.UpdateDragImage(warehouse, token, position, eventCamera, warehouse.PreviewRotated, warehouse.DragGrabOffset);
        if (!backpack.view.TryScreenToCell(position, eventCamera, out Vector2Int cell))
        {
            if (backpack != null && backpack.view != null && !backpack.IsDragging) { backpack.view.HidePreview(); }
            return;
        }
        Vector2Int target = cell - warehouse.DragGrabOffset;
        BackpackResult result = backpack.manager.CanPlaceExternal(item.InstanceId, target.x, target.y, warehouse.PreviewRotated);
        backpack.view.ShowPreview(item, target.x, target.y, warehouse.PreviewRotated, result == BackpackResult.Success);
        backpack.SetFeedback(BackpackUIController.ResultKey(result));
    }

    private bool OnBackpackDrop(int token, Vector2 position, Camera eventCamera)
    {
        if (submitting || coordinator == null) { return false; }
        coordinator.player = backpack.player;
        if (backpack.IsExternalPreview)
        {
            LootSourceState source = lootSource;
            string entryId = lootEntryId;
            int session = lootSession;
            ItemTransferResult result = ItemTransferResult.StaleRequest;
            submitting = true;
            try
            {
                if (source != null && loot != null && backpack.view.TryScreenToCell(position, eventCamera, out Vector2Int cell))
                {
                    Vector2Int target = cell - backpack.DragGrabOffset;
                    result = coordinator.TrySourceToBackpack(source, entryId, target.x, target.y, backpack.PreviewRotated,
                        () => backpack.IsDragValid(token) && loot.IsSessionValid(session, source));
                }
                else if (source != null) { result = ItemTransferResult.OutOfBounds; }
            }
            finally { submitting = false; }
            ShowResult(result);
            return true;
        }
        if (loot != null && loot.view != null && loot.view.TryHitReturnViewport(position, eventCamera))
        {
            LootSourceState source = returnSource;
            int session = returnSession;
            submitting = true;
            try
            {
                ItemTransferResult result = ItemTransferResult.OutOfBounds;
                if (loot.view.TryScreenToCell(position, eventCamera, out Vector2Int cell))
                {
                    Vector2Int boxDestination = cell - backpack.DragGrabOffset;
                    result = coordinator.TryBackpackToSource(source, backpack.DragInstanceId,
                        boxDestination.x, boxDestination.y, backpack.PreviewRotated,
                        () => returnDragToken == token && backpack.IsDragValid(token) && loot.IsSessionValid(session, source));
                }
                ShowResult(result);
            }
            finally { submitting = false; }
            return true;
        }
        if (warehouse == null || !warehouse.IsOpen || warehouse.view == null
            || !warehouse.view.TryScreenToCell(position, eventCamera, out Vector2Int warehouseCell)) { return false; }
        Vector2Int destination = warehouseCell - backpack.DragGrabOffset;
        submitting = true;
        try
        {
            ItemTransferResult result = coordinator.TryBackpackToWarehouse(backpack.DragInstanceId,
                destination.x, destination.y, backpack.PreviewRotated, this,
                () => backpack.IsDragValid(token) && warehouse.IsOpen);
            ShowResult(result);
        }
        finally { submitting = false; }
        return true;
    }

    private bool OnWarehouseDrop(int token, Vector2 position, Camera eventCamera)
    {
        if (submitting || coordinator == null || backpack == null || !backpack.IsOpen || backpack.view == null
            || !backpack.view.TryScreenToCell(position, eventCamera, out Vector2Int cell)) { return false; }
        Vector2Int destination = cell - warehouse.DragGrabOffset;
        coordinator.player = backpack.player;
        submitting = true;
        try
        {
            ItemTransferResult result = coordinator.TryWarehouseToBackpack(warehouse.DragInstanceId,
                destination.x, destination.y, warehouse.PreviewRotated, warehouse,
                () => warehouse.IsDragValid(token) && backpack.IsOpen);
            ShowResult(result);
        }
        finally { submitting = false; }
        return true;
    }

    public void DropSelected()
    {
        if (submitting || IsAwaitingDiscardConfirmation || coordinator == null || !coordinator.Initialize()
            || !CanRequestDiscard()) { return; }
        // 确认界面未接好时只能拒绝，不能把一次请求当成玩家已确认。
        if (!HasDiscardUI() || nextDiscardToken == int.MaxValue)
        {
            ShowFeedback("loot.discard.unavailable");
            return;
        }
        string id = backpack.SelectedInstanceId;
        if (backpack.manager.TryGetCarriedItem(id, out ItemInstance item) != BackpackResult.Success
            || !backpack.manager.TryGetPlacement(id, out BackpackPlacement placement)) { return; }
        discardItem = item;
        discardPlacement = placement;
        discardBackpack = backpack;
        discardManager = backpack.manager;
        discardItems = discardManager.Items;
        discardPlayer = backpack.player;
        discardCoordinator = coordinator;
        discardCoordinator.player = discardPlayer;
        discardRuntime = coordinator.lootRuntime;
        discardMapId = discardRuntime != null ? discardRuntime.ActiveMapId : null;
        discardRound = discardItems.CurrentRound;
        discardSceneHandle = SceneManager.GetActiveScene().handle;
        PendingDiscardInstanceId = id;
        DiscardToken = ++nextDiscardToken;
        activeDiscardPanel = discardPanel;
        activeConfirmDiscardButton = confirmDiscardButton;
        activeCancelDiscardButton = cancelDiscardButton;
        // 每次按钮监听都捕获本次编号。上一确认的迟到回调不能操作下一确认。
        int token = DiscardToken;
        confirmDiscardListener = () => ConfirmDiscard(token);
        cancelDiscardListener = () => CancelDiscard(token);
        activeConfirmDiscardButton.onClick.AddListener(confirmDiscardListener);
        activeCancelDiscardButton.onClick.AddListener(cancelDiscardListener);
        RefreshText();
        activeDiscardPanel.SetActive(true);
        activeDiscardPanel.transform.SetAsLastSibling();
    }

    public void ConfirmDiscard(int token)
    {
        if (submitting || token == 0 || token != DiscardToken) { return; }
        if (!IsDiscardRequestValid(token))
        {
            ClearDiscardConfirmation();
            ShowFeedback("loot.discard.invalid");
            return;
        }
        string id = PendingDiscardInstanceId;
        bool unique = !string.IsNullOrEmpty(discardItem.UniqueId);
        ItemTransferCoordinator target = discardCoordinator;
        target.player = discardPlayer;
        submitting = true;
        try
        {
            ItemTransferResult result = target.TryDiscardFromBackpack(id, () => IsDiscardRequestValid(token));
            ClearDiscardConfirmation();
            if (result == ItemTransferResult.Success)
            {
                ShowFeedback(unique ? "loot.discard.pending" : "loot.discard.destroyed");
            }
            else { ShowResult(result); }
        }
        finally
        {
            ClearDiscardConfirmation();
            submitting = false;
        }
    }

    public void CancelDiscard(int token)
    {
        if (submitting || token == 0 || token != DiscardToken) { return; }
        ClearDiscardConfirmation();
        ShowFeedback("loot.discard.cancelled");
    }

    private bool CanRequestDiscard()
    {
        return isActiveAndEnabled && coordinator != null && coordinator.isActiveAndEnabled
            && backpack != null && backpack == boundBackpack && backpack.isActiveAndEnabled
            && backpack.IsOpen && !backpack.shelterContext && !backpack.IsDragging
            && backpack.player != null && backpack.player.isActiveAndEnabled && backpack.player.IsPrepared
            && !backpack.player.IsDead && backpack.player.IsBackpackOpen
            && (warehouse == null || (!warehouse.IsDragging && !warehouse.IsAwaitingAbandonConfirmation))
            && (coordinator.lootRuntime == null || (coordinator.lootRuntime.isActiveAndEnabled
                && coordinator.lootRuntime.IsExplorationActive))
            && (coordinator.warehouseManager == null || !coordinator.warehouseManager.IsManagementAllowed)
            && backpack.manager != null && backpack.manager.isActiveAndEnabled
            && backpack.manager == coordinator.backpackManager && backpack.manager.Items != null
            && backpack.manager.Items == coordinator.itemManager && coordinator.itemManager.isActiveAndEnabled
            && backpack.manager.TryGetCarriedItem(backpack.SelectedInstanceId, out _) == BackpackResult.Success;
    }

    private bool HasDiscardUI()
    {
        return discardPanel != null && discardTitleText != null && discardMessageText != null
            && confirmDiscardText != null && cancelDiscardText != null
            && confirmDiscardButton != null && cancelDiscardButton != null;
    }

    private bool IsDiscardRequestValid(int token)
    {
        if (token == 0 || token != DiscardToken || !CanRequestDiscard() || !HasDiscardUI()
            || discardBackpack != backpack || discardCoordinator != coordinator || discardPlayer != backpack.player
            || discardCoordinator.player != discardPlayer
            || discardManager != backpack.manager || discardItems != discardManager.Items
            || discardItems.CurrentRound != discardRound || discardRuntime != coordinator.lootRuntime
            || (discardRuntime != null && discardRuntime.ActiveMapId != discardMapId)
            || SceneManager.GetActiveScene().handle != discardSceneHandle
            || PendingDiscardInstanceId != backpack.SelectedInstanceId
            || activeDiscardPanel != discardPanel || !activeDiscardPanel.activeInHierarchy
            || activeConfirmDiscardButton != confirmDiscardButton || activeCancelDiscardButton != cancelDiscardButton
            || discardItem == null || discardItem.Definition == null || discardPlacement == null)
        {
            return false;
        }
        return discardManager.TryGetCarriedItem(PendingDiscardInstanceId, out ItemInstance current) == BackpackResult.Success
            && ReferenceEquals(current, discardItem)
            && discardManager.TryGetPlacement(PendingDiscardInstanceId, out BackpackPlacement placement)
            && placement.X == discardPlacement.X && placement.Y == discardPlacement.Y
            && placement.Rotated == discardPlacement.Rotated;
    }

    private void ClearDiscardConfirmation()
    {
        // 先撤销资格再隐藏，隐藏产生的生命周期回调也不能复用本次确认。
        DiscardToken = 0;
        PendingDiscardInstanceId = null;
        if (activeConfirmDiscardButton != null && confirmDiscardListener != null)
        {
            activeConfirmDiscardButton.onClick.RemoveListener(confirmDiscardListener);
        }
        if (activeCancelDiscardButton != null && cancelDiscardListener != null)
        {
            activeCancelDiscardButton.onClick.RemoveListener(cancelDiscardListener);
        }
        if (activeDiscardPanel != null) { activeDiscardPanel.SetActive(false); }
        activeDiscardPanel = null;
        activeConfirmDiscardButton = null;
        activeCancelDiscardButton = null;
        confirmDiscardListener = null;
        cancelDiscardListener = null;
        discardItem = null;
        discardPlacement = null;
        discardBackpack = null;
        discardManager = null;
        discardItems = null;
        discardPlayer = null;
        discardCoordinator = null;
        discardRuntime = null;
        discardMapId = null;
        discardRound = 0;
        discardSceneHandle = 0;
    }

    private void ShowFeedback(string key)
    {
        if (backpack != null) { backpack.SetFeedback(key); }
        if (warehouse != null) { warehouse.SetFeedback(key); }
        if (loot != null) { loot.SetFeedback(key); }
    }

    private void ShowResult(ItemTransferResult result)
    {
        // 先结束预览提示，再写提交结果；同步取消和随后EndDrag的清理不能覆盖结果。
        HideLootReturnPreview();
        ShowFeedback(ItemTransferCoordinator.ResultKey(result));
    }

    public void CancelAll()
    {
        quickRevision++;
        ClearDiscardConfirmation();
        if (backpack != null) { backpack.CancelDrag(); }
        if (warehouse != null) { warehouse.CancelDrag(); }
        ReleaseWarehouseClaim();
        ClearLootDrag();
        ClearReturnDrag();
        HideLootReturnPreview();
    }

    private void OnBackpackCancelled()
    {
        ReleaseWarehouseClaim();
        ClearLootDrag();
        ClearReturnDrag();
        HideLootReturnPreview();
        if (warehouse != null && warehouse.view != null && !warehouse.IsDragging) { warehouse.view.HidePreview(); }
    }

    private void OnWarehouseCancelled()
    {
        BackpackView imageView = boundBackpack != null ? boundBackpack.view : null;
        BackpackDragImage image = imageView != null ? imageView.DragImage : null;
        if (image != null && image.Owner is WarehouseUIController owner
            && (!owner.IsDragging || owner.DragToken != image.Token)) { imageView.EndDragImage(owner, image.Token); }
        if (backpack != null && backpack.view != null && !backpack.IsDragging) { backpack.view.HidePreview(); }
    }

    private void HideLootReturnPreview()
    {
        if (loot != null && loot.view != null) { loot.view.HideReturnPreview(); }
        string key = returnFeedbackKey;
        returnFeedbackKey = null;
        if (string.IsNullOrEmpty(key)) { return; }
        // UpdateDrag已写入新目标提示时保留它；只清理由本预览留下的文字。
        if (backpack != null && backpack.FeedbackKey == key)
        {
            backpack.SetFeedback(backpack.IsDragging ? "backpack.outside" : "backpack.cancelled");
        }
        if (loot != null && loot.FeedbackKey == key)
        {
            loot.SetFeedback(loot.IsOpen ? (loot.Source.IsGround ? "loot.drag_hint" : "loot.return_hint") : "loot.closed");
        }
    }

    private void ClearReturnDrag()
    {
        returnSource = null;
        returnSession = 0;
        returnDragToken = 0;
    }

    private void ReleaseWarehouseClaim()
    {
        WarehouseUIController owner = boundWarehouse != null ? boundWarehouse : warehouse;
        // 直接核对05保存的占用；领取通知尚未返回时，本地bool还来不及记录结果。
        if (owner != null && owner.manager != null) { owner.manager.ReleaseInteraction(this); }
    }

    private void ClearLootDrag()
    {
        lootDragRevision++;
        lootSource = null;
        lootEntryId = null;
        lootSession = 0;
    }

    private void RefreshText()
    {
        if (dropText != null) { dropText.text = Text("loot.discard"); }
        if (discardTitleText != null) { discardTitleText.text = Text("loot.discard.title"); }
        if (confirmDiscardText != null) { confirmDiscardText.text = Text("loot.discard.confirm"); }
        if (cancelDiscardText != null) { cancelDiscardText.text = Text("loot.discard.cancel"); }
        if (discardMessageText != null)
        {
            if (IsAwaitingDiscardConfirmation && discardItem != null && discardItem.Definition != null)
            {
                string key = string.IsNullOrEmpty(discardItem.UniqueId) ? "loot.discard.ordinary" : "loot.discard.unique";
                string itemName = Text(discardItem.Definition.nameKey);
                discardMessageText.text = language != null ? language.FormatText(key, itemName) : "[" + key + "]";
            }
            else { discardMessageText.text = string.Empty; }
        }
    }

    private string Text(string key)
    {
        return language != null ? language.GetText(key) : "[" + key + "]";
    }

    private void Unbind()
    {
        quickRevision++;
        ClearDiscardConfirmation();
        if (boundBackpack != null) { boundBackpack.CancelDrag(); }
        if (boundWarehouse != null) { boundWarehouse.CancelDrag(); }
        ReleaseWarehouseClaim();
        ClearLootDrag();
        ClearReturnDrag();
        HideLootReturnPreview();
        if (boundBackpack != null)
        {
            if (boundBackpack.CanBeginDrag == CanBeginBackpackDrag) { boundBackpack.CanBeginDrag = null; }
            if (boundBackpack.DragUpdated == OnBackpackDrag) { boundBackpack.DragUpdated = null; }
            if (boundBackpack.ExternalDropRequested == OnBackpackDrop) { boundBackpack.ExternalDropRequested = null; }
            if (boundBackpack.QuickTransferRequested == OnBackpackQuickTransfer) { boundBackpack.QuickTransferRequested = null; }
            boundBackpack.Closing -= CancelAll;
            boundBackpack.DragCancelled -= OnBackpackCancelled;
        }
        if (boundWarehouse != null)
        {
            if (boundWarehouse.CanBeginDrag == CanBeginWarehouseDrag) { boundWarehouse.CanBeginDrag = null; }
            if (boundWarehouse.DragUpdated == OnWarehouseDrag) { boundWarehouse.DragUpdated = null; }
            if (boundWarehouse.ExternalDropRequested == OnWarehouseDrop) { boundWarehouse.ExternalDropRequested = null; }
            if (boundWarehouse.QuickTransferRequested == OnWarehouseQuickTransfer) { boundWarehouse.QuickTransferRequested = null; }
            boundWarehouse.Closing -= CancelAll;
            boundWarehouse.DragCancelled -= OnWarehouseCancelled;
        }
        boundBackpack = null;
        boundWarehouse = null;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) { ClearDiscardConfirmation(); }
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) { ClearDiscardConfirmation(); }
    }

    private void OnDisable()
    {
        Unbind();
        if (language != null) { language.LanguageChanged -= RefreshText; }
        language = null;
        if (dropButton != null) { dropButton.onClick.RemoveListener(DropSelected); }
    }
}
