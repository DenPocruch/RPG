using UnityEngine;

/// <summary>
/// NPC Лесоруб. Реализует IInteractable.
/// Открывает LumberjackUI где игрок сам кладёт древесину и решает сколько перерабатывать.
/// </summary>
public class LumberjackInteraction : MonoBehaviour, IInteractable
{
    [Header("Анимация (опционально)")]
    public Animator lumberjackAnimator;
    public string workAnimationTrigger = "Chop";

    // ── IInteractable ──────────────────────────────────────────
    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        if (LumberjackUI.Instance != null)
            LumberjackUI.Instance.Open();

        if (lumberjackAnimator != null)
            lumberjackAnimator.SetTrigger(workAnimationTrigger);
    }
    // ───────────────────────────────────────────────────────────
}