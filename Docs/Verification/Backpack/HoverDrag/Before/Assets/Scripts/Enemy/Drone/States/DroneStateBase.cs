// 与现有PlayerStateBase相同：状态只适配自己的宿主，不复制公共状态机。
public abstract class DroneStateBase : StateBase
{
    protected DroneController drone;

    public override void Init(IStateMachineOwner owner)
    {
        drone = (DroneController)owner;
    }

    public override void UnInit()
    {
        drone = null;
    }
}
