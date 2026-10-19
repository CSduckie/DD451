using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 修改复用 CardGame/CardDragHandler 的三段 UGUI 拖拽；没有世界坐标、卡牌效果或全局拖拽单例。
public class WarehouseItemView : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Image background;
    public TMP_Text nameText;
    public TMP_Text sizeText;

    private WarehouseUIController controller;
    private int dragToken;
    private bool pointerPressed;
    private bool quickTransferGesture;
    private Vector2 pressLocalPoint;

    public string InstanceId { get; private set; }

    public void Bind(WarehouseUIController owner, string instanceId)
    {
        if (controller != owner || InstanceId != instanceId)
        {
            CancelPointerGesture();
            CancelOwnDrag();
            controller = owner;
            InstanceId = instanceId;
        }
    }

    public void RefreshText(WarehousePlacement placement, LocalizationManager localization)
    {
        if (placement == null || placement.Item == null || placement.Item.Definition == null) { return; }
        if (nameText != null)
        {
            // 单列物品也保持整词；完整名称仍在选中详情中显示。
            nameText.textWrappingMode = TextWrappingModes.NoWrap;
            nameText.enableAutoSizing = true;
            nameText.fontSizeMin = 10f;
            nameText.overflowMode = TextOverflowModes.Ellipsis;
            nameText.text = localization != null ? localization.GetText(placement.Item.Definition.nameKey)
                : "[" + placement.Item.Definition.nameKey + "]";
        }
        if (sizeText != null)
        {
            sizeText.textWrappingMode = TextWrappingModes.NoWrap;
            sizeText.enableAutoSizing = true;
            sizeText.fontSizeMin = 8f;
            sizeText.fontSizeMax = 15f;
            sizeText.text = localization != null ? localization.FormatText("warehouse.item_size", placement.Width, placement.Height)
                : placement.Width + " × " + placement.Height;
        }
    }

    public void SetSelected(bool selected, ItemDataSO definition)
    {
        if (background == null || definition == null) { return; }
        Color normal = definition.canUse ? new Color(0.20f, 0.38f, 0.36f, 1f) : new Color(0.33f, 0.30f, 0.23f, 1f);
        background.color = selected ? Color.Lerp(normal, new Color(0.83f, 0.75f, 0.50f), 0.45f) : normal;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || controller == null) { return; }
        pointerPressed = true;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, eventData.position,
            eventData.pressEventCamera, out pressLocalPoint);
        Keyboard keyboard = Keyboard.current;
        // 按下时决定本次手势；中途松开Ctrl也不会退回普通拖拽。
        quickTransferGesture = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || controller == null || !pointerPressed) { return; }
        bool quickTransfer = quickTransferGesture;
        CancelPointerGesture();
        if (quickTransfer)
        {
            // 先消费，再请求；失败或同步回调都不能把一次点击执行两次。
            eventData.Use();
            controller.TryQuickTransfer(InstanceId);
            return;
        }
        controller.SelectItem(InstanceId);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || controller == null) { return; }
        eventData.eligibleForClick = false;
        bool canDrag = pointerPressed && !quickTransferGesture;
        CancelPointerGesture();
        if (!canDrag) { return; }
        // ScrollRect按下后仍可能有惯性，用按下时的局部点保留像素抓取偏移。
        Vector2 pressPosition = RectTransformUtility.WorldToScreenPoint(eventData.pressEventCamera, transform.TransformPoint(pressLocalPoint));
        dragToken = controller.BeginItemDrag(InstanceId, pressPosition, eventData.pressEventCamera);
        if (dragToken != 0) { controller.UpdateDrag(dragToken, eventData.position, eventData.pressEventCamera); }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && dragToken != 0 && controller != null)
        {
            controller.UpdateDrag(dragToken, eventData.position, eventData.pressEventCamera);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) { return; }
        int endedToken = dragToken;
        dragToken = 0;
        if (endedToken != 0 && controller != null)
        {
            controller.EndDrag(endedToken, eventData.position, eventData.pressEventCamera);
        }
    }

    private void CancelOwnDrag()
    {
        int token = dragToken;
        dragToken = 0;
        if (controller != null && token != 0) { controller.CancelDrag(token); }
    }

    public void CancelPointerGesture()
    {
        pointerPressed = false;
        quickTransferGesture = false;
    }

    private void OnDisable()
    {
        CancelPointerGesture();
        CancelOwnDrag();
    }
}
