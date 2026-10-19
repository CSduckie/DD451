using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public class CoverArea : MonoBehaviour
{
    private static readonly List<CoverArea> activeAreas = new List<CoverArea>();
    private BoxCollider volume;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetActiveAreas()
    {
        activeAreas.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestoreActiveAreas()
    {
        // 同时关闭Domain Reload和Scene Reload时，现有掩体也要重新登记。
        foreach (CoverArea area in FindObjectsByType<CoverArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (area.isActiveAndEnabled) { area.Register(); }
        }
    }

    private void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnValidate()
    {
        GetComponent<BoxCollider>().isTrigger = true;
    }

    private void OnEnable()
    {
        Register();
    }

    private void Register()
    {
        volume = GetComponent<BoxCollider>();
        volume.isTrigger = true;
        if (!activeAreas.Contains(this)) { activeAreas.Add(this); }
    }

    private void OnDisable()
    {
        activeAreas.Remove(this);
    }

    public static bool IsHidden(PlayerController player)
    {
        if (player == null || !player.isActiveAndEnabled || !player.IsPrepared || player.IsDead
            || player.IsSceneTransitionBlocked || !player.IsCrouching) { return false; }

        // 不保存玩家引用或Trigger进出计数；瞬移、多Collider和重叠掩体都查询当前真实形状。
        Collider[] playerColliders = player.GetComponentsInChildren<Collider>();
        for (int i = activeAreas.Count - 1; i >= 0; i--)
        {
            CoverArea area = activeAreas[i];
            if (area == null)
            {
                activeAreas.RemoveAt(i);
                continue;
            }
            if (!area.isActiveAndEnabled || area.volume == null || !area.volume.enabled
                || !area.volume.isTrigger) { continue; }

            foreach (Collider playerCollider in playerColliders)
            {
                if (!playerCollider.enabled || !playerCollider.gameObject.activeInHierarchy || playerCollider.isTrigger
                    || playerCollider.GetComponentInParent<PlayerController>() != player) { continue; }
                if (Physics.ComputePenetration(area.volume, area.volume.transform.position, area.volume.transform.rotation,
                    playerCollider, playerCollider.transform.position, playerCollider.transform.rotation, out _, out _))
                {
                    return true;
                }
            }
        }
        return false;
    }
}
