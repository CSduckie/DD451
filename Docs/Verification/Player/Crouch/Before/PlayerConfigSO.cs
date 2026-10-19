using UnityEngine;

[CreateAssetMenu(menuName = "Player/Player Config")]
public class PlayerConfigSO : ScriptableObject
{
    [Header("运动（沿用2DARPG手感）")]
    [Min(0f)] public float moveSpeed = 5f;
    [Min(0f)] public float crouchSpeed = 2f;
    [Min(0f)] public float acceleration = 20f;
    [Min(0f)] public float deceleration = 30f;
    [Min(0f)] public float jumpSpeed = 13f;
    [Min(0f)] public float gravityScale = 3.5f;
    [Tooltip("与原PlayerBetterJump一致：在基础重力上额外增加倍率减1，不乘gravityScale。")]
    [Min(1f)] public float fallMultiplier = 2.5f;
    [Min(1f)] public float lowJumpMultiplier = 3f;
    [Min(0f)] public float maxFallSpeed = 12f;
    [Header("跳跃容错（秒，0关闭）")]
    [Tooltip("落地前提前按跳，可在此窗口内落地起跳。")]
    [Min(0f)] public float jumpBufferSeconds = 0.12f;
    [Tooltip("走出平台后仍可起跳的时间；主动起跳后不可再次使用。")]
    [Min(0f)] public float coyoteTimeSeconds = 0.12f;
    public float planeZ;
    [Header("3D地面检测")]
    [Min(0.01f)] public float groundProbeDistance = 0.12f;
    [Min(0.01f)] public float groundProbeRadius = 0.25f;
    [Range(0f, 1f)] public float groundMinNormalY = 0.65f;
    public LayerMask groundLayers = ~0;
    [Header("生命")]
    [Min(1)] public int maxHealth = 3;
    [Min(0f)] public float damageInvulnerabilitySeconds = 0.5f;
    [Header("表现（模型默认朝向+X）")]
    public float modelYawOffset;
    [Range(0.1f, 1f)] public float crouchVisualHeight = 0.65f;
    [Min(0f)] public float animationBlendSeconds = 0.12f;
    public string idleAnimation = "Idle";
    public string moveAnimation = "Run";
    public string jumpAnimation = "Jump";
    public string fallAnimation = "Fall";
    public string crouchIdleAnimation = "CrouchIdle";
    public string crouchMoveAnimation = "CrouchMove";
    public string dieAnimation = "Die";
}
