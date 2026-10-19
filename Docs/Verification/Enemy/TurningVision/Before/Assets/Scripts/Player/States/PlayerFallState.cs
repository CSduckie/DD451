public class PlayerFallState : PlayerStateBase
{
    public override void Enter()
    {
        if (!player.IsBackpackOpen) { player.SetCrouching(false); }
        player.PlayAnimation(player.config.fallAnimation);
    }
    public override void Update()
    {
        if (player.IsCrouching && !player.IsBackpackOpen && !player.WantsCrouch) { player.SetCrouching(false); }
        if (player.IsGrounded)
        {
            player.ChangeState(player.GroundState());
            return;
        }
    }
    public override void FixedUpdate()
    {
        player.ApplyHorizontalVelocity(player.config.moveSpeed);
    }
}
