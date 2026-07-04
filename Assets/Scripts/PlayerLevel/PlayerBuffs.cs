using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Активный бафф еды. Один за раз (классика RPG) — новая еда
/// перезаписывает старый бафф. По истечении времени бафф спадает,
/// PlayerStats пересчитывается автоматически.
/// Вешается на Player рядом с PlayerStats.
/// </summary>
public class PlayerBuffs : MonoBehaviour
{
    public static PlayerBuffs Instance { get; private set; }

    private ItemData activeFood = null;
    private float timeRemaining = 0f;

    // Событие — бафф применён/спал (для UI индикатора в будущем)
    public System.Action onBuffChanged;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (activeFood == null) return;

        timeRemaining -= Time.deltaTime;
        if (timeRemaining <= 0f)
            ClearBuff();
    }

    /// <summary>Применить бафф от еды. Перезаписывает предыдущий.</summary>
    public void ApplyFoodBuff(ItemData food)
    {
        if (food == null || food.foodBuffType == FoodBuffType.None || food.foodBuffDuration <= 0f)
            return;

        activeFood = food;
        timeRemaining = food.foodBuffDuration;

        Debug.Log("[Бафф] " + food.itemName + ": " + food.foodBuffType +
            " +" + food.foodBuffValue + " на " + food.foodBuffDuration + "с");

        Recalculate();
        onBuffChanged?.Invoke();
    }

    void ClearBuff()
    {
        Debug.Log("[Бафф] Эффект еды закончился: " + (activeFood != null ? activeFood.itemName : ""));
        activeFood = null;
        timeRemaining = 0f;
        Recalculate();
        onBuffChanged?.Invoke();
    }

    void Recalculate()
    {
        if (PlayerStats.Instance != null)
            PlayerStats.Instance.RecalculateBonuses(
                EquipmentManager.Instance != null
                    ? EquipmentManager.Instance.GetAllEquipped()
                    : new List<ItemData>());
    }

    /// <summary>Значение баффа нужного типа (0 если нет). Вызывается из PlayerStats.</summary>
    public float GetBuffValue(FoodBuffType type)
    {
        if (activeFood == null || activeFood.foodBuffType != type) return 0f;
        return activeFood.foodBuffValue;
    }

    public ItemData GetActiveFood() => activeFood;
    public float GetTimeRemaining() => timeRemaining;
    public bool HasBuff() => activeFood != null;
}