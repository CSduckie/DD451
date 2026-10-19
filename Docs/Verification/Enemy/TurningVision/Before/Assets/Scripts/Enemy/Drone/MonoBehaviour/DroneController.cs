using UnityEngine;

public enum DroneState
{
    None, Patrol, Chase, Search, Explode, Alert
}

// 参考原ARPG的枚举switch和宿主状态；复用02适配好的公共StateMachine。
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public class DroneController : MonoBehaviour, IStateMachineOwner
{
    public DroneConfigSO config;
    public Transform visual;
    public DroneVision vision;
    public DroneFlightVisual flightVisual;
    [Header("本机巡逻路线：在场景实例上配置")]
    [Tooltip("按数组顺序拖入场景点位Transform，移动点位改变X/Y路线，末点后循环回首点。Z固定为本机出生Z；空引用会跳过，全空时原地观察。速度与停留时间在config中配置。")]
    public Transform[] patrolPoints = new Transform[0];
    public int initialFacing = 1;
    [Min(0.01f)] public float turnSmoothTime = 0.18f;
    public LayerMask collisionMask = Physics.DefaultRaycastLayers;

    private readonly StateMachine stateMachine = new StateMachine();
    private SphereCollider contactCollider;
    private Vector3 spawnPosition;
    private Quaternion spawnRotation;
    private bool initialized;
    private bool resolvingContact;
    private bool resettingRound;
    private int patrolIndex;
    private float facingYaw;
    private float turnVelocity;
    private const float Skin = 0.02f;

    public Rigidbody Body { get; private set; }
    public DroneState CurrentState { get; private set; }
    public int CurrentRound { get; private set; }
    public bool HasExploded { get; private set; }
    public bool IsBlocked { get; private set; }
    public int FacingDirection { get; private set; } = 1;
    public Quaternion FacingRotation { get { return Quaternion.Euler(0f, facingYaw, 0f); } }
    public PlayerController Target { get; private set; }
    public Vector3 LastKnownPosition { get; private set; }
    public Vector3 FlightVelocity { get; private set; }
    public float AlertSecondsRemaining { get; private set; }
    public bool IsCoverAlert { get; private set; }
    public bool IsRunning { get { return isActiveAndEnabled && CurrentRound > 0 && !HasExploded && config != null; } }

    private void Awake()
    {
        InitializeReferences();
    }

    private void InitializeReferences()
    {
        if (initialized) { return; }
        Body = GetComponent<Rigidbody>();
        contactCollider = GetComponent<SphereCollider>();
        vision = vision != null ? vision : GetComponentInChildren<DroneVision>(true);
        flightVisual = flightVisual != null ? flightVisual : GetComponent<DroneFlightVisual>();
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
        Body.useGravity = false;
        Body.isKinematic = true;
        Body.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ;
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        contactCollider.isTrigger = false;
        FacingDirection = initialFacing < 0 ? -1 : 1;
        facingYaw = FacingDirection < 0 ? 180f : 0f;
        initialized = true;
    }

    private void OnEnable()
    {
        InitializeReferences();
        // 启用只恢复仍存活的本轮行为，绝不能清除HasExploded或推进轮次。
        if (IsRunning && !resettingRound)
        {
            if (flightVisual != null) { flightVisual.ResetPose(); }
            if (vision != null) { vision.Rebuild(); vision.SetHeading(FacingRotation); }
            StartPatrol();
        }
    }

    private void Update()
    {
        if (!IsRunning) { return; }
        // 模型可前倾；视野只跟随左右水平转向，随后执行发现查询，物理根保持固定旋转。
        facingYaw = Mathf.SmoothDampAngle(facingYaw, FacingDirection < 0 ? 180f : 0f,
            ref turnVelocity, Mathf.Max(0.01f, turnSmoothTime), Mathf.Infinity, Time.deltaTime);
        if (flightVisual != null) { flightVisual.UpdatePose(); }
        if (vision != null) { vision.SetHeading(FacingRotation); }
        if (Target != null && !CanTarget(Target))
        {
            ForgetTarget();
            ChangeState(DroneState.Patrol);
        }
        stateMachine.CurrentState?.Update();
    }

    private void FixedUpdate()
    {
        if (!IsRunning) { return; }
        FlightVelocity = Vector3.zero;
        // 草丛隐藏会停止追击，但真实机身接触仍独立触发爆炸。
        TryStartCoverAlert();
        CheckContacts();
        if (!HasExploded) { stateMachine.CurrentState?.FixedUpdate(); }
    }

    private void LateUpdate()
    {
        if (IsRunning) { stateMachine.CurrentState?.LateUpdate(); }
    }

    private void OnDisable()
    {
        stateMachine.Stop();
        ForgetTarget();
        FlightVelocity = Vector3.zero;
        AlertSecondsRemaining = 0f;
        IsCoverAlert = false;
        turnVelocity = 0f;
        CurrentState = HasExploded ? DroneState.Explode : DroneState.None;
        if (vision != null) { vision.Clear(); }
    }

    private void OnDestroy()
    {
        stateMachine.Stop();
    }

    // 010传入同一个递增探索轮次。首次也必须显式调用；同轮/旧轮完全不改现状。
    public bool BeginRound(int roundId)
    {
        InitializeReferences();
        if (resolvingContact || config == null || roundId <= 0 || roundId <= CurrentRound) { return false; }
        stateMachine.Stop();
        CurrentRound = roundId;
        HasExploded = false;
        IsBlocked = false;
        AlertSecondsRemaining = 0f;
        IsCoverAlert = false;
        ForgetTarget();
        patrolIndex = 0;
        FlightVelocity = Vector3.zero;
        resettingRound = true;
        Body.position = spawnPosition;
        Body.rotation = spawnRotation;
        contactCollider.enabled = true;
        if (visual != null) { visual.gameObject.SetActive(true); }
        if (vision != null) { vision.gameObject.SetActive(true); vision.enabled = true; }
        FacingDirection = initialFacing < 0 ? -1 : 1;
        facingYaw = FacingDirection < 0 ? 180f : 0f;
        turnVelocity = 0f;
        if (flightVisual != null) { flightVisual.ResetPose(); }
        if (vision != null) { vision.Rebuild(); vision.SetHeading(FacingRotation); }
        enabled = true;
        gameObject.SetActive(true);
        resettingRound = false;
        StartPatrol();
        if (!TryGetPatrolPoint(out Vector3 unused))
        {
            Debug.LogWarning("无人机没有有效巡逻点，将原地观察。请在DroneController配置路线。", this);
        }
        return true;
    }

    private void StartPatrol()
    {
        stateMachine.Stop();
        stateMachine.Init(this);
        ChangeState(DroneState.Patrol);
    }

    public void ChangeState(DroneState state)
    {
        if (CurrentState == state && stateMachine.hasState) { return; }
        IsCoverAlert = false;
        AlertSecondsRemaining = state == DroneState.Alert ? Mathf.Max(0.01f, config.discoveryAlertSeconds) : 0f;
        if (state == DroneState.Alert)
        {
            // 警戒只悬停；原来的模型动画会随零速度缓慢回正。
            FlightVelocity = Vector3.zero;
            IsBlocked = false;
        }
        CurrentState = state;
        switch (state)
        {
            case DroneState.Patrol: stateMachine.ChangeState<DronePatrolState>(); break;
            case DroneState.Chase: stateMachine.ChangeState<DroneChaseState>(); break;
            case DroneState.Search: stateMachine.ChangeState<DroneSearchState>(); break;
            case DroneState.Explode: stateMachine.ChangeState<DroneExplodeState>(); break;
            case DroneState.Alert: stateMachine.ChangeState<DroneAlertState>(); break;
        }
        if (vision != null)
        {
            // 追击只隐藏画面，真实Collider和感知继续工作。
            vision.SetViewVisible(state != DroneState.Chase && state != DroneState.Explode);
            vision.SetTint(state == DroneState.Chase ? new Color(1f, 0.22f, 0.12f, 0.24f)
                : state == DroneState.Search || state == DroneState.Alert ? new Color(1f, 0.67f, 0.1f, 0.20f) : new Color(0.25f, 0.8f, 1f, 0.16f));
        }
    }

    public bool TryStartCoverAlert()
    {
        if (!IsRunning || IsCoverAlert
            || (CurrentState != DroneState.Chase && CurrentState != DroneState.Alert)
            || !CanTarget(Target) || !CoverArea.IsHidden(Target)) { return false; }
        ChangeState(DroneState.Alert);
        IsCoverAlert = true;
        AlertSecondsRemaining = Mathf.Max(0.01f, config.coverAlertSeconds);
        // 首次警戒内进入掩体也只设置一次；不再保留或更新隐藏玩家坐标。
        ForgetTarget();
        return true;
    }

    public void TickAlertTimer()
    {
        if (CurrentState == DroneState.Alert && IsRunning)
        {
            AlertSecondsRemaining = Mathf.Max(0f, AlertSecondsRemaining - Time.deltaTime);
        }
    }

    public static bool CanTarget(PlayerController player)
    {
        // 背包只锁玩家操作，不能用CanAct给玩家增加免伤。
        return player != null && player.isActiveAndEnabled && player.IsPrepared && !player.IsDead
            && !player.IsSceneTransitionBlocked && player.health != null;
    }

    public bool ObservePlayer()
    {
        PlayerController seen = vision != null ? vision.FindVisiblePlayer() : null;
        Target = seen;
        if (seen == null) { return false; }
        // 仅在真正可见时读取位置；Search只使用这一份快照。
        CapsuleCollider capsule = seen.GetComponent<CapsuleCollider>();
        LastKnownPosition = capsule != null ? capsule.bounds.center : seen.Position;
        LastKnownPosition = new Vector3(LastKnownPosition.x, LastKnownPosition.y, spawnPosition.z);
        return true;
    }

    public void ForgetTarget()
    {
        Target = null;
        LastKnownPosition = Vector3.zero;
    }

    public bool TryGetPatrolPoint(out Vector3 point)
    {
        point = Body.position;
        if (patrolPoints == null || patrolPoints.Length == 0) { return false; }
        for (int i = 0; i < patrolPoints.Length; i++)
        {
            patrolIndex %= patrolPoints.Length;
            Transform candidate = patrolPoints[patrolIndex];
            if (candidate != null)
            {
                point = new Vector3(candidate.position.x, candidate.position.y, spawnPosition.z);
                return true;
            }
            patrolIndex++;
        }
        return false;
    }

    public void AdvancePatrolPoint()
    {
        if (patrolPoints != null && patrolPoints.Length > 0) { patrolIndex = (patrolIndex + 1) % patrolPoints.Length; }
    }

    private void OnDrawGizmosSelected()
    {
        if (patrolPoints == null) { return; }
        float planeZ = initialized ? spawnPosition.z : transform.position.z;
        Vector3 first = Vector3.zero;
        Vector3 previous = Vector3.zero;
        int count = 0;
        Gizmos.color = new Color(0.2f, 0.8f, 1f);
        for (int i = 0; i < patrolPoints.Length; i++)
        {
            if (patrolPoints[i] == null) { continue; }
            Vector3 point = patrolPoints[i].position;
            point.z = planeZ;
            Gizmos.DrawWireSphere(point, 0.12f);
            if (count == 0) { first = point; }
            else { Gizmos.DrawLine(previous, point); }
            previous = point;
            count++;
#if UNITY_EDITOR
            UnityEditor.Handles.Label(point + Vector3.up * 0.18f, (i + 1).ToString());
#endif
        }
        if (count > 1) { Gizmos.DrawLine(previous, first); }
    }

    public bool MoveTowards(Vector3 destination, float speed)
    {
        IsBlocked = false;
        destination.z = spawnPosition.z;
        Vector3 delta = destination - Body.position;
        float distance = delta.magnitude;
        if (distance <= Mathf.Max(0.01f, config.arrivalDistance)) { return true; }
        if (Mathf.Abs(delta.x) > 0.03f)
        {
            FacingDirection = delta.x < 0f ? -1 : 1;
        }
        Vector3 direction = delta / distance;
        float step = Mathf.Min(distance, Mathf.Max(0f, speed) * Time.fixedDeltaTime);
        Vector3 center = Body.position + Body.rotation * Vector3.Scale(contactCollider.center, transform.lossyScale);
        float radius = WorldRadius();
        RaycastHit[] hits = Physics.SphereCastAll(center, radius, direction, step, collisionMask, QueryTriggerInteraction.Ignore);
        RaycastHit nearest = new RaycastHit();
        float nearestDistance = step;
        float travel = step;
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) { continue; }
            if (hit.distance <= nearestDistance)
            {
                nearestDistance = hit.distance;
                travel = Mathf.Max(0f, hit.distance - Skin);
                nearest = hit;
                IsBlocked = true;
            }
        }
        Vector3 movement = direction * travel;
        Body.MovePosition(Body.position + movement);
        FlightVelocity = movement / Time.fixedDeltaTime;
        if (nearest.collider != null)
        {
            PlayerController player = nearest.collider.GetComponentInParent<PlayerController>();
            // Sweep已证明本物理步将接触，巡逻或警戒中的机身也会被触碰引爆。
            if (CanExplodeOnContact(player)) { Explode(player); }
        }
        return false;
    }

    private float WorldRadius()
    {
        Vector3 scale = transform.lossyScale;
        return contactCollider.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }

    private void CheckContacts()
    {
        Vector3 center = Body.position + Body.rotation * Vector3.Scale(contactCollider.center, transform.lossyScale);
        foreach (Collider other in Physics.OverlapSphere(center, WorldRadius(), collisionMask, QueryTriggerInteraction.Ignore))
        {
            if (other.transform.IsChildOf(transform)) { continue; }
            if (TryExplode(other.GetComponentInParent<PlayerController>())) { return; }
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // 物理引擎已确认实体接触，不用重叠查询再次排除接触余量处的玩家。
        Explode(collision.collider.GetComponentInParent<PlayerController>());
    }

    private void OnCollisionStay(Collision collision)
    {
        Explode(collision.collider.GetComponentInParent<PlayerController>());
    }

    private bool CanExplodeOnContact(PlayerController player)
    {
        // 隐藏只影响视线；任何存活状态的真实机身接触都会引爆一次。
        return IsRunning && !resolvingContact && CanTarget(player);
    }

    public bool TryExplode(PlayerController player)
    {
        if (!CanExplodeOnContact(player)) { return false; }
        Vector3 center = Body.position + Body.rotation * Vector3.Scale(contactCollider.center, transform.lossyScale);
        float radius = WorldRadius();
        foreach (Collider other in player.GetComponentsInChildren<Collider>())
        {
            if (!other.enabled || other.isTrigger || !other.gameObject.activeInHierarchy) { continue; }
            if ((other.ClosestPoint(center) - center).sqrMagnitude <= radius * radius)
            {
                Explode(player);
                return true;
            }
        }
        return false;
    }

    private void Explode(PlayerController player)
    {
        if (!CanExplodeOnContact(player)) { return; }
        HasExploded = true;
        resolvingContact = true;
        try
        {
            // 返回false也已自爆，绝不重试，也不绕过PlayerHealth的无敌时间。
            player.health.TakeDamage(Mathf.Max(1, config.contactDamage));
        }
        finally
        {
            if (stateMachine.hasState) { ChangeState(DroneState.Explode); }
            else
            {
                // 外部伤害监听者可能停用本机；此时状态已释放，仍须收尾碰撞与显示。
                CurrentState = DroneState.Explode;
                FinishExplosion();
            }
            // 不在Enter/Exit内Stop或递归切换；进入表现后才释放所有缓存状态。
            stateMachine.Stop();
            resolvingContact = false;
        }
    }

    public void FinishExplosion()
    {
        ForgetTarget();
        AlertSecondsRemaining = 0f;
        IsCoverAlert = false;
        FlightVelocity = Vector3.zero;
        contactCollider.enabled = false;
        if (visual != null) { visual.gameObject.SetActive(false); }
        if (vision != null) { vision.Clear(); vision.gameObject.SetActive(false); }
    }
}

