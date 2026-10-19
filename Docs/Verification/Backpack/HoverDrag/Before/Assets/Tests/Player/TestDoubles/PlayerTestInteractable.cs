using UnityEngine;

// 只记录调用次数；测试箱子不接管真实物品或背包业务。
public class PlayerTestInteractable : Interactable
{
    public bool available = true;
    public string promptKey = "player.interact.test";

    public int InteractionCount { get; private set; }
    public override string PromptKey => promptKey;

    // 测试构造器显式调用；不在Awake/OnEnable补范围，避免掩盖正式资产漏接Trigger的问题。
    public void ConfigureTestTrigger(Vector3 center, Vector3 size)
    {
        if (interactionTrigger == null)
        {
            GameObject triggerObject = new GameObject("TriggerBox");
            triggerObject.transform.SetParent(transform, false);
            interactionTrigger = triggerObject.AddComponent<BoxCollider>();
        }
        interactionTrigger.isTrigger = true;
        interactionTrigger.center = center;
        interactionTrigger.size = size;
    }

    public override bool CanInteract(PlayerInteraction player)
    {
        return available;
    }

    public override void Interact(PlayerInteraction player)
    {
        InteractionCount++;
    }
}
