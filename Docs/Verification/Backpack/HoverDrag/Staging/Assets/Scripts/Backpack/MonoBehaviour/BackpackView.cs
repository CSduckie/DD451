using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BackpackView : MonoBehaviour
{
    [Header("面板与格子（左上角为原点）")]
    public GameObject panelRoot;
    public RectTransform gridRoot;
    public RectTransform itemRoot;
    public Image cellPrefab;
    public BackpackItemView itemPrefab;
    public Image preview;
    [Min(1f)] public float cellSize = 66f;
    [Min(0f)] public float gap = 4f;
    [Min(0f)] public float padding = 6f;
    [Header("跟随光标的物品影像")]
    [Range(0f, 1f)] public float dragSourceAlpha = 0.20f;
    [Range(0f, 1f)] public float dragImageAlpha = 0.90f;

    public BackpackDragImage DragImage { get; private set; }

    [Header("当前语言文字")]
    public TMP_Text titleText;
    public TMP_Text closeText;
    public TMP_Text hintText;
    public TMP_Text detailsText;
    public TMP_Text feedbackText;
    public TMP_Text lootTitleText;
    public TMP_Text lootStatusText;
    public Button closeButton;

    private BackpackUIController controller;
    private BackpackManager manager;
    private LocalizationManager localization;
    private LocalizationManager subscribedLocalization;
    private readonly List<Image> cells = new List<Image>();
    private readonly Dictionary<string, BackpackItemView> itemViews = new Dictionary<string, BackpackItemView>();
    private readonly List<RaycastResult> pointerResults = new List<RaycastResult>();
    private int drawnWidth;
    private int drawnHeight;

    public void Bind(BackpackUIController owner, BackpackManager backpack, LocalizationManager language)
    {
        if (controller == owner && manager == backpack && localization == language)
        {
            SubscribeLanguage();
            RefreshText();
            return;
        }
        controller = owner;
        manager = backpack;
        localization = language;
        SubscribeLanguage();
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(OnCloseClicked);
            closeButton.onClick.AddListener(OnCloseClicked);
        }
        HidePreview();
        RefreshItems();
    }

    private void OnEnable()
    {
        SubscribeLanguage();
        RefreshText();
    }

    private void OnDisable()
    {
        ClearDragImage();
        UnsubscribeLanguage();
        if (controller != null && controller.IsOpen)
        {
            controller.Close();
        }
    }

    private void OnDestroy()
    {
        if (DragImage != null) { Destroy(DragImage.gameObject); }
        UnsubscribeLanguage();
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(OnCloseClicked);
        }
    }

    private void SubscribeLanguage()
    {
        if (subscribedLocalization == localization)
        {
            return;
        }
        UnsubscribeLanguage();
        if (isActiveAndEnabled && localization != null)
        {
            subscribedLocalization = localization;
            subscribedLocalization.LanguageChanged += RefreshText;
        }
    }

    private void UnsubscribeLanguage()
    {
        if (subscribedLocalization != null)
        {
            subscribedLocalization.LanguageChanged -= RefreshText;
        }
        subscribedLocalization = null;
    }

    public void SetVisible(bool visible)
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(visible);
        }
        if (visible)
        {
            SubscribeLanguage();
            RefreshItems();
        }
        else
        {
            HidePreview();
            ClearDragImage();
            ClearHover();
        }
    }

    public void RefreshItems()
    {
        if (manager == null || !manager.IsReady || gridRoot == null || itemRoot == null || itemPrefab == null)
        {
            RefreshText();
            return;
        }

        DrawCells();
        IReadOnlyList<BackpackPlacement> placements = manager.GetPlacements();
        HashSet<string> visibleIds = new HashSet<string>();
        foreach (BackpackPlacement placement in placements)
        {
            visibleIds.Add(placement.InstanceId);
            if (!itemViews.TryGetValue(placement.InstanceId, out BackpackItemView itemView) || itemView == null)
            {
                itemView = Instantiate(itemPrefab, itemRoot);
                itemView.gameObject.name = "BackpackItem_" + placement.InstanceId;
                itemView.gameObject.SetActive(true);
                itemViews[placement.InstanceId] = itemView;
            }
            itemView.Bind(controller, placement.InstanceId);
            PlaceRect((RectTransform)itemView.transform, placement.X, placement.Y, placement.Width, placement.Height);
        }

        List<string> removedIds = new List<string>();
        foreach (KeyValuePair<string, BackpackItemView> pair in itemViews)
        {
            if (!visibleIds.Contains(pair.Key))
            {
                if (pair.Value != null)
                {
                    pair.Value.gameObject.SetActive(false);
                    Destroy(pair.Value.gameObject);
                }
                removedIds.Add(pair.Key);
            }
        }
        foreach (string removedId in removedIds)
        {
            itemViews.Remove(removedId);
        }
        if (preview != null)
        {
            preview.transform.SetAsLastSibling();
        }
        RefreshText();
    }

    private void DrawCells()
    {
        float width = padding * 2f + manager.Width * cellSize + (manager.Width - 1) * gap;
        float height = padding * 2f + manager.Height * cellSize + (manager.Height - 1) * gap;
        gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        gridRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        itemRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        itemRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        if (drawnWidth == manager.Width && drawnHeight == manager.Height)
        {
            return;
        }
        foreach (Image cell in cells)
        {
            if (cell != null)
            {
                Destroy(cell.gameObject);
            }
        }
        cells.Clear();
        drawnWidth = manager.Width;
        drawnHeight = manager.Height;
        if (cellPrefab == null)
        {
            return;
        }

        // 参考 CardGame/UIGridController 的行列循环，只生成表现，不承担占格判断。
        for (int y = 0; y < manager.Height; y++)
        {
            for (int x = 0; x < manager.Width; x++)
            {
                Image cell = Instantiate(cellPrefab, gridRoot);
                cell.gameObject.name = "Cell_" + x + "_" + y;
                cell.gameObject.SetActive(true);
                cell.raycastTarget = false;
                PlaceRect(cell.rectTransform, x, y, 1, 1);
                cells.Add(cell);
            }
        }
    }

    private void PlaceRect(RectTransform target, int x, int y, int width, int height)
    {
        target.anchorMin = new Vector2(0f, 1f);
        target.anchorMax = new Vector2(0f, 1f);
        target.pivot = new Vector2(0f, 1f);
        target.anchoredPosition = new Vector2(padding + x * (cellSize + gap), -padding - y * (cellSize + gap));
        target.sizeDelta = new Vector2(width * cellSize + (width - 1) * gap, height * cellSize + (height - 1) * gap);
    }

    public void RefreshText()
    {
        if (DragImage != null) { DragImage.RefreshText(localization); }
        // F10只走此方法，不销毁UI物品、不重新绑定拖拽，更不执行物品业务。
        SetText(titleText, Text("backpack.title"));
        SetText(closeText, Text("backpack.close"));
        SetText(hintText, Text("backpack.controls") + "\n" + Text("backpack.language_hint"));
        SetText(lootTitleText, Text("backpack.loot.title"));
        SetText(lootStatusText, Text("backpack.loot.disconnected"));
        string feedbackKey = controller != null ? controller.FeedbackKey : "backpack.result.not_ready";
        SetText(feedbackText, string.IsNullOrEmpty(feedbackKey) ? string.Empty : Text(feedbackKey));

        if (manager != null && manager.IsReady)
        {
            foreach (KeyValuePair<string, BackpackItemView> pair in itemViews)
            {
                if (pair.Value != null && manager.TryGetPlacement(pair.Key, out BackpackPlacement placement))
                {
                    pair.Value.RefreshText(placement.Item, placement, localization);
                    pair.Value.SetSelected(controller != null && controller.SelectedInstanceId == pair.Key, placement.Item.Definition);
                }
            }
        }

        ItemInstance item = null;
        int width = 0;
        int height = 0;
        if (controller != null && manager != null)
        {
            if (manager.TryGetPlacement(controller.SelectedInstanceId, out BackpackPlacement selected))
            {
                item = selected.Item;
                width = selected.Width;
                height = selected.Height;
            }
            else if (controller.IsExternalPreview && manager.Items != null
                && manager.Items.TryGetInstance(controller.SelectedInstanceId, out item) && item.Definition != null)
            {
                width = controller.PreviewRotated ? item.Definition.height : item.Definition.width;
                height = controller.PreviewRotated ? item.Definition.width : item.Definition.height;
            }
        }
        if (item == null || item.Definition == null)
        {
            SetText(detailsText, Text("backpack.select_hint"));
            return;
        }

        ItemDataSO definition = item.Definition;
        StringBuilder details = new StringBuilder();
        details.Append("<b>").Append(Text(definition.nameKey)).Append("</b>\n");
        details.Append(Text(definition.descriptionKey)).Append('\n');
        details.Append(Format("backpack.dimensions", width, height)).Append(" · ");
        details.Append(Text(definition.canRotate ? "backpack.rotatable" : "backpack.fixed_orientation")).Append('\n');
        if (!definition.canUse)
        {
            details.Append(Text("items.use.not_usable"));
        }
        else if (definition.usesPerRound > 0)
        {
            details.Append(Format("backpack.uses", item.UsesThisRound, definition.usesPerRound));
        }
        else
        {
            details.Append(Text("backpack.uses_unlimited"));
        }
        if (definition.canUse)
        {
            details.Append('\n').Append(Text(definition.consumedOnUse ? "backpack.consumed_on_use" : "backpack.kept_on_use"));
        }
        SetText(detailsText, details.ToString());
    }

    public void ShowPreview(ItemInstance item, int x, int y, bool rotated, bool valid)
    {
        if (preview == null || itemRoot == null || item == null || item.Definition == null)
        {
            return;
        }
        // 目标占格与自由跟随的影像分开；隐藏落点预览不隐藏影像。
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
        if (preview != null)
        {
            preview.gameObject.SetActive(false);
        }
    }

    public bool TryScreenToCell(Vector2 screenPoint, Camera eventCamera, out Vector2Int cell)
    {
        cell = new Vector2Int(-1, -1);
        if (gridRoot == null || manager == null || !manager.IsReady || panelRoot == null || !panelRoot.activeInHierarchy
            || !IsVisibleGridTarget(screenPoint) || cellSize <= 0f || gap < 0f
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(gridRoot, screenPoint, eventCamera, out Vector2 local))
        {
            return false;
        }

        float localX = local.x - gridRoot.rect.xMin - padding;
        float localY = gridRoot.rect.yMax - local.y - padding;
        float pitch = cellSize + gap;
        int x = Mathf.FloorToInt(localX / pitch);
        int y = Mathf.FloorToInt(localY / pitch);
        if (x < 0 || y < 0 || x >= manager.Width || y >= manager.Height
            || localX - x * pitch >= cellSize || localY - y * pitch >= cellSize)
        {
            return false;
        }
        cell = new Vector2Int(x, y);
        return true;
    }

    public RectTransform GetItemRect(string instanceId)
    {
        return !string.IsNullOrEmpty(instanceId) && itemViews.TryGetValue(instanceId, out BackpackItemView itemView)
            && itemView != null ? (RectTransform)itemView.transform : null;
    }

    public Vector2Int GetGrabOffset(string instanceId, Vector2 position, Camera eventCamera, BackpackPlacement placement)
    {
        RectTransform rect = GetItemRect(instanceId);
        if (rect == null || cellSize + gap <= 0f
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, position, eventCamera, out Vector2 local)) { return Vector2Int.zero; }
        // 大物品内部的格缝仍属于该物品；这里只算抓取点，目标格缝仍不可放置。
        float pitch = cellSize + gap;
        return new Vector2Int(Mathf.Clamp(Mathf.FloorToInt((local.x - rect.rect.xMin) / pitch), 0, placement.Width - 1),
            Mathf.Clamp(Mathf.FloorToInt((rect.rect.yMax - local.y) / pitch), 0, placement.Height - 1));
    }

    public void ClearHover()
    {
        foreach (BackpackItemView itemView in itemViews.Values) { if (itemView != null) { itemView.ClearHover(); } }
    }

    public void BeginDragImage(Object owner, int token, ItemInstance item, RectTransform source,
        Vector2 pressPosition, Camera eventCamera, bool rotated, Vector2Int grabOffset,
        float sourceCellSize, float sourceGap, Image sourceIcon = null)
    {
        if (source == null || token == 0) { return; }
        if (DragImage != null && DragImage.Matches(owner, token)) { return; }
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { return; }
        if (DragImage == null)
        {
            DragImage = new GameObject("BackpackDragImage", typeof(RectTransform), typeof(BackpackDragImage)).GetComponent<BackpackDragImage>();
            DragImage.transform.SetParent(canvas.rootCanvas.transform, false);
            DragImage.gameObject.layer = canvas.gameObject.layer;
            DragImage.Initialize(itemPrefab);
        }
        ClearHover();
        DragImage.Begin(owner, token, item, source, pressPosition, eventCamera, rotated, grabOffset,
            sourceCellSize, sourceGap, dragSourceAlpha, dragImageAlpha, sourceIcon);
    }

    public void UpdateDragImage(Object owner, int token, Vector2 position, Camera eventCamera, bool rotated, Vector2Int grabOffset)
    {
        if (DragImage != null) { DragImage.Follow(owner, token, position, eventCamera, rotated, grabOffset); }
    }

    public void EndDragImage(Object owner, int token)
    {
        if (DragImage != null) { DragImage.End(owner, token); }
    }

    private void ClearDragImage()
    {
        if (DragImage != null) { DragImage.End(); }
    }

    private bool IsVisibleGridTarget(Vector2 screenPoint)
    {
        if (EventSystem.current == null) { return true; }
        PointerEventData pointer = new PointerEventData(EventSystem.current) { position = screenPoint };
        pointerResults.Clear();
        EventSystem.current.RaycastAll(pointer, pointerResults);
        if (pointerResults.Count == 0) { return false; }
        Transform hit = pointerResults[0].gameObject.transform;
        // 与仓库一样，只接受实际最上层的本窗口，不能穿过来源或确认框放入背包。
        return hit == gridRoot || hit.IsChildOf(gridRoot) || gridRoot.IsChildOf(hit)
            || (itemRoot != null && (hit == itemRoot || hit.IsChildOf(itemRoot)));
    }

    public string ItemAtScreenPoint(Vector2 screenPoint, Camera eventCamera)
    {
        if (EventSystem.current == null || itemRoot == null || panelRoot == null || !panelRoot.activeInHierarchy)
        {
            return null;
        }
        PointerEventData pointer = new PointerEventData(EventSystem.current) { position = screenPoint };
        pointerResults.Clear();
        EventSystem.current.RaycastAll(pointer, pointerResults);
        if (pointerResults.Count == 0)
        {
            return null;
        }

        // 只接受实际位于最上层的本包UI物品；被其他窗口遮住时不能穿透使用。
        BackpackItemView itemView = pointerResults[0].gameObject.GetComponentInParent<BackpackItemView>();
        return itemView != null && itemView.transform.IsChildOf(itemRoot) ? itemView.InstanceId : null;
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
        if (target != null)
        {
            target.text = value;
        }
    }

    private void OnCloseClicked()
    {
        if (controller != null)
        {
            controller.Close();
        }
    }
}
