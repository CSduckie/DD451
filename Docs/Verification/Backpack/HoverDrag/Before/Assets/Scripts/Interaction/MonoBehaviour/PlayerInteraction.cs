using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerInteraction : MonoBehaviour
{
    [Header("视线起点")]
    public Vector3 queryOffset = Vector3.up;
    [Header("世界点/丢放校验（不限制TriggerBox交互）")]
    [Min(0.01f)] public float interactionRadius = 2f;
    [Min(0f)] public float maxDepthDifference = 0.65f;
    [Header("世界交互与遮挡检测层")]
    public LayerMask interactableLayers = ~0;
    public LayerMask obstructionLayers = ~0;

    private readonly HashSet<Interactable> checkedTargets = new HashSet<Interactable>();
    private readonly List<Collider> bodyColliders = new List<Collider>();
    private Interactable currentTarget;
    private Rigidbody ownRigidbody;

    // 首轮准备、死亡和UI占用由Controller合并后传入，交互层不反向依赖玩家程序集。
    public bool InputBlocked { get; private set; } = true;

    public Interactable CurrentTarget
    {
        get
        {
            if (!CanQuery() || currentTarget == null) { currentTarget = null; }
            if (currentTarget != null)
            {
                PrepareSpatialQuery();
                if (!TryGetTargetDistance(currentTarget, out _)) { currentTarget = null; }
            }

            return currentTarget;
        }
    }

    public string CurrentPromptKey
    {
        get
        {
            Interactable target = CurrentTarget;
            return target != null ? target.PromptKey : string.Empty;
        }
    }

    private void Awake()
    {
        ownRigidbody = GetComponentInParent<Rigidbody>();
    }

    private void OnDisable()
    {
        currentTarget = null;
        checkedTargets.Clear();
        bodyColliders.Clear();
    }

    public void SetInputBlocked(bool blocked)
    {
        InputBlocked = blocked;
        if (blocked)
        {
            currentTarget = null;
        }
    }

    public void RefreshTarget()
    {
        currentTarget = null;
        checkedTargets.Clear();
        if (!CanQuery())
        {
            return;
        }

        PrepareSpatialQuery();
        float nearestDistance = float.PositiveInfinity;
        foreach (Collider body in bodyColliders)
        {
            if (!IsUsableBody(body)) { continue; }
            // 包围盒只做粗筛；随后用真实身体Collider重叠判定，不扩大交互范围。
            Bounds bounds = body.bounds;
            Collider[] overlaps = Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity,
                interactableLayers, QueryTriggerInteraction.Collide);
            foreach (Collider overlap in overlaps)
            {
                if (IsOwnCollider(overlap)) { continue; }
                Interactable candidate = overlap.GetComponentInParent<Interactable>();
                if (candidate == null || !checkedTargets.Add(candidate)
                    || !TryGetTargetDistance(candidate, out float distance)) { continue; }

                // 距离取交互点，相等时用实例ID稳定选择，不依赖物理查询返回顺序。
                if (distance < nearestDistance || (distance == nearestDistance && currentTarget != null
                    && candidate.GetInstanceID() < currentTarget.GetInstanceID()))
                {
                    nearestDistance = distance;
                    currentTarget = candidate;
                }
            }
        }
    }

    public bool TryInteract()
    {
        if (!CanQuery())
        {
            currentTarget = null;
            return false;
        }

        // CurrentTarget同步最新Transform并重验范围、墙和业务条件，不执行失效缓存。
        Interactable target = CurrentTarget;
        if (target == null)
        {
            return false;
        }

        target.Interact(this);
        RefreshTarget();
        return true;
    }

    private bool CanQuery()
    {
        return isActiveAndEnabled && !InputBlocked;
    }

    private Vector3 QueryCenter()
    {
        return transform.TransformPoint(queryOffset);
    }

    private void PrepareSpatialQuery()
    {
        // EditMode调用或稍后挂接身体时也能解析归属，不能只依赖Awake缓存。
        if (ownRigidbody == null) { ownRigidbody = GetComponentInParent<Rigidbody>(); }
        // 传送、启用时已重叠、同帧添加身体Collider，都不依赖下一次OnTriggerEnter。
        Physics.SyncTransforms();
        Transform bodyRoot = ownRigidbody != null ? ownRigidbody.transform : transform;
        bodyRoot.GetComponentsInChildren<Collider>(bodyColliders);
    }

    // UI打开后禁止再次按E，但正在搜刮的来源仍按同一Trigger与视线规则检查空间可达。
    public bool CanReachTarget(Interactable target)
    {
        if (!isActiveAndEnabled) { return false; }
        PrepareSpatialQuery();
        return TryGetTargetDistance(target, out _, false);
    }

    public bool CanReachWorldPoint(Vector3 point)
    {
        if (!isActiveAndEnabled || interactionRadius <= 0f) { return false; }
        if (ownRigidbody == null) { ownRigidbody = GetComponentInParent<Rigidbody>(); }
        Physics.SyncTransforms();
        Vector3 center = QueryCenter();
        return Mathf.Abs(point.z - center.z) <= Mathf.Max(0f, maxDepthDifference)
            && (point - center).sqrMagnitude <= interactionRadius * interactionRadius
            && !IsObstructed(center, point, null);
    }

    private bool TryGetTargetDistance(Interactable target, out float distance, bool checkCanInteract = true)
    {
        distance = float.PositiveInfinity;
        if (target == null || !target.isActiveAndEnabled || target.transform.IsChildOf(transform)
            || !target.TryGetInteractionTrigger(out BoxCollider trigger) || IsOwnCollider(trigger)
            || (interactableLayers.value & (1 << trigger.gameObject.layer)) == 0
            || (checkCanInteract && !target.CanInteract(this))) { return false; }

        bool overlapsBody = false;
        foreach (Collider body in bodyColliders)
        {
            if (IsUsableBody(body) && Physics.ComputePenetration(body, body.transform.position, body.transform.rotation,
                trigger, trigger.transform.position, trigger.transform.rotation, out _, out _))
            {
                overlapsBody = true;
                break;
            }
        }
        if (!overlapsBody) { return false; }

        Vector3 center = QueryCenter();
        Vector3 point = target.interactionPoint != null ? target.interactionPoint.position : target.transform.position;
        // 玩家在Trigger内部时ClosestPoint等于玩家位置；用目标锚点才保留墙体遮挡。
        if (IsObstructed(center, point, target)) { return false; }
        distance = (point - center).sqrMagnitude;
        return true;
    }

    private bool IsUsableBody(Collider candidate)
    {
        return candidate != null && candidate.enabled && candidate.gameObject.activeInHierarchy
            && !candidate.isTrigger && IsOwnCollider(candidate);
    }

    private bool IsOwnCollider(Collider candidate)
    {
        if (candidate == null) { return false; }
        return ownRigidbody != null ? candidate.attachedRigidbody == ownRigidbody
            : candidate.attachedRigidbody == null && candidate.transform.IsChildOf(transform);
    }

    private bool IsObstructed(Vector3 center, Vector3 point, Interactable target)
    {
        Vector3 direction = point - center;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
        {
            return false;
        }

        RaycastHit[] hits = Physics.RaycastAll(center, direction / distance, distance,
            obstructionLayers, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit hit in hits)
        {
            if (!IsOwnCollider(hit.collider) && (target == null || !hit.collider.transform.IsChildOf(target.transform)))
            {
                return true;
            }
        }

        return false;
    }
}
