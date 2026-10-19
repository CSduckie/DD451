public class DroneAlertState : DroneStateBase
{
    public override void Update()
    {
        if (drone.TryStartCoverAlert()) { return; }
        bool visible = drone.ObservePlayer();
        if (drone.IsCoverAlert)
        {
            // 草丛脱战的等待期间，真实再发现立即追击，不重复首次警戒。
            if (visible) { drone.ChangeState(DroneState.Chase); return; }
            drone.TickAlertTimer();
            if (drone.AlertSecondsRemaining <= 0f) { drone.ChangeState(DroneState.Patrol); }
            return;
        }
        // 普通丢失仍去最后看见的位置搜索；计时到点也不能盲追。
        if (!visible) { drone.ChangeState(DroneState.Search); return; }
        drone.TickAlertTimer();
        if (drone.AlertSecondsRemaining <= 0f) { drone.ChangeState(DroneState.Chase); }
    }
}
