using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 参考CardGame/GamePlayPanel的直接刷新与Button.interactable，不在显示层推进业务。
public class GameFlowHUD : MonoBehaviour
{
    public TMP_Text stageText;
    public TMP_Text healthText;
    public TMP_Text promptText;
    public TMP_Text hintText;
    public TMP_Text feedbackText;
    public Button retryButton;
    public TMP_Text retryText;

    private GameFlowManager manager;
    private PlayerController player;
    private PlayerHealth health;
    private LocalizationManager language;
    private string lastPrompt;

    public void Bind(GameFlowManager source, PlayerController target)
    {
        Unsubscribe();
        manager = source;
        player = target;
        health = target != null ? target.health : null;
        language = source != null ? source.Language : LocalizationManager.Instance;
        if (isActiveAndEnabled) { Subscribe(); }
        Refresh();
    }

    private void OnEnable() { Subscribe(); Refresh(); }
    private void OnDisable() { Unsubscribe(); }

    private void Subscribe()
    {
        Unsubscribe();
        if (manager != null) { manager.Changed += Refresh; }
        if (language != null) { language.LanguageChanged += Refresh; }
        if (health != null) { health.HealthChanged += OnHealthChanged; }
        if (retryButton != null) { retryButton.onClick.AddListener(Retry); }
    }

    private void Unsubscribe()
    {
        if (manager != null) { manager.Changed -= Refresh; }
        if (language != null) { language.LanguageChanged -= Refresh; }
        if (health != null) { health.HealthChanged -= OnHealthChanged; }
        if (retryButton != null) { retryButton.onClick.RemoveListener(Retry); }
    }

    private void LateUpdate()
    {
        string prompt = player != null && player.interaction != null ? player.interaction.CurrentPromptKey : string.Empty;
        if (prompt != lastPrompt) { lastPrompt = prompt; RefreshPrompt(); }
    }

    public void Refresh()
    {
        LocalizationManager currentLanguage = manager != null ? manager.Language : language;
        if (currentLanguage != language)
        {
            if (language != null) { language.LanguageChanged -= Refresh; }
            language = currentLanguage;
            if (language != null && isActiveAndEnabled) { language.LanguageChanged += Refresh; }
        }
        string stageKey = "flow.stage." + (manager != null ? manager.Stage.ToString().ToLowerInvariant() : "booting");
        Set(stageText, Get(stageKey));
        Set(healthText, language != null && health != null
            ? language.FormatText("flow.health", health.CurrentHealth, health.MaxHealth) : string.Empty);
        Set(hintText, Get("flow.controls"));
        Set(retryText, Get("flow.retry"));
        string feedbackKey = manager != null ? manager.FeedbackKey : "flow.loading";
        Set(feedbackText, string.IsNullOrEmpty(feedbackKey) ? string.Empty : Get(feedbackKey));
        if (retryButton != null)
        {
            retryButton.gameObject.SetActive(manager != null && manager.CanRetry);
            retryButton.interactable = manager != null && manager.CanRetry;
        }
        RefreshPrompt();
    }

    private void RefreshPrompt() { Set(promptText, string.IsNullOrEmpty(lastPrompt) ? string.Empty : Get(lastPrompt)); }
    private void OnHealthChanged(int current, int maximum) { Refresh(); }
    private void Retry() { if (manager != null) { manager.Retry(); } }
    private string Get(string key) { return language != null ? language.GetText(key) : "[" + key + "]"; }
    private static void Set(TMP_Text target, string text) { if (target != null) { target.text = text; } }
}
