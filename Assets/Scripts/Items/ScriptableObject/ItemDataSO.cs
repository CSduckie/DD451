using System.Collections.Generic;
using UnityEngine;

// 沿用 CardGame 的物品 SO 形式；这里只保存定义，不保存实例的归属和次数。
[CreateAssetMenu(fileName = "ItemData", menuName = "Items/Item Data")]
public class ItemDataSO : ScriptableObject
{
    [Header("稳定身份与双语文本")]
    public string definitionId;
    public string nameZh;
    public string nameEn;
    [TextArea] public string descriptionZh;
    [TextArea] public string descriptionEn;
    public string nameKey;
    public string descriptionKey;

    [Header("占格与价值")]
    [Min(1)] public int width = 1;
    [Min(1)] public int height = 1;
    public bool canRotate = true;
    [Min(0)] public int ev;

    [Header("生成参考值（由后续搜刮模块使用）")]
    [Tooltip("概率范围 0 到 1，1 表示 100%。示例数值是可调整的测试默认值。")]
    [Range(0f, 1f)] public float defaultSpawnProbability = 1f;
    [Tooltip("搜索秒数。示例数值是可调整的测试默认值。")]
    [Min(0f)] public float searchSeconds;

    [Header("唯一剧情物品")]
    public bool isUnique;
    public string uniqueId;

    [Header("使用规则")]
    public bool canUse;
    public bool consumedOnUse;
    [Tooltip("0 表示不限次数；正数表示每轮成功使用的上限。")]
    [Min(0)] public int usesPerRound;
    // 沿用 CardDataSO 的效果列表配置；P0 支持多个治疗效果顺序执行。
    public List<ItemEffectSO> effects = new List<ItemEffectSO>();

    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(definitionId))
        {
            error = "物品定义 ID 不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(nameZh) || string.IsNullOrWhiteSpace(nameEn)
            || string.IsNullOrWhiteSpace(descriptionZh) || string.IsNullOrWhiteSpace(descriptionEn)
            || string.IsNullOrWhiteSpace(nameKey) || string.IsNullOrWhiteSpace(descriptionKey))
        {
            error = "中英文名称、描述和稳定文本键均不能为空。";
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            error = "物品占格宽高必须大于 0。";
            return false;
        }

        if (ev < 0)
        {
            error = "物品 EV 不能为负数。";
            return false;
        }

        if (float.IsNaN(defaultSpawnProbability) || float.IsInfinity(defaultSpawnProbability)
            || defaultSpawnProbability < 0f || defaultSpawnProbability > 1f)
        {
            error = "默认生成概率必须是 0 到 1 之间的有限数值。";
            return false;
        }

        if (float.IsNaN(searchSeconds) || float.IsInfinity(searchSeconds) || searchSeconds < 0f)
        {
            error = "搜索时间必须是非负的有限秒数。";
            return false;
        }

        if (isUnique && string.IsNullOrWhiteSpace(uniqueId))
        {
            error = "唯一剧情物品必须填写稳定唯一标识。";
            return false;
        }

        if (isUnique && consumedOnUse)
        {
            error = "P0 唯一剧情物品不能配置为使用后消耗。";
            return false;
        }

        if (usesPerRound < 0)
        {
            error = "每轮使用上限不能为负数；0 表示不限次数。";
            return false;
        }

        if (!canUse)
        {
            if (consumedOnUse || usesPerRound != 0 || (effects != null && effects.Count > 0))
            {
                error = "不可使用的物品不能配置消耗、使用次数或效果。";
                return false;
            }

            error = null;
            return true;
        }

        if (effects == null || effects.Count == 0)
        {
            error = "可使用的物品至少需要一个治疗效果。";
            return false;
        }

        // 使用前检查整个列表，防止执行一半才发现不支持的配置。
        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i] == null)
            {
                error = $"效果列表第 {i + 1} 项为空。";
                return false;
            }

            if (!(effects[i] is HealEffectSO healEffect))
            {
                error = $"效果列表第 {i + 1} 项不是 P0 支持的治疗效果。";
                return false;
            }

            if (!healEffect.TryValidate(out string effectError))
            {
                error = $"效果列表第 {i + 1} 项无效：{effectError}";
                return false;
            }
        }

        error = null;
        return true;
    }

    [ContextMenu("校验物品配置")]
    private void ValidateConfiguration()
    {
        if (TryValidate(out string error))
        {
            Debug.Log($"物品配置校验通过：{definitionId}", this);
        }
        else
        {
            Debug.LogWarning($"物品配置无效：{error}", this);
        }
    }
}
