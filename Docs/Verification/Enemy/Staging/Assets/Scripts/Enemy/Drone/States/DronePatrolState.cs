using UnityEngine;

public class DronePatrolState : DroneStateBase
{
    private float waitTime;
    private float blockedTime;
    private bool waiting;

    public override void Enter()
    {
        waitTime = 0f;
        blockedTime = 0f;
        waiting = false;
        drone.ForgetTarget();
    }

    public override void Update()
    {
        if (drone.ObservePlayer()) { drone.ChangeState(DroneState.Chase); }
    }

    public override void FixedUpdate()
    {
        if (!drone.TryGetPatrolPoint(out Vector3 point)) { return; }
        if (waiting)
        {
            waitTime += Time.fixedDeltaTime;
            if (waitTime >= Mathf.Max(0f, drone.config.patrolWaitSeconds))
            {
                drone.AdvancePatrolPoint();
                waiting = false;
                waitTime = 0f;
            }
            return;
        }
        bool arrived = drone.MoveTowards(point, drone.config.patrolSpeed);
        if (drone == null || drone.HasExploded) { return; }
        blockedTime = drone.IsBlocked ? blockedTime + Time.fixedDeltaTime : 0f;
        // 简单路线不寻路：到点或持续受阻就等待后换下一点。
        if (arrived || blockedTime >= Mathf.Max(0.1f, drone.config.blockedReturnSeconds))
        {
            waiting = true;
            blockedTime = 0f;
        }
    }
}
