public class DroneChaseState : DroneStateBase
{
    public override void Update()
    {
        if (drone.TryStartCoverAlert()) { return; }
        if (!drone.ObservePlayer()) { drone.ChangeState(DroneState.Search); }
    }

    public override void FixedUpdate()
    {
        drone.MoveTowards(drone.LastKnownPosition, drone.config.chaseSpeed);
    }
}
