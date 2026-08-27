using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Магазин: список товаров + логика покупки.
/// Товар берётся из ShopInteraction NPC. Открытие окна — ShopUI.
/// </summary>
public class ShopManager : MonoBehaviour, ISaveable
{
    public static ShopManager Instance;

    [System.Serializable]
    public class ShopItem
    {
        public ItemData item;
        public int price = 10; // цена за 1 штуку
        [Tooltip("Тег разблокировки (unlocksFeature тег перка). Товар виден только после покупки перка.")]
        public string unlockTag = "";
    }

    [Header("Ассортимент магазина")]
    public ShopItem[] itemsForSale;

    // Куплено детёнышей по тегам (animal_*): лимит = 2 на 1-й ранг перка, +1 за ранг, макс 10
    private Dictionary<string, int> boughtByTag = new Dictionary<string, int>();

    void Awake()
    {
        // Защита от дубликата: копия PersistentRoot при возврате в сцену
        // создаёт второй экземпляр — копию уничтожаем, оригинал живёт
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Цена товара с учётом скидки (перк ReduceServiceCost)
    public int GetPrice(ShopItem shopItem)
    {
        if (shopItem == null || shopItem.price <= 0) return 0;

        float discount = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetServiceCostReduction()
            : 0f;

        return Mathf.Max(1, Mathf.RoundToInt(shopItem.price * (1f - discount / 100f)));
    }

    /// <summary>Купить amount штук. Возвращает true при успехе.</summary>
    public bool TryBuy(ShopItem shopItem, int amount)
    {
        if (shopItem == null || shopItem.item == null || amount <= 0) return false;
        if (InventoryUI.Instance == null) return false;

        // Лимит детёнышей: ранг перка animal_* → разрешено 2 на 1-й ранг, +1 за ранг, макс 10
        if (!string.IsNullOrEmpty(shopItem.unlockTag) && shopItem.unlockTag.StartsWith("animal_"))
        {
            int rank = SkillTreeManager.Instance != null
                ? SkillTreeManager.Instance.GetNodeRankByFeature(shopItem.unlockTag) : 0;
            int allowed = rank == 0 ? 0 : Mathf.Min(rank + 1, 10);
            boughtByTag.TryGetValue(shopItem.unlockTag, out int bought);
            if (bought + amount > allowed)
            {
                ActionLogUI.Show("[Магазин] Лимит животных: куплено " + bought + "/" + allowed +
                    ". Прокачай перк в дереве навыков!");
                return false;
            }
            boughtByTag[shopItem.unlockTag] = bought + amount;
        }

        int totalCost = GetPrice(shopItem) * amount;

        // Проверка золота
        if (CurrencyManager.Instance == null || CurrencyManager.Instance.Gold < totalCost)
        {
            ActionLogUI.Show("[Магазин] Недостаточно золота! Нужно " + totalCost);
            return false;
        }

        // Добавляем в инвентарь (сначала проверка)
        bool added = InventoryUI.Instance.AddItem(shopItem.item, amount);
        if (!added)
        {
            ActionLogUI.Show("[Магазин] Инвентарь полон!");
            return false;
        }

        // Списываем золото только после успешного добавления
        CurrencyManager.Instance.SpendGold(totalCost);

        // Сейв по событию: покупка
        SaveManager.Instance?.Save();

        ActionLogUI.Show("[Магазин] Куплено " + shopItem.item.itemName + " x" + amount + " за " + totalCost + "g");
        return true;
    }

    /// <summary>Сколько детёнышей уже куплено по тегу (для UI-лимита).</summary>
    public int GetBoughtCount(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return 0;
        boughtByTag.TryGetValue(tag, out int bought);
        return bought;
    }

    // ─── ISaveable: купленные детёныши ───
    [System.Serializable] private class TagCount { public string tag; public int count; }
    [System.Serializable] private class BoughtSave { public List<TagCount> items = new List<TagCount>(); }

    public string SaveKey => "animal_shop";

    public string CaptureState()
    {
        BoughtSave save = new BoughtSave();
        foreach (var kvp in boughtByTag)
            save.items.Add(new TagCount { tag = kvp.Key, count = kvp.Value });
        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        BoughtSave save = JsonUtility.FromJson<BoughtSave>(json);
        boughtByTag.Clear();
        if (save == null || save.items == null) return;
        foreach (var t in save.items) boughtByTag[t.tag] = t.count;
    }
}
