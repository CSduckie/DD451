using UnityEngine;

// 仅供物品模块验证，不能作为02的正式玩家生命实现。
public class ItemsTestHealth : MonoBehaviour, IHealthTarget
{
    [Min(1)] public int maxHealth = 5;
    [Min(0)] public int currentHealth = 2;

    public int CurrentHealth { get { return currentHealth; } }
    public int MaxHealth { get { return maxHealth; } }

    public void Heal(int amount)
    {
        if (amount <= 0 || currentHealth <= 0 || currentHealth >= maxHealth)
        {
            return;
        }

        currentHealth += Mathf.Min(amount, maxHealth - currentHealth);
    }

    public void Damage(int amount)
    {
        if (amount > 0 && currentHealth > 0)
        {
            currentHealth -= Mathf.Min(amount, currentHealth);
        }
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
    }
}
