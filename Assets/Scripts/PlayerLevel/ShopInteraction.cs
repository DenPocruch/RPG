using UnityEngine;

/// <summary>
/// NPC Продавец. Реализует IInteractable — открывает магазин (ShopUI).
/// </summary>
public class ShopInteraction : MonoBehaviour, IInteractable
{
    [Header("Анимация (опционально)")]
    public Animator shopkeeperAnimator;
    public string greetAnimationTrigger = "Greet";

    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        if (ShopUI.Instance != null)
            ShopUI.Instance.Open();

        if (shopkeeperAnimator != null)
            shopkeeperAnimator.SetTrigger(greetAnimationTrigger);
    }
}