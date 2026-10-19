using System;
using UnityEngine;

// 参考原CharacterStats的生命查询、PlayerStats的封顶治疗；不迁入战斗属性。
[DisallowMultipleComponent]
public class PlayerHealth : MonoBehaviour, IHealthTarget
{
    public PlayerConfigSO config;

    private int maxHealth = 3;
    private double nextDamageTime = double.NegativeInfinity;
    private bool isNotifying;

    public bool IsPrepared { get; private set; }
    public bool IsDead { get; private set; }
    public int CurrentRound { get; private set; }
    public int CurrentHealth { get; private set; }
    public int MaxHealth { get { return maxHealth; } }

    public event Action<int, int> HealthChanged;
    public event Action<int> NonLethalDamaged;
    public event Action Died;

    // 初次庇护所准备不代表探索，保持第0轮；已准备玩家不能用它补血或复活。
    internal bool PrepareInitialShelter()
    {
        if (isNotifying || IsPrepared || CurrentRound != 0) { return false; }
        PrepareHealth();
        return true;
    }

    // 只有Controller的新轮次入口调用，OnEnable不会补血或复活。
    internal bool PrepareForRound(int roundId)
    {
        if (isNotifying || roundId <= 0 || roundId <= CurrentRound)
        {
            return false;
        }

        CurrentRound = roundId;
        PrepareHealth();
        return true;
    }

    // 死亡结算后的庇护所恢复不算新探索；保留轮次，禁止在生命通知中重入。
    internal bool RecoverAfterDeathInShelter()
    {
        if (isNotifying || !IsPrepared || !IsDead || CurrentRound <= 0) { return false; }
        PrepareHealth();
        return true;
    }

    private void PrepareHealth()
    {
        maxHealth = config != null ? Mathf.Max(1, config.maxHealth) : 3;
        CurrentHealth = maxHealth;
        IsPrepared = true;
        IsDead = false;
        nextDamageTime = double.NegativeInfinity;
    }

    internal void NotifyPrepared()
    {
        if (!isNotifying && IsPrepared)
        {
            NotifyChanges(0, false);
        }
    }

    public bool TakeDamage(int amount)
    {
        if (isNotifying || !IsPrepared || IsDead || amount <= 0 || Time.timeAsDouble < nextDamageTime)
        {
            return false;
        }

        // 转场已受理后不再接受伤害；真实死亡先发生则流程只能走死亡返回。
        // 只检查场景锁，开背包、蹲伏和普通输入锁仍可受到真实敌人伤害。
        if (TryGetComponent(out PlayerController transitionPlayer) && transitionPlayer.IsSceneTransitionBlocked)
        {
            return false;
        }

        int actualDamage = Mathf.Min(amount, CurrentHealth);
        CurrentHealth -= actualDamage;
        float invulnerability = config != null ? Mathf.Max(0f, config.damageInvulnerabilitySeconds) : 0.5f;
        nextDamageTime = Time.timeAsDouble + invulnerability;
        IsDead = CurrentHealth == 0;
        if (IsDead && TryGetComponent(out PlayerController controller))
        {
            // 显示和死亡回调开始前，运动/交互必须已经进入死亡限制。
            controller.HandleDeath();
        }
        NotifyChanges(actualDamage, IsDead);
        return true;
    }

    public void Heal(int amount)
    {
        if (isNotifying || !IsPrepared || IsDead || amount <= 0 || CurrentHealth >= maxHealth)
        {
            return;
        }

        // 先限制增量，避免极大治疗量使int相加溢出；无敌期仍允许合法治疗。
        CurrentHealth += Mathf.Min(amount, maxHealth - CurrentHealth);
        NotifyChanges(0, false);
    }

    private void NotifyChanges(int damage, bool died)
    {
        // 通知只更新显示/协作，不能在回调中重入伤害、治疗或下一轮准备。
        isNotifying = true;
        try
        {
            NotifyHealthChanged();
            if (died)
            {
                NotifyDied();
            }
            else if (damage > 0)
            {
                NotifyNonLethalDamage(damage);
            }
        }
        finally
        {
            isNotifying = false;
        }
    }

    private void NotifyHealthChanged()
    {
        if (HealthChanged == null)
        {
            return;
        }
        foreach (Action<int, int> listener in HealthChanged.GetInvocationList())
        {
            try
            {
                listener(CurrentHealth, maxHealth);
            }
            catch (Exception exception)
            {
                // 显示异常不能把已完成的治疗变成物品未计次。
                Debug.LogException(exception, this);
            }
        }
    }

    private void NotifyNonLethalDamage(int damage)
    {
        if (NonLethalDamaged == null)
        {
            return;
        }
        foreach (Action<int> listener in NonLethalDamaged.GetInvocationList())
        {
            try
            {
                listener(damage);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }

    private void NotifyDied()
    {
        if (Died == null)
        {
            return;
        }
        foreach (Action listener in Died.GetInvocationList())
        {
            try
            {
                listener();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }
}
