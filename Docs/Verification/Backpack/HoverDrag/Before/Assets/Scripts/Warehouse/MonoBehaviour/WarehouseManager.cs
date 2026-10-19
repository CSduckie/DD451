using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class WarehouseManager : MonoBehaviour
{
    public WarehouseConfigSO config;
    public ItemManager itemManager;
    public string containerId = "shelter-warehouse";

    private WarehouseGrid grid;
    private ItemManager runtimeItems;
    private string runtimeContainerId;
    private bool busy;
    private bool managementAllowed;
    private Scene contextScene;
    private UnityEngine.Object interactionOwner;
    private static int playSession;
    private int initializedSession = -1;

    public bool IsReady { get { return initializedSession == playSession && grid != null && runtimeItems != null; } }
    public int Width { get { return IsReady ? grid.Width : 0; } }
    public int Height { get { return IsReady ? grid.Height : 0; } }
    public string ContainerId { get { return runtimeContainerId ?? containerId; } }
    public ItemManager Items { get { return runtimeItems; } }
    public bool IsManagementAllowed
    {
        get { return IsReady && isActiveAndEnabled && managementAllowed && contextScene.IsValid() && contextScene.isLoaded; }
    }
    public bool HasPendingInteraction { get { return interactionOwner != null; } }
    public event Action Changed;
    public event Action ContextChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void BeginPlaySession()
    {
        // 关闭Domain Reload时，也必须把完整新运行与同次运行的场景重载分开。
        playSession++;
    }

    private void Awake()
    {
        ItemManager source = itemManager;
        GameBootstrap root = GetComponentInParent<GameBootstrap>();
        if (source == null && root != null) { source = root.GetComponentInChildren<ItemManager>(true); }
        Initialize(source);
    }

    private void OnEnable()
    {
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    public bool Initialize(ItemManager source)
    {
        if (initializedSession != playSession)
        {
            initializedSession = playSession;
            grid = null;
            runtimeItems = null;
            runtimeContainerId = null;
            busy = false;
            managementAllowed = false;
            contextScene = default;
            interactionOwner = null;
        }
        // 打开新UI或重载场景只重绑，不能按SO重建已经扩容的运行布局。
        if (grid != null) { return runtimeItems != null && runtimeItems == source; }
        if (source == null || config == null || config.width <= 0 || config.height <= 0
            || string.IsNullOrWhiteSpace(containerId))
        {
            return false;
        }
        runtimeItems = source;
        runtimeContainerId = containerId;
        grid = new WarehouseGrid(config.width, config.height);
        return true;
    }

    public static WarehouseManager Resolve()
    {
        GameBootstrap root = GameBootstrap.Instance;
        if (root == null || !root.isActiveAndEnabled) { return null; }
        ItemManager source = root.GetComponentInChildren<ItemManager>(true);
        foreach (WarehouseManager manager in root.GetComponentsInChildren<WarehouseManager>(true))
        {
            if (manager.isActiveAndEnabled && manager.GetComponentInParent<GameBootstrap>() == root
                && manager.Initialize(source))
            {
                return manager;
            }
        }
        return null;
    }

    public void SetShelterContext(bool allowed, Scene ownerScene)
    {
        // 旧场景迟到的关闭不能撤销新场景已明确授予的权限。
        if (!allowed && managementAllowed && ownerScene != contextScene) { return; }
        bool nextAllowed = allowed && ownerScene.IsValid() && ownerScene.isLoaded;
        bool changed = managementAllowed != nextAllowed || (nextAllowed && contextScene != ownerScene);
        managementAllowed = nextAllowed;
        contextScene = nextAllowed ? ownerScene : default;
        if (changed || !nextAllowed) { interactionOwner = null; }
        if (changed) { RaiseEvent(ContextChanged); }
    }

    public bool TryClaimInteraction(UnityEngine.Object owner)
    {
        if (owner == null || !IsReady || !IsManagementAllowed || busy
            || (interactionOwner != null && interactionOwner != owner))
        {
            return false;
        }
        if (interactionOwner != owner)
        {
            interactionOwner = owner;
            RaiseEvent(ContextChanged);
        }
        return interactionOwner == owner;
    }

    public bool OwnsInteraction(UnityEngine.Object owner)
    {
        return owner != null && interactionOwner == owner;
    }

    public void ReleaseInteraction(UnityEngine.Object owner)
    {
        if (interactionOwner != null && interactionOwner == owner)
        {
            interactionOwner = null;
            RaiseEvent(ContextChanged);
        }
    }

    public IReadOnlyList<WarehousePlacement> GetPlacements()
    {
        return IsReady ? grid.GetPlacements() : Array.AsReadOnly(new WarehousePlacement[0]);
    }

    public bool TryGetPlacement(string instanceId, out WarehousePlacement placement)
    {
        placement = null;
        return IsReady && grid.TryGetPlacement(instanceId, out placement);
    }

    public WarehouseResult TryGetStoredItem(string instanceId, out ItemInstance item)
    {
        item = null;
        if (!IsReady) { return WarehouseResult.NotReady; }
        if (!runtimeItems.TryGetInstance(instanceId, out item)) { return WarehouseResult.NotFound; }
        if (item.Owner != ItemOwner.Storage) { return WarehouseResult.WrongOwner; }
        if (item.ContainerId != runtimeContainerId) { return WarehouseResult.WrongContainer; }
        if (!grid.TryGetPlacement(instanceId, out WarehousePlacement placement))
        {
            return WarehouseResult.MissingPlacement;
        }
        if (!ReferenceEquals(item, placement.Item)) { return WarehouseResult.InvalidConfiguration; }
        return grid.CanMove(instanceId, placement.X, placement.Y, placement.Rotated);
    }

    public WarehouseResult CanMove(string instanceId, int x, int y, bool rotated)
    {
        if (!IsManagementAllowed) { return WarehouseResult.NotInShelter; }
        WarehouseResult result = TryGetStoredItem(instanceId, out _);
        return result == WarehouseResult.Success ? grid.CanMove(instanceId, x, y, rotated) : result;
    }

    public WarehouseResult TryMove(string instanceId, int x, int y, bool rotated)
    {
        if (busy) { return WarehouseResult.Busy; }
        WarehouseResult result = CanMove(instanceId, x, y, rotated);
        if (result != WarehouseResult.Success) { return result; }
        busy = true;
        try
        {
            result = grid.TryMove(instanceId, x, y, rotated);
            if (result == WarehouseResult.Success) { RaiseEvent(Changed); }
            return result;
        }
        finally { busy = false; }
    }

    public WarehouseResult TryAbandon(string instanceId)
    {
        if (busy) { return WarehouseResult.Busy; }
        if (!IsManagementAllowed) { return WarehouseResult.NotInShelter; }
        WarehouseResult result = TryGetStoredItem(instanceId, out _);
        if (result != WarehouseResult.Success) { return result; }
        busy = true;
        try
        {
            if (!runtimeItems.RecordLoss(instanceId, ItemLossReason.ShelterAbandonment))
            {
                return WarehouseResult.LossRejected;
            }
            // 01已将Owner改为Lost/PendingRecovery；按已验证的ID清布局，不能再要求Storage。
            grid.TryRemove(instanceId, out _);
            RaiseEvent(Changed);
            return WarehouseResult.Success;
        }
        finally { busy = false; }
    }

    public WarehouseResult CanPlaceExternal(string instanceId, int x, int y, bool rotated)
    {
        WarehouseResult result = GetPlacementCandidate(instanceId, out ItemInstance item);
        return result == WarehouseResult.Success ? grid.CanPlace(item, x, y, rotated) : result;
    }

    // 只供快捷转移查询；正常拖拽仍按指针指定的格子和方向调用CanPlaceExternal。
    public WarehouseResult TryFindSpaceForExternal(string instanceId, bool preferredRotated,
        out Vector2Int cell, out bool rotated)
    {
        cell = new Vector2Int(-1, -1);
        rotated = preferredRotated;
        if (!IsReady) { return WarehouseResult.NotReady; }
        if (!IsManagementAllowed) { return WarehouseResult.NotInShelter; }
        if (busy || HasPendingInteraction) { return WarehouseResult.Busy; }
        WarehouseResult result = GetPlacementCandidate(instanceId, out ItemInstance item);
        if (result != WarehouseResult.Success) { return result; }

        result = FindSpaceForOrientation(item, preferredRotated, out cell);
        if (result != WarehouseResult.Overlap && result != WarehouseResult.OutOfBounds) { return result; }
        if (!item.Definition.canRotate || item.Definition.width == item.Definition.height) { return result; }

        // 先查完当前方向，再试另一个方向，不能让靠前的旋转空位抢先。
        WarehouseResult alternate = FindSpaceForOrientation(item, !preferredRotated, out cell);
        if (alternate == WarehouseResult.Success) { rotated = !preferredRotated; }
        return alternate == WarehouseResult.OutOfBounds && result == WarehouseResult.Overlap
            ? WarehouseResult.Overlap : alternate;
    }

    // 06同步协调来源、目标与01归属；这些局部入口不改归属、不设保有、不提前通知。
    public WarehouseResult TryAddPlacement(string instanceId, int x, int y, bool rotated)
    {
        if (busy) { return WarehouseResult.Busy; }
        WarehouseResult result = GetPlacementCandidate(instanceId, out ItemInstance item);
        return result == WarehouseResult.Success ? grid.TryAdd(item, x, y, rotated) : result;
    }

    public WarehouseResult TryRemovePlacement(string instanceId, out WarehousePlacement removed)
    {
        removed = null;
        if (busy) { return WarehouseResult.Busy; }
        if (!IsReady) { return WarehouseResult.NotReady; }
        // 转移或损失完成后的旧摆放仍需清理，所以不再检查旧Owner。
        return grid.TryRemove(instanceId, out removed);
    }

    public WarehouseResult TryRestorePlacement(WarehousePlacement placement)
    {
        if (busy) { return WarehouseResult.Busy; }
        if (placement == null) { return WarehouseResult.MissingPlacement; }
        WarehouseResult result = GetPlacementCandidate(placement.InstanceId, out ItemInstance item);
        if (result != WarehouseResult.Success) { return result; }
        if (!ReferenceEquals(item, placement.Item)) { return WarehouseResult.InvalidConfiguration; }
        return grid.TryAdd(item, placement.X, placement.Y, placement.Rotated);
    }

    public WarehouseResult CanExpand(int width, int height)
    {
        if (!IsReady) { return WarehouseResult.NotReady; }
        if (HasPendingInteraction) { return WarehouseResult.Busy; }
        foreach (WarehousePlacement placement in grid.GetPlacements())
        {
            WarehouseResult result = TryGetStoredItem(placement.InstanceId, out _);
            if (result != WarehouseResult.Success) { return result; }
        }
        return grid.CanExpand(width, height);
    }

    public WarehouseResult TryExpand(int width, int height)
    {
        if (busy) { return WarehouseResult.Busy; }
        WarehouseResult result = CanExpand(width, height);
        // 扩容保持静默，Shelter写入已激活标记后才统一NotifyChanged。
        return result == WarehouseResult.Success ? grid.TryExpand(width, height) : result;
    }

    public void NotifyChanged()
    {
        if (!IsReady || busy) { return; }
        busy = true;
        try { RaiseEvent(Changed); }
        finally { busy = false; }
    }

    private WarehouseResult FindSpaceForOrientation(ItemInstance item, bool rotated, out Vector2Int cell)
    {
        cell = new Vector2Int(-1, -1);
        WarehouseResult result = grid.CanPlace(item, 0, 0, rotated);
        if (result == WarehouseResult.Success)
        {
            cell = Vector2Int.zero;
            return result;
        }
        // 原点已越界时，该方向不可能在其他非负格子放入；其余校验错误原样返回。
        if (result != WarehouseResult.Overlap) { return result; }
        int width = rotated ? item.Definition.height : item.Definition.width;
        int height = rotated ? item.Definition.width : item.Definition.height;
        for (int y = 0; y <= grid.Height - height; y++)
        {
            for (int x = 0; x <= grid.Width - width; x++)
            {
                result = grid.CanPlace(item, x, y, rotated);
                if (result == WarehouseResult.Success)
                {
                    cell = new Vector2Int(x, y);
                    return result;
                }
                if (result != WarehouseResult.Overlap) { return result; }
            }
        }
        return WarehouseResult.Overlap;
    }

    private WarehouseResult GetPlacementCandidate(string instanceId, out ItemInstance item)
    {
        item = null;
        if (!IsReady) { return WarehouseResult.NotReady; }
        if (!runtimeItems.TryGetInstance(instanceId, out item)) { return WarehouseResult.NotFound; }
        if (item.Owner != ItemOwner.World && item.Owner != ItemOwner.Container
            && item.Owner != ItemOwner.Backpack && item.Owner != ItemOwner.Storage)
        {
            return WarehouseResult.WrongOwner;
        }
        if (item.Owner == ItemOwner.Storage && item.ContainerId != runtimeContainerId)
        {
            return WarehouseResult.WrongContainer;
        }
        return WarehouseResult.Success;
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (managementAllowed && contextScene == scene) { SetShelterContext(false, scene); }
    }

    private void OnDisable()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SetShelterContext(false, contextScene);
    }

    private void OnDestroy()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        interactionOwner = null;
        Changed = null;
        ContextChanged = null;
    }

    private void RaiseEvent(Action callbacks)
    {
        if (callbacks == null) { return; }
        foreach (Action callback in callbacks.GetInvocationList())
        {
            try { callback(); }
            catch (Exception exception) { Debug.LogException(exception, this); }
        }
    }
}
