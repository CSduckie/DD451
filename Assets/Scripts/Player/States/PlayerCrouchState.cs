using UnityEngine;

public class PlayerCrouchState : PlayerStateBase
{
    private bool moving;
    public override void Enter()
    {
        player.SetCrouching(true);
        moving = Mathf.Abs(player.MoveInput) > 0.01f;
        PlayPose();
    }
    public override void Update()
    {
        if (ChangeGroundState()) { return; }
        bool nowMoving = Mathf.Abs(player.MoveInput) > 0.01f;
        if (nowMoving != moving)
        {
            moving = nowMoving;
            PlayPose();
        }
    }
    private void PlayPose()
    {
        player.PlayAnimation(moving ? player.config.crouchMoveAnimation : player.config.crouchIdleAnimation);
    }
    public override void FixedUpdate()
    {
        player.ApplyHorizontalVelocity(player.config.crouchSpeed);
    }
}
