using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 一份可复用的纯显示影像。真实物品、占格及提交资格仍由来源控制器保存。
public class BackpackDragImage : MonoBehaviour
{
    private Image background;
    private Image icon;
    private TMP_Text nameText;
    private TMP_Text sizeText;
    private CanvasGroup sourceGroup;
    private CanvasGroup imageGroup;
    private Camera canvasCamera;
    private float sourceAlpha;
    private ItemInstance item;
    private Vector2 cellSize;
    private Vector2 gap;
    private Vector2 withinCell;
    private int width;
    private int height;

    public Object Owner { get; private set; }
    public int Token { get; private set; }
    public RectTransform SourceRect { get; private set; }
    public RectTransform RectTransform { get { return (RectTransform)transform; } }
    public bool IsVisible { get { return Token != 0 && gameObject.activeInHierarchy; } }

    public void Initialize(BackpackItemView template)
    {
        background = gameObject.AddComponent<Image>();
        imageGroup = gameObject.AddComponent<CanvasGroup>();
        imageGroup.blocksRaycasts = false;
        imageGroup.interactable = false;
        Canvas canvas = GetComponentInParent<Canvas>().rootCanvas;
        canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        icon.transform.SetParent(transform, false);
        icon.rectTransform.anchorMin = Vector2.zero;
        icon.rectTransform.anchorMax = Vector2.one;
        icon.rectTransform.offsetMin = new Vector2(8f, 8f);
        icon.rectTransform.offsetMax = new Vector2(-8f, -8f);
        icon.preserveAspect = true;
        if (template != null)
        {
            if (template.nameText != null) { nameText = Instantiate(template.nameText, transform); }
            if (template.sizeText != null) { sizeText = Instantiate(template.sizeText, transform); }
        }
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true)) { graphic.raycastTarget = false; }
        foreach (Transform child in GetComponentsInChildren<Transform>(true)) { child.gameObject.layer = gameObject.layer; }
        gameObject.SetActive(false);
    }

    public bool Matches(Object owner, int token)
    {
        return token != 0 && Token == token && Owner == owner;
    }

    public void Begin(Object owner, int token, ItemInstance value, RectTransform source,
        Vector2 pressPosition, Camera eventCamera, bool rotated, Vector2Int grabOffset,
        float sourceCellSize, float sourceGap, float dimAlpha, float imageAlpha, Image sourceIcon = null)
    {
        if (owner == null || token == 0 || value == null || value.Definition == null || source == null) { return; }
        RectTransform root = (RectTransform)transform.parent;
        if (root == null || sourceCellSize <= 0f || sourceGap < 0f
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(root, pressPosition, canvasCamera, out Vector2 press)) { return; }
        End();
        Vector3 topLeft = root.InverseTransformPoint(source.TransformPoint(new Vector3(source.rect.xMin, source.rect.yMax)));
        Vector3 scaleX = root.InverseTransformVector(source.TransformVector(Vector3.right));
        Vector3 scaleY = root.InverseTransformVector(source.TransformVector(Vector3.up));
        Vector2 scale = new Vector2(scaleX.magnitude, scaleY.magnitude);
        cellSize = scale * sourceCellSize;
        gap = scale * sourceGap;
        Vector2 offset = new Vector2(press.x - topLeft.x, topLeft.y - press.y);
        withinCell = offset - Vector2.Scale(grabOffset, cellSize + gap);
        item = value;
        Owner = owner;
        Token = token;
        SourceRect = source;
        sourceGroup = source.GetComponent<CanvasGroup>();
        if (sourceGroup == null) { sourceGroup = source.gameObject.AddComponent<CanvasGroup>(); }
        sourceAlpha = sourceGroup.alpha;
        sourceGroup.alpha = sourceAlpha * dimAlpha;
        Image sourceBackground = source.GetComponent<Image>();
        background.sprite = sourceBackground != null ? sourceBackground.sprite : null;
        background.type = sourceBackground != null ? sourceBackground.type : Image.Type.Simple;
        background.color = sourceBackground != null ? sourceBackground.color : new Color(0.42f, 0.30f, 0.18f);
        imageGroup.alpha = imageAlpha;
        icon.sprite = sourceIcon != null && sourceIcon.isActiveAndEnabled ? sourceIcon.sprite : null;
        icon.color = sourceIcon != null ? sourceIcon.color : Color.white;
        icon.gameObject.SetActive(icon.sprite != null);
        RectTransform.anchorMin = RectTransform.anchorMax = root.pivot;
        RectTransform.pivot = new Vector2(0f, 1f);
        transform.SetAsLastSibling();
        gameObject.SetActive(true);
        width = height = 0;
        Follow(owner, token, pressPosition, eventCamera, rotated, grabOffset);
    }

    public void Follow(Object owner, int token, Vector2 position, Camera eventCamera, bool rotated, Vector2Int grabOffset)
    {
        if (!Matches(owner, token)) { return; }
        if (SourceRect == null || !SourceRect.gameObject.activeInHierarchy || item == null || item.Definition == null)
        {
            End(owner, token);
            return;
        }
        RectTransform root = (RectTransform)transform.parent;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, position, canvasCamera, out Vector2 pointer)) { return; }
        int oldWidth = width;
        int oldHeight = height;
        width = rotated ? item.Definition.height : item.Definition.width;
        height = rotated ? item.Definition.width : item.Definition.height;
        Vector2 size = new Vector2(width * cellSize.x + (width - 1) * gap.x,
            height * cellSize.y + (height - 1) * gap.y);
        // R沿用来源控制器夹紧后的抓取格，保留格内的真实像素偏移。
        Vector2 offset = Vector2.Scale(grabOffset, cellSize + gap) + withinCell;
        offset = new Vector2(Mathf.Clamp(offset.x, 0f, size.x), Mathf.Clamp(offset.y, 0f, size.y));
        RectTransform.sizeDelta = size;
        RectTransform.anchoredPosition = pointer - new Vector2(offset.x, -offset.y);
        if (width != oldWidth || height != oldHeight) { RefreshText(LocalizationManager.Instance); }
    }

    public void RefreshText(LocalizationManager language)
    {
        if (item == null || item.Definition == null) { return; }
        if (nameText != null)
        {
            nameText.text = language != null ? language.GetText(item.Definition.nameKey) : "[" + item.Definition.nameKey + "]";
        }
        if (sizeText != null)
        {
            sizeText.text = language != null ? language.FormatText("backpack.item_size", width, height) : width + " × " + height;
        }
    }

    public void End(Object owner, int token)
    {
        if (Matches(owner, token)) { End(); }
    }

    public void End()
    {
        ClearSource();
        gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (Token != 0 && (Owner == null || SourceRect == null || !SourceRect.gameObject.activeInHierarchy)) { End(); }
    }

    private void OnDisable()
    {
        ClearSource();
    }

    private void ClearSource()
    {
        // 根Canvas/场景关闭时也还原仍存在的源外观，绝不重建已被业务删除的物品。
        if (sourceGroup != null) { sourceGroup.alpha = sourceAlpha; }
        sourceGroup = null;
        SourceRect = null;
        Owner = null;
        Token = 0;
        item = null;
    }
}
