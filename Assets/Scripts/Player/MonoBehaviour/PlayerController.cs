using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlayerState
{
    None, Idle, Move, Jump, Fall, Crouch, Die
}

// 改编自 CSduckie/2DArpg-Portfolio 的 PlayerController：枚举switch → 泛型状态 → Enter播放动画。
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider), typeof(PlayerHealth))]
[RequireComponent(typeof(PlayerInputReader), typeof(PlayerInteraction))]
public class PlayerController : MonoBehaviour, IStateMachineOwner
{
    public PlayerConfigSO config;
    public PlayerInputReader inputReader;
    public PlayerHealth health;
    public PlayerInteraction interaction;
    public Transform visual;
    public Transform cameraTarget;
    public Animator animator;

    private readonly StateMachine stateMachine = new StateMachine();
    private readonly HashSet<string> missingAnimations = new HashSet<string>();
    private CapsuleCollider capsule;
    private float standingHeight;
    private Vector3 standingCenter;
    private Vector3 visualScale;
    private Vector3 visualPosition;
    private bool initialized;
    private bool jumpRequested;
    private bool interactionUiBlocked;
    private bool sceneTransitionBlocked;
    private float moveInput;
    private bool crouchRequested;
    private bool jumpHeld;
    private bool jumpConsumedSinceGrounded;
    private double jumpExpiresAt = double.NegativeInfinity;
    private double lastGroundedTime = double.NegativeInfinity;

    public Rigidbody Body { get; private set; }
    public PlayerState CurrentState { get; private set; }
    public bool IsPrepared { get { return health != null && health.IsPrepared; } }
    public bool IsDead { get { return health != null && health.IsDead; } }
    public bool IsGrounded { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsBackpackOpen { get; private set; }
    public bool IsSceneTransitionBlocked { get { return sceneTransitionBlocked; } }
    public int FacingDirection { get; private set; } = 1;
    public Vector3 Position { get { return transform.position; } }
    public bool CanAct { get { return isActiveAndEnabled && IsPrepared && !IsDead && !IsBackpackOpen && !sceneTransitionBlocked; } }
    public float MoveInput { get { return CanAct ? moveInput : 0f; } }
    public bool WantsCrouch
    {
        get { return IsBackpackOpen ? IsCrouching : CanAct && (crouchRequested || (IsCrouching && !CanStand())); }
    }
    public bool JumpRequested { get { return jumpRequested && CanAct && Time.timeAsDouble <= jumpExpiresAt; } }
    public bool CanJump
    {
        get
        {
            return CanAct && config != null && !IsCrouching && !WantsCrouch && !jumpConsumedSinceGrounded
                && (IsGrounded || (config.coyoteTimeSeconds > 0f
                    && Time.timeAsDouble - lastGroundedTime <= config.coyoteTimeSeconds));
        }
    }
    public event Action<Vector3> Teleported;

    private void Awake()
    {
        InitializeReferences();
    }
    private void InitializeReferences()
    {
        if (initialized) { return; }
        Body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        standingHeight = capsule.height;
        standingCenter = capsule.center;
        inputReader = inputReader != null ? inputReader : GetComponent<PlayerInputReader>();
        health = health != null ? health : GetComponent<PlayerHealth>();
        interaction = interaction != null ? interaction : GetComponent<PlayerInteraction>();
        if (config != null) { health.config = config; }
        Body.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        Body.useGravity = true;
        Body.isKinematic = !IsPrepared || sceneTransitionBlocked;
        if (visual != null)
        {
            visualScale = visual.localScale;
            visualPosition = visual.localPosition;
        }
        if (animator != null) { animator.applyRootMotion = false; }
        initialized = true;
    }

    private void OnEnable()
    {
        InitializeReferences();
        stateMachine.Init(this);
        ClearRequests();
        UpdateGrounded();
        if (IsPrepared) { ChangeState(IsDead ? PlayerState.Die : IsGrounded ? GroundState() : PlayerState.Fall); }
        RefreshInteraction();
    }

    private void Update()
    {
        if (sceneTransitionBlocked)
        {
            ClearRequests();
            RefreshInteraction();
            return;
        }
        moveInput = inputReader.MoveX;
        bool pressedCrouch = inputReader.ConsumeCrouch();
        if (CanAct)
        {
            if (config != null && config.crouchInputMode == CrouchInputMode.Toggle)
            {
                if (pressedCrouch) { crouchRequested = !crouchRequested; }
            }
            else { crouchRequested = inputReader.CrouchHeld; }
        }
        jumpHeld = CanAct && inputReader.JumpHeld;
        UpdateGrounded();
        bool pressedJump = inputReader.ConsumeJump();
        bool pressedInteract = inputReader.ConsumeInteract();
        if (CanAct && config != null && pressedJump && !IsCrouching && !WantsCrouch)
        {
            // 按下时开始计时，空中也能缓存；关闭buffer仍保留一次正常按键到下一物理帧。
            jumpRequested = config.jumpBufferSeconds > 0f || CanJump;
            jumpExpiresAt = config.jumpBufferSeconds > 0f
                ? inputReader.LastJumpPressedTime + config.jumpBufferSeconds : double.PositiveInfinity;
        }
        if (!CanAct || Time.timeAsDouble > jumpExpiresAt) { jumpRequested = false; }
        // 背包只限制主动操作，落地、死亡和状态计时始终转发。
        stateMachine.CurrentState?.Update();
        RefreshInteraction();
        if (pressedInteract && CanAct && !interactionUiBlocked) { interaction.TryInteract(); }
        UpdateFacing();
    }

    private void FixedUpdate()
    {
        // 切场景冻结期间不探测重叠场景，也不让现有状态向kinematic刚体写速度。
        if (sceneTransitionBlocked || Body.isKinematic) { return; }
        UpdateGrounded();
        // 同一个物理入口处理地面、落地buffer与土狼跳，先消费再进入Jump，避免重复起跳。
        if (ExecuteJump()) { ChangeState(PlayerState.Jump, CurrentState == PlayerState.Jump); }
        stateMachine.CurrentState?.FixedUpdate();
        ApplyJumpGravity();
        if (config != null && config.jumpBufferSeconds <= 0f) { jumpRequested = false; }
    }

    private void RefreshInteraction()
    {
        interaction.SetInputBlocked(!CanAct || interactionUiBlocked);
        interaction.RefreshTarget();
    }

    public void SetBackpackOpen(bool open)
    {
        if (IsBackpackOpen == open) { return; }
        IsBackpackOpen = open;
        // 开关背包丢弃按钮输入，但保留开包前的切换意图；受击关包也走这里。
        ClearRequests(false);
        RefreshInteraction();
    }

    public void SetInteractionUiBlocked(bool blocked)
    {
        interactionUiBlocked = blocked;
        inputReader.ClearRequests();
        RefreshInteraction();
    }

    public void SetSceneTransitionBlocked(bool blocked)
    {
        InitializeReferences();
        sceneTransitionBlocked = blocked;
        ClearRequests();
        // 先清速度再冻结；关闭背包和准备新轮都不能解除这个独立流程锁。
        if (!Body.isKinematic)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
        }
        Body.isKinematic = blocked || !IsPrepared;
        if (!blocked && isActiveAndEnabled && IsPrepared)
        {
            UpdateGrounded();
            ChangeState(IsDead ? PlayerState.Die : IsGrounded ? GroundState() : PlayerState.Fall);
        }
        RefreshInteraction();
    }

    public bool PrepareInitialShelter(Vector3 spawnPosition)
    {
        InitializeReferences();
        if (!CanRelocate(spawnPosition)) { return false; }
        health.config = config;
        if (!health.PrepareInitialShelter()) { return false; }
        IsBackpackOpen = false;
        interactionUiBlocked = false;
        RelocatePlayer(spawnPosition, true);
        health.NotifyPrepared();
        return true;
    }

    public bool RelocateWithinRun(Vector3 spawnPosition)
    {
        InitializeReferences();
        if (!IsPrepared || !CanRelocate(spawnPosition)) { return false; }
        // 返回只清输入、速度与位置；生命、死亡、轮次和物品使用次数保持原值。
        RelocatePlayer(spawnPosition, false);
        return true;
    }

    public bool RecoverAfterDeathInShelter(Vector3 spawnPosition)
    {
        InitializeReferences();
        // 仅流程锁持有期间恢复；损失结算与庇护所准备由GameFlow先完成。
        if (!sceneTransitionBlocked || !CanRelocate(spawnPosition)) { return false; }
        health.config = config;
        if (!health.RecoverAfterDeathInShelter()) { return false; }
        IsBackpackOpen = false;
        interactionUiBlocked = false;
        RelocatePlayer(spawnPosition, true);
        health.NotifyPrepared();
        return true;
    }

    public bool ResetForExploration(int roundId, Vector3 spawnPosition)
    {
        InitializeReferences();
        if (!CanRelocate(spawnPosition)) { return false; }
        health.config = config;
        if (!health.PrepareForRound(roundId)) { return false; }
        IsBackpackOpen = false;
        interactionUiBlocked = false;
        RelocatePlayer(spawnPosition, true);
        health.NotifyPrepared();
        return true;
    }

    private bool CanRelocate(Vector3 spawnPosition)
    {
        return config != null && isActiveAndEnabled && IsFinite(spawnPosition.x)
            && IsFinite(spawnPosition.y) && IsFinite(config.planeZ);
    }

    private void RelocatePlayer(Vector3 spawnPosition, bool resetPose)
    {
        Vector3 oldPosition = transform.position;
        stateMachine.Stop();
        stateMachine.Init(this);
        CurrentState = PlayerState.None;
        ClearRequests();
        if (resetPose)
        {
            FacingDirection = 1;
        }
        // 明确准备/定位入口共用瞬移步骤，Z始终取玩法配置。
        spawnPosition.z = config.planeZ;
        Body.isKinematic = sceneTransitionBlocked;
        Body.position = spawnPosition;
        transform.position = spawnPosition;
        Body.rotation = Quaternion.identity;
        if (!Body.isKinematic)
        {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
        }
        Physics.SyncTransforms();
        // 先到新位置，再检查起身空间；旧位置的低顶不能挡住新一轮复位。
        if (resetPose) { SetCrouching(false); }
        UpdateGrounded();
        ChangeState(IsDead ? PlayerState.Die : IsGrounded ? GroundState() : PlayerState.Fall);
        UpdateFacing();
        RefreshInteraction();
        Teleported?.Invoke(spawnPosition - oldPosition);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
    internal void HandleDeath()
    {
        ClearRequests();
        if (isActiveAndEnabled) { ChangeState(PlayerState.Die); }
        interaction.SetInputBlocked(true);
    }

    private void ClearRequests(bool resetCrouch = true)
    {
        jumpRequested = false;
        jumpExpiresAt = double.NegativeInfinity;
        lastGroundedTime = double.NegativeInfinity;
        jumpConsumedSinceGrounded = false;
        moveInput = 0f;
        if (resetCrouch) { crouchRequested = false; }
        jumpHeld = false;
        inputReader.ClearRequests();
    }

    public bool ChangeState(PlayerState state, bool reenter = false)
    {
        if (!IsPrepared || (IsDead && state != PlayerState.Die)) { return false; }
        bool changed;
        switch (state)
        {
            case PlayerState.Idle: changed = stateMachine.ChangeState<PlayerIdleState>(); break;
            case PlayerState.Move: changed = stateMachine.ChangeState<PlayerMoveState>(); break;
            case PlayerState.Jump: changed = stateMachine.ChangeState<PlayerJumpState>(reenter); break;
            case PlayerState.Fall: changed = stateMachine.ChangeState<PlayerFallState>(); break;
            case PlayerState.Crouch: changed = stateMachine.ChangeState<PlayerCrouchState>(); break;
            case PlayerState.Die: changed = stateMachine.ChangeState<PlayerDieState>(); break;
            default: return false;
        }
        if (changed) { CurrentState = state; }
        return changed;
    }

    public PlayerState GroundState()
    {
        if (WantsCrouch) { return PlayerState.Crouch; }
        return Mathf.Abs(MoveInput) > 0.01f ? PlayerState.Move : PlayerState.Idle;
    }

    public void SetCrouching(bool crouching)
    {
        if (capsule == null || config == null) { return; }
        if (!crouching && IsCrouching && !CanStand()) { return; }
        IsCrouching = crouching;
        float height = crouching
            ? Mathf.Max(capsule.radius * 2f, standingHeight * Mathf.Clamp01(config.crouchVisualHeight))
            : standingHeight;
        capsule.height = height;
        capsule.center = standingCenter + Vector3.up * ((height - standingHeight) * 0.5f);
        if (visual == null) { return; }
        float ratio = height / standingHeight;
        float feetY = standingCenter.y - standingHeight * 0.5f;
        visual.localScale = new Vector3(visualScale.x, visualScale.y * ratio, visualScale.z);
        visual.localPosition = new Vector3(visualPosition.x,
            feetY + (visualPosition.y - feetY) * ratio, visualPosition.z);
    }

    private bool CanStand()
    {
        if (!IsCrouching || capsule == null) { return true; }
        // 玩家固定为Y轴胶囊。只检查头顶扩展区域，避免脚下地面阻止起身。
        Vector3 scale = transform.lossyScale;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float heightScale = Mathf.Abs(scale.y);
        Vector3 crouchTop = Body.position + Vector3.Scale(capsule.center, scale)
            + Vector3.up * Mathf.Max(0f, capsule.height * heightScale * 0.5f - radius);
        Vector3 standingTop = Body.position + Vector3.Scale(standingCenter, scale)
            + Vector3.up * Mathf.Max(0f, standingHeight * heightScale * 0.5f - radius);
        foreach (Collider obstacle in Physics.OverlapCapsule(crouchTop, standingTop,
            Mathf.Max(0.001f, radius - 0.01f), ~0, QueryTriggerInteraction.Ignore))
        {
            if (obstacle.attachedRigidbody == Body || obstacle.transform.IsChildOf(transform)) { continue; }
            if (Physics.GetIgnoreLayerCollision(gameObject.layer, obstacle.gameObject.layer)
                || Physics.GetIgnoreCollision(capsule, obstacle)) { continue; }
            return false;
        }
        return true;
    }

    public void ApplyHorizontalVelocity(float speed)
    {
        if (Body == null || Body.isKinematic) { return; }
        Vector3 velocity = Body.linearVelocity;
        if (!CanAct || config == null) { velocity.x = 0f; }
        else
        {
            float targetSpeed = MoveInput * Mathf.Max(0f, speed);
            float speedDifference = targetSpeed - velocity.x;
            float rate = Mathf.Abs(targetSpeed) > 0.01f ? config.acceleration : config.deceleration;
            // 原ARPG公式pow(abs(speedDiff)*rate, 0.9)，地面/空中一致；限制末步防止过冲抖动。
            float step = Mathf.Pow(Mathf.Abs(speedDifference) * Mathf.Max(0f, rate), 0.9f) * Time.fixedDeltaTime;
            velocity.x = Mathf.MoveTowards(velocity.x, targetSpeed, step);
        }
        velocity.z = 0f;
        Body.linearVelocity = velocity;
    }

    public bool ExecuteJump()
    {
        if (Body == null || Body.isKinematic || !JumpRequested || !CanJump) { return false; }
        jumpRequested = false;
        jumpExpiresAt = double.NegativeInfinity;
        lastGroundedTime = double.NegativeInfinity;
        jumpConsumedSinceGrounded = true;
        Vector3 velocity = Body.linearVelocity;
        velocity.y = Mathf.Max(0f, config.jumpSpeed);
        velocity.z = 0f;
        Body.linearVelocity = velocity;
        IsGrounded = false;
        return true;
    }

    private void ApplyJumpGravity()
    {
        if (!IsPrepared || config == null || Body.isKinematic || !Body.useGravity) { return; }
        Vector3 velocity = Body.linearVelocity;
        float extraScale = Mathf.Max(0f, config.gravityScale) - 1f;
        if (velocity.y < 0f) { extraScale += Mathf.Max(1f, config.fallMultiplier) - 1f; }
        else if (velocity.y > 0f && !jumpHeld) { extraScale += Mathf.Max(1f, config.lowJumpMultiplier) - 1f; }
        // 3D Rigidbody没有gravityScale；补足基础倍率，再加原BetterJump的额外重力。
        float engineGravityStep = Physics.gravity.y * Time.fixedDeltaTime;
        velocity.y += engineGravityStep * extraScale;
        // Unity稍后还会施加一次引擎重力，提前计入才能让实际下落速度不超过上限。
        velocity.y = Mathf.Max(velocity.y, -Mathf.Max(0f, config.maxFallSpeed) - engineGravityStep);
        Body.linearVelocity = velocity;
    }

    private void UpdateGrounded()
    {
        IsGrounded = false;
        if (config == null || capsule == null || Body == null || Body.linearVelocity.y > 0.1f) { return; }
        Vector3 bottom = Body.position + capsule.center - Vector3.up * capsule.height * 0.5f;
        float radius = Mathf.Min(config.groundProbeRadius, capsule.radius * 0.9f);
        Vector3 origin = bottom + Vector3.up * (radius + 0.05f);
        foreach (RaycastHit hit in Physics.SphereCastAll(origin, radius, Vector3.down,
            config.groundProbeDistance + 0.05f, config.groundLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.attachedRigidbody == Body || hit.collider.transform.IsChildOf(transform)) { continue; }
            if (hit.normal.y >= config.groundMinNormalY)
            {
                IsGrounded = true;
                if (CanAct)
                {
                    lastGroundedTime = Time.timeAsDouble;
                    jumpConsumedSinceGrounded = false;
                }
                return;
            }
        }
    }

    private void UpdateFacing()
    {
        if (Mathf.Abs(MoveInput) > 0.01f) { FacingDirection = MoveInput > 0f ? 1 : -1; }
        if (visual != null && config != null)
        {
            visual.localRotation = Quaternion.Euler(0f, config.modelYawOffset + (FacingDirection == 1 ? 0f : 180f), 0f);
        }
    }

    public void PlayAnimation(string animationName, float fixedTransitionDuration = -1f)
    {
        if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrEmpty(animationName)) { return; }
        int hash = Animator.StringToHash(animationName);
        if (!animator.HasState(0, hash))
        {
            if (missingAnimations.Add(animationName)) { Debug.LogWarning("玩家Animator缺少状态：" + animationName, this); }
            return;
        }
        animator.CrossFadeInFixedTime(hash, fixedTransitionDuration >= 0f ? fixedTransitionDuration : config.animationBlendSeconds);
    }

    private void OnDisable()
    {
        ClearRequests();
        stateMachine.Stop();
        CurrentState = PlayerState.None;
        interaction.SetInputBlocked(true);
        // Controller停用后不再有FixedUpdate，清掉最后一次主动横移，但保留重力。
        if (Body != null && !Body.isKinematic) { Body.linearVelocity = new Vector3(0f, Body.linearVelocity.y, 0f); }
    }

    private void OnDestroy()
    {
        stateMachine.Stop();
    }
}
