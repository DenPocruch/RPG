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

    static DebugItemGiver Instance;

    void Awake()
    {
        // Сейв может сразу перебросить игрока в другую сцену
        // (SaveManager → "Старт в сохранённой сцене"), и стартовая сцена
        // выгружается ДО того, как сработает выдача. Переживаем смену сцены.
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (!giveItemsOnStart) return;

        // Ждём инициализации инвентаря, затем небольшая пауза,
        // чтобы LoadInto (загрузка сейва) всех систем успел отработать
        StartCoroutine(GiveWhenReady());
    }

    System.Collections.IEnumerator GiveWhenReady()
    {
        yield return new WaitUntil(() =>
            InventoryUI.Instance != null &&
            HotbarManager.Instance != null &&
            SaveManager.Instance != null);
        yield return new WaitForSeconds(0.2f);
        GiveItems();
    }

    void GiveItems()
    {
        // Добавляем предметы в инвентарь
        foreach (DebugItem di in testItems)
        {
            if (di.item == null) continue;

            int freeBackpack = CountFree(InventoryUI.Instance != null ? InventoryUI.Instance.slots : null);
            int freeHotbar = CountFree(HotbarManager.Instance != null ? HotbarManager.Instance.slots : null);

            bool ok = InventoryUI.Instance != null && InventoryUI.Instance.AddItem(di.item, di.amount);

            string stackInfo = di.item.isStackable ? "" : " | НЕ стакается — нужен пустой слот!";
            Debug.Log("[DEBUG] " + di.item.name + " x" + di.amount + ": " +
                (ok ? "выдан" : "НЕ ВМЕСТИЛСЯ — инвентарь полон!") +
                " | пустых слотов: рюкзак=" + freeBackpack + ", хотбар=" + freeHotbar + stackInfo +
                (ok ? " | лёг в: " + FindItemLocations(di.item) : " | нигде нет свободных слотов"));
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

    static int CountFree(InventorySlot[] slots)
    {
        if (slots == null) return 0;
        int n = 0;
        foreach (InventorySlot s in slots)
            if (s != null && s.IsEmpty()) n++;
        return n;
    }

    static string FindItemLocations(ItemData item)
    {
        var parts = new List<string>();

        if (InventoryUI.Instance != null)
        {
            var idx = new List<int>();
            for (int i = 0; i < InventoryUI.Instance.slots.Length; i++)
            {
                InventorySlot s = InventoryUI.Instance.slots[i];
                if (s != null && !s.IsEmpty() && s.currentItem == item) idx.Add(i);
            }
            if (idx.Count > 0) parts.Add("рюкзак[" + string.Join(",", idx) + "]");
        }

        if (HotbarManager.Instance != null)
        {
            var idx = new List<int>();
            for (int i = 0; i < HotbarManager.Instance.slots.Length; i++)
            {
                InventorySlot s = HotbarManager.Instance.slots[i];
                if (s != null && !s.IsEmpty() && s.currentItem == item) idx.Add(i);
            }
            if (idx.Count > 0) parts.Add("хотбар[" + string.Join(",", idx) + "]");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "НЕ НАЙДЕН НИГДЕ!";
    }
}