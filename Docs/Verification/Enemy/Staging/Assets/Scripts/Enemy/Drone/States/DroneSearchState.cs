using UnityEngine;

public class DroneSearchState : DroneStateBase
{
    private Vector3 lastSeen;
    private bool searching;
    private float searchTime;
    private float blockedTime;

    public override void Enter()
    {
        lastSeen = drone.LastKnownPosition;
        searching = false;
        searchTime = 0f;
        blockedTime = 0f;
    }

    public override void Update()
    {
        if (drone.ObservePlayer()) { drone.ChangeState(DroneState.Chase); return; }
        if (!searching) { return; }
        searchTime += Time.deltaTime;
        if (searchTime >= Mathf.Max(0f, drone.config.searchSeconds)) { drone.ChangeState(DroneState.Patrol); }
    }

    public override void FixedUpdate()
    {
        if (searching) { return; }
        bool arrived = drone.MoveTowards(lastSeen, drone.config.chaseSpeed);
        if (drone == null || drone.HasExploded) { return; }
        bool stopped = drone.IsBlocked || drone.FlightVelocity.sqrMagnitude < 0.000001f;
        blockedTime = stopped ? blockedTime + Time.fixedDeltaTime : 0f;
        // 最后位置若隔着墙不可达，在墙前搜索后返回，不穿墙也不永久卡在追踪。
        searching = arrived || blockedTime >= Mathf.Max(0.1f, drone.config.blockedReturnSeconds);
    }
}
