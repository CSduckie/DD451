using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 修改复用 CardGame/CardDragHandler 的开始、拖动、结束流程；具体提交由控制器负责。
public class BackpackItemView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Image background;
    public TMP_Text nameText;
    public TMP_Text sizeText;

    private BackpackUIController controller;
    private int dragToken;
    private bool pointerHeld;
    private bool clickPending;
    private bool quickPress;
    private bool dragStarted;
    private int pointerId;
    private int inputSession;

    public string InstanceId { get; private set; }

    public void Bind(BackpackUIController owner, string instanceId)
    {
        if (controller != owner || InstanceId != instanceId) { ClearPointer(); }
        controller = owner;
        InstanceId = instanceId;
        // 相同实例的刷新不清除 token，否则语言切换后会丢失当前拖拽回调。
    }

    public void RefreshText(ItemInstance item, BackpackPlacement placement, LocalizationManager localization)
    {
        if (item == null || item.Definition == null)
        {
            return;
        }

        if (nameText != null)
        {
            nameText.text = localization != null
                ? localization.GetText(item.Definition.nameKey)
                : "[" + item.Definition.nameKey + "]";
        }
        if (sizeText != null)
        {
            sizeText.text = localization != null
                ? localization.FormatText("backpack.item_size", placement.Width, placement.Height)
                : placement.Width + " × " + placement.Height;
        }
    }

    public void SetSelected(bool selected, ItemDataSO definition)
    {
        if (background == null || definition == null)
        {
            return;
        }

        Color normal = definition.canUse
            ? new Color(0.34f, 0.36f, 0.24f, 1f)
            : new Color(0.42f, 0.30f, 0.18f, 1f);
        background.color = selected ? Color.Lerp(normal, new Color(0.68f, 0.54f, 0.30f), 0.35f) : normal;
        Color textColor = new Color(0.97f, 0.93f, 0.80f, 1f);
        if (nameText != null) { nameText.color = textColor; }
        if (sizeText != null) { sizeText.color = textColor; }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || pointerHeld
            || controller == null || !controller.IsOpen || !isActiveAndEnabled) { return; }

        pointerHeld = true;
        clickPending = false;
        dragStarted = false;
        pointerId = eventData.pointerId;
        inputSession = controller.InputSession;
        Keyboard keyboard = Keyboard.current;
        // 修饰键只在按下时判断；途中按Ctrl不能把普通拖拽变成快捷转移。
        quickPress = keyboard != null && (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);
        if (quickPress) { controller.BeginQuickTransferPress(this); }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || !pointerHeld
            || eventData.pointerId != pointerId) { return; }
        pointerHeld = false;
        clickPending = !dragStarted;
        if (controller != null) { controller.EndQuickTransferPress(this); }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 右键仍只由UseItem处理；没有本轮完整按压的迟到点击不执行业务。
        if (eventData.button != PointerEventData.InputButton.Left || !clickPending
            || eventData.pointerId != pointerId || controller == null) { return; }
        clickPending = false;
        if (!isActiveAndEnabled || !controller.IsOpen || inputSession != controller.InputSession) { return; }
        if (quickPress)
        {
            // 即使目标拒绝也消费本次点击，不回退成选择、拖拽或使用。
            controller.TryQuickTransfer(InstanceId);
        }
        else
        {
            controller.SelectItem(InstanceId);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || controller == null
            || !pointerHeld || eventData.pointerId != pointerId
            || inputSession != controller.InputSession || dragStarted)
        {
            return;
        }

        dragStarted = true;
        // 超过拖拽阈值的快捷按压只取消；UGUI仍可能给同一物品发Click。
        if (quickPress) { return; }
        // 用按下位置计算抓取格，避免快速拖动跨过阈值时把目标格误当成起点。
        dragToken = controller.BeginItemDrag(InstanceId, eventData.pressPosition, eventData.pressEventCamera);
        if (dragToken != 0)
        {
            controller.UpdateDrag(dragToken, eventData.position, eventData.pressEventCamera);
        }
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
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        int endedToken = dragToken;
        dragToken = 0;
        if (endedToken != 0 && controller != null)
        {
            // 受击取消后仍可能收到旧回调；控制器检查这一次拖拽编号再决定是否提交。
            controller.EndDrag(endedToken, eventData.position, eventData.pressEventCamera);
        }
    }

    private void OnDisable()
    {
        ClearPointer();
    }

    private void ClearPointer()
    {
        int token = dragToken;
        dragToken = 0;
        pointerHeld = false;
        clickPending = false;
        quickPress = false;
        dragStarted = false;
        if (controller != null) { controller.EndQuickTransferPress(this); }
        if (controller != null && token != 0) { controller.CancelDrag(token); }
    }
}
