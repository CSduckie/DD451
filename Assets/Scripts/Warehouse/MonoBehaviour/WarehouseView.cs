using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class WarehouseView : MonoBehaviour
{
    [Header("只允许上下滚动；所有列适配可见宽度，gridRoot 原点在左上角")]
    public GameObject panelRoot;
    public ScrollRect scrollRect;
    public RectTransform viewport;
    public RectTransform gridRoot;
    public RectTransform itemRoot;
    public Image cellPrefab;
    public WarehouseItemView itemPrefab;
    public Image preview;
    [Tooltip("单格最大边长。实际显示会缩小以完整显示全部列，不改变逻辑占格。")]
    [Min(1f)] public float cellSize = 66f;
    [Min(0f)] public float gap = 4f;
    [Min(0f)] public float padding = 6f;

    [Header("仓库文字与按钮")]
    public TMP_Text titleText;
    public TMP_Text sizeText;
    public TMP_Text closeText;
    public TMP_Text hintText;
    public TMP_Text detailsText;
    public TMP_Text feedbackText;
    public TMP_Text abandonText;
    public TMP_Text cancelDragText;
    public Button closeButton;
    public Button abandonButton;
    public Button cancelDragButton;

    [Header("确认框只显示控制器保存的实例 ID")]
    public GameObject confirmPanel;
    public TMP_Text confirmTitleText;
    public TMP_Text confirmMessageText;
    public TMP_Text confirmButtonText;
    public TMP_Text cancelButtonText;
    public Button confirmButton;
    public Button cancelButton;

    private WarehouseUIController controller;
    private WarehouseManager manager;
    private LocalizationManager localization;
    private LocalizationManager subscribedLocalization;
    private readonly List<Image> cells = new List<Image>();
    private readonly Dictionary<string, WarehouseItemView> itemViews = new Dictionary<string, WarehouseItemView>();
    private readonly List<RaycastResult> pointerResults = new List<RaycastResult>();
    private PointerEventData pointerData;
    private EventSystem pointerEventSystem;
    private int drawnWidth;
    private int drawnHeight;
    private float drawnCellSize;
    private float drawnGap;
    private float drawnPadding;
    private float drawnViewportWidth;

    public float EffectiveCellSize
    {
        get
        {
            RectTransform visibleArea = GetViewport();
            if (visibleArea == null || manager == null || manager.Width <= 0) { return cellSize; }
            float available = visibleArea.rect.width - padding * 2f - (manager.Width - 1) * gap;
            return Mathf.Min(cellSize, Mathf.Max(0.01f, available / manager.Width));
        }
    }

    public void Bind(WarehouseUIController owner, WarehouseManager warehouse, LocalizationManager language)
    {
        bool bindingsChanged = controller != owner || manager != warehouse;
        controller = owner;
        manager = warehouse;
        localization = language;
        SubscribeLanguage();
        BindButtons();
        if (bindingsChanged) { HidePreview(); RefreshItems(); }
        else { RefreshText(); }
    }

    private void OnEnable()
    {
        LockHorizontalScrolling();
        SubscribeLanguage();
        BindButtons();
        RefreshText();
    }

    private void LateUpdate()
    {
        // 窗口或布局变化时才调整格子表现；稳定帧只比较尺寸，不重建格子。
        if (panelRoot != null && panelRoot.activeInHierarchy) { RefreshGeometry(); }
    }

    private void OnDisable()
    {
        UnsubscribeLanguage();
        if (controller != null && controller.IsOpen) { controller.Close(); }
    }

    private void OnDestroy()
    {
        UnsubscribeLanguage();
        RemoveButtonListeners();
    }

    private void SubscribeLanguage()
    {
        if (subscribedLocalization == localization) { return; }
        UnsubscribeLanguage();
        if (isActiveAndEnabled && localization != null)
        {
            subscribedLocalization = localization;
            subscribedLocalization.LanguageChanged += RefreshText;
        }
    }

    private void UnsubscribeLanguage()
    {
        if (subscribedLocalization != null) { subscribedLocalization.LanguageChanged -= RefreshText; }
        subscribedLocalization = null;
    }

    private void BindButtons()
    {
        RemoveButtonListeners();
        if (closeButton != null) { closeButton.onClick.AddListener(OnClose); }
        if (abandonButton != null) { abandonButton.onClick.AddListener(OnAbandon); }
        if (cancelDragButton != null) { cancelDragButton.onClick.AddListener(OnCancelDrag); }
        if (confirmButton != null) { confirmButton.onClick.AddListener(OnConfirm); }
        if (cancelButton != null) { cancelButton.onClick.AddListener(OnCancelConfirm); }
    }

    private void RemoveButtonListeners()
    {
        if (closeButton != null) { closeButton.onClick.RemoveListener(OnClose); }
        if (abandonButton != null) { abandonButton.onClick.RemoveListener(OnAbandon); }
        if (cancelDragButton != null) { cancelDragButton.onClick.RemoveListener(OnCancelDrag); }
        if (confirmButton != null) { confirmButton.onClick.RemoveListener(OnConfirm); }
        if (cancelButton != null) { cancelButton.onClick.RemoveListener(OnCancelConfirm); }
    }

    public void SetVisible(bool visible)
    {
        if (panelRoot != null) { panelRoot.SetActive(visible); }
        if (visible) { SubscribeLanguage(); RefreshItems(); }
        else { HidePreview(); }
    }

    public void RefreshItems()
    {
        if (manager == null || !manager.IsReady || gridRoot == null || itemRoot == null || itemPrefab == null)
        {
            ClearItemViews();
            RefreshText();
            return;
        }
        RefreshGeometry();
        IReadOnlyList<WarehousePlacement> placements = manager.GetPlacements();
        HashSet<string> visibleIds = new HashSet<string>();
        foreach (WarehousePlacement placement in placements)
        {
            visibleIds.Add(placement.InstanceId);
            if (!itemViews.TryGetValue(placement.InstanceId, out WarehouseItemView itemView) || itemView == null)
            {
                itemView = Instantiate(itemPrefab, itemRoot);
                itemView.gameObject.name = "WarehouseItem_" + placement.InstanceId;
                itemView.gameObject.SetActive(true);
                itemViews[placement.InstanceId] = itemView;
            }
            itemView.Bind(controller, placement.InstanceId);
            PlaceRect((RectTransform)itemView.transform, placement.X, placement.Y, placement.Width, placement.Height);
        }
        List<string> removedIds = new List<string>();
        foreach (KeyValuePair<string, WarehouseItemView> pair in itemViews)
        {
            if (!visibleIds.Contains(pair.Key))
            {
                RemoveItemView(pair.Value);
                removedIds.Add(pair.Key);
            }
        }
        foreach (string removedId in removedIds) { itemViews.Remove(removedId); }
        if (preview != null) { preview.transform.SetAsLastSibling(); }
        RefreshText();
    }

    private void ClearItemViews()
    {
        foreach (WarehouseItemView itemView in itemViews.Values) { RemoveItemView(itemView); }
        itemViews.Clear();
    }

    private static void RemoveItemView(WarehouseItemView itemView)
    {
        if (itemView == null) { return; }
        itemView.gameObject.SetActive(false);
        Destroy(itemView.gameObject);
    }

    private void RefreshGeometry()
    {
        LockHorizontalScrolling();
        if (manager == null || !manager.IsReady || gridRoot == null || itemRoot == null) { return; }
        RectTransform visibleArea = GetViewport();
        float viewportWidth = visibleArea != null ? visibleArea.rect.width : 0f;
        float actualCellSize = EffectiveCellSize;
        if (drawnWidth == manager.Width && drawnHeight == manager.Height && Mathf.Approximately(drawnCellSize, actualCellSize)
            && drawnGap == gap && drawnPadding == padding && Mathf.Approximately(drawnViewportWidth, viewportWidth)) { return; }
        float width = padding * 2f + manager.Width * actualCellSize + (manager.Width - 1) * gap;
        if (viewportWidth > 0f) { width = Mathf.Min(width, viewportWidth); }
        float height = padding * 2f + manager.Height * actualCellSize + (manager.Height - 1) * gap;
        gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        itemRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        itemRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        int neededCells = manager.Width * manager.Height;
        while (cells.Count > neededCells)
        {
            Image cell = cells[cells.Count - 1];
            if (cell != null) { cell.gameObject.SetActive(false); Destroy(cell.gameObject); }
            cells.RemoveAt(cells.Count - 1);
        }
        if (cellPrefab != null)
        {
            while (cells.Count < neededCells)
            {
                Image cell = Instantiate(cellPrefab, gridRoot);
                cell.gameObject.SetActive(true);
                cell.raycastTarget = false;
                cells.Add(cell);
            }
            // 参考 CardGame/UIGridController 的行列创建形式；改窗口只重排既有表现。
            for (int y = 0; y < manager.Height; y++)
            {
                for (int x = 0; x < manager.Width; x++)
                {
                    Image cell = cells[y * manager.Width + x];
                    cell.name = "Cell_" + x + "_" + y;
                    PlaceRect(cell.rectTransform, x, y, 1, 1);
                }
            }
        }
        // 宽度完整显示全部列，高度保持方格比例，只沿纵向查看远端行。
        itemRoot.SetAsLastSibling();
        bool firstDraw = drawnWidth == 0;
        drawnWidth = manager.Width;
        drawnHeight = manager.Height;
        drawnCellSize = actualCellSize;
        drawnGap = gap;
        drawnPadding = padding;
        drawnViewportWidth = viewportWidth;
        foreach (KeyValuePair<string, WarehouseItemView> pair in itemViews)
        {
            if (pair.Value != null && manager.TryGetPlacement(pair.Key, out WarehousePlacement placement))
            {
                PlaceRect((RectTransform)pair.Value.transform, placement.X, placement.Y, placement.Width, placement.Height);
            }
        }
        if (preview != null && preview.gameObject.activeSelf && controller != null && controller.IsDragging
            && manager.TryGetPlacement(controller.DragInstanceId, out WarehousePlacement dragged))
        {
            ShowPreview(dragged.Item, controller.PreviewX, controller.PreviewY, controller.PreviewRotated,
                controller.PreviewResult == WarehouseResult.Success);
        }
        if (firstDraw && scrollRect != null)
        {
            scrollRect.horizontalNormalizedPosition = 0f;
            scrollRect.verticalNormalizedPosition = 1f;
        }
        LockHorizontalScrolling();
    }

    private void PlaceRect(RectTransform rect, int x, int y, int width, int height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        float actualCellSize = EffectiveCellSize;
        rect.anchoredPosition = new Vector2(padding + x * (actualCellSize + gap), -padding - y * (actualCellSize + gap));
        rect.sizeDelta = new Vector2(width * actualCellSize + (width - 1) * gap, height * actualCellSize + (height - 1) * gap);
    }

    private RectTransform GetViewport()
    {
        return viewport != null ? viewport : scrollRect != null ? scrollRect.viewport : null;
    }

    private void LockHorizontalScrolling()
    {
        if (scrollRect != null)
        {
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.horizontalNormalizedPosition = 0f;
            Vector2 velocity = scrollRect.velocity;
            velocity.x = 0f;
            scrollRect.velocity = velocity;
            if (scrollRect.horizontalScrollbar != null) { scrollRect.horizontalScrollbar.gameObject.SetActive(false); }
        }
        if (gridRoot != null)
        {
            Vector2 position = gridRoot.anchoredPosition;
            position.x = 0f;
            gridRoot.anchoredPosition = position;
        }
    }

    public void RefreshText()
    {
        // 语言通知仅更新文字和按钮表现，不创建物品、不改 token、不重新执行确认。
        SetText(titleText, Text("warehouse.title"));
        SetText(closeText, Text("warehouse.close"));
        SetText(hintText, Text("warehouse.controls") + "\n" + Text("common.language.toggle_hint"));
        SetText(abandonText, Text("warehouse.abandon"));
        SetText(cancelDragText, Text("warehouse.cancel_drag"));
        SetText(feedbackText, Text(controller != null ? controller.FeedbackKey : "warehouse.result.not_ready"));
        SetText(sizeText, manager != null && manager.IsReady
            ? Format("warehouse.size", manager.Width, manager.Height, (long)manager.Width * manager.Height)
            : Text("warehouse.result.not_ready"));

        foreach (KeyValuePair<string, WarehouseItemView> pair in itemViews)
        {
            if (manager != null && pair.Value != null && manager.TryGetPlacement(pair.Key, out WarehousePlacement placement))
            {
                pair.Value.RefreshText(placement, localization);
                pair.Value.SetSelected(controller != null && controller.SelectedInstanceId == pair.Key, placement.Item.Definition);
            }
        }
        WarehousePlacement selected = null;
        if (controller != null && manager != null) { manager.TryGetPlacement(controller.SelectedInstanceId, out selected); }
        RefreshDetails(selected);
        bool canManage = controller != null && controller.IsOpen && manager != null && manager.IsManagementAllowed;
        if (abandonButton != null)
        {
            abandonButton.interactable = canManage && selected != null && !controller.IsAwaitingAbandonConfirmation;
        }
        if (cancelDragButton != null) { cancelDragButton.interactable = canManage && controller.IsDragging; }
        RefreshConfirmation(canManage);
    }

    private void RefreshDetails(WarehousePlacement selected)
    {
        if (selected == null || selected.Item == null || selected.Item.Definition == null)
        {
            SetText(detailsText, Text("warehouse.select_hint"));
            return;
        }
        ItemDataSO definition = selected.Item.Definition;
        StringBuilder details = new StringBuilder();
        details.Append("<b>").Append(Text(definition.nameKey)).Append("</b>  |  ");
        details.Append(Format("warehouse.dimensions", selected.Width, selected.Height)).Append("  |  ");
        details.Append(Format("warehouse.value", selected.Item.IsRetained ? definition.ev : 0)).Append("  |  ");
        details.Append(Text(definition.canRotate ? "warehouse.rotatable" : "warehouse.fixed_orientation")).Append('\n');
        details.Append(Text(definition.descriptionKey));
        if (definition.canUse) { details.Append('\n').Append(Text("warehouse.stored_use_hint")); }
        SetText(detailsText, details.ToString());
    }

    private void RefreshConfirmation(bool canManage)
    {
        bool visible = controller != null && controller.IsAwaitingAbandonConfirmation;
        if (confirmPanel != null) { confirmPanel.SetActive(visible); }
        SetText(confirmTitleText, Text("warehouse.confirm_title"));
        SetText(confirmButtonText, Text("warehouse.confirm"));
        SetText(cancelButtonText, Text("warehouse.cancel"));
        ItemInstance item = null;
        bool targetValid = visible && manager != null
            && manager.TryGetStoredItem(controller.PendingAbandonInstanceId, out item) == WarehouseResult.Success;
        SetText(confirmMessageText, targetValid ? Format("warehouse.confirm_body", Text(item.Definition.nameKey))
            : Text("warehouse.result.not_found"));
        if (confirmButton != null) { confirmButton.interactable = canManage && targetValid; }
    }

    public void ShowPreview(ItemInstance item, int x, int y, bool rotated, bool valid)
    {
        if (preview == null || itemRoot == null || item == null || item.Definition == null) { return; }
        preview.transform.SetParent(itemRoot, false);
        int width = rotated ? item.Definition.height : item.Definition.width;
        int height = rotated ? item.Definition.width : item.Definition.height;
        PlaceRect(preview.rectTransform, x, y, width, height);
        preview.raycastTarget = false;
        preview.color = valid ? new Color(0.40f, 0.93f, 0.70f, 0.55f) : new Color(1f, 0.34f, 0.34f, 0.58f);
        preview.gameObject.SetActive(true);
        preview.transform.SetAsLastSibling();
    }

    public void HidePreview()
    {
        if (preview != null) { preview.gameObject.SetActive(false); }
    }

    public bool TryScreenToCell(Vector2 screenPoint, Camera eventCamera, out Vector2Int cell)
    {
        RefreshGeometry();
        cell = new Vector2Int(-1, -1);
        RectTransform visibleArea = GetViewport();
        if (manager == null || !manager.IsReady || panelRoot == null || !panelRoot.activeInHierarchy
            || visibleArea == null || !RectTransformUtility.RectangleContainsScreenPoint(visibleArea, screenPoint, eventCamera)
            || !IsVisibleViewportTarget(screenPoint, visibleArea)
            || !TryLocalGridPoint(screenPoint, eventCamera, out Vector2 local)) { return false; }
        float actualCellSize = EffectiveCellSize;
        float pitch = actualCellSize + gap;
        int x = Mathf.FloorToInt(local.x / pitch);
        int y = Mathf.FloorToInt(local.y / pitch);
        if (x < 0 || y < 0 || x >= manager.Width || y >= manager.Height
            || local.x - x * pitch >= actualCellSize || local.y - y * pitch >= actualCellSize) { return false; }
        cell = new Vector2Int(x, y);
        return true;
    }

    private bool IsVisibleViewportTarget(Vector2 screenPoint, RectTransform visibleArea)
    {
        if (EventSystem.current == null) { return true; }
        if (pointerData == null || pointerEventSystem != EventSystem.current)
        {
            pointerEventSystem = EventSystem.current;
            pointerData = new PointerEventData(pointerEventSystem);
        }
        pointerData.Reset();
        pointerData.position = screenPoint;
        pointerResults.Clear();
        pointerEventSystem.RaycastAll(pointerData, pointerResults);
        if (pointerResults.Count == 0) { return false; }
        // 另一个窗口盖住仓库时不能透过它提交；空格命中 viewport，物品命中其子层。
        Transform hit = pointerResults[0].gameObject.transform;
        return hit == visibleArea || hit.IsChildOf(visibleArea);
    }

    public Vector2Int GetGrabOffset(Vector2 screenPoint, Camera eventCamera, WarehousePlacement placement)
    {
        RefreshGeometry();
        if (placement == null || !TryLocalGridPoint(screenPoint, eventCamera, out Vector2 local)) { return Vector2Int.zero; }
        // 物品图标跨过格子缝隙；按在缝隙上也应保留正确抓取位置。
        float pitch = EffectiveCellSize + gap;
        int x = Mathf.FloorToInt(local.x / pitch) - placement.X;
        int y = Mathf.FloorToInt(local.y / pitch) - placement.Y;
        return new Vector2Int(Mathf.Clamp(x, 0, placement.Width - 1), Mathf.Clamp(y, 0, placement.Height - 1));
    }

    private bool TryLocalGridPoint(Vector2 screenPoint, Camera eventCamera, out Vector2 point)
    {
        point = Vector2.zero;
        if (gridRoot == null || EffectiveCellSize <= 0f || gap < 0f
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(gridRoot, screenPoint, eventCamera, out Vector2 local)) { return false; }
        // 转换使用实际滚动后的 RectTransform；Canvas 缩放由 Unity 处理。
        point = new Vector2(local.x - gridRoot.rect.xMin - padding, gridRoot.rect.yMax - local.y - padding);
        return true;
    }

    private string Text(string key)
    {
        return localization != null ? localization.GetText(key) : "[" + key + "]";
    }

    private string Format(string key, params object[] arguments)
    {
        return localization != null ? localization.FormatText(key, arguments) : "[" + key + "]";
    }

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null) { target.text = value; }
    }

    private void OnClose() { if (controller != null) { controller.Close(); } }
    private void OnAbandon() { if (controller != null) { controller.RequestAbandonSelected(); } }
    private void OnCancelDrag() { if (controller != null) { controller.CancelDrag(); } }
    private void OnConfirm() { if (controller != null) { controller.ConfirmAbandon(); } }
    private void OnCancelConfirm() { if (controller != null) { controller.CancelAbandon(); } }
}
