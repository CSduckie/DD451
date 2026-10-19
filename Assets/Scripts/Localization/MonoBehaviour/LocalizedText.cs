using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class LocalizedText : MonoBehaviour
{
    public TMP_Text targetText;
    public string key;
    public LocalizationManager localizationManager;
    private LocalizationManager subscribedManager;

    private void Awake()
    {
        if (targetText == null)
        {
            targetText = GetComponent<TMP_Text>();
        }
    }

    private void OnEnable()
    {
        RefreshText();
    }

    private void Start()
    {
        // 首次 OnEnable 可能早于 Bootstrap.Awake，首帧显示前再尝试一次。
        RefreshText();
    }

    private void LateUpdate()
    {
        if (subscribedManager == null || !subscribedManager.isActiveAndEnabled)
        {
            RefreshText();
        }
    }

    public void RefreshText()
    {
        LocalizationManager resolved = LocalizationManager.Resolve(localizationManager);
        if (resolved != subscribedManager)
        {
            Unsubscribe();
            if (isActiveAndEnabled && resolved != null)
            {
                subscribedManager = resolved;
                subscribedManager.LanguageChanged += RefreshText;
            }
        }
        localizationManager = resolved;
        if (targetText == null)
        {
            targetText = GetComponent<TMP_Text>();
        }
        if (targetText != null)
        {
            targetText.text = resolved != null ? resolved.GetText(key) : "[" + key + "]";
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (subscribedManager != null)
        {
            subscribedManager.LanguageChanged -= RefreshText;
        }
        subscribedManager = null;
    }
}
