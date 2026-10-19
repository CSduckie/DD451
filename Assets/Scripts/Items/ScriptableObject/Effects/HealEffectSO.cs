using System;
using UnityEngine;

[CreateAssetMenu(fileName = "HealEffect", menuName = "Items/Effects/Heal")]
public sealed class HealEffectSO : ItemEffectSO
{
    public bool healToFull;
    [Tooltip("固定回复的 HP；勾选回满时不使用此值。")]
    [Min(1)] public int amount = 1;

    public bool TryValidate(out string error)
    {
        if (!healToFull && amount <= 0)
        {
            error = "固定治疗量必须大于 0。";
            return false;
        }

        error = null;
        return true;
    }

    public override void Execute(IHealthTarget target)
    {
        if (target == null || (target is UnityEngine.Object unityTarget && unityTarget == null)
            || !TryValidate(out _))
        {
            return;
        }

        int currentHealth = target.CurrentHealth;
        int maxHealth = target.MaxHealth;
        if (currentHealth <= 0 || maxHealth <= 0 || currentHealth >= maxHealth)
        {
            // 前一个效果已回满时跳过，不能把整次有效组合误判为失败。
            return;
        }

        int missingHealth = maxHealth - currentHealth;
        target.Heal(healToFull ? missingHealth : Math.Min(amount, missingHealth));
    }

    [ContextMenu("校验治疗效果")]
    private void ValidateConfiguration()
    {
        if (TryValidate(out string error))
        {
            Debug.Log("治疗效果校验通过。", this);
        }
        else
        {
            Debug.LogWarning($"治疗效果无效：{error}", this);
        }
    }
}
