public class PlayerJumpState : PlayerStateBase
{
    public override void Enter()
    {
        player.SetCrouching(false);
        player.PlayAnimation(player.config.jumpAnimation);
    }
    public override void Update()
    {
        if (player.Body.linearVelocity.y <= 0f)
        {
            player.ChangeState(PlayerState.Fall);
            return;
        }
    }
    public override void FixedUpdate()
    {
        player.ApplyHorizontalVelocity(player.config.moveSpeed);
    }
}
