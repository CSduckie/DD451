using System;
using UnityEngine;

public enum ShelterUpgradeResult
{
    Success,
    NotReady,
    NotInShelter,
    AlreadyActivated,
    InsufficientEv,
    InvalidConfiguration,
    Busy
}

[DisallowMultipleComponent]
public class ShelterManager : MonoBehaviour
{
    public ItemManager itemManager;
    public WarehouseManager warehouseManager;
    public ShelterUpgradeSO firstUpgrade;

    private ItemManager runtimeItems;
    private WarehouseManager runtimeWarehouse;
    private bool upgrading;
    private bool notifying;
    private static int playSession;
    private int initializedSession = -1;

    public int CurrentEv { get; private set; }
    public bool IsUpgradeActivated { get; private set; }
    public WarehouseManager Warehouse { get { return runtimeWarehouse; } }
    public bool IsReady { get { return runtimeItems != null && runtimeWarehouse != null && runtimeWarehouse.IsReady; } }
    public event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void BeginSession()
    {
        playSession++;
    }

    public static ShelterManager Resolve(ShelterManager preferred = null)
    {
        GameBootstrap bootstrap = GameBootstrap.Instance;
        if (bootstrap != null && bootstrap.isActiveAndEnabled)
        {
            foreach (ShelterManager candidate in bootstrap.GetComponentsInChildren<ShelterManager>(true))
            {
                if (candidate.isActiveAndEnabled && candidate.Initialize())
                {
                    return candidate;
                }
            }
        }
        return !Application.isPlaying && preferred != null && preferred.Initialize() ? preferred : null;
    }

    private void Start()
    {
        Initialize();
        RefreshEv();
    }

    private void OnEnable()
    {
        Initialize();
    }

    public bool Initialize()
    {
        if (!isActiveAndEnabled) { return false; }
        if (initializedSession != playSession)
        {
            Unsubscribe();
            runtimeItems = null;
            runtimeWarehouse = null;
            CurrentEv = 0;
            IsUpgradeActivated = false;
            initializedSession = playSession;
        }

        GameBootstrap bootstrap = GetComponentInParent<GameBootstrap>();
        if (Application.isPlaying && (bootstrap == null || bootstrap != GameBootstrap.Instance || !bootstrap.isActiveAndEnabled))
        {
            return false;
        }
        ItemManager source = bootstrap != null ? bootstrap.GetComponentInChildren<ItemManager>(true) : itemManager;
        WarehouseManager warehouse = bootstrap != null ? bootstrap.GetComponentInChildren<WarehouseManager>(true) : warehouseManager;
        if (source == null || warehouse == null || !warehouse.Initialize(source))
        {
            return false;
        }
        // 已建立的运行数据不能因为另一场景的Inspector引用而被换成另一份身份表。
        if (runtimeItems != null && (runtimeItems != source || runtimeWarehouse != warehouse))
        {
            return false;
        }
        runtimeItems = source;
        runtimeWarehouse = warehouse;
        if (isActiveAndEnabled)
        {
            Unsubscribe();
            runtimeWarehouse.Changed += OnWarehouseChanged;
            runtimeWarehouse.ContextChanged += NotifyChanged;
        }
        return true;
    }

    public int RefreshEv()
    {
        if (!Initialize())
        {
            return CurrentEv;
        }
        int next = CalculateEv();
        if (CurrentEv != next)
        {
            CurrentEv = next;
            NotifyChanged();
        }
        return CurrentEv;
    }

    private int CalculateEv()
    {
        long total = 0;
        foreach (ItemInstance item in runtimeItems.GetInstances())
        {
            if (item == null || item.Definition == null || !item.IsRetained)
            {
                continue;
            }
            // 临时丢地仍保有；真正损失由01清除。终止状态不能贡献EV。
            if (item.Owner == ItemOwner.World || item.Owner == ItemOwner.Container
                || item.Owner == ItemOwner.Backpack || item.Owner == ItemOwner.Storage)
            {
                total += Math.Max(0, item.Definition.ev);
            }
        }
        return (int)Math.Min(int.MaxValue, total);
    }

    // 这里只读上次显示值；实际提交仍会重新从01求和。
    public ShelterUpgradeResult GetUpgradeAvailability()
    {
        if (!Initialize() || !IsReady) { return ShelterUpgradeResult.NotReady; }
        if (!runtimeWarehouse.IsManagementAllowed) { return ShelterUpgradeResult.NotInShelter; }
        if (IsUpgradeActivated) { return ShelterUpgradeResult.AlreadyActivated; }
        if (runtimeWarehouse.HasPendingInteraction) { return ShelterUpgradeResult.Busy; }
        if (firstUpgrade == null || firstUpgrade.requiredEv <= 0
            || runtimeWarehouse.CanExpand(firstUpgrade.targetWidth, firstUpgrade.targetHeight) != WarehouseResult.Success)
        {
            return ShelterUpgradeResult.InvalidConfiguration;
        }
        return CurrentEv < firstUpgrade.requiredEv ? ShelterUpgradeResult.InsufficientEv : ShelterUpgradeResult.Success;
    }

    public ShelterUpgradeResult TryUpgrade()
    {
        if (upgrading || notifying) { return ShelterUpgradeResult.Busy; }
        if (!Initialize()) { return ShelterUpgradeResult.NotReady; }
        RefreshEv();
        ShelterUpgradeResult result = GetUpgradeAvailability();
        if (result != ShelterUpgradeResult.Success) { return result; }
        upgrading = true;
        try
        {
            WarehouseResult expanded = runtimeWarehouse.TryExpand(firstUpgrade.targetWidth, firstUpgrade.targetHeight);
            if (expanded != WarehouseResult.Success)
            {
                return expanded == WarehouseResult.Busy ? ShelterUpgradeResult.Busy : ShelterUpgradeResult.InvalidConfiguration;
            }
            // 静默扩容完成后才写激活标记，再让所有界面读取完整状态。
            IsUpgradeActivated = true;
            runtimeWarehouse.NotifyChanged();
            return ShelterUpgradeResult.Success;
        }
        finally
        {
            upgrading = false;
        }
    }

    private void OnWarehouseChanged()
    {
        if (Initialize()) { CurrentEv = CalculateEv(); }
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        if (notifying) { return; }
        notifying = true;
        try
        {
            if (Changed == null) { return; }
            foreach (Action listener in Changed.GetInvocationList())
            {
                try { listener(); }
                catch (Exception exception) { Debug.LogException(exception, this); }
            }
        }
        finally { notifying = false; }
    }

    private void Unsubscribe()
    {
        if (runtimeWarehouse == null) { return; }
        runtimeWarehouse.Changed -= OnWarehouseChanged;
        runtimeWarehouse.ContextChanged -= NotifyChanged;
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        Changed = null;
    }
}
