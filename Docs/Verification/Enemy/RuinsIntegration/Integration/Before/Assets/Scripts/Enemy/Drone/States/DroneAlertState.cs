public class DroneAlertState : DroneStateBase
{
    public override void Update()
    {
        // 目标失效、离开视野或隐藏都按同一规则退条，不读取不可见玩家的位置。
        bool visible = drone.ObservePlayer();
        drone.UpdateAlertProgress(visible);
        if (visible && drone.AlertProgress >= 1f)
        {
            drone.ChangeState(DroneState.Chase);
        }
        else if (!visible && drone.AlertProgress <= 0f)
        {
            // 巡逻索引保存在Controller中，回巡逻继续原路线。
            drone.ChangeState(DroneState.Patrol);
        }
    }
}
