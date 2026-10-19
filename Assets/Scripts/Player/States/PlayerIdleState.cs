public class PlayerIdleState : PlayerStateBase
{
    public override void Enter()
    {
        player.SetCrouching(false);
        player.PlayAnimation(player.config.idleAnimation);
    }
    public override void Update()
    {
        if (ChangeGroundState()) { return; }
    }
    public override void FixedUpdate()
    {
        player.ApplyHorizontalVelocity(0f);
    }
}
