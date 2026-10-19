using TMPro;
using UnityEngine;

// 这里只演示文字刷新。真实使用物品与玩家生命的联验仍在两个原测试面板。
public class LocalizationTestDriver : MonoBehaviour
{
    public LocalizationManager localizationManager;
    public ItemDataSO item;
    public TMP_Text currentLanguageText;
    public TMP_Text valueText;
    public TMP_Text itemNameText;
    public TMP_Text itemDescriptionText;
    public TMP_Text resultText;
    public int sampleCurrent = 2;
    public int sampleMaximum = 3;

    private LocalizationManager subscribedManager;

    private void OnEnable()
    {
        RefreshText();
    }

    private void Start()
    {
        RefreshText();
    }

    private void OnDisable()
    {
        if (subscribedManager != null)
        {
            subscribedManager.LanguageChanged -= RefreshText;
        }
        subscribedManager = null;
    }

    public void RefreshText()
    {
        LocalizationManager manager = LocalizationManager.Resolve(localizationManager);
        if (subscribedManager != manager)
        {
            if (subscribedManager != null) { subscribedManager.LanguageChanged -= RefreshText; }
            subscribedManager = manager;
            if (subscribedManager != null && isActiveAndEnabled)
            {
                subscribedManager.LanguageChanged += RefreshText;
            }
        }
        if (manager == null) { return; }
        localizationManager = manager;
        currentLanguageText.text = manager.FormatText("tests.localization.current_language",
            manager.GetText(manager.CurrentLanguage == GameLanguage.Chinese
                ? "common.language.chinese" : "common.language.english"));
        valueText.text = manager.FormatText("player.health.value", sampleCurrent, sampleMaximum);
        if (item != null)
        {
            itemNameText.text = manager.GetText(item.nameKey);
            itemDescriptionText.text = manager.GetText(item.descriptionKey);
            resultText.text = manager.FormatText("tests.localization.sample_result",
                manager.GetText(item.nameKey), manager.GetText("items.use.full_health"));
        }
    }
}
