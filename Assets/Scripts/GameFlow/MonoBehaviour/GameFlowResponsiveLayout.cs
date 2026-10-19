using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 007 场景的显示适配；不改变物品、格子坐标、输入动作或共享 04/05/06 Prefab。
[DefaultExecutionOrder(200)]
public class GameFlowResponsiveLayout : MonoBehaviour
{
    public Canvas canvas;
    public BackpackView backpackView;
    public WarehouseView warehouseView;
    public ShelterView shelterView;
    public LootView lootView;
    public GameFlowHUD hud;

    private const float CompactWarehouseViewportWidth = 514f;
    private const int WarehouseColumns = 20;

    private readonly Dictionary<RectTransform, RectState> rectangles = new Dictionary<RectTransform, RectState>();
    private readonly Dictionary<TMP_Text, TextState> texts = new Dictionary<TMP_Text, TextState>();
    private readonly Dictionary<CanvasScaler, ScaleState> scalers = new Dictionary<CanvasScaler, ScaleState>();
    private readonly Dictionary<WarehouseView, WarehouseGeometryState> warehouseGeometry = new Dictionary<WarehouseView, WarehouseGeometryState>();
    private readonly List<BackpackItemView> backpackItems = new List<BackpackItemView>();
    private readonly List<WarehouseItemView> warehouseItems = new List<WarehouseItemView>();
    private readonly List<LootItemView> lootItems = new List<LootItemView>();
    private int previousWidth = -1;
    private int previousHeight = -1;
    private bool compact;
    private float compactBackpackGridScale = 1f;
    private bool wideStaticConfigured;
    private bool wideDiscardConfigured;
    private string wideDiscardMessage;

    private void LateUpdate()
    {
        if (Screen.width != previousWidth || Screen.height != previousHeight) { ApplyNow(); }
        // 新物品、旋转与语言切换在本帧完成显示适配；稳定帧只比较，不反复写字体/矩形。
        RefreshItemTypography();
        if (!compact) { LayoutWidePresentation(); }
        if (hud != null && hud.promptText != null)
        {
            bool inventoryOpen = backpackView != null && backpackView.panelRoot != null
                && backpackView.panelRoot.activeInHierarchy;
            Remember(hud.promptText);
            bool visible = !inventoryOpen && texts[hud.promptText].enabled;
            if (hud.promptText.enabled != visible) { hud.promptText.enabled = visible; }
        }
    }

    public void ApplyNow()
    {
        previousWidth = Screen.width;
        previousHeight = Screen.height;
        bool useCompact = Screen.width <= 1000;
        if (!useCompact)
        {
            if (compact) { Restore(); }
            compact = false;
            RefreshItemTypography();
            LayoutWidePresentation();
            return;
        }
        if (!compact) { Restore(); }
        compact = true;
        ConfigureCanvas(canvas);
        if (hud != null) { ConfigureCanvas(hud.GetComponentInParent<Canvas>()); }
        LayoutWarehouse();
        UpdateCompactGridScale();
        LayoutBackpack();
        LayoutShelter();
        LayoutLoot();
        LayoutHud();
        RefreshItemTypography();
    }

    private void LayoutWidePresentation()
    {
        if (!wideStaticConfigured)
        {
            ConfigureWideCanvas(canvas);
            if (hud != null) { ConfigureWideCanvas(hud.GetComponentInParent<Canvas>()); }
            LayoutWideWarehouse();
            LayoutWideShelter();
            // 复用06A的完整箱格内部布局，避免正式场景旧420宽覆盖截断六列格子。
            if (lootView != null) { Box(lootView.transform, 1412, 132, 484, 808); }
            if (warehouseView != null)
            {
                WideButtonLabel(warehouseView.abandonButton);
                WideButtonLabel(warehouseView.cancelDragButton);
            }
            if (shelterView != null && shelterView.titleText != null)
            {
                RectTransform title = shelterView.titleText.rectTransform;
                Remember(title);
                title.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 46f);
            }
            wideStaticConfigured = true;
        }
        LayoutWideDiscard();
    }

    private void ConfigureWideCanvas(Canvas target)
    {
        if (target == null) { return; }
        CanvasScaler scaler = target.GetComponent<CanvasScaler>();
        if (scaler == null) { return; }
        if (!scalers.ContainsKey(scaler)) { scalers.Add(scaler, new ScaleState(scaler)); }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;
    }

    private void LayoutWideWarehouse()
    {
        if (warehouseView == null) { return; }
        // 66边长的20列需要1408宽。扩大视口，避免仓库把格子再次缩小。
        Box(warehouseView.transform, 408, 160, 1488, 720);
        Label(warehouseView.titleText, 18, 12, 220, 48, 30);
        Label(warehouseView.sizeText, 300, 20, 1000, 38, 24);
        Button(warehouseView.closeButton, 1344, 12, 124, 44, 22);
        if (warehouseView.scrollRect != null)
        {
            Box(warehouseView.scrollRect.transform, 18, 74, 1452, 440);
            Box(warehouseView.viewport, 0, 0, 1430, 440);
            if (warehouseView.scrollRect.verticalScrollbar != null)
            { Box(warehouseView.scrollRect.verticalScrollbar.transform, 1434, 0, 18, 440); }
        }
        Transform details = warehouseView.transform.Find("DetailsScroll");
        Box(details, 18, 530, 1076, 84);
        ConfigureDetails(details != null ? details.GetComponent<ScrollRect>() : null,
            warehouseView.detailsText, 1076, 84, 18);
        Button(warehouseView.abandonButton, 1114, 530, 354, 40, 18);
        Button(warehouseView.cancelDragButton, 1114, 578, 354, 36, 18);
        Label(warehouseView.hintText, 18, 632, 1452, 52, 18);
        Label(warehouseView.feedbackText, 18, 688, 1452, 28, 18);
        if (warehouseView.confirmPanel != null)
        {
            Box(warehouseView.confirmPanel.transform, 0, 0, 1488, 720);
            Label(warehouseView.confirmTitleText, 24, 20, 1440, 44, 30);
            Label(warehouseView.confirmMessageText, 24, 84, 1440, 500, 24);
            Button(warehouseView.confirmButton, 24, 620, 710, 64, 22);
            Button(warehouseView.cancelButton, 754, 620, 710, 64, 22);
        }
    }

    private void LayoutWideShelter()
    {
        if (shelterView == null) { return; }
        // 升级信息放到下方，给背包与20列仓库留出横向空间。
        Box(shelterView.transform, 24, 904, 1872, 152);
        Label(shelterView.titleText, 18, 10, 160, 46, 28);
        Label(shelterView.evText, 200, 12, 330, 38, 22);
        Label(shelterView.currentSizeText, 560, 12, 350, 42, 20);
        Label(shelterView.targetSizeText, 930, 12, 350, 42, 20);
        Button(shelterView.upgradeButton, 1540, 12, 310, 52, 22);
        Label(shelterView.statusText, 18, 62, 440, 68, 20);
        Label(shelterView.resultText, 480, 62, 950, 68, 18);
        Label(shelterView.languageHintText, 1460, 80, 390, 54, 17);
    }

    private void WideButtonLabel(Button button)
    {
        if (button == null) { return; }
        RectTransform rect = button.transform as RectTransform;
        if (rect == null) { return; }
        // 保留原按钮外框；30高文字区足以容纳18字号中英文，不依赖自动缩字。
        Label(button.GetComponentInChildren<TMP_Text>(true), 6, 2, rect.rect.width - 12, rect.rect.height - 4, 18);
    }

    private void LayoutWideDiscard()
    {
        if (backpackView == null) { return; }
        RectTransform dialog = backpackView.transform.Find("DiscardConfirmation/Dialog") as RectTransform;
        if (dialog == null) { return; }
        TMP_Text message = FindText(dialog, "Message");
        if (message == null || (wideDiscardConfigured && wideDiscardMessage == message.text)) { return; }
        // 完整保留普通/唯一物品后果说明；F10或物品改变时按正文实际高度把按钮向下排。
        Font(message, 22);
        message.textWrappingMode = TextWrappingModes.Normal;
        message.overflowMode = TextOverflowModes.Overflow;
        float messageHeight = Mathf.Max(160f, Mathf.Ceil(message.GetPreferredValues(message.text, 504f, Mathf.Infinity).y) + 8f);
        float buttonY = 74f + messageHeight + 24f;
        Remember(dialog);
        dialog.anchorMin = dialog.anchorMax = dialog.pivot = new Vector2(0.5f, 0.5f);
        dialog.anchoredPosition = Vector2.zero;
        dialog.sizeDelta = new Vector2(560f, buttonY + 44f + 22f);
        Label(FindText(dialog, "Title"), 28, 20, 504, 40, 26);
        Label(message, 28, 74, 504, messageHeight, 22);
        Button(FindButton(dialog, "ConfirmDiscard"), 28, buttonY, 238, 44, 21);
        Button(FindButton(dialog, "CancelDiscard"), 294, buttonY, 238, 44, 21);
        wideDiscardMessage = message.text;
        wideDiscardConfigured = true;
    }

    private void ConfigureCanvas(Canvas target)
    {
        if (target == null) { return; }
        CanvasScaler scaler = target.GetComponent<CanvasScaler>();
        if (scaler == null) { return; }
        if (!scalers.ContainsKey(scaler)) { scalers.Add(scaler, new ScaleState(scaler)); }
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800, 600);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
    }

    private void LayoutBackpack()
    {
        if (backpackView == null) { return; }
        Transform inventory = backpackView.transform.Find("PlayerInventory");
        if (inventory == null) { return; }
        Box(inventory, 8, 80, 216, 512);
        Box(inventory.Find("Inset"), 3, 3, 210, 506);
        Box(inventory.Find("HeaderLine"), 10, 36, 196, 2);
        Label(backpackView.titleText, 10, 7, 108, 26, 18);
        Button(backpackView.closeButton, 124, 8, 82, 26, 14);
        GridScale(backpackView.gridRoot);
        GridScale(backpackView.itemRoot);
        Transform details = inventory.Find("ItemDetailsScroll");
        Box(details, 10, 228, 196, 84);
        ScrollRect detailsScroll = details != null ? details.GetComponent<ScrollRect>() : null;
        ConfigureDetails(detailsScroll, backpackView.detailsText, 196, 84, 14);
        Label(backpackView.feedbackText, 10, 318, 196, 44, 13);
        Label(backpackView.hintText, 10, 410, 196, 94, 12);
        Transform discard = inventory.Find("DiscardItem");
        if (discard != null) { Button(discard.GetComponent<Button>(), 10, 370, 196, 32, 14); }

        Transform dialog = backpackView.transform.Find("DiscardConfirmation/Dialog");
        if (dialog == null) { return; }
        RectTransform rect = dialog as RectTransform;
        Remember(rect);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(560, 300);
        Label(FindText(dialog, "Title"), 24, 18, 512, 32, 20);
        Label(FindText(dialog, "Message"), 24, 64, 512, 160, 16);
        Button(FindButton(dialog, "ConfirmDiscard"), 24, 244, 244, 36, 14);
        Button(FindButton(dialog, "CancelDiscard"), 292, 244, 244, 36, 14);
    }

    private void GridScale(RectTransform grid)
    {
        if (grid == null) { return; }
        Remember(grid);
        grid.anchorMin = grid.anchorMax = grid.pivot = new Vector2(0, 1);
        grid.anchoredPosition = new Vector2(18, -40);
        grid.localScale = new Vector3(compactBackpackGridScale, compactBackpackGridScale, 1);
    }

    private void UpdateCompactGridScale()
    {
        if (backpackView == null) { compactBackpackGridScale = 1f; return; }
        // 以背包 Prefab 的原始显示参数统一格子、间距及多格物品外框。
        // 仓库 View 已支持几何参数变化后的重排；背包保持字段不变，只缩放两个表现根节点。
        // Ruins 没有仓库 View，也使用相同 20 列视口公式。
        float cell = Mathf.Max(0.01f, backpackView.cellSize);
        float gap = Mathf.Max(0f, backpackView.gap);
        float padding = Mathf.Max(0f, backpackView.padding);
        float viewportWidth = warehouseView != null && warehouseView.viewport != null
            ? warehouseView.viewport.rect.width : CompactWarehouseViewportWidth;
        float naturalWidth = WarehouseColumns * cell + (WarehouseColumns - 1) * gap + 2f * padding;
        compactBackpackGridScale = Mathf.Max(0.01f, viewportWidth) / naturalWidth;
        if (warehouseView == null) { return; }
        if (!warehouseGeometry.ContainsKey(warehouseView))
        { warehouseGeometry.Add(warehouseView, new WarehouseGeometryState(warehouseView)); }
        warehouseView.cellSize = cell * compactBackpackGridScale;
        warehouseView.gap = gap * compactBackpackGridScale;
        warehouseView.padding = padding * compactBackpackGridScale;
    }

    private void LayoutWarehouse()
    {
        if (warehouseView == null) { return; }
        Box(warehouseView.transform, 236, 80, 556, 350);
        Label(warehouseView.titleText, 10, 7, 150, 27, 18);
        Label(warehouseView.sizeText, 164, 9, 316, 24, 14);
        Button(warehouseView.closeButton, 490, 7, 56, 28, 14);
        if (warehouseView.scrollRect != null)
        {
            Box(warehouseView.scrollRect.transform, 10, 42, 536, 154);
            Box(warehouseView.viewport, 0, 0, CompactWarehouseViewportWidth, 154);
            if (warehouseView.scrollRect.verticalScrollbar != null)
            { Box(warehouseView.scrollRect.verticalScrollbar.transform, 518, 0, 18, 154); }
        }
        Transform details = warehouseView.transform.Find("DetailsScroll");
        Box(details, 10, 206, 340, 62);
        ConfigureDetails(details != null ? details.GetComponent<ScrollRect>() : null,
            warehouseView.detailsText, 340, 62, 14);
        Button(warehouseView.abandonButton, 360, 206, 186, 28, 14);
        Button(warehouseView.cancelDragButton, 360, 240, 186, 28, 14);
        Label(warehouseView.hintText, 10, 276, 536, 48, 12);
        Label(warehouseView.feedbackText, 10, 330, 536, 18, 12);
        if (warehouseView.confirmPanel != null)
        {
            Box(warehouseView.confirmPanel.transform, 0, 0, 556, 350);
            Label(warehouseView.confirmTitleText, 20, 18, 516, 30, 20);
            Label(warehouseView.confirmMessageText, 20, 62, 516, 210, 14);
            Button(warehouseView.confirmButton, 20, 292, 246, 40, 14);
            Button(warehouseView.cancelButton, 286, 292, 246, 40, 14);
        }
    }

    private void LayoutShelter()
    {
        if (shelterView == null) { return; }
        Box(shelterView.transform, 236, 440, 556, 152);
        Label(shelterView.titleText, 10, 7, 160, 26, 18);
        Label(shelterView.evText, 180, 8, 190, 25, 14);
        Button(shelterView.upgradeButton, 375, 6, 171, 30, 14);
        Label(shelterView.currentSizeText, 10, 39, 170, 38, 12);
        Label(shelterView.targetSizeText, 188, 39, 170, 38, 12);
        Label(shelterView.statusText, 374, 44, 172, 62, 12);
        Label(shelterView.resultText, 10, 82, 348, 62, 12);
        Label(shelterView.languageHintText, 374, 112, 172, 32, 12);
    }

    private void LayoutLoot()
    {
        if (lootView == null) { return; }
        Box(lootView.transform, 236, 80, 556, 512);
        Label(lootView.titleText, 10, 7, 410, 28, 18);
        Button(lootView.closeButton, 430, 8, 116, 28, 14);
        Label(lootView.statusText, 10, 44, 536, 42, 14);
        Label(lootView.progressText, 10, 90, 536, 24, 14);
        if (lootView.progressBar != null) { Box(lootView.progressBar.transform, 10, 118, 536, 10); }
        Transform scroll = lootView.transform.Find("EntriesScroll");
        Box(scroll, 10, 140, 536, 280);
        ScrollRect scrollRect = scroll != null ? scroll.GetComponent<ScrollRect>() : null;
        if (scrollRect != null && scrollRect.viewport != null && scrollRect.viewport != scroll)
        { Box(scrollRect.viewport, 0, 0, 536, 280); }
        if (lootView.contentRoot != null)
        {
            Remember(lootView.contentRoot);
            lootView.contentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 536);
        }
        Label(lootView.feedbackText, 10, 432, 536, 72, 14);
    }

    private void LayoutHud()
    {
        if (hud == null) { return; }
        Label(hud.stageText, 8, 2, 602, 28, 18);
        Label(hud.healthText, 622, 4, 170, 25, 16);
        Label(hud.hintText, 8, 30, 784, 22, 12);
        Label(hud.feedbackText, 8, 54, 650, 22, 12);
        Button(hud.retryButton, 674, 52, 118, 26, 14);
        if (hud.promptText != null)
        {
            Label(hud.promptText, 0, 0, 760, 30, 16);
            RectTransform prompt = hud.promptText.rectTransform;
            prompt.anchorMin = prompt.anchorMax = prompt.pivot = new Vector2(0.5f, 0f);
            prompt.anchoredPosition = new Vector2(0, 18);
        }
    }

    private void RefreshItemTypography()
    {
        if (backpackView != null && backpackView.itemRoot != null)
        {
            backpackView.itemRoot.GetComponentsInChildren(true, backpackItems);
            foreach (BackpackItemView item in backpackItems)
            {
                if (!compact)
                {
                    WideThumbnail((RectTransform)item.transform, item.nameText, item.sizeText);
                    continue;
                }
                float readableFontSize = 12f / Mathf.Max(0.01f, compactBackpackGridScale);
                ItemText(item.nameText, readableFontSize);
                ItemText(item.sizeText, readableFontSize);
            }
        }
        if (warehouseView != null && warehouseView.itemRoot != null)
        {
            warehouseView.itemRoot.GetComponentsInChildren(true, warehouseItems);
            foreach (WarehouseItemView item in warehouseItems)
            {
                if (!compact)
                {
                    WideThumbnail((RectTransform)item.transform, item.nameText, item.sizeText);
                    continue;
                }
                ItemText(item.nameText, 12);
                ItemText(item.sizeText, 12);
            }
        }
        if (compact && lootView != null && lootView.contentRoot != null)
        {
            lootView.contentRoot.GetComponentsInChildren(true, lootItems);
            foreach (LootItemView item in lootItems)
            {
                Font(item.nameText, 18);
                Font(item.descriptionText, 14);
                Font(item.statusText, 12);
            }
        }
    }

    private void WideThumbnail(RectTransform item, TMP_Text name, TMP_Text size)
    {
        if (item.rect.height <= 0f) { return; }
        // 缩略名称保持单行，完整中英文名称仍由选中详情显示。
        // 旋转为一格高时，两条文字各占半行高，避免原 Prefab 的固定负 inset 吃掉名称区域。
        bool oneRow = item.rect.height < 52f;
        float nameHeight = oneRow ? item.rect.height * 0.5f : 24f;
        float sizeHeight = oneRow ? item.rect.height * 0.5f : 22f;
        float top = oneRow ? 0f : 4f;
        ThumbnailStrip(name, top, nameHeight);
        ThumbnailStrip(size, item.rect.height - top - sizeHeight, sizeHeight);
        ItemText(name, oneRow || item.rect.width < 48f ? 12f : 16f);
        ItemText(size, oneRow || item.rect.width < 48f ? 12f : 14f);
    }

    private void ThumbnailStrip(TMP_Text text, float top, float height)
    {
        if (text == null) { return; }
        RectTransform rect = text.rectTransform;
        Remember(rect);
        Vector2 minimum = new Vector2(0f, 1f);
        Vector2 maximum = Vector2.one;
        Vector2 position = new Vector2(4f, -top);
        Vector2 size = new Vector2(-8f, height);
        if (rect.anchorMin != minimum) { rect.anchorMin = minimum; }
        if (rect.anchorMax != maximum) { rect.anchorMax = maximum; }
        if (rect.pivot != minimum) { rect.pivot = minimum; }
        if (rect.anchoredPosition != position) { rect.anchoredPosition = position; }
        if (rect.sizeDelta != size) { rect.sizeDelta = size; }
    }

    private void ItemText(TMP_Text text, float size)
    {
        Font(text, size);
        if (text == null) { return; }
        if (text.textWrappingMode != TextWrappingModes.NoWrap) { text.textWrappingMode = TextWrappingModes.NoWrap; }
        if (text.overflowMode != TextOverflowModes.Ellipsis) { text.overflowMode = TextOverflowModes.Ellipsis; }
    }

    private void ConfigureDetails(ScrollRect scroll, TMP_Text text, float width, float height, float size)
    {
        if (scroll != null)
        {
            // 05 的详情滚动框自身就是 viewport，不能把整个框移回面板左上角。
            if (scroll.viewport != scroll.transform) { Box(scroll.viewport, 0, 0, width - 14, height); }
            if (scroll.verticalScrollbar != null) { Box(scroll.verticalScrollbar.transform, width - 10, 0, 10, height); }
        }
        if (text != null)
        {
            Remember(text.rectTransform);
            text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width - 20);
            Font(text, size);
        }
    }

    private void Label(TMP_Text text, float x, float y, float width, float height, float size)
    {
        if (text == null) { return; }
        Box(text.transform, x, y, width, height);
        Font(text, size);
    }

    private void Button(Button button, float x, float y, float width, float height, float size)
    {
        if (button == null) { return; }
        Box(button.transform, x, y, width, height);
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        Label(label, 4, 2, width - 8, height - 4, size);
    }

    private void Font(TMP_Text text, float size)
    {
        if (text == null) { return; }
        Remember(text);
        if (text.enableAutoSizing) { text.enableAutoSizing = false; }
        if (!Mathf.Approximately(text.fontSize, size)) { text.fontSize = size; }
    }

    private void Box(Transform transform, float x, float y, float width, float height)
    {
        RectTransform rect = transform as RectTransform;
        if (rect == null) { return; }
        Remember(rect);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
    }

    private void Remember(RectTransform rect)
    {
        if (rect != null && !rectangles.ContainsKey(rect)) { rectangles.Add(rect, new RectState(rect)); }
    }

    private void Remember(TMP_Text text)
    {
        if (text != null && !texts.ContainsKey(text)) { texts.Add(text, new TextState(text)); }
    }

    private static TMP_Text FindText(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        return child != null ? child.GetComponent<TMP_Text>() : null;
    }

    private static Button FindButton(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        return child != null ? child.GetComponent<Button>() : null;
    }

    private void Restore()
    {
        foreach (KeyValuePair<RectTransform, RectState> pair in rectangles) { if (pair.Key != null) { pair.Value.Restore(pair.Key); } }
        foreach (KeyValuePair<TMP_Text, TextState> pair in texts) { if (pair.Key != null) { pair.Value.Restore(pair.Key); } }
        foreach (KeyValuePair<CanvasScaler, ScaleState> pair in scalers) { if (pair.Key != null) { pair.Value.Restore(pair.Key); } }
        foreach (KeyValuePair<WarehouseView, WarehouseGeometryState> pair in warehouseGeometry)
        { if (pair.Key != null) { pair.Value.Restore(pair.Key); } }
        wideStaticConfigured = false;
        wideDiscardConfigured = false;
    }

    private void OnDisable()
    {
        Restore();
        compact = false;
        previousWidth = previousHeight = -1;
    }

    private struct RectState
    {
        private Vector2 min, max, pivot, position, size;
        private Vector3 scale;
        public RectState(RectTransform rect)
        {
            min = rect.anchorMin; max = rect.anchorMax; pivot = rect.pivot;
            position = rect.anchoredPosition; size = rect.sizeDelta; scale = rect.localScale;
        }
        public void Restore(RectTransform rect)
        {
            rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot;
            rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = scale;
        }
    }

    private struct TextState
    {
        private float size;
        private bool auto;
        private TextWrappingModes wrapping;
        private TextOverflowModes overflow;
        public bool enabled;
        public TextState(TMP_Text text)
        {
            size = text.fontSize; auto = text.enableAutoSizing; wrapping = text.textWrappingMode;
            overflow = text.overflowMode; enabled = text.enabled;
        }
        public void Restore(TMP_Text text)
        {
            text.fontSize = size; text.enableAutoSizing = auto; text.textWrappingMode = wrapping;
            text.overflowMode = overflow; text.enabled = enabled;
        }
    }

    private struct ScaleState
    {
        private CanvasScaler.ScaleMode mode;
        private CanvasScaler.ScreenMatchMode matchMode;
        private Vector2 reference;
        private float match;
        public ScaleState(CanvasScaler scaler)
        {
            mode = scaler.uiScaleMode; matchMode = scaler.screenMatchMode;
            reference = scaler.referenceResolution; match = scaler.matchWidthOrHeight;
        }
        public void Restore(CanvasScaler scaler)
        {
            scaler.uiScaleMode = mode; scaler.screenMatchMode = matchMode;
            scaler.referenceResolution = reference; scaler.matchWidthOrHeight = match;
        }
    }

    private struct WarehouseGeometryState
    {
        private float cell, gap, padding;
        public WarehouseGeometryState(WarehouseView view)
        {
            cell = view.cellSize; gap = view.gap; padding = view.padding;
        }
        public void Restore(WarehouseView view)
        {
            view.cellSize = cell; view.gap = gap; view.padding = padding;
        }
    }
}
