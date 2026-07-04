using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Логика кузни. Проверяет входные слоты, считает шанс провала,
/// выдаёт результат крафта.
/// </summary>
public class CraftingManager : MonoBehaviour
{
    public static CraftingManager Instance { get; private set; }

    [Header("Пулы случайных предметов по редкостям")]
    [Tooltip("Все Uncommon предметы в игре — выпадают при крафте из разных Common")]
    public ItemData[] uncommonPool;
    [Tooltip("Все Rare предметы")]
    public ItemData[] rarePool;
    [Tooltip("Все Epic предметы")]
    public ItemData[] epicPool;
    [Tooltip("Все Legendary предметы")]
    public ItemData[] legendaryPool;

    [Header("Базовый шанс провала по редкостям (%)")]
    public float failChanceCommonToUncommon = 0f;
    public float failChanceUncommonToRare = 10f;
    public float failChanceRareToEpic = 25f;
    public float failChanceEpicToLegendary = 50f;

    [Header("Опыт за успешный крафт")]
    public int xpCommonToUncommon = 20;
    public int xpUncommonToRare = 50;
    public int xpRareToEpic = 100;
    public int xpEpicToLegendary = 250;

    public const int REQUIRED_ITEMS = 9;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ═══════════════════════════════════════════════════════════
    // РЕЗУЛЬТАТ КРАФТА
    // ═══════════════════════════════════════════════════════════
    public struct CraftResult
    {
        public bool success;
        public ItemData outputItem;
        public ItemRarity outputRarity;
        public string message;
    }

    /// <summary>
    /// Попытка крафта. Принимает 9 слотов.
    /// Возвращает результат (успех/провал/ошибка).
    /// </summary>
    public CraftResult TryCraft(InventorySlot[] inputSlots)
    {
        CraftResult result = new CraftResult();

        // Проверяем что все 9 слотов заполнены
        if (!AllSlotsFilled(inputSlots, out string fillError))
        {
            result.success = false;
            result.message = fillError;
            return result;
        }

        // Проверяем что все предметы одной редкости
        if (!AllSameRarity(inputSlots, out ItemRarity rarity, out string rarityError))
        {
            result.success = false;
            result.message = rarityError;
            return result;
        }

        // Проверяем следующую редкость
        ItemRarity nextRarity = GetNextRarity(rarity);
        if (nextRarity == rarity)
        {
            result.success = false;
            result.message = "Легендарные предметы уже максимальной редкости!";
            return result;
        }

        // Шанс провала
        float failChance = GetFailChance(rarity);
        float reduction = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetFailChanceReduction()
            : 0f;
        failChance = Mathf.Max(0f, failChance - reduction);

        if (failChance > 0 && Random.Range(0f, 100f) < failChance)
        {
            // ПРОВАЛ — материалы возвращаются
            result.success = false;
            result.message = "Провал! Попробуй ещё раз.";
            return result;
        }

        // УСПЕХ — определяем выходной предмет
        ItemData outputItem = DetermineOutput(inputSlots, rarity, nextRarity);
        if (outputItem == null)
        {
            result.success = false;
            result.message = "Нет предметов " + TranslateRarity(nextRarity) + " редкости в пуле!";
            return result;
        }

        // Опыт за крафт
        int xp = GetCraftXp(rarity);
        if (PlayerLevel.Instance != null && xp > 0)
            PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Crafting, xp);

        result.success = true;
        result.outputItem = outputItem;
        result.outputRarity = nextRarity;
        result.message = "Успех! Получено: " + outputItem.itemName;
        return result;
    }

    // ═══════════════════════════════════════════════════════════
    // ПРОВЕРКИ
    // ═══════════════════════════════════════════════════════════
    bool AllSlotsFilled(InventorySlot[] slots, out string error)
    {
        error = "";
        foreach (InventorySlot slot in slots)
        {
            if (slot.IsEmpty())
            {
                error = "Заполни все " + REQUIRED_ITEMS + " слотов!";
                return false;
            }
        }
        return true;
    }

    bool AllSameRarity(InventorySlot[] slots, out ItemRarity rarity, out string error)
    {
        rarity = slots[0].currentItem.rarity;
        error = "";
        foreach (InventorySlot slot in slots)
        {
            if (slot.currentItem.rarity != rarity)
            {
                error = "Все предметы должны быть одной редкости!";
                rarity = ItemRarity.Common;
                return false;
            }
        }
        return true;
    }

    // ═══════════════════════════════════════════════════════════
    // ОПРЕДЕЛЕНИЕ ВЫХОДНОГО ПРЕДМЕТА
    // ═══════════════════════════════════════════════════════════
    ItemData DetermineOutput(InventorySlot[] slots, ItemRarity currentRarity, ItemRarity nextRarity)
    {
        // Проверяем — все одинаковые предметы?
        ItemData firstItem = slots[0].currentItem;
        bool allSame = true;
        foreach (InventorySlot slot in slots)
            if (slot.currentItem != firstItem) { allSame = false; break; }

        if (allSame && firstItem.nextRarityVersion != null)
            return firstItem.nextRarityVersion; // точный крафт

        // Разные предметы — случайный из пула
        return GetRandomFromPool(nextRarity);
    }

    ItemData GetRandomFromPool(ItemRarity rarity)
    {
        ItemData[] pool = null;
        switch (rarity)
        {
            case ItemRarity.Uncommon: pool = uncommonPool; break;
            case ItemRarity.Rare: pool = rarePool; break;
            case ItemRarity.Epic: pool = epicPool; break;
            case ItemRarity.Legendary: pool = legendaryPool; break;
        }

        if (pool == null || pool.Length == 0) return null;
        return pool[Random.Range(0, pool.Length)];
    }

    // ═══════════════════════════════════════════════════════════
    // ВСПОМОГАТЕЛЬНЫЕ
    // ═══════════════════════════════════════════════════════════
    public float GetFailChance(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common: return failChanceCommonToUncommon;
            case ItemRarity.Uncommon: return failChanceUncommonToRare;
            case ItemRarity.Rare: return failChanceRareToEpic;
            case ItemRarity.Epic: return failChanceEpicToLegendary;
        }
        return 0f;
    }

    int GetCraftXp(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common: return xpCommonToUncommon;
            case ItemRarity.Uncommon: return xpUncommonToRare;
            case ItemRarity.Rare: return xpRareToEpic;
            case ItemRarity.Epic: return xpEpicToLegendary;
        }
        return 0;
    }

    ItemRarity GetNextRarity(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common: return ItemRarity.Uncommon;
            case ItemRarity.Uncommon: return ItemRarity.Rare;
            case ItemRarity.Rare: return ItemRarity.Epic;
            case ItemRarity.Epic: return ItemRarity.Legendary;
            default: return rarity;
        }
    }

    public string TranslateRarity(ItemRarity r)
    {
        switch (r)
        {
            case ItemRarity.Common: return "Обычный";
            case ItemRarity.Uncommon: return "Необычный";
            case ItemRarity.Rare: return "Редкий";
            case ItemRarity.Epic: return "Эпический";
            case ItemRarity.Legendary: return "Легендарный";
        }
        return "";
    }

    // Превью шанса провала для UI
    public float GetCurrentFailChance(ItemRarity rarity)
    {
        float base_ = GetFailChance(rarity);
        float reduce = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetFailChanceReduction()
            : 0f;
        return Mathf.Max(0f, base_ - reduce);
    }
}