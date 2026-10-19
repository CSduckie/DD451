public class PlayerDieState : PlayerStateBase
{
    public override void Enter()
    {
        player.PlayAnimation(player.config.dieAnimation);
    }
    public override void FixedUpdate()
    {
        player.ApplyHorizontalVelocity(0f);
    }
}
