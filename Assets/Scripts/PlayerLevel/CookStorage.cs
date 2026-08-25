using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Очередь готовки повара. Игрок заказывает рецепты (ингредиенты и золото
/// списываются сразу), блюда готовятся в фоне по одному, складываются
/// в выходной слот. Работает независимо от того, открыта ли панель.
/// Вешается на NPC повара.
/// </summary>
public class CookStorage : MonoBehaviour, ISaveable
{
    public static CookStorage Instance;

    [Header("Все рецепты игры (заполни в Inspector)")]
    public RecipeData[] allRecipes;

    [Header("Базовая вместимость склада готовых блюд")]
    public int baseDishCapacity = 20;

    private List<RecipeData> orderQueue = new List<RecipeData>();
    private InventorySlot outputSlot;

    private RecipeData currentOrder = null;
    private float timeRemaining = 0f;
    private float totalTime = 0f;

    public System.Action onStorageChanged;

    void Awake()
    {
        Instance = this;

        GameObject outGo = new GameObject("DishOutputDataSlot");
        outGo.transform.SetParent(transform);
        outGo.SetActive(false);
        outputSlot = outGo.AddComponent<InventorySlot>();
        outputSlot.allowOverflow = true;
        outputSlot.acceptsManualDeposit = false; // только повар кладёт сюда

        SaveManager.Instance?.Register(this);
    }

    void Start()
    {
        SaveManager.Instance?.LoadInto(this);

        // Переподписываем UI на СВОЁ событие (UI вечный, склады пересоздаются)
        CookUI.Instance?.BindToStorage();
    }

    // ─── ISaveable ─────────────────────────────────────────────
    [System.Serializable]
    private class CookSave
    {
        public List<string> queue = new List<string>(); // имена ассетов рецептов в очереди
        public string currentRecipe;             // текущий готовящийся
        public float timeRemaining;              // остаток времени текущего заказа
        public long savedAtTicks;                // реальное время сохранения — для оффлайн-готовки
        public string outputItem;
        public int outputQty;
    }

    public string SaveKey => "cook";

    public string CaptureState()
    {
        CookSave save = new CookSave
        {
            savedAtTicks = DateTime.UtcNow.Ticks
        };

        if (currentOrder != null)
        {
            save.currentRecipe = currentOrder.name;
            save.timeRemaining = timeRemaining;
        }
        foreach (RecipeData r in orderQueue)
            if (r != null) save.queue.Add(r.name);

        if (outputSlot != null && !outputSlot.IsEmpty())
        {
            save.outputItem = outputSlot.currentItem.name;
            save.outputQty = outputSlot.quantity;
        }
        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        CookSave save = JsonUtility.FromJson<CookSave>(json);
        if (save == null) return;

        orderQueue.Clear();
        currentOrder = null;
        timeRemaining = 0f;

        // Текущий заказ продолжается с сохранённым остатком времени
        // (раньше он начинался заново с полным временем)
        if (!string.IsNullOrEmpty(save.currentRecipe))
        {
            RecipeData r = FindRecipe(save.currentRecipe);
            if (r != null)
            {
                currentOrder = r;
                totalTime = GetCookTime(r);
                timeRemaining = save.timeRemaining > 0f ? save.timeRemaining : totalTime;
            }
        }
        foreach (string name in save.queue)
        {
            RecipeData r = FindRecipe(name);
            if (r != null) orderQueue.Add(r);
        }

        // Оффлайн-готовка: вычитаем реальное время с момента сохранения
        if (currentOrder != null && save.savedAtTicks > 0 && save.timeRemaining > 0f)
        {
            double elapsed = (DateTime.UtcNow.Ticks - save.savedAtTicks) / (double)TimeSpan.TicksPerSecond;
            if (elapsed > 0)
                timeRemaining = Mathf.Max(0f, timeRemaining - (float)elapsed);
        }

        if (outputSlot != null) outputSlot.ClearSlot();
        if (!string.IsNullOrEmpty(save.outputItem))
        {
            ItemData outItem = ItemDatabase.Find(save.outputItem);
            if (outItem != null) outputSlot.SetItem(outItem, save.outputQty);
        }

        onStorageChanged?.Invoke();
    }

    void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
        if (Instance == this) Instance = null;
    }

    RecipeData FindRecipe(string assetName)
    {
        if (allRecipes == null) return null;
        foreach (RecipeData r in allRecipes)
            if (r != null && r.name == assetName) return r;
        return null;
    }

    void Update()
    {
        outputSlot.overflowCapacity = GetDishCapacity();
        ProcessTick();
    }

    // ═══════════════════════════════════════════════════════════
    // ФОНОВАЯ ГОТОВКА
    // ═══════════════════════════════════════════════════════════
    void ProcessTick()
    {
        if (currentOrder == null)
        {
            PickNextOrder();
            if (currentOrder == null) return;
        }

        // Склад занят другим блюдом или полон — ждём пока заберут
        if (!outputSlot.IsEmpty() &&
            (outputSlot.currentItem != currentOrder.outputItem ||
             outputSlot.quantity >= outputSlot.overflowCapacity))
            return;

        timeRemaining -= Time.deltaTime;
        if (timeRemaining <= 0f)
            CompleteOrder();
    }

    void PickNextOrder()
    {
        if (orderQueue.Count == 0) return;

        // Берём первый заказ, чьё блюдо совместимо со складом
        for (int i = 0; i < orderQueue.Count; i++)
        {
            RecipeData r = orderQueue[i];
            if (!outputSlot.IsEmpty() && outputSlot.currentItem != r.outputItem) continue;
            if (!outputSlot.IsEmpty() && outputSlot.quantity >= GetDishCapacity()) continue;

            currentOrder = r;
            orderQueue.RemoveAt(i);
            totalTime = GetCookTime(r);
            timeRemaining = totalTime;
            return;
        }
    }

    void CompleteOrder()
    {
        // Защита от подмены содержимого склада (аналогично лесопилке)
        if (!outputSlot.IsEmpty() && outputSlot.currentItem != currentOrder.outputItem)
            return;

        if (outputSlot.IsEmpty())
            outputSlot.SetItem(currentOrder.outputItem, currentOrder.outputAmount);
        else
        {
            outputSlot.quantity += currentOrder.outputAmount;
            outputSlot.UpdateUI();
        }

        if (PlayerLevel.Instance != null && currentOrder.xpReward > 0)
            PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Crafting, currentOrder.xpReward);

        Debug.Log("[Повар] Готово: " + currentOrder.recipeName);

        currentOrder = null;
        onStorageChanged?.Invoke();

        // Сейв по событию: блюдо готово
        SaveManager.Instance?.Save();
    }

    // ═══════════════════════════════════════════════════════════
    // ЗАКАЗ
    // ═══════════════════════════════════════════════════════════
    public bool HasIngredients(RecipeData r)
    {
        if (InventoryUI.Instance == null || r == null) return false;
        foreach (RecipeIngredient ing in r.ingredients)
            if (ing.item != null && CountIngredients(ing.item) < ing.amount) return false;
        return true;
    }

    // Все слоты: инвентарь + хотбар (повар видит и то, и то)
    System.Collections.Generic.IEnumerable<InventorySlot> AllSlots()
    {
        if (InventoryUI.Instance != null)
            foreach (InventorySlot s in InventoryUI.Instance.slots)
                if (s != null) yield return s;
        if (HotbarManager.Instance != null)
            foreach (InventorySlot s in HotbarManager.Instance.slots)
                if (s != null) yield return s;
    }

    // Считаем обычный урожай + все звёздные варианты (серебро/золото/пурпур)
    public int CountIngredients(ItemData item)
    {
        int total = 0;
        foreach (InventorySlot s in AllSlots())
        {
            if (s.IsEmpty()) continue;
            if (IsSameCrop(s.currentItem, item)) total += s.quantity;
        }
        return total;
    }

    // Звёздные варианты считаются тем же ингредиентом (повар принимает любое качество)
    bool IsSameCrop(ItemData slotItem, ItemData ingredient)
    {
        if (slotItem == ingredient) return true;
        if (slotItem == null || ingredient == null) return false;
        if (!slotItem.name.StartsWith(ingredient.name + " ")) return false;
        string suffix = slotItem.name.Substring(ingredient.name.Length + 1);
        return suffix == "Silver" || suffix == "Gold" || suffix == "Purple";
    }

    // Списываем ингредиент: сначала обычный, затем серебро, золото, пурпур
    void ConsumeIngredient(ItemData item, int amount)
    {
        ItemData[] variants =
        {
            item,
            ItemDatabase.Find(item.name + " Silver"),
            ItemDatabase.Find(item.name + " Gold"),
            ItemDatabase.Find(item.name + " Purple")
        };
        foreach (ItemData v in variants)
        {
            if (v == null || amount <= 0) continue;

            // Списание из инвентаря И хотбара
            foreach (InventorySlot s in AllSlots())
            {
                if (amount <= 0) break;
                if (s.IsEmpty() || s.currentItem != v) continue;
                int takeNow = Mathf.Min(s.quantity, amount);
                s.quantity -= takeNow;
                amount -= takeNow;
                if (s.quantity <= 0) s.ClearSlot();
                else s.UpdateUI();
            }
        }

        // Если изменился активный слот хотбара — обновляем зеркало оружия/статы
        HotbarManager.Instance?.NotifyActiveItemChanged();
    }

    public bool TryOrder(RecipeData recipe)
    {
        if (recipe == null || !recipe.IsUnlocked()) return false;
        if (InventoryUI.Instance == null) return false;
        if (!HasIngredients(recipe)) return false;

        int cost = GetCookCost(recipe);
        if (cost > 0)
        {
            if (CurrencyManager.Instance == null || !CurrencyManager.Instance.SpendGold(cost))
                return false;
        }

        // Списываем ингредиенты сразу (включая звёздные варианты)
        foreach (RecipeIngredient ing in recipe.ingredients)
            if (ing.item != null)
                ConsumeIngredient(ing.item, ing.amount);

        orderQueue.Add(recipe);
        onStorageChanged?.Invoke();
        Debug.Log("[Повар] Заказ принят: " + recipe.recipeName);
        return true;
    }

    // ═══════════════════════════════════════════════════════════
    // МОДИФИКАТОРЫ И ГЕТТЕРЫ
    // ═══════════════════════════════════════════════════════════
    public float GetCookTime(RecipeData r)
    {
        float t = r.cookTime;
        if (SkillTreeManager.Instance != null)
            t = Mathf.Max(0f, t - SkillTreeManager.Instance.GetCraftTimeReduction());
        return t;
    }

    public int GetCookCost(RecipeData r)
    {
        if (r.goldCost <= 0) return 0;
        float discount = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetServiceCostReduction()
            : 0f;
        return Mathf.Max(0, Mathf.RoundToInt(r.goldCost * (1f - discount / 100f)));
    }

    public int GetDishCapacity()
    {
        int bonus = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetStorageCapacityBonus()
            : 0;
        return baseDishCapacity + bonus;
    }

    public InventorySlot GetOutputSlot() => outputSlot;
    public int GetQueueCount() => orderQueue.Count + (currentOrder != null ? 1 : 0);
    public RecipeData GetCurrentOrder() => currentOrder;
    public float GetTimeRemaining() => timeRemaining;
    public float GetTotalTime() => totalTime;
}