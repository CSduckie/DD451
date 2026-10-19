public class DroneExplodeState : DroneStateBase
{
    public override void Enter()
    {
        drone.FinishExplosion();
    }
}
