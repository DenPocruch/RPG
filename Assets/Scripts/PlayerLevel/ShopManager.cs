using UnityEngine;

/// <summary>
/// Данные магазина: список товаров с индивидуальными ценами.
/// Вешается на NPC продавца. Бесконечный запас — покупать можно сколько угодно.
/// </summary>
public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance;

    [System.Serializable]
    public class ShopItem
    {
        public ItemData item;
        public int price = 10; // цена за 1 штуку
    }

    [Header("Ассортимент магазина")]
    public ShopItem[] itemsForSale;

    void Awake()
    {
        Instance = this;
    }

    // Цена товара с учётом скидки от навыков (переиспользуем ReduceServiceCost)
    public int GetPrice(ShopItem shopItem)
    {
        if (shopItem == null || shopItem.price <= 0) return 0;

        float discount = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetServiceCostReduction()
            : 0f;

        return Mathf.Max(1, Mathf.RoundToInt(shopItem.price * (1f - discount / 100f)));
    }

    /// <summary>Купить amount штук товара. Возвращает true при успехе.</summary>
    public bool TryBuy(ShopItem shopItem, int amount)
    {
        if (shopItem == null || shopItem.item == null || amount <= 0) return false;
        if (InventoryUI.Instance == null) return false;

        int totalCost = GetPrice(shopItem) * amount;

        // Проверяем золото
        if (CurrencyManager.Instance == null || CurrencyManager.Instance.Gold < totalCost)
        {
            Debug.Log("[Магазин] Недостаточно золота! Нужно " + totalCost);
            return false;
        }

        // Проверяем место в инвентаре (пробуем добавить)
        bool added = InventoryUI.Instance.AddItem(shopItem.item, amount);
        if (!added)
        {
            Debug.Log("[Магазин] Инвентарь полон!");
            return false;
        }

        // Списываем золото только после успешного добавления
        CurrencyManager.Instance.SpendGold(totalCost);
        Debug.Log("[Магазин] Куплено " + shopItem.item.itemName + " x" + amount + " за " + totalCost + "g");
        return true;
    }
}