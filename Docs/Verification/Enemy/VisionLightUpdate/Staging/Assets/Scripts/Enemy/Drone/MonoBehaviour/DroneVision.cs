using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshCollider), typeof(MeshFilter), typeof(MeshRenderer))]
public class DroneVision : MonoBehaviour
{
    [Tooltip("模型下的视野起点：局部X轴朝向机头，局部Z轴为扇形厚度方向。")]
    public Transform visionOrigin;
    [Min(0.1f)] public float radius = 6f;
    [Range(1f, 170f)] public float angleDegrees = 70f;
    [Min(0.05f)] public float thickness = 2f;
    [Range(4, 60)] public int segments = 24;
    [Range(-180f, 180f)] public float directionDegrees;
    public LayerMask playerMask = Physics.DefaultRaycastLayers;
    public LayerMask obstructionMask = 1;

    private MeshCollider rangeCollider;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private Transform owner;
    private Mesh rangeMesh;
    private Mesh viewMesh;
    private MaterialPropertyBlock properties;
    private readonly List<Collider> obstacles = new List<Collider>();
    private readonly HashSet<PlayerController> checkedPlayers = new HashSet<PlayerController>();
    private readonly List<float> angles = new List<float>();
    private readonly List<Vector3> viewVertices = new List<Vector3>();
    private readonly List<int> viewTriangles = new List<int>();
    private Collider[] overlapBuffer = new Collider[32];
    private RaycastHit[] castBuffer = new RaycastHit[32];
    private readonly Vector3[] obstacleCorners = new Vector3[8];
    private Quaternion headingRotation = Quaternion.identity;
    private bool dirty = true;
    private bool running;
    private bool originBlocked;

    public MeshCollider RangeCollider { get { return rangeCollider != null ? rangeCollider : GetComponent<MeshCollider>(); } }
    private bool HasActiveRange { get { return running && rangeCollider != null && rangeCollider.enabled && rangeCollider.sharedMesh != null; } }

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnValidate()
    {
        // OnValidate可能来自加载线程；创建/销毁Mesh留给主线程。
        dirty = true;
    }

    private void LateUpdate()
    {
        if (dirty) { Rebuild(); }
        RefreshPose();
        // 人工关闭实际检测Collider时同时隐藏显示；再次开启只恢复刷新，不强行重建Collider。
        bool activeRange = HasActiveRange;
        if (meshRenderer != null && meshRenderer.enabled != activeRange) { meshRenderer.enabled = activeRange; }
        if (!activeRange) { return; }
        RefreshObstacles();
        RefreshView();
    }

    public void Rebuild()
    {
        radius = Mathf.Max(0.1f, radius);
        angleDegrees = Mathf.Clamp(angleDegrees, 1f, 170f);
        thickness = Mathf.Max(0.05f, thickness);
        segments = Mathf.Clamp(segments, 4, 60);
        rangeCollider = GetComponent<MeshCollider>();
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        DroneController controller = GetComponentInParent<DroneController>();
        owner = controller != null ? controller.transform : transform;
        rangeCollider.sharedMesh = null;
        meshFilter.sharedMesh = null;
        ReleaseMeshes();

        rangeMesh = NewMesh("DroneVision Range");
        viewMesh = NewMesh("DroneVision Visible");
        viewMesh.MarkDynamic();
        BuildRange();
        rangeCollider.convex = true;
        rangeCollider.isTrigger = true;
        rangeCollider.sharedMesh = rangeMesh;
        meshFilter.sharedMesh = viewMesh;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        running = isActiveAndEnabled;
        rangeCollider.enabled = running;
        meshRenderer.enabled = running;
        dirty = false;
        if (!Application.isPlaying && controller != null)
        {
            headingRotation = Quaternion.Euler(0f, controller.initialFacing < 0 ? 180f : 0f, 0f);
        }
        SetHeading(headingRotation);
        if (running)
        {
            RefreshObstacles();
            RefreshView();
        }
    }

    private static Mesh NewMesh(string meshName)
    {
        // Prefab只保存形状字段，场景重载后自动生成；绝不改动共享模型资产。
        return new Mesh { name = meshName, hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild };
    }

    public void SetFacing(int direction)
    {
        SetHeading(Quaternion.Euler(0f, direction < 0 ? 180f : 0f, 0f));
    }

    public void SetHeading(Quaternion rotation)
    {
        headingRotation = rotation;
        RefreshPose();
    }

    private void RefreshPose()
    {
        Quaternion offset = Quaternion.Euler(0f, 0f, directionDegrees);
        if (visionOrigin != null && visionOrigin != transform && !visionOrigin.IsChildOf(transform))
        {
            // 直接跟随模型锚点的世界位姿，不能再乘一次Controller转头或美术校正角。
            Quaternion pose = visionOrigin.rotation * offset;
            if (transform.position == visionOrigin.position && transform.rotation == pose) { return; }
            transform.SetPositionAndRotation(visionOrigin.position, pose);
        }
        else
        {
            // 无模型的独立几何测试仍可直接指定朝向。
            Quaternion pose = headingRotation * offset;
            if (transform.localRotation == pose) { return; }
            transform.localRotation = pose;
        }
        // Update里的发现查询必须使用此刻转头后的真实Collider，不能滞后到下次物理步。
        if (Application.isPlaying) { Physics.SyncTransforms(); }
    }

    public void SetTint(Color color)
    {
        if (meshRenderer == null) { meshRenderer = GetComponent<MeshRenderer>(); }
        if (properties == null) { properties = new MaterialPropertyBlock(); }
        meshRenderer.GetPropertyBlock(properties);
        properties.SetColor("_BaseColor", color);
        properties.SetColor("_Color", color);
        meshRenderer.SetPropertyBlock(properties);
    }

    public PlayerController FindVisiblePlayer()
    {
        if (dirty) { Rebuild(); }
        RefreshPose();
        if (!HasActiveRange) { return null; }
        RefreshObstacles();
        checkedPlayers.Clear();
        int count = OverlapRange(playerMask, QueryTriggerInteraction.Collide);
        PlayerController nearest = null;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            PlayerController player = overlapBuffer[i].GetComponentInParent<PlayerController>();
            if (player == null || !checkedPlayers.Add(player) || !CanSeePrepared(player)) { continue; }
            float distance = (player.transform.position - transform.position).sqrMagnitude;
            if (distance < nearestDistance) { nearest = player; nearestDistance = distance; }
        }
        checkedPlayers.Clear();
        System.Array.Clear(overlapBuffer, 0, overlapBuffer.Length);
        return nearest;
    }

    public bool CanSee(PlayerController player)
    {
        if (dirty) { Rebuild(); }
        RefreshPose();
        if (!HasActiveRange) { return false; }
        RefreshObstacles();
        return CanSeePrepared(player);
    }

    private bool CanSeePrepared(PlayerController player)
    {
        // 开包只限制主动操作，不能用CanAct把玩家从敌人检测中移除。
        if (player == null || !player.isActiveAndEnabled || !player.IsPrepared || player.IsDead
            || player.IsSceneTransitionBlocked || CoverArea.IsHidden(player)) { return false; }
        CapsuleCollider capsule = player.GetComponent<CapsuleCollider>();
        if (capsule == null || !capsule.enabled || (playerMask.value & (1 << capsule.gameObject.layer)) == 0) { return false; }
        Vector3 point = capsule.bounds.center;
        // 主身体中心必须实际落在扇形Collider内，不以外接Box或另一套角度数值代替。
        if ((rangeCollider.ClosestPoint(point) - point).sqrMagnitude > 0.000001f) { return false; }
        // 投影到当前视野的局部平面，与显示共用覆盖完整局部Z厚度的BoxCast。
        Vector3 offset = Vector3.ProjectOnPlane(point - transform.position, transform.forward);
        float distance = offset.magnitude;
        if (originBlocked) { return false; }
        if (distance < 0.001f) { return true; }
        Vector3 localDirection = transform.InverseTransformVector(offset).normalized;
        float angle = Mathf.Atan2(localDirection.y, localDirection.x) * Mathf.Rad2Deg;
        float fullDistance = transform.TransformVector(localDirection * RadiusOnPolygon(angle)).magnitude;
        // 与显示一样扫到扇形外缘，墙前的安全余量也不会因目标距离不同而变化。
        return BlockingDistance(offset / distance, fullDistance) >= distance;
    }

    private int OverlapRange(int mask, QueryTriggerInteraction triggers)
    {
        Bounds bounds = rangeCollider.bounds;
        int count;
        do
        {
            count = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents + Vector3.one * 0.01f,
                overlapBuffer, Quaternion.identity, mask, triggers);
            if (count < overlapBuffer.Length) { return count; }
            overlapBuffer = new Collider[overlapBuffer.Length * 2];
        }
        while (true);
    }

    private void RefreshObstacles()
    {
        obstacles.Clear();
        originBlocked = false;
        int count = OverlapRange(obstructionMask.value & ~(1 << 2), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider obstacle = overlapBuffer[i];
            if (IsObstruction(obstacle)) { obstacles.Add(obstacle); }
        }
        if (obstacles.Count > 0)
        {
            // 起点整段厚度只要与墙相交就保守阻挡，避免BoxCast漏掉初始重叠。
            do
            {
                count = Physics.OverlapBoxNonAlloc(transform.position, OcclusionHalfExtents(), overlapBuffer,
                    transform.rotation, obstructionMask.value & ~(1 << 2), QueryTriggerInteraction.Ignore);
                if (count < overlapBuffer.Length) { break; }
                overlapBuffer = new Collider[overlapBuffer.Length * 2];
            }
            while (true);
            for (int i = 0; i < count; i++)
            {
                if (IsObstruction(overlapBuffer[i])) { originBlocked = true; break; }
            }
        }
        System.Array.Clear(overlapBuffer, 0, overlapBuffer.Length);
    }

    private bool IsObstruction(Collider collider)
    {
        return collider != null && !collider.transform.IsChildOf(owner)
            && collider.GetComponentInParent<PlayerController>() == null;
    }

    private Vector3 OcclusionHalfExtents()
    {
        return new Vector3(0.001f, 0.001f, thickness * Mathf.Abs(transform.lossyScale.z) * 0.5f);
    }

    private float BlockingDistance(Vector3 direction, float distance)
    {
        if (originBlocked) { return 0f; }
        if (obstacles.Count == 0) { return distance; }
        // 转头时厚度方向也跟随视野；局部Z任一切片的墙都遮挡整条方向。
        int count;
        do
        {
            count = Physics.BoxCastNonAlloc(transform.position, OcclusionHalfExtents(), direction, castBuffer,
                transform.rotation, distance, obstructionMask.value & ~(1 << 2), QueryTriggerInteraction.Ignore);
            if (count < castBuffer.Length) { break; }
            castBuffer = new RaycastHit[castBuffer.Length * 2];
        }
        while (true);
        float nearest = distance;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = castBuffer[i];
            if (IsObstruction(hit.collider))
            {
                nearest = Mathf.Min(nearest, Mathf.Max(0f, hit.distance - 0.01f));
            }
        }
        System.Array.Clear(castBuffer, 0, castBuffer.Length);
        return nearest;
    }

    private void BuildRange()
    {
        int ringCount = segments + 2;
        Vector3[] vertices = new Vector3[ringCount * 2];
        List<int> triangles = new List<int>();
        for (int side = 0; side < 2; side++)
        {
            int start = side * ringCount;
            float z = (side == 0 ? -0.5f : 0.5f) * thickness;
            vertices[start] = new Vector3(0f, 0f, z);
            for (int i = 0; i <= segments; i++)
            {
                float angle = (-angleDegrees * 0.5f + angleDegrees * i / segments) * Mathf.Deg2Rad;
                vertices[start + i + 1] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, z);
            }
            for (int i = 1; i <= segments; i++)
            {
                triangles.Add(start);
                triangles.Add(start + (side == 0 ? i + 1 : i));
                triangles.Add(start + (side == 0 ? i : i + 1));
            }
        }
        for (int i = 0; i < ringCount; i++)
        {
            int next = (i + 1) % ringCount;
            triangles.Add(i); triangles.Add(next); triangles.Add(i + ringCount);
            triangles.Add(next); triangles.Add(next + ringCount); triangles.Add(i + ringCount);
        }
        // 4 * segments + 4 <= 244；角度小于180°使整个扇形柱保持凸体。
        rangeMesh.vertices = vertices;
        rangeMesh.SetTriangles(triangles, 0);
        rangeMesh.RecalculateBounds();
    }

    private void RefreshView()
    {
        angles.Clear();
        int sampleCount = Mathf.Max(segments, Mathf.CeilToInt(angleDegrees * 2f));
        for (int i = 0; i <= sampleCount; i++) { angles.Add(-angleDegrees * 0.5f + angleDegrees * i / sampleCount); }
        // 同时采样Collider本身的每个折线顶点，显示外缘与检测外缘严格同源。
        for (int i = 0; i <= segments; i++) { angles.Add(-angleDegrees * 0.5f + angleDegrees * i / segments); }
        for (int i = 0; i < obstacles.Count; i++) { AddObstacleAngles(obstacles[i]); }
        angles.Sort();
        viewVertices.Clear();
        viewTriangles.Clear();
        viewVertices.Add(Vector3.zero);
        float previous = float.NegativeInfinity;
        for (int i = 0; i < angles.Count; i++)
        {
            float angle = angles[i];
            if (angle - previous < 0.0001f) { continue; }
            previous = angle;
            float radians = angle * Mathf.Deg2Rad;
            Vector3 local = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * RadiusOnPolygon(angle);
            Vector3 worldOffset = transform.TransformVector(local);
            float distance = worldOffset.magnitude;
            float visibleDistance = BlockingDistance(worldOffset / distance, distance);
            viewVertices.Add(local * (visibleDistance / distance));
            int index = viewVertices.Count - 1;
            if (index > 1)
            {
                viewTriangles.Add(0); viewTriangles.Add(index); viewTriangles.Add(index - 1);
                viewTriangles.Add(0); viewTriangles.Add(index - 1); viewTriangles.Add(index);
            }
        }
        viewMesh.Clear();
        viewMesh.SetVertices(viewVertices);
        viewMesh.SetTriangles(viewTriangles, 0);
        viewMesh.RecalculateBounds();
    }

    private float RadiusOnPolygon(float angle)
    {
        float step = angleDegrees / segments;
        int edge = Mathf.Clamp(Mathf.FloorToInt((angle + angleDegrees * 0.5f) / step), 0, segments - 1);
        float midpoint = -angleDegrees * 0.5f + (edge + 0.5f) * step;
        return radius * Mathf.Cos(step * 0.5f * Mathf.Deg2Rad) / Mathf.Cos((angle - midpoint) * Mathf.Deg2Rad);
    }

    private void AddObstacleAngles(Collider obstacle)
    {
        BoxCollider box = obstacle as BoxCollider;
        Bounds bounds = obstacle.bounds;
        for (int i = 0; i < obstacleCorners.Length; i++)
        {
            Vector3 sign = new Vector3((i & 1) == 0 ? -1f : 1f,
                (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
            Vector3 point = box != null
                ? box.transform.TransformPoint(box.center + Vector3.Scale(box.size * 0.5f, sign))
                : bounds.center + Vector3.Scale(bounds.extents, sign);
            obstacleCorners[i] = transform.InverseTransformPoint(point);
            AddAngleSamples(obstacleCorners[i]);
        }
        if (box == null) { return; }
        // 转头时，墙与有限厚度的交线会产生新角点；遍历Box的12条边补齐。
        for (int i = 0; i < obstacleCorners.Length; i++)
        for (int axis = 0; axis < 3; axis++)
        {
            int next = i | (1 << axis);
            if (next == i) { continue; }
            Vector3 from = obstacleCorners[i];
            Vector3 to = obstacleCorners[next];
            float deltaZ = to.z - from.z;
            if (Mathf.Abs(deltaZ) < 0.000001f) { continue; }
            for (int side = -1; side <= 1; side += 2)
            {
                float t = (side * thickness * 0.5f - from.z) / deltaZ;
                if (t > 0f && t < 1f) { AddAngleSamples(Vector3.LerpUnclamped(from, to, t)); }
            }
        }
    }

    private void AddAngleSamples(Vector3 local)
    {
        float angle = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
        // 轮廓角点两侧各补一个方向，避免相邻三角形跨过薄墙边缘。
        for (int side = -1; side <= 1; side++)
        {
            float sample = angle + side * 0.01f;
            if (sample > -angleDegrees * 0.5f && sample < angleDegrees * 0.5f) { angles.Add(sample); }
        }
    }

    public void Clear()
    {
        running = false;
        dirty = false;
        obstacles.Clear();
        checkedPlayers.Clear();
        System.Array.Clear(overlapBuffer, 0, overlapBuffer.Length);
        System.Array.Clear(castBuffer, 0, castBuffer.Length);
        if (rangeCollider != null) { rangeCollider.enabled = false; }
        if (meshRenderer != null) { meshRenderer.enabled = false; }
    }

    private void OnDisable() { Clear(); }
    private void OnDestroy() { ReleaseMeshes(); }

    private void ReleaseMeshes()
    {
        ReleaseMesh(rangeMesh);
        ReleaseMesh(viewMesh);
        rangeMesh = null;
        viewMesh = null;
    }

    private static void ReleaseMesh(Mesh mesh)
    {
        if (mesh == null) { return; }
        if (Application.isPlaying) { Destroy(mesh); }
        else { DestroyImmediate(mesh); }
    }
}
