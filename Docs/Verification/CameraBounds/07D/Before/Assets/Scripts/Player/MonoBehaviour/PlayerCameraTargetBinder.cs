using UnityEngine;
using Unity.Cinemachine;

// 只处理绑定、场景边界有效性和瞬移；位置约束完全由原生Confiner3D负责。
[DefaultExecutionOrder(-100)]
public class PlayerCameraTargetBinder : MonoBehaviour
{
    public PlayerController player;
    public CinemachineCamera followCamera;
    [Tooltip("放在同一SideFollow上的原生扩展；边界只编辑它的BoundingVolume，不在Binder重复保存。")]
    public CinemachineConfiner3D confiner;
    private PlayerController boundPlayer;
    private CinemachineCamera boundCamera;
    private BoxCollider boundVolume;
    private Vector3 boundVolumeCenter;
    private Vector3 boundVolumeSize;
    private Matrix4x4 boundVolumeMatrix;
    private bool hasBinding;
    private string lastBoundsError;

    public string LastBoundsError { get { return lastBoundsError; } }

    private void OnEnable()
    {
        Bind(player);
    }
    public void Bind(PlayerController target)
    {
        Unbind();
        player = target;
        lastBoundsError = null;
        RefreshBinding();
    }

    // 供场景激活前校验：祖先未激活是Additive加载的正常阶段，不等于Collider被禁用。
    public bool ValidateBounds(out string error)
    {
        if (followCamera == null || followCamera.gameObject.scene != gameObject.scene || !followCamera.enabled)
        {
            error = "FollowCamera必须是本场景内启用的CinemachineCamera。";
            return false;
        }
        if (confiner == null) { confiner = followCamera.GetComponent<CinemachineConfiner3D>(); }
        if (confiner == null || confiner.gameObject != followCamera.gameObject || !confiner.enabled)
        {
            error = "SideFollow必须配置并启用CinemachineConfiner3D。";
            return false;
        }
        BoxCollider volume = confiner.BoundingVolume as BoxCollider;
        if (volume == null)
        {
            error = "Confiner3D.BoundingVolume必须引用本场景的CameraBounds BoxCollider。";
            return false;
        }
        if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded || volume.gameObject.scene != gameObject.scene)
        {
            error = "CameraBounds必须属于当前相机场景，不能引用其他Additive场景的边界。";
            return false;
        }
        if (!volume.enabled || !volume.gameObject.activeSelf || !volume.isTrigger)
        {
            error = "CameraBounds必须启用BoxCollider与自身GameObject，并设为isTrigger；祖先可暂时未激活。";
            return false;
        }
        if (volume.attachedRigidbody != null || volume.GetComponent<Renderer>() != null
            || volume.GetComponentInParent<Interactable>(true) != null
            || volume.GetComponentInParent<Camera>(true) != null
            || volume.GetComponentInParent<CinemachineCamera>(true) != null
            || (player != null && (volume.transform.IsChildOf(player.transform)
                || (player.cameraTarget != null && volume.transform.IsChildOf(player.cameraTarget)))))
        {
            error = "CameraBounds须为独立静态范围：无Rigidbody/Renderer/Interactable，不能跟随玩家或相机移动。";
            return false;
        }
        error = null;
        return true;
    }

    private void Update()
    {
        // 不修改位置；每帧只检查销毁、禁用和Inspector换引用，早于Brain的LateUpdate。
        RefreshBinding();
    }

    private void RefreshBinding()
    {
        if (player == null)
        {
            if (hasBinding) { Unbind(); }
            return;
        }
        string error;
        if (!ValidateBounds(out error) || player.gameObject.scene != gameObject.scene)
        {
            if (error == null) { error = "跟随玩家必须先移入当前相机所属场景。"; }
            Unbind();
            // 不让原生扩展继续持有并使用卸载前/其他地图的错误范围。
            if (confiner != null && followCamera != null && confiner.gameObject == followCamera.gameObject
                && confiner.BoundingVolume != null && confiner.BoundingVolume.gameObject.scene != gameObject.scene)
            {
                confiner.BoundingVolume = null;
            }
            if (lastBoundsError == null) { Debug.LogWarning("[PlayerCameraBounds] " + error, this); }
            lastBoundsError = error;
            return;
        }
        lastBoundsError = null;
        BoxCollider volume = (BoxCollider)confiner.BoundingVolume;
        if (!isActiveAndEnabled || !followCamera.gameObject.activeInHierarchy || !volume.gameObject.activeInHierarchy)
        {
            // 保留唯一Inspector引用，等待场景环境和表现根激活；未就绪前不开放跟随。
            Unbind();
            return;
        }
        Transform target = player.cameraTarget != null ? player.cameraTarget : player.transform;
        if (hasBinding && boundPlayer == player && boundCamera == followCamera
            && boundVolume == volume && followCamera.Follow == target)
        {
            if (volume.center != boundVolumeCenter || volume.size != boundVolumeSize
                || volume.transform.localToWorldMatrix != boundVolumeMatrix)
            {
                // 同一Box的Inspector调参也要丢弃旧边界减速位置，避免从旧范围拖回。
                Physics.SyncTransforms();
                RememberVolumeGeometry(volume);
                followCamera.PreviousStateIsValid = false;
            }
            return;
        }
        Unbind();
        Physics.SyncTransforms();
        boundPlayer = player;
        boundCamera = followCamera;
        boundVolume = volume;
        RememberVolumeGeometry(volume);
        boundPlayer.Teleported += OnTeleported;
        followCamera.Follow = target;
        followCamera.PreviousStateIsValid = false;
        hasBinding = true;
    }

    private void RememberVolumeGeometry(BoxCollider volume)
    {
        boundVolumeCenter = volume.center;
        boundVolumeSize = volume.size;
        boundVolumeMatrix = volume.transform.localToWorldMatrix;
    }

    private void OnTeleported(Vector3 delta)
    {
        RefreshBinding();
        if (followCamera == null || followCamera.Follow == null) { return; }
        Physics.SyncTransforms();
        followCamera.OnTargetObjectWarped(followCamera.Follow, delta);
        followCamera.PreviousStateIsValid = false;
    }

    private void Unbind()
    {
        if (boundPlayer != null) { boundPlayer.Teleported -= OnTeleported; }
        boundPlayer = null;
        boundVolume = null;
        hasBinding = false;
        if (boundCamera != null)
        {
            boundCamera.Follow = null;
            boundCamera.PreviousStateIsValid = false;
        }
        if (followCamera != null)
        {
            followCamera.Follow = null;
            followCamera.PreviousStateIsValid = false;
        }
        boundCamera = null;
    }

    private void OnDisable()
    {
        Unbind();
    }
}
