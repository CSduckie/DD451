using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

[DisallowMultipleComponent]
public class PlayerInputReader : MonoBehaviour
{
    public InputActionAsset inputActions;
    private InputAction move;
    private InputAction jump;
    private InputAction crouch;
    private InputAction interact;
    private bool jumpPending;
    private bool interactPending;
    private bool crouchPending;
    private bool crouchNeedsRelease;

    public float MoveX { get { return move != null && move.enabled ? move.ReadValue<Vector2>().x : 0f; } }
    public bool CrouchHeld { get { return crouch != null && crouch.enabled && crouch.IsPressed(); } }
    public bool JumpHeld { get { return jump != null && jump.enabled && jump.IsPressed(); } }
    public double LastJumpPressedTime { get; private set; } = double.NegativeInfinity;

    private void OnEnable()
    {
        if (inputActions == null)
        {
            return;
        }
        // 只复制四个动作的运行实例，不改共享资源，也不禁用其他模块的动作表。
        move = CopyAction("Move");
        jump = CopyAction("Jump");
        crouch = CopyAction("Crouch");
        interact = CopyAction("Interact");
        jump.performed += OnJump;
        interact.performed += OnInteract;
        crouch.performed += OnCrouch;
        crouch.canceled += OnCrouchReleased;
        crouch.wantsInitialStateCheck = true;
        move.Enable();
        jump.Enable();
        crouch.Enable();
        interact.Enable();
        ClearRequests();
    }

    private InputAction CopyAction(string actionName)
    {
        InputAction action = inputActions.FindAction("Player/" + actionName, true).Clone();
        action.bindingMask = InputBinding.MaskByGroup("Keyboard&Mouse");
        return action;
    }

    private void OnJump(InputAction.CallbackContext context)
    {
        jumpPending = true;
        LastJumpPressedTime = Time.timeAsDouble;
    }
    private void OnInteract(InputAction.CallbackContext context)
    {
        interactPending = true;
    }
    private void OnCrouch(InputAction.CallbackContext context)
    {
        if (!crouchNeedsRelease) { crouchPending = true; }
    }
    private void OnCrouchReleased(InputAction.CallbackContext context)
    {
        crouchNeedsRelease = false;
    }

    public bool ConsumeCrouch()
    {
        bool pending = crouchPending;
        crouchPending = false;
        return pending;
    }
    public bool ConsumeJump()
    {
        bool pending = jumpPending;
        jumpPending = false;
        return pending;
    }

    public bool ConsumeInteract()
    {
        bool pending = interactPending;
        interactPending = false;
        return pending;
    }

    public void ClearRequests()
    {
        jumpPending = false;
        interactPending = false;
        crouchPending = false;
        // 锁定、复活或重新启用时，旧的长按必须先松开，才算下一次切换。
        // 读取动作当前绑定的按钮，不把C写死，改键后仍然有效。
        crouchNeedsRelease = false;
        if (crouch != null)
        {
            foreach (var control in crouch.controls)
            {
                if (control is ButtonControl button && button.isPressed)
                {
                    crouchNeedsRelease = true;
                    break;
                }
            }
        }
        LastJumpPressedTime = double.NegativeInfinity;
    }

    private void OnDisable()
    {
        ClearRequests();
        if (jump != null) { jump.performed -= OnJump; }
        if (interact != null) { interact.performed -= OnInteract; }
        if (crouch != null)
        {
            crouch.performed -= OnCrouch;
            crouch.canceled -= OnCrouchReleased;
        }
        move?.Dispose();
        jump?.Dispose();
        crouch?.Dispose();
        interact?.Dispose();
        move = jump = crouch = interact = null;
    }
}
