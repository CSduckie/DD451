using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class LootTableEntry
{
    public string entryId;
    public ItemDataSO item;
    public bool useDefaultProbability = true;
    [Range(0f, 1f)] public float overrideProbability = 1f;

    public float Probability { get { return useDefaultProbability && item != null ? item.defaultSpawnProbability : overrideProbability; } }
}

[CreateAssetMenu(fileName = "LootTable", menuName = "Loot/Loot Table")]
public class LootTableSO : ScriptableObject
{
    public List<LootTableEntry> entries = new List<LootTableEntry>();

    public bool TryValidate(out string error)
    {
        if (entries == null)
        {
            error = name + ": 掉落条目列表为空引用；空箱请使用空列表。";
            return false;
        }
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (LootTableEntry entry in entries)
        {
            if (entry == null || !LootSourceState.IsStableSegment(entry.entryId) || !ids.Add(entry.entryId))
            {
                error = name + ": entryId 为空、包含斜线或重复。";
                return false;
            }
            if (entry.item == null)
            {
                error = name + "/" + entry.entryId + ": 物品引用为空。";
                return false;
            }
            if (!entry.item.TryValidate(out string itemError))
            {
                error = name + "/" + entry.entryId + ": " + itemError;
                return false;
            }
            float probability = entry.Probability;
            if (float.IsNaN(probability) || float.IsInfinity(probability) || probability < 0f || probability > 1f)
            {
                error = name + "/" + entry.entryId + ": 概率必须在 0 到 1 之间。";
                return false;
            }
            if (entry.item.isUnique && !uniqueIds.Add(entry.item.uniqueId))
            {
                error = name + "/" + entry.entryId + ": 同一个唯一物品配置了多个原点。";
                return false;
            }
        }
        error = null;
        return true;
    }
}
