using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ShelterView : MonoBehaviour
{
    public ShelterManager manager;
    public LocalizationManager localization;
    public WarehouseUIController warehouseUI;
    public GameObject panelRoot;
    public TMP_Text titleText;
    public TMP_Text evText;
    public TMP_Text currentSizeText;
    public TMP_Text targetSizeText;
    public TMP_Text statusText;
    public TMP_Text resultText;
    public TMP_Text upgradeText;
    public TMP_Text openWarehouseText;
    public TMP_Text languageHintText;
    public Button upgradeButton;
    public Button openWarehouseButton;

    private ShelterManager boundManager;
    private LocalizationManager boundLanguage;
    private bool hasResult;
    private ShelterUpgradeResult lastResult;
    private int fromWidth;
    private int fromHeight;
    private int toWidth;
    private int toHeight;

    private void OnEnable()
    {
        if (upgradeButton != null) { upgradeButton.onClick.AddListener(Upgrade); }
        if (openWarehouseButton != null) { openWarehouseButton.onClick.AddListener(OpenWarehouse); }
        Bind(manager, localization);
    }

    private void Start()
    {
        // 场景UI可能先于Bootstrap Awake，Start时再次绑定存活入口。
        Bind(manager, localization);
    }

    public void Bind(ShelterManager source, LocalizationManager language)
    {
        Unsubscribe();
        manager = source;
        localization = language;
        boundManager = ShelterManager.Resolve(source);
        boundLanguage = LocalizationManager.Resolve(language);
        if (isActiveAndEnabled)
        {
            if (boundManager != null) { boundManager.Changed += RefreshText; }
            if (boundLanguage != null) { boundLanguage.LanguageChanged += RefreshText; }
        }
        if (boundManager != null) { boundManager.RefreshEv(); }
        RefreshText();
    }

    public void Open()
    {
        if (panelRoot != null) { panelRoot.SetActive(true); }
        Bind(manager, localization);
    }

    public void Close()
    {
        if (panelRoot != null) { panelRoot.SetActive(false); }
    }

    public void Upgrade()
    {
        if (boundManager == null) { Bind(manager, localization); }
        WarehouseManager warehouse = boundManager != null ? boundManager.Warehouse : null;
        fromWidth = warehouse != null ? warehouse.Width : 0;
        fromHeight = warehouse != null ? warehouse.Height : 0;
        lastResult = boundManager != null ? boundManager.TryUpgrade() : ShelterUpgradeResult.NotReady;
        toWidth = warehouse != null ? warehouse.Width : 0;
        toHeight = warehouse != null ? warehouse.Height : 0;
        hasResult = true;
        RefreshText();
    }

    public void OpenWarehouse()
    {
        if (warehouseUI != null) { warehouseUI.Open(); }
        if (boundManager != null) { boundManager.RefreshEv(); }
        RefreshText();
    }

    public void RefreshText()
    {
        if (boundLanguage == null) { return; }
        Set(titleText, boundLanguage.GetText("shelter.title"));
        Set(upgradeText, boundLanguage.GetText("shelter.free_upgrade"));
        Set(openWarehouseText, boundLanguage.GetText("shelter.open_warehouse"));
        Set(languageHintText, boundLanguage.GetText("common.language.toggle_hint"));
        WarehouseManager warehouse = boundManager != null ? boundManager.Warehouse : null;
        ShelterUpgradeSO upgrade = boundManager != null ? boundManager.firstUpgrade : null;
        int width = warehouse != null ? warehouse.Width : 0;
        int height = warehouse != null ? warehouse.Height : 0;
        Set(evText, boundLanguage.FormatText("shelter.ev", boundManager != null ? boundManager.CurrentEv : 0,
            upgrade != null ? upgrade.requiredEv : 7));
        Set(currentSizeText, boundLanguage.FormatText("shelter.current_size", width, height, (long)width * height));
        Set(targetSizeText, upgrade != null ? boundLanguage.FormatText("shelter.target_size",
            upgrade.targetWidth, upgrade.targetHeight, (long)upgrade.targetWidth * upgrade.targetHeight) : boundLanguage.GetText("shelter.result.invalid_configuration"));
        ShelterUpgradeResult availability = boundManager != null ? boundManager.GetUpgradeAvailability() : ShelterUpgradeResult.NotReady;
        Set(statusText, boundLanguage.GetText(ResultKey(availability)));
        // 参考GamePlayPanel的明确条件赋值；文字不参与业务计算。
        if (upgradeButton != null) { upgradeButton.interactable = availability == ShelterUpgradeResult.Success; }
        if (openWarehouseButton != null) { openWarehouseButton.interactable = warehouse != null && warehouse.IsManagementAllowed; }
        if (!hasResult)
        {
            Set(resultText, boundLanguage.GetText("shelter.explanation"));
        }
        else if (lastResult == ShelterUpgradeResult.Success)
        {
            Set(resultText, boundLanguage.FormatText("shelter.upgraded", fromWidth, fromHeight,
                (long)fromWidth * fromHeight, toWidth, toHeight, (long)toWidth * toHeight));
        }
        else
        {
            Set(resultText, boundLanguage.GetText(ResultKey(lastResult)));
        }
    }

    public static string ResultKey(ShelterUpgradeResult result)
    {
        switch (result)
        {
            case ShelterUpgradeResult.Success: return "shelter.result.eligible";
            case ShelterUpgradeResult.NotReady: return "shelter.result.not_ready";
            case ShelterUpgradeResult.NotInShelter: return "shelter.result.not_in_shelter";
            case ShelterUpgradeResult.AlreadyActivated: return "shelter.result.already_activated";
            case ShelterUpgradeResult.InsufficientEv: return "shelter.result.insufficient_ev";
            case ShelterUpgradeResult.Busy: return "shelter.result.busy";
            default: return "shelter.result.invalid_configuration";
        }
    }

    private static void Set(TMP_Text target, string text)
    {
        if (target != null) { target.text = text; }
    }

    private void Unsubscribe()
    {
        if (boundManager != null) { boundManager.Changed -= RefreshText; }
        if (boundLanguage != null) { boundLanguage.LanguageChanged -= RefreshText; }
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (upgradeButton != null) { upgradeButton.onClick.RemoveListener(Upgrade); }
        if (openWarehouseButton != null) { openWarehouseButton.onClick.RemoveListener(OpenWarehouse); }
    }
}
