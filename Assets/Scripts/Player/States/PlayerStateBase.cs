// 保留原ARPG的宿主强类型适配；每个Controller持有独立状态缓存。
public abstract class PlayerStateBase : StateBase
{
    protected PlayerController player;
    public override void Init(IStateMachineOwner owner)
    {
        base.Init(owner);
        player = (PlayerController)owner;
    }
    public override void UnInit()
    {
        player = null;
    }
    protected bool ChangeGroundState()
    {
        if (!player.IsGrounded)
        {
            player.ChangeState(PlayerState.Fall);
            return true;
        }
        PlayerState target = player.GroundState();
        if (target != player.CurrentState)
        {
            player.ChangeState(target);
            return true;
        }
        return false;
    }
}
