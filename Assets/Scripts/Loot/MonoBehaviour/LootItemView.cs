using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 参考CardDragHandler的UGUI生命周期和ItemTemplet明确刷新文字的方法。
public class LootItemView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Image background;
    public Image icon;
    public TMP_Text nameText;
    public TMP_Text descriptionText;
    public TMP_Text statusText;
    public TMP_Text countdownText;
    public Image[] outline;

    private LootUIController controller;
    private string entryId;
    private int dragToken;
    private ScrollRect draggingScroll;
    private bool pointerPressed;
    private bool clickPending;
    private bool dragStarted;
    private bool quickGesture;
    private bool quickHandled;
    private int pressedPointerId;
    private int pressedSession;
    private Vector2 pressLocalPoint;

    public string EntryId { get { return entryId; } }

    public void Bind(LootUIController owner, string id)
    {
        if (controller != owner || entryId != id)
        {
            CancelOwnDrag();
            controller = owner;
            entryId = id;
        }
    }

    public void RefreshText(LocalizationManager language)
    {
        LootEntryState entry = null;
        if (controller != null && controller.Source != null) { controller.Source.TryGetEntry(entryId, out entry); }
        ItemInstance item = null;
        bool revealed = entry != null && entry.Revealed;
        if (revealed && controller.runtime != null && controller.runtime.itemManager != null)
        {
            controller.runtime.itemManager.TryGetInstance(entry.InstanceId, out item);
        }
        ItemDataSO definition = item != null ? item.Definition : null;
        // 未知项只显示占格轮廓，不查询名称、描述、图标或使用信息。
        SetText(nameText, revealed && definition != null ? Text(language, definition.nameKey) : string.Empty);
        SetText(descriptionText, string.Empty);
        SetText(statusText, string.Empty);
        if (nameText != null) { nameText.gameObject.SetActive(revealed && definition != null); }
        if (descriptionText != null) { descriptionText.gameObject.SetActive(false); }
        if (statusText != null) { statusText.gameObject.SetActive(false); }
        if (icon != null)
        {
            // 01当前没有图标配置；使用Prefab的统一装饰，不能另建物品图标权威表。
            icon.gameObject.SetActive(revealed && definition != null && entry != null && !entry.Taken);
        }
        if (background != null)
        {
            background.color = revealed ? new Color(0.37f, 0.31f, 0.21f, 1f) : Color.clear;
            background.raycastTarget = true;
        }
        if (outline != null)
        {
            foreach (Image edge in outline)
            {
                if (edge == null) { continue; }
                edge.color = new Color(0.64f, 0.65f, 0.68f, 1f);
                edge.raycastTarget = false;
            }
        }
        if (icon != null) { icon.raycastTarget = false; }
        if (nameText != null) { nameText.raycastTarget = false; }
    }

    public void ShowCountdown(string text)
    {
        if (countdownText == null) { return; }
        countdownText.text = text;
        countdownText.raycastTarget = false;
        countdownText.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || controller == null || !controller.IsOpen
            || !isActiveAndEnabled || pointerPressed || dragToken != 0) { return; }
        pointerPressed = true;
        clickPending = false;
        dragStarted = false;
        pressedPointerId = eventData.pointerId;
        pressedSession = controller.SessionToken;
        RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, eventData.position,
            eventData.pressEventCamera, out pressLocalPoint);
        quickHandled = false;
        Keyboard keyboard = Keyboard.current;
        quickGesture = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
        if (quickGesture) { eventData.Use(); }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && pointerPressed && eventData.pointerId == pressedPointerId)
        {
            pointerPressed = false;
            clickPending = !dragStarted;
            // PointerClick在PointerUp之后，保留本次Ctrl标记直到点击完成或下一次按下。
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !clickPending
            || eventData.pointerId != pressedPointerId) { return; }
        clickPending = false;
        if (!quickGesture || quickHandled) { return; }
        quickHandled = true;
        eventData.Use();
        if (isActiveAndEnabled && controller != null && pressedSession != 0 && controller.SessionToken == pressedSession)
        {
            // 失败也消费快捷手势，不能随后变成拖拽或重复转移。
            controller.TryQuickTransfer(entryId);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || controller == null || !pointerPressed || dragStarted
            || eventData.pointerId != pressedPointerId || pressedSession == 0 || pressedSession != controller.SessionToken) { return; }
        dragStarted = true;
        clickPending = false;
        eventData.eligibleForClick = false;
        // 同一对象既接收按下又接收拖拽时，UGUI仍可能发Click；跨阈值的Ctrl手势只取消。
        if (quickGesture) { quickHandled = true; eventData.Use(); return; }
        // 用按下时物品局部点抵消ScrollRect惯性，未知项仍走下方原滚动流程。
        Vector2 pressPosition = RectTransformUtility.WorldToScreenPoint(eventData.pressEventCamera, transform.TransformPoint(pressLocalPoint));
        dragToken = controller.BeginItemDrag(entryId, eventData.position, eventData.pressEventCamera, pressPosition);
        if (dragToken == 0)
        {
            draggingScroll = GetComponentInParent<ScrollRect>();
            if (draggingScroll != null) { draggingScroll.OnBeginDrag(eventData); }
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (quickGesture) { return; }
        if (eventData.button == PointerEventData.InputButton.Left && dragToken != 0 && controller != null)
        {
            controller.UpdateItemDrag(dragToken, eventData.position, eventData.pressEventCamera);
        }
        else if (draggingScroll != null) { draggingScroll.OnDrag(eventData); }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || quickGesture) { return; }
        int token = dragToken;
        dragToken = 0;
        if (token != 0 && controller != null) { controller.EndItemDrag(token, eventData.position, eventData.pressEventCamera); }
        if (draggingScroll != null) { draggingScroll.OnEndDrag(eventData); draggingScroll = null; }
    }

    private void CancelOwnDrag()
    {
        int token = dragToken;
        dragToken = 0;
        draggingScroll = null;
        pointerPressed = false;
        clickPending = false;
        dragStarted = false;
        quickGesture = false;
        quickHandled = false;
        pressedSession = 0;
        pressedPointerId = 0;
        if (token != 0 && controller != null) { controller.CancelItemDrag(token); }
    }

    private void OnDisable() { CancelOwnDrag(); }
    private static string Text(LocalizationManager language, string key) { return language != null ? language.GetText(key) : "[" + key + "]"; }
    private static void SetText(TMP_Text target, string text) { if (target != null) { target.text = text; } }
}
