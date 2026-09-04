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

    [Header("Руда за крафт (штук тира вещи)")]
    public int oreCostCommonToUncommon = 5;
    public int oreCostUncommonToRare = 10;
    public int oreCostRareToEpic = 20;
    public int oreCostEpicToLegendary = 35;

    public const int REQUIRED_ITEMS = 9; // размер сетки слотов в UI

    /// <summary>Сколько вещей нужно на апгрейд С текущей редкости: 9/6/3/3.
    /// Синхронно с EquipmentBuilder.UPGRADE_COUNT!</summary>
    public static int RequiredForRarity(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common: return 9;
            case ItemRarity.Uncommon: return 6;
            case ItemRarity.Rare: return 3;
            case ItemRarity.Epic: return 3;
            default: return 9;
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        EnsurePools();
    }

    /// <summary>Пустые пулы в инспекторе добираем из Equipment-ассетов —
    /// сцену править не нужно (MCP не умеет биндить ссылки).</summary>
    void EnsurePools()
    {
        if (uncommonPool == null || uncommonPool.Length == 0)
            uncommonPool = LoadPool(ItemRarity.Uncommon);
        if (rarePool == null || rarePool.Length == 0)
            rarePool = LoadPool(ItemRarity.Rare);
        if (epicPool == null || epicPool.Length == 0)
            epicPool = LoadPool(ItemRarity.Epic);
        if (legendaryPool == null || legendaryPool.Length == 0)
            legendaryPool = LoadPool(ItemRarity.Legendary);
    }

    ItemData[] LoadPool(ItemRarity rarity)
    {
        ItemData[] all = Resources.LoadAll<ItemData>("Items/Equipment");
        var list = new List<ItemData>();
        foreach (ItemData a in all)
            if (a != null && a.rarity == rarity) list.Add(a);
        list.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        return list.ToArray();
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
    /// Попытка крафта. Слоты могут быть заполнены частично: нужно столько
    /// вещей текущей редкости, сколько требует шаг (9/6/3/3) + руда тира.
    /// Возвращает результат (успех/провал/ошибка). Списание вещей и руды
    /// делает вызывающий код (CraftingUI) при успехе.
    /// </summary>
    public CraftResult TryCraft(InventorySlot[] inputSlots)
    {
        CraftResult result = new CraftResult();

        // Собираем непустые слоты
        var filled = new List<InventorySlot>();
        foreach (InventorySlot slot in inputSlots)
            if (slot != null && !slot.IsEmpty() && slot.currentItem != null)
                filled.Add(slot);

        if (filled.Count == 0)
        {
            result.success = false;
            result.message = "Положи предметы для ковки!";
            return result;
        }

        // Все предметы одной редкости?
        ItemRarity rarity = filled[0].currentItem.rarity;
        foreach (InventorySlot slot in filled)
        {
            if (slot.currentItem.rarity != rarity)
            {
                result.success = false;
                result.message = "Все предметы должны быть одной редкости!";
                return result;
            }
        }

        int required = RequiredForRarity(rarity);
        if (filled.Count != required)
        {
            result.success = false;
            result.message = "Нужно " + required + " шт. (" + TranslateRarity(rarity) + ")!";
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

        // Руда тира (берём у первого входа, где она задана; нет ни у кого — без руды)
        ItemData ore = null;
        foreach (InventorySlot slot in filled)
            if (slot.currentItem.upgradeOre != null) { ore = slot.currentItem.upgradeOre; break; }
        int oreNeed = GetOreCost(rarity);
        if (ore != null && oreNeed > 0 && CountItem(ore) < oreNeed)
        {
            result.success = false;
            result.message = "Нужно руды: " + oreNeed + " × " + ore.itemName + "!";
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
        ItemData outputItem = DetermineOutput(filled, rarity, nextRarity);
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
    // РУДА — требование и подсчёт (для превью в UI)
    // ═══════════════════════════════════════════════════════════
    public int GetOreCost(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common: return oreCostCommonToUncommon;
            case ItemRarity.Uncommon: return oreCostUncommonToRare;
            case ItemRarity.Rare: return oreCostRareToEpic;
            case ItemRarity.Epic: return oreCostEpicToLegendary;
        }
        return 0;
    }

    /// <summary>Какая руда и сколько нужна для текущих слотов (для превью).
    /// ore=null — руда не требуется.</summary>
    public void GetOreRequirement(InventorySlot[] inputSlots, out ItemData ore, out int need, out int have)
    {
        ore = null; need = 0; have = 0;
        if (inputSlots == null) return;
        ItemRarity? rarity = null;
        foreach (InventorySlot slot in inputSlots)
        {
            if (slot == null || slot.IsEmpty() || slot.currentItem == null) continue;
            if (rarity == null) rarity = slot.currentItem.rarity;
            else if (slot.currentItem.rarity != rarity) return; // mixed — превью не показываем
            if (ore == null && slot.currentItem.upgradeOre != null)
                ore = slot.currentItem.upgradeOre;
        }
        if (rarity == null || ore == null) return;
        need = GetOreCost(rarity.Value);
        have = CountItem(ore);
    }

    /// <summary>Сколько штук предмета в инвентаре + хотбаре.</summary>
    public int CountItem(ItemData item)
    {
        if (item == null) return 0;
        int total = 0;
        foreach (InventorySlot s in AllPlayerSlots())
            if (s != null && !s.IsEmpty() && s.currentItem == item)
                total += Mathf.Max(s.quantity, 1);
        return total;
    }

    /// <summary>Списать штуки предмета из инвентаря + хотбара (по нескольким
    /// слотам при нужде). Возвращает false если не хватило (не списывает).</summary>
    public bool ConsumeItem(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return true;
        if (CountItem(item) < amount) return false;
        int left = amount;
        foreach (InventorySlot s in AllPlayerSlots())
        {
            if (left <= 0) break;
            if (s == null || s.IsEmpty() || s.currentItem != item) continue;
            int take = Mathf.Min(Mathf.Max(s.quantity, 1), left);
            s.quantity -= take;
            left -= take;
            if (s.quantity <= 0) s.ClearSlot();
            else s.UpdateUI();
        }
        return true;
    }

    System.Collections.Generic.IEnumerable<InventorySlot> AllPlayerSlots()
    {
        if (InventoryUI.Instance != null && InventoryUI.Instance.slots != null)
            foreach (InventorySlot s in InventoryUI.Instance.slots)
                if (s != null) yield return s;
        if (HotbarManager.Instance != null && HotbarManager.Instance.slots != null)
            foreach (InventorySlot s in HotbarManager.Instance.slots)
                if (s != null) yield return s;
    }

    // ═══════════════════════════════════════════════════════════
    // ОПРЕДЕЛЕНИЕ ВЫХОДНОГО ПРЕДМЕТА
    // ═══════════════════════════════════════════════════════════
    ItemData DetermineOutput(List<InventorySlot> slots, ItemRarity currentRarity, ItemRarity nextRarity)
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