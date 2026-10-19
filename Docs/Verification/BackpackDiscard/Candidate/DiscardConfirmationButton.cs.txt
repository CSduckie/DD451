using UnityEngine.EventSystems;
using UnityEngine.UI;

// 只允许本次确认内按下的指针完成点击；隐藏后迟到的松手不能确认新目标。
public class DiscardConfirmationButton : Button
{
    public ItemTransferUIBridge bridge;

    private int pressedDiscardToken;

    public override void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            pressedDiscardToken = IsActive() && IsInteractable() && bridge != null
                && bridge.IsAwaitingDiscardConfirmation ? bridge.DiscardToken : 0;
        }
        base.OnPointerDown(eventData);
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) { return; }
        try
        {
            if (pressedDiscardToken != 0 && bridge != null && bridge.IsAwaitingDiscardConfirmation
                && pressedDiscardToken == bridge.DiscardToken)
            {
                base.OnPointerClick(eventData);
            }
        }
        finally { pressedDiscardToken = 0; }
    }

    protected override void OnDisable()
    {
        pressedDiscardToken = 0;
        base.OnDisable();
    }
}
