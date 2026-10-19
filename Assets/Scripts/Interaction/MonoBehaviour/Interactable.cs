using UnityEngine;

// 06、08继承此组件实现世界交互；目标不再独立监听E键。
public abstract class Interactable : MonoBehaviour
{
    [Header("世界交互范围")]
    [Tooltip("必填：自身或子物体上的BoxCollider，isTrigger必须启用；范围不依赖模型接触或玩家球形半径。")]
    public BoxCollider interactionTrigger;
    [Tooltip("可选：墙体遮挡与目标排序使用此点，未指定则使用当前目标根的位置。不要使用Trigger内部最近点。")]
    public Transform interactionPoint;

    private bool reportedTriggerConfigurationError;

    public abstract string PromptKey { get; }

    // 不在OnEnable注册：业务子类已有自己的生命周期，查询也必须支持传送和初始重叠。
    public bool TryGetInteractionTrigger(out BoxCollider trigger)
    {
        trigger = interactionTrigger;
        if (ReferenceEquals(trigger, null))
        {
            ReportTriggerConfigurationError();
            return false;
        }
        // Unity已销毁的引用和临时禁用属于正常生命周期，不反复输出配置警告。
        if (trigger == null) { return false; }
        if (!trigger.isTrigger || trigger.GetComponentInParent<Interactable>(true) != this)
        {
            ReportTriggerConfigurationError();
            return false;
        }
        reportedTriggerConfigurationError = false;
        return isActiveAndEnabled && trigger.enabled && trigger.gameObject.activeInHierarchy;
    }

    private void ReportTriggerConfigurationError()
    {
        if (reportedTriggerConfigurationError) { return; }
        reportedTriggerConfigurationError = true;
        Debug.LogWarning("交互配置错误 [interactionTrigger]：" + name
            + " 必须指定自身或子物体上属于本Interactable的BoxCollider TriggerBox，并启用isTrigger；未配置时拒绝交互。", this);
    }

    public abstract bool CanInteract(PlayerInteraction player);

    public abstract void Interact(PlayerInteraction player);
}
