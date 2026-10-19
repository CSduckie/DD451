using System;
using System.Collections.Generic;
using UnityEngine;

public static class LootGenerator
{
    // 改编CardGame/LastColumnChangeValueEffect的Random.value比较；每项独立抽样。
    public static bool IsHit(float probability, float sample)
    {
        if (float.IsNaN(probability) || float.IsInfinity(probability) || probability < 0f || probability > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(probability));
        }
        if (probability == 0f) { return false; }
        if (probability == 1f) { return true; }
        if (float.IsNaN(sample) || float.IsInfinity(sample) || sample < 0f || sample > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(sample));
        }
        return sample < probability;
    }

    public static bool TrySelect(LootTableSO table, ItemManager items, string sourceId,
        out List<LootTableEntry> selected, out string error, Func<float> nextSample = null)
    {
        selected = new List<LootTableEntry>();
        if (table == null) { error = "掉落表为空。"; return false; }
        if (!table.TryValidate(out error)) { return false; }
        if (items == null || items.CurrentRound <= 0) { error = "01尚未开始轮次。"; return false; }
        HashSet<string> recoverable = new HashSet<string>();
        foreach (ItemInstance item in items.GetRecoverableItems()) { recoverable.Add(item.InstanceId); }
        foreach (LootTableEntry entry in table.entries)
        {
            if (entry.item.isUnique && items.TryGetUniqueInstance(entry.item.uniqueId, out ItemInstance existing))
            {
                string origin = sourceId + "/" + entry.entryId;
                if (existing.OriginalSpawnPointId != origin)
                {
                    error = origin + ": 唯一物品原点冲突，已登记原点为 " + existing.OriginalSpawnPointId;
                    selected.Clear();
                    return false;
                }
                // 找回不再抽概率；仍在原来源的实例也必须接回同一个身份。
                if (recoverable.Contains(existing.InstanceId)
                    || ((existing.Owner == ItemOwner.World || existing.Owner == ItemOwner.Container)
                        && (existing.ContainerId == sourceId || existing.ContainerId == origin)))
                {
                    selected.Add(entry);
                }
                continue;
            }
            float probability = entry.Probability;
            if (probability == 1f || (probability > 0f && IsHit(probability, nextSample == null ? UnityEngine.Random.value : nextSample())))
            {
                selected.Add(entry);
            }
        }
        error = null;
        return true;
    }
}
