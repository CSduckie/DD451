using UnityEngine;

public enum GameFlowInteraction
{
    Storage,
    Departure,
    Extraction
}

// E仍由02检测与执行；这里只把有效交互交给当前流程。
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class GameFlowInteractable : Interactable
{
    public GameFlowInteraction interactionType;
    public GameFlowManager manager;

    public override string PromptKey
    {
        get
        {
            switch (interactionType)
            {
                case GameFlowInteraction.Storage: return "flow.interact.storage";
                case GameFlowInteraction.Departure: return "flow.interact.departure";
                default: return "flow.interact.extraction";
            }
        }
    }

    public override bool CanInteract(PlayerInteraction player)
    {
        if (manager == null || player == null || manager.Player == null
            || player != manager.Player.interaction || !manager.Player.CanAct
            || manager.Player.gameObject.scene != gameObject.scene) { return false; }
        return interactionType == GameFlowInteraction.Extraction
            ? manager.Stage == GameFlowStage.Exploring : manager.Stage == GameFlowStage.Shelter;
    }

    public override void Interact(PlayerInteraction player)
    {
        if (!CanInteract(player)) { return; }
        switch (interactionType)
        {
            case GameFlowInteraction.Storage: manager.TryOpenStorage(); break;
            case GameFlowInteraction.Departure: manager.TryDepart(); break;
            case GameFlowInteraction.Extraction: manager.TryExtract(); break;
        }
    }
}
