using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class ItemManager : MonoBehaviour
{
    public ItemLibrarySO itemLibrary;

    private readonly Dictionary<string, ItemInstance> instances = new Dictionary<string, ItemInstance>();
    private readonly Dictionary<string, ItemInstance> uniqueInstances = new Dictionary<string, ItemInstance>();
    private readonly Dictionary<string, ItemDataSO> registeredDefinitions = new Dictionary<string, ItemDataSO>();
    private bool isUsingItem;

    public int CurrentRound { get; private set; }
    public int InstanceCount { get { return instances.Count; } }

    public bool TryCreateInstance(ItemDataSO definition, ItemOwner owner, string containerId,
        string sourceId, string originalSpawnPointId, out ItemInstance instance)
    {
        instance = null;
        if (isUsingItem || definition == null || !definition.TryValidate(out _) || !IsActiveOwner(owner)
            || !HasContainerId(owner, containerId))
        {
            return false;
        }
        if (registeredDefinitions.TryGetValue(definition.definitionId, out ItemDataSO registered)
            && registered != definition)
        {
            return false;
        }
        foreach (ItemInstance existing in instances.Values)
        {
            if (existing.Definition == definition && !IdentityMatches(existing, definition))
            {
                return false;
            }
        }
        if (definition.isUnique && (string.IsNullOrWhiteSpace(originalSpawnPointId)
            || uniqueInstances.ContainsKey(definition.uniqueId)))
        {
            // 重复创建不返回生成许可；要取已有数据请用TryGetUniqueInstance。
            return false;
        }

        instance = new ItemInstance(definition, owner, containerId ?? string.Empty,
            sourceId ?? string.Empty, originalSpawnPointId ?? string.Empty, CurrentRound);
        instances.Add(instance.InstanceId, instance);
        registeredDefinitions[definition.definitionId] = definition;
        if (definition.isUnique)
        {
            uniqueInstances.Add(instance.UniqueId, instance);
        }
        return true;
    }

    public bool TryGetInstance(string instanceId, out ItemInstance instance)
    {
        instance = null;
        return !string.IsNullOrWhiteSpace(instanceId) && instances.TryGetValue(instanceId, out instance);
    }

    public bool IsUniqueRegistered(string uniqueId)
    {
        return !string.IsNullOrWhiteSpace(uniqueId) && uniqueInstances.ContainsKey(uniqueId);
    }

    public bool TryGetUniqueInstance(string uniqueId, out ItemInstance instance)
    {
        instance = null;
        return !string.IsNullOrWhiteSpace(uniqueId) && uniqueInstances.TryGetValue(uniqueId, out instance);
    }

    public List<ItemInstance> GetInstances()
    {
        // 返回集合副本，容器不能绕过入口增删登记。
        return new List<ItemInstance>(instances.Values);
    }

    public bool TrySetOwner(string instanceId, ItemOwner owner, string containerId)
    {
        if (isUsingItem || !TryGetInstance(instanceId, out ItemInstance instance)
            || !IsActiveOwner(instance.Owner) || !IsActiveOwner(owner) || !HasContainerId(owner, containerId))
        {
            return false;
        }
        instance.Owner = owner;
        instance.ContainerId = containerId ?? string.Empty;
        instance.SpawnClaimed = true;
        // 移动不改变保有状态，也不把临时落地当作丢失。
        return true;
    }

    public bool TrySetRetained(string instanceId, bool isRetained)
    {
        if (isUsingItem || !TryGetInstance(instanceId, out ItemInstance instance)
            || !IsActiveOwner(instance.Owner))
        {
            return false;
        }
        instance.IsRetained = isRetained;
        return true;
    }

    public bool BeginRound(int round)
    {
        if (isUsingItem || round <= CurrentRound || round <= 0)
        {
            return false;
        }
        CurrentRound = round;
        foreach (ItemInstance instance in instances.Values)
        {
            instance.UseRound = round;
            instance.UsesThisRound = 0;
        }
        return true;
    }

    public bool RecordLoss(string instanceId, ItemLossReason reason)
    {
        if (isUsingItem || !Enum.IsDefined(typeof(ItemLossReason), reason)
            || !TryGetInstance(instanceId, out ItemInstance instance) || !IsActiveOwner(instance.Owner)
            || CurrentRound == int.MaxValue)
        {
            return false;
        }
        instance.Owner = string.IsNullOrEmpty(instance.UniqueId) ? ItemOwner.Lost : ItemOwner.PendingRecovery;
        instance.ContainerId = string.Empty;
        instance.IsRetained = false;
        instance.LastLossReason = reason;
        instance.RecoveryRound = instance.IsPendingRecovery ? CurrentRound + 1 : 0;
        instance.SpawnClaimed = false;
        return true;
    }

    public List<ItemInstance> GetRecoverableItems()
    {
        List<ItemInstance> result = new List<ItemInstance>();
        foreach (ItemInstance instance in uniqueInstances.Values)
        {
            if (instance.IsPendingRecovery && CurrentRound >= instance.RecoveryRound)
            {
                result.Add(instance);
            }
        }
        return result;
    }

    public bool TryClaimUniqueSpawn(string instanceId, string spawnPointId)
    {
        if (isUsingItem || !TryGetInstance(instanceId, out ItemInstance instance)
            || string.IsNullOrEmpty(instance.UniqueId) || instance.SpawnClaimed
            || instance.OriginalSpawnPointId != spawnPointId)
        {
            return false;
        }
        if (instance.IsPendingRecovery)
        {
            if (CurrentRound < instance.RecoveryRound)
            {
                return false;
            }
            instance.Owner = ItemOwner.World;
            instance.ContainerId = spawnPointId;
            instance.RecoveryRound = 0;
        }
        else if (instance.Owner != ItemOwner.World && instance.Owner != ItemOwner.Container)
        {
            return false;
        }
        // 06只有收到true才创建地图表现；查询数据本身不授予许可。
        instance.SpawnClaimed = true;
        return true;
    }

    public ItemUseResult TryUseItem(string instanceId, IHealthTarget target)
    {
        if (isUsingItem)
        {
            return ItemUseResult.Busy;
        }
        if (!TryGetInstance(instanceId, out ItemInstance instance))
        {
            return ItemUseResult.NotFound;
        }
        if (instance.Owner != ItemOwner.Backpack)
        {
            return ItemUseResult.NotCarried;
        }
        ItemDataSO definition = instance.Definition;
        if (definition == null || !IdentityMatches(instance, definition) || !definition.TryValidate(out _))
        {
            return ItemUseResult.InvalidConfiguration;
        }
        if (!definition.canUse)
        {
            return ItemUseResult.NotUsable;
        }
        isUsingItem = true;
        try
        {
            // 接口引用不自动识别Unity已销毁对象，需同时检查Unity的空值语义。
            if (target == null || (target is UnityEngine.Object unityTarget && unityTarget == null)
                || target.MaxHealth <= 0 || target.CurrentHealth <= 0 || target.CurrentHealth > target.MaxHealth)
            {
                return ItemUseResult.InvalidTarget;
            }
            if (target.CurrentHealth == target.MaxHealth)
            {
                return ItemUseResult.FullHealth;
            }
            if (CurrentRound <= 0)
            {
                return ItemUseResult.RoundNotStarted;
            }
            if (definition.usesPerRound > 0 && instance.UsesThisRound >= definition.usesPerRound)
            {
                return ItemUseResult.RoundLimitReached;
            }
            int healthBefore = target.CurrentHealth;
            // TryValidate已检查全部组合。前面的治疗回满后，后面的治疗自然跳过。
            foreach (ItemEffectSO effect in definition.effects)
            {
                effect.Execute(target);
            }
            if (target.CurrentHealth <= healthBefore)
            {
                return ItemUseResult.NoHealingApplied;
            }
            instance.UsesThisRound++;
            if (definition.consumedOnUse)
            {
                instance.Owner = ItemOwner.Consumed;
                instance.ContainerId = string.Empty;
                instance.IsRetained = false;
            }
            // 不在此通知UI；04/06完成布局移除后再统一发布显示更新。
            return ItemUseResult.Success;
        }
        finally
        {
            isUsingItem = false;
        }
    }

    [ContextMenu("校验物品目录 / Validate Item Library")]
    private void ValidateLibrary()
    {
        if (itemLibrary == null)
        {
            Debug.LogWarning("ItemManager尚未配置物品定义目录。", this);
        }
        else if (!itemLibrary.TryValidate(out string error))
        {
            Debug.LogWarning(error, itemLibrary);
        }
        else
        {
            Debug.Log("物品定义目录校验通过。", this);
        }
    }

    private static bool IsActiveOwner(ItemOwner owner)
    {
        return owner == ItemOwner.World || owner == ItemOwner.Container
            || owner == ItemOwner.Backpack || owner == ItemOwner.Storage;
    }

    private static bool IdentityMatches(ItemInstance instance, ItemDataSO definition)
    {
        // Play期间改身份必须拒绝，不能借编辑SO绕过唯一登记。
        return instance.DefinitionId == definition.definitionId
            && (string.IsNullOrEmpty(instance.UniqueId) ? !definition.isUnique
                : definition.isUnique && instance.UniqueId == definition.uniqueId);
    }

    private static bool HasContainerId(ItemOwner owner, string containerId)
    {
        return owner == ItemOwner.World || !string.IsNullOrWhiteSpace(containerId);
    }
}
