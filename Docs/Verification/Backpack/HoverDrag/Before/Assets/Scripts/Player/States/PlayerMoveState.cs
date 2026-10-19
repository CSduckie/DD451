public class PlayerMoveState : PlayerStateBase
{
    public override void Enter()
    {
        player.SetCrouching(false);
        player.PlayAnimation(player.config.moveAnimation);
    }
    public override void Update()
    {
        if (ChangeGroundState()) { return; }
    }
    public override void FixedUpdate()
    {
        player.ApplyHorizontalVelocity(player.config.moveSpeed);
    }
}
