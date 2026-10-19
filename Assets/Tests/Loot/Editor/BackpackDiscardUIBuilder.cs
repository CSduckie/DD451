using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 仅搭建丢弃按钮和确认表现；实际资格、取消和记损由现有Bridge/01负责。
public static class BackpackDiscardUIBuilder
{
    public static void Attach(ItemTransferUIBridge bridge, TMP_FontAsset font, float buttonY = -24f)
    {
        Transform inventory = bridge.backpack.view.transform.Find("PlayerInventory");
        if (bridge.dropButton == null)
        {
            bridge.dropButton = Button(inventory, "DiscardItem", font, 20, buttonY, 320, 34, out bridge.dropText);
        }
        else { bridge.dropButton.gameObject.name = "DiscardItem"; }
        if (bridge.discardPanel == null)
        {
            RectTransform overlay = Rect(bridge.backpack.view.transform, "DiscardConfirmation", 0, 0, 0, 0);
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            Image shade = overlay.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.65f);
            Canvas canvas = overlay.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 200;
            overlay.gameObject.AddComponent<GraphicRaycaster>();
            RectTransform box = Rect(overlay, "Dialog", 0, 0, 560, 276);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            Image background = box.gameObject.AddComponent<Image>();
            background.color = new Color(0.15f, 0.14f, 0.10f, 1f);
            bridge.discardTitleText = Text(box, "Title", font, 26, 28, 20, 504, 40);
            bridge.discardMessageText = Text(box, "Message", font, 22, 28, 74, 504, 104);
            bridge.confirmDiscardButton = Button(box, "ConfirmDiscard", font, 28, 210, 238, 44, out bridge.confirmDiscardText);
            bridge.cancelDiscardButton = Button(box, "CancelDiscard", font, 294, 210, 238, 44, out bridge.cancelDiscardText);
            bridge.discardPanel = overlay.gameObject;
        }
        bridge.confirmDiscardButton = ProtectConfirmationButton(bridge.confirmDiscardButton, bridge);
        bridge.cancelDiscardButton = ProtectConfirmationButton(bridge.cancelDiscardButton, bridge);
        bridge.discardPanel.SetActive(false);
        EditorUtility.SetDirty(bridge);
    }

    private static Button ProtectConfirmationButton(Button button, ItemTransferUIBridge bridge)
    {
        DiscardConfirmationButton guarded = button as DiscardConfirmationButton;
        if (guarded == null)
        {
            GameObject target = button.gameObject;
            Object.DestroyImmediate(button);
            guarded = target.AddComponent<DiscardConfirmationButton>();
            guarded.targetGraphic = target.GetComponent<Image>();
        }
        guarded.bridge = bridge;
        return guarded;
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static TMP_Text Text(Transform parent, string name, TMP_FontAsset font, int size,
        float x, float y, float width, float height)
    {
        TextMeshProUGUI text = Rect(parent, name, x, y, width, height).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.color = new Color(0.94f, 0.90f, 0.78f);
        text.raycastTarget = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = 17f;
        text.fontSizeMax = size;
        return text;
    }

    private static Button Button(Transform parent, string name, TMP_FontAsset font,
        float x, float y, float width, float height, out TMP_Text label)
    {
        RectTransform rect = Rect(parent, name, x, y, width, height);
        Image background = rect.gameObject.AddComponent<Image>();
        background.color = new Color(0.34f, 0.29f, 0.19f);
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        label = Text(rect, "Label", font, 21, 8, 2, width - 16f, height - 4f);
        label.alignment = TextAlignmentOptions.Center;
        return button;
    }
}
