// 修改复用自CSduckie/2DArpg-Portfolio：FrameWork/StateMachine/StateBase.cs。
public abstract class StateBase
{
    // 一个缓存实例只初始化一次，每次进入需要重置的计时放在Enter。
    public virtual void Init(IStateMachineOwner owner)
    {
    }

    public virtual void UnInit()
    {
    }

    public virtual void Enter()
    {
    }

    public virtual void Exit()
    {
    }

    public virtual void Update()
    {
    }

    public virtual void LateUpdate()
    {
    }

    public virtual void FixedUpdate()
    {
    }
}
