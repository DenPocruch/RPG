using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Очередь готовки повара. Игрок заказывает рецепты (ингредиенты и золото
/// списываются сразу), блюда готовятся в фоне по одному, складываются
/// в выходной слот. Работает независимо от того, открыта ли панель.
/// Вешается на NPC повара.
/// </summary>
public class CookStorage : MonoBehaviour
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
    }

    // ═══════════════════════════════════════════════════════════
    // ЗАКАЗ
    // ═══════════════════════════════════════════════════════════
    public bool HasIngredients(RecipeData r)
    {
        if (InventoryUI.Instance == null || r == null) return false;
        foreach (RecipeIngredient ing in r.ingredients)
            if (ing.item != null && CountInInventory(ing.item) < ing.amount) return false;
        return true;
    }

    int CountInInventory(ItemData item)
    {
        int total = 0;
        foreach (InventorySlot s in InventoryUI.Instance.slots)
            if (!s.IsEmpty() && s.currentItem == item) total += s.quantity;
        return total;
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

        // Списываем ингредиенты сразу
        foreach (RecipeIngredient ing in recipe.ingredients)
            if (ing.item != null)
                InventoryUI.Instance.RemoveItem(ing.item, ing.amount);

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