using UnityEngine;

/// <summary>
/// NPC Шахтёр. Реализует IInteractable.
/// Открывает MinerUI — слоты руды + склад слитков, drag&drop как у лесопилки.
/// </summary>
public class MinerInteraction : MonoBehaviour, IInteractable
{
    [Header("Анимация (опционально)")]
    public Animator minerAnimator;
    public string workAnimationTrigger = "Mine";

    // ── IInteractable ──────────────────────────────────────────
    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        if (MinerUI.Instance != null)
            MinerUI.Instance.Open();

        if (minerAnimator != null)
            minerAnimator.SetTrigger(workAnimationTrigger);
    }
    // ───────────────────────────────────────────────────────────
}