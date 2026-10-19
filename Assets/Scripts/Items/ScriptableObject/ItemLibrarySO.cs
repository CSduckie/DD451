using System;
using System.Collections.Generic;
using UnityEngine;

// 保留 CardGame 的列表引用形式；目录不持有运行中的物品实例。
[CreateAssetMenu(fileName = "ItemLibrary", menuName = "Items/Item Library")]
public class ItemLibrarySO : ScriptableObject
{
    public List<ItemLibraryEntry> itemLibraryList = new List<ItemLibraryEntry>();

    public ItemDataSO FindDefinition(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId) || itemLibraryList == null)
        {
            return null;
        }

        foreach (ItemLibraryEntry entry in itemLibraryList)
        {
            if (entry.itemData != null
                && string.Equals(entry.itemData.definitionId, definitionId, StringComparison.Ordinal))
            {
                return entry.itemData;
            }
        }

        return null;
    }

    public bool TryValidate(out string error)
    {
        if (itemLibraryList == null || itemLibraryList.Count == 0)
        {
            error = "物品目录不能为空。";
            return false;
        }

        HashSet<string> definitionIds = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> uniqueIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < itemLibraryList.Count; i++)
        {
            ItemDataSO itemData = itemLibraryList[i].itemData;
            if (itemData == null)
            {
                error = $"物品目录第 {i + 1} 项没有引用定义。";
                return false;
            }

            if (!itemData.TryValidate(out string itemError))
            {
                error = $"物品目录第 {i + 1} 项无效：{itemError}";
                return false;
            }

            if (!definitionIds.Add(itemData.definitionId))
            {
                error = $"物品定义 ID 重复：{itemData.definitionId}";
                return false;
            }

            if (itemData.isUnique && !uniqueIds.Add(itemData.uniqueId))
            {
                error = $"唯一剧情物品标识重复：{itemData.uniqueId}";
                return false;
            }
        }

        error = null;
        return true;
    }

    [ContextMenu("校验物品目录")]
    private void ValidateConfiguration()
    {
        if (TryValidate(out string error))
        {
            Debug.Log($"物品目录校验通过，共 {itemLibraryList.Count} 项。", this);
        }
        else
        {
            Debug.LogWarning($"物品目录无效：{error}", this);
        }
    }
}

[Serializable]
public struct ItemLibraryEntry
{
    public ItemDataSO itemData;
}
