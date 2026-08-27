using UnityEngine;

/// <summary>
/// Прилавок/NPC-торговец с БЕЗ диалога: удар рядом → сразу открывается
/// магазин со СВОИМ ассортиментом (у каждого прилавка свой товар).
/// Для торговца с диалогом используй NPCInteractable + TraderNPC.
/// </summary>
public class ShopInteraction : MonoBehaviour, IInteractable
{
    [Header("Товар этого прилавка (у каждого свой!)")]
    public ShopManager.ShopItem[] itemsForSale;
    [Header("Вторая вкладка (опц. — напр. животные)")]
    public ShopManager.ShopItem[] itemsForSaleAnimals;

    [Header("Заголовок окна магазина (опц.)")]
    public string shopTitle = "";

    [Header("Анимация приветствия (опц.)")]
    public Animator shopkeeperAnimator;
    public string greetAnimationTrigger = "Greet";

    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        if (ShopUI.Instance != null)
            ShopUI.Instance.Open(itemsForSale, itemsForSaleAnimals, shopTitle);

        if (shopkeeperAnimator != null)
            shopkeeperAnimator.SetTrigger(greetAnimationTrigger);
    }
}
