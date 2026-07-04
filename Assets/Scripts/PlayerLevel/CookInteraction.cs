using UnityEngine;

/// <summary>
/// NPC Повар. Реализует IInteractable — открывает книгу рецептов (CookUI).
/// </summary>
public class CookInteraction : MonoBehaviour, IInteractable
{
    [Header("Анимация (опционально)")]
    public Animator cookAnimator;
    public string workAnimationTrigger = "Cook";

    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        if (CookUI.Instance != null)
            CookUI.Instance.Open();

        if (cookAnimator != null)
            cookAnimator.SetTrigger(workAnimationTrigger);
    }
}