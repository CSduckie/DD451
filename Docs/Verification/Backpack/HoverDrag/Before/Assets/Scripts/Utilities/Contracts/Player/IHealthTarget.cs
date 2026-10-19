// 生命由02维护。Heal须同步执行、封顶且不复活；事件订阅者不应在治疗中改变物品或抛异常。
public interface IHealthTarget
{
    int CurrentHealth { get; }
    int MaxHealth { get; }
    void Heal(int amount);
}
