using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class LootView : MonoBehaviour
{
    public GameObject panelRoot;
    public RectTransform contentRoot;
    public RectTransform gridRoot;
    public Image cellPrefab;
    public LootItemView itemPrefab;
    public TMP_Text titleText;
    public TMP_Text statusText;
    public TMP_Text progressText;
    public TMP_Text feedbackText;
    public TMP_Text closeText;
    public Button closeButton;
    public Slider progressBar;
    [Header("箱子单格显示（容量来自当前容器）")]
    [Min(1f)] public float cellSize = 66f;
    [Min(0f)] public float gap = 4f;
    [Min(0f)] public float padding = 6f;
    [Header("背包放回箱子的实际格子区域（留空时使用ScrollRect）")]
    public RectTransform viewport;
    public Image returnTarget;

    private LootUIController controller;
    private LocalizationManager localization;
    private LocalizationManager subscribedLocalization;
    private readonly Dictionary<string, LootItemView> itemViews = new Dictionary<string, LootItemView>();
    private readonly List<Image> cells = new List<Image>();
    private readonly List<RaycastResult> pointerResults = new List<RaycastResult>();
    private RectTransform returnPreviewRect;
    private Image[] returnPreviewEdges;
    private LootSourceState displayedSource;
    private bool hasWideContent;
    private bool layoutInvalid;
    private int drawnWidth;
    private int drawnHeight;
    private float drawnCellSize;
    private float drawnGap;
    private float drawnPadding;

    public bool IsReturnPreviewVisible { get; private set; }
    public RectTransform ReturnPreviewRect { get { return returnPreviewRect; } }

    public LootItemView GetItemView(string entryId)
    {
        return !string.IsNullOrEmpty(entryId) && itemViews.TryGetValue(entryId, out LootItemView itemView) ? itemView : null;
    }

    public void Bind(LootUIController owner, LocalizationManager language)
    {
        controller = owner;
        localization = language;
        Subscribe();
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(OnClose);
            closeButton.onClick.AddListener(OnClose);
        }
        RefreshText();
    }

    public void SetVisible(bool visible)
    {
        if (!visible) { HideReturnPreview(); }
        if (panelRoot != null) { panelRoot.SetActive(visible); }
        if (visible) { Subscribe(); RefreshItems(); }
    }

    public RectTransform GetReturnViewport()
    {
        if (viewport == null && contentRoot != null)
        {
            ScrollRect scroll = contentRoot.GetComponentInParent<ScrollRect>();
            if (scroll != null) { viewport = scroll.viewport; }
        }
        return viewport;
    }

    public bool TryHitReturnTarget(Vector2 position, Camera eventCamera)
    {
        return TryScreenToCell(position, eventCamera, out _);
    }

    public bool TryScreenToCell(Vector2 position, Camera eventCamera, out Vector2Int cell)
    {
        cell = new Vector2Int(-1, -1);
        if (layoutInvalid || contentRoot == null || cellSize <= 0f || gap < 0f || !TryHitReturnViewport(position, eventCamera)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(contentRoot, position, eventCamera, out Vector2 local)) { return false; }
        float localX = local.x - contentRoot.rect.xMin - padding;
        float localY = contentRoot.rect.yMax - local.y - padding;
        float pitch = cellSize + gap;
        int x = Mathf.FloorToInt(localX / pitch);
        int y = Mathf.FloorToInt(localY / pitch);
        if (x < 0 || y < 0 || x >= controller.Source.GridWidth || y >= controller.Source.GridHeight
            || localX - x * pitch >= cellSize || localY - y * pitch >= cellSize) { return false; }
        cell = new Vector2Int(x, y);
        return true;
    }

    public bool TryHitReturnViewport(Vector2 position, Camera eventCamera)
    {
        RectTransform target = GetReturnViewport();
        if (controller == null || !controller.IsOpen || controller.Source == null || controller.Source.IsGround
            || panelRoot == null || !panelRoot.activeInHierarchy || target == null || EventSystem.current == null
            || !RectTransformUtility.RectangleContainsScreenPoint(target, position, eventCamera)) { return false; }
        PointerEventData pointer = new PointerEventData(EventSystem.current) { position = position };
        pointerResults.Clear();
        EventSystem.current.RaycastAll(pointer, pointerResults);
        if (pointerResults.Count == 0) { return false; }
        Transform hit = pointerResults[0].gameObject.transform;
        // 只供桥识别此窗口与无效松手反馈；视口命中本身不表示格缝/下方空白可放置。
        return hit == target || hit.IsChildOf(target);
    }

    public bool TryGetItemGrabOffset(string entryId, Vector2 position, Camera eventCamera, out Vector2Int offset)
    {
        offset = Vector2Int.zero;
        if (controller == null || controller.Source == null || cellSize <= 0f || gap < 0f
            || !controller.Source.TryGetEntry(entryId, out LootEntryState entry) || !entry.HasLayout
            || !itemViews.TryGetValue(entryId, out LootItemView itemView) || itemView == null || !itemView.isActiveAndEnabled) { return false; }
        RectTransform rect = (RectTransform)itemView.transform;
        if (!RectTransformUtility.RectangleContainsScreenPoint(rect, position, eventCamera)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, position, eventCamera, out Vector2 local)) { return false; }
        float pitch = cellSize + gap;
        offset = new Vector2Int(Mathf.Clamp(Mathf.FloorToInt((local.x - rect.rect.xMin) / pitch), 0, entry.LayoutWidth - 1),
            Mathf.Clamp(Mathf.FloorToInt((rect.rect.yMax - local.y) / pitch), 0, entry.LayoutHeight - 1));
        return true;
    }

    public void ShowReturnPreview(RectInt position, bool valid = true)
    {
        LootSourceState source = controller != null ? controller.Source : null;
        if (controller == null || !controller.IsOpen || source == null || source.IsGround || layoutInvalid
            || contentRoot == null || !contentRoot.gameObject.activeInHierarchy || panelRoot == null || !panelRoot.activeInHierarchy
            || position.x < 0 || position.y < 0 || position.width <= 0 || position.height <= 0
            || (long)position.x + position.width > source.GridWidth || (long)position.y + position.height > source.GridHeight)
        {
            HideReturnPreview();
            return;
        }
        if (returnPreviewRect == null)
        {
            returnPreviewRect = new GameObject("ReturnPreview", typeof(RectTransform)).GetComponent<RectTransform>();
            returnPreviewRect.SetParent(contentRoot, false);
            returnPreviewRect.gameObject.layer = contentRoot.gameObject.layer;
            // 只画鼠标目标的外框，不改视口/背景格颜色，也不填充未知物的中心。
            returnPreviewEdges = new[]
            {
                CreateReturnEdge("Top", new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), new Vector2(0f, 3f)),
                CreateReturnEdge("Bottom", Vector2.zero, new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 3f)),
                CreateReturnEdge("Left", Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(3f, 0f)),
                CreateReturnEdge("Right", new Vector2(1f, 0f), Vector2.one, new Vector2(1f, 0.5f), new Vector2(3f, 0f))
            };
        }
        foreach (Image edge in returnPreviewEdges)
        {
            edge.color = valid ? new Color(0.40f, 0.93f, 0.70f, 1f) : new Color(1f, 0.34f, 0.34f, 1f);
        }
        returnPreviewRect.anchorMin = returnPreviewRect.anchorMax = returnPreviewRect.pivot = new Vector2(0f, 1f);
        returnPreviewRect.anchoredPosition = new Vector2(padding + position.x * (cellSize + gap),
            -padding - position.y * (cellSize + gap));
        returnPreviewRect.sizeDelta = new Vector2(GridLength(position.width), GridLength(position.height));
        returnPreviewRect.SetAsLastSibling();
        returnPreviewRect.gameObject.SetActive(true);
        IsReturnPreviewVisible = true;
    }

    private Image CreateReturnEdge(string name, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size)
    {
        Image edge = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        RectTransform rect = edge.rectTransform;
        rect.SetParent(returnPreviewRect, false);
        edge.gameObject.layer = returnPreviewRect.gameObject.layer;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = pivot;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        edge.color = new Color(0.40f, 0.93f, 0.70f, 1f);
        edge.raycastTarget = false;
        return edge;
    }

    public void HideReturnPreview()
    {
        if (returnPreviewRect != null) { returnPreviewRect.gameObject.SetActive(false); }
        IsReturnPreviewVisible = false;
    }

    public void RefreshItems()
    {
        HashSet<string> visible = new HashSet<string>();
        layoutInvalid = false;
        if (controller != null && controller.Source != null && contentRoot != null && itemPrefab != null)
        {
            LootSourceState source = controller.Source;
            layoutInvalid = !source.EnsureLayout(controller.runtime != null ? controller.runtime.itemManager : null)
                || gridRoot == null || cellPrefab == null;
            ConfigureContent(source);
            DrawCells(source.IsGround ? source.LayoutColumns : source.GridWidth,
                source.IsGround ? source.LayoutRows : source.GridHeight);
            // 布局失败不能只露出部分物品，避免把不完整来源当作有效可操作内容。
            if (!layoutInvalid)
            {
                foreach (LootEntryState entry in source.Entries)
                {
                    if (!entry.HasLayout) { continue; }
                    visible.Add(entry.EntryId);
                    if (!itemViews.TryGetValue(entry.EntryId, out LootItemView itemView) || itemView == null)
                    {
                        itemView = Instantiate(itemPrefab, contentRoot);
                        itemViews[entry.EntryId] = itemView;
                    }
                    itemView.Bind(controller, entry.EntryId);
                    RectTransform rect = (RectTransform)itemView.transform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
                    rect.anchoredPosition = new Vector2(padding + entry.LayoutX * (cellSize + gap),
                        -padding - entry.LayoutY * (cellSize + gap));
                    rect.sizeDelta = new Vector2(GridLength(entry.LayoutWidth), GridLength(entry.LayoutHeight));
                    itemView.gameObject.SetActive(!entry.Taken);
                }
            }
        }
        List<string> removed = new List<string>();
        foreach (KeyValuePair<string, LootItemView> pair in itemViews)
        {
            if (!visible.Contains(pair.Key))
            {
                if (pair.Value != null) { pair.Value.gameObject.SetActive(false); Destroy(pair.Value.gameObject); }
                removed.Add(pair.Key);
            }
        }
        foreach (string id in removed) { itemViews.Remove(id); }
        if (layoutInvalid || controller == null || controller.Source == null) { HideReturnPreview(); }
        else if (IsReturnPreviewVisible && returnPreviewRect != null) { returnPreviewRect.SetAsLastSibling(); }
        RefreshText();
    }

    private float GridLength(int count)
    {
        return count * cellSize + Mathf.Max(0, count - 1) * gap;
    }

    private void DrawCells(int width, int height)
    {
        if (gridRoot == null || cellPrefab == null) { return; }
        gridRoot.anchorMin = gridRoot.anchorMax = gridRoot.pivot = new Vector2(0f, 1f);
        gridRoot.anchoredPosition = Vector2.zero;
        gridRoot.sizeDelta = contentRoot.sizeDelta;
        gridRoot.SetAsFirstSibling();
        Image gridBackground = gridRoot.GetComponent<Image>();
        if (gridBackground == null) { gridBackground = gridRoot.gameObject.AddComponent<Image>(); }
        gridBackground.color = new Color(0.025f, 0.055f, 0.065f, 1f);
        gridBackground.raycastTarget = false;
        if (drawnWidth == width && drawnHeight == height && drawnCellSize == cellSize
            && drawnGap == gap && drawnPadding == padding) { return; }
        foreach (Image cell in cells)
        {
            if (cell != null) { cell.gameObject.SetActive(false); Destroy(cell.gameObject); }
        }
        cells.Clear();
        drawnWidth = width;
        drawnHeight = height;
        drawnCellSize = cellSize;
        drawnGap = gap;
        drawnPadding = padding;
        // 沿用BackpackView的行列背景，只负责表现，不能据物品是否未知/已取走改变样式。
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Image cell = Instantiate(cellPrefab, gridRoot);
                cell.gameObject.name = "Cell_" + x + "_" + y;
                cell.raycastTarget = false;
                cell.rectTransform.anchorMin = cell.rectTransform.anchorMax = cell.rectTransform.pivot = new Vector2(0f, 1f);
                cell.rectTransform.anchoredPosition = new Vector2(padding + x * (cellSize + gap), -padding - y * (cellSize + gap));
                cell.rectTransform.sizeDelta = new Vector2(cellSize, cellSize);
                cell.gameObject.SetActive(true);
                cells.Add(cell);
            }
        }
    }

    private void ConfigureContent(LootSourceState source)
    {
        if (!ReferenceEquals(displayedSource, source)) { HideReturnPreview(); }
        // 旧场景的列表组件不再决定尺寸；坐标由本轮来源保存。
        LayoutGroup layout = contentRoot.GetComponent<LayoutGroup>();
        if (layout != null) { layout.enabled = false; }
        ContentSizeFitter fitter = contentRoot.GetComponent<ContentSizeFitter>();
        if (fitter != null) { fitter.enabled = false; }
        contentRoot.anchorMin = contentRoot.anchorMax = contentRoot.pivot = new Vector2(0f, 1f);
        int width = source.IsGround ? source.LayoutColumns : source.GridWidth;
        int height = source.IsGround ? source.LayoutRows : source.GridHeight;
        contentRoot.sizeDelta = new Vector2(GridLength(width) + padding * 2f,
            GridLength(height) + padding * 2f);
        ScrollRect scroll = contentRoot.GetComponentInParent<ScrollRect>();
        RectTransform target = GetReturnViewport();
        // 视口仍接收滚轮和透明区域射线，深色格缝只画在实际网格范围内。
        Image viewportBackground = target != null ? target.GetComponent<Image>() : null;
        if (viewportBackground != null) { viewportBackground.color = Color.clear; }
        hasWideContent = target != null && contentRoot.rect.width > target.rect.width + 0.5f;
        if (scroll != null)
        {
            scroll.vertical = true;
            // 宽容器保持同一格大小，拖动空白区或横滚可到达右侧，不改变容量。
            scroll.horizontal = hasWideContent;
            if (!ReferenceEquals(displayedSource, source))
            {
                scroll.StopMovement();
                contentRoot.anchoredPosition = Vector2.zero;
            }
        }
        displayedSource = source;
    }

    public void RefreshText()
    {
        SetText(titleText, Text("loot.title"));
        SetText(closeText, Text("loot.close"));
        SetText(feedbackText, Text(controller != null ? controller.FeedbackKey : "loot.closed"));
        foreach (LootItemView itemView in itemViews.Values)
        {
            if (itemView != null) { itemView.RefreshText(localization); }
        }
        RefreshProgress();
    }

    public void RefreshProgress()
    {
        LootSourceState source = controller != null ? controller.Source : null;
        LootEntryState next = source != null ? source.GetNextUnrevealed() : null;
        bool remaining = false;
        if (source != null)
        {
            foreach (LootEntryState entry in source.Entries) { if (!entry.Taken) { remaining = true; break; } }
        }
        string status = Text(source == null ? "loot.closed" : layoutInvalid ? "loot.layout_invalid" : !remaining ? "loot.empty"
            : next != null ? "loot.searching" : "loot.complete");
        if (source != null && hasWideContent) { status += "\n" + Text("loot.wide_hint"); }
        SetText(statusText, status);
        float elapsed = source != null ? source.SearchElapsed : 0f;
        float duration = 0f;
        if (next != null && controller.runtime != null && controller.runtime.itemManager != null
            && controller.runtime.itemManager.TryGetInstance(next.InstanceId, out ItemInstance item) && item.Definition != null)
        {
            duration = item.Definition.searchSeconds;
        }
        foreach (LootItemView itemView in itemViews.Values)
        {
            if (itemView == null) { continue; }
            bool current = source != null && next != null && source.SearchEntryId == next.EntryId
                && itemView.EntryId == next.EntryId;
            itemView.ShowCountdown(current ? Format("loot.countdown", Mathf.Max(0f, duration - elapsed)) : string.Empty);
        }
        SetText(progressText, string.Empty);
        if (progressText != null) { progressText.gameObject.SetActive(false); }
        if (progressBar != null)
        {
            progressBar.gameObject.SetActive(false);
        }
    }

    private void OnEnable() { Subscribe(); RefreshText(); }
    private void OnDisable()
    {
        HideReturnPreview();
        Unsubscribe();
        if (controller != null && controller.IsOpen) { controller.CloseSource(); }
    }

    private void Subscribe()
    {
        if (subscribedLocalization == localization) { return; }
        Unsubscribe();
        if (isActiveAndEnabled && localization != null)
        {
            subscribedLocalization = localization;
            subscribedLocalization.LanguageChanged += RefreshText;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedLocalization != null) { subscribedLocalization.LanguageChanged -= RefreshText; }
        subscribedLocalization = null;
    }

    private string Text(string key) { return localization != null ? localization.GetText(key) : "[" + key + "]"; }
    private string Format(string key, params object[] args) { return localization != null ? localization.FormatText(key, args) : "[" + key + "]"; }
    private static void SetText(TMP_Text target, string text) { if (target != null) { target.text = text; } }
    private void OnClose() { if (controller != null) { controller.CloseSource(); } }
    private void OnDestroy()
    {
        Unsubscribe();
        if (closeButton != null) { closeButton.onClick.RemoveListener(OnClose); }
    }
}
