using UnityEngine;

// 从 CardGame 的 Effect 精简而来，只保留当前实际需要的执行入口。
public abstract class ItemEffectSO : ScriptableObject
{
    public abstract void Execute(IHealthTarget target);
}
