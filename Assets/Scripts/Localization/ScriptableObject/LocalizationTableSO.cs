using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "LocalizationTable", menuName = "Localization/Text Table")]
public class LocalizationTableSO : ScriptableObject
{
    [Header("稳定文本键与中英译文")]
    public List<LocalizationEntry> entries = new List<LocalizationEntry>();

    public bool TryValidate(out string error)
    {
        if (entries == null)
        {
            error = "文本列表为空。";
            return false;
        }

        HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < entries.Count; i++)
        {
            LocalizationEntry entry = entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.key))
            {
                error = $"第 {i + 1} 条文本没有稳定 key。";
                return false;
            }

            if (!keys.Add(entry.key))
            {
                error = $"重复 key：{entry.key}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(entry.chinese) || string.IsNullOrWhiteSpace(entry.english))
            {
                error = $"key '{entry.key}' 缺少中文或英文译文。";
                return false;
            }
        }

        error = null;
        return true;
    }

    [ContextMenu("校验语言表")]
    private void ValidateConfiguration()
    {
        if (TryValidate(out string error))
        {
            Debug.Log($"语言表 '{name}' 校验通过，共 {entries.Count} 条。", this);
        }
        else
        {
            Debug.LogWarning($"语言表 '{name}'：{error}", this);
        }
    }
}

// 参考 Dialogue 的简单 Serializable 数据和 TextArea，不引入对话流程。
[Serializable]
public class LocalizationEntry
{
    public string key;
    [TextArea(2, 5)] public string chinese;
    [TextArea(2, 5)] public string english;
}
