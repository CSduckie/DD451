using UnityEngine;
using UnityEngine.InputSystem;

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
        LastJumpPressedTime = double.NegativeInfinity;
    }

    private void OnDisable()
    {
        ClearRequests();
        if (jump != null) { jump.performed -= OnJump; }
        if (interact != null) { interact.performed -= OnInteract; }
        move?.Dispose();
        jump?.Dispose();
        crouch?.Dispose();
        interact?.Dispose();
        move = jump = crouch = interact = null;
    }
}
