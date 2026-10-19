// 仅用于验证：不支持的效果必须在任何治疗执行前被拒绝。
public class UnsupportedItemEffectSO : ItemEffectSO
{
    public int ExecutionCount { get; private set; }

    public override void Execute(IHealthTarget target)
    {
        ExecutionCount++;
        target.Heal(1);
    }
}
