using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ТОЛЬКО ДЛЯ ТЕСТИРОВАНИЯ — удали или отключи перед релизом!
/// Добавляет предметы в инвентарь при старте игры.
/// </summary>
public class DebugItemGiver : MonoBehaviour
{
    [System.Serializable]
    public class DebugItem
    {
        public ItemData item;
        public int amount = 1;
    }

    [Header("⚠️ ТОЛЬКО ДЛЯ ТЕСТА — отключи перед релизом")]
    public bool giveItemsOnStart = true;

    [Header("Предметы для теста")]
    public List<DebugItem> testItems = new List<DebugItem>();

    [Header("Дать золото")]
    public int testGold = 500;

    [Header("Дать опыт (XP по веткам)")]
    public int testCombatXp = 0;
    public int testFarmingXp = 0;
    public int testCraftingXp = 0;

    void Start()
    {
        if (!giveItemsOnStart) return;

        // Небольшая задержка чтобы инвентарь успел инициализироваться
        Invoke(nameof(GiveItems), 0.2f);
    }

    void GiveItems()
    {
        // Добавляем предметы в инвентарь
        foreach (DebugItem di in testItems)
        {
            if (di.item == null) continue;
            if (InventoryUI.Instance != null)
                InventoryUI.Instance.AddItem(di.item, di.amount);
        }

        // Добавляем золото
        if (testGold > 0 && CurrencyManager.Instance != null)
            CurrencyManager.Instance.AddGold(testGold);

        // Добавляем опыт по веткам
        if (PlayerLevel.Instance != null)
        {
            if (testCombatXp > 0) PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Combat, testCombatXp);
            if (testFarmingXp > 0) PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Farming, testFarmingXp);
            if (testCraftingXp > 0) PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Crafting, testCraftingXp);
        }

        Debug.Log("[DEBUG] Тестовые предметы выданы!");
    }

    // Можно вызвать из кнопки в игре для повторной выдачи
    public void GiveAgain()
    {
        GiveItems();
    }
}