using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Вид рыбы. Предмет в инвентарь — fishItem (продаётся Мореку, лечится).
/// </summary>
[CreateAssetMenu(fileName = "NewFish", menuName = "RPG/Fish")]
public class FishData : ScriptableObject
{
    [Header("Основное")]
    public string fishName = "Рыба";
    public Sprite icon;
    [TextArea(2, 3)]
    public string description = "";
    [Tooltip("Предмет, падающий в инвентарь при улове")]
    public ItemData fishItem;

    [Header("Сложность (0 обычная / 1 редкая / 2 легендарная)")]
    [Range(0, 2)]
    public int difficulty = 0;

    [Header("Тир силы 1-6 (инфо; сейчас решает вес рыбы vs лимит удочки)")]
    [Tooltip("Справочно: раньше давал gap против тира удочки. Сложность боя сейчас — от веса и difficulty")]
    [Range(1, 6)]
    public int fishTier = 1;

    [Header("Экономика (Морек): цена ЗА КИЛОГРАММ")]
    public int price = 10;          // скупка за кг
    public int firstCatchBonus = 20; // бонус за первый улов вида (флэт)

    [Header("Вес улова (кг): 1 рыба = 1 вес, рыба не стакается")]
    public float minWeightKg = 0.1f;
    public float maxWeightKg = 1f;

    /// <summary>Случайный вес пойманной рыбы.</summary>
    public float RollWeight()
    {
        float a = Mathf.Min(minWeightKg, maxWeightKg);
        float b = Mathf.Max(minWeightKg, maxWeightKg);
        if (b <= 0f) return 0f;
        return Random.Range(Mathf.Max(0.01f, a), b);
    }

    /// <summary>Вес в пересечении диапазона вида с диапазоном крючка:
    /// клюнувшая рыба всегда внутри [loKg, hiKg] — крючок держит что обещает.</summary>
    public float RollWeightInRange(float loKg, float hiKg)
    {
        float a = Mathf.Min(minWeightKg, maxWeightKg);
        float b = Mathf.Max(minWeightKg, maxWeightKg);
        float lo = Mathf.Max(Mathf.Max(0.01f, a), loKg);
        float hi = Mathf.Min(b, hiKg);
        if (hi <= lo) return lo;
        return Random.Range(lo, hi);
    }

    /// <summary>Формат веса: кг или граммы.</summary>
    public static string FormatWeight(float kg)
    {
        if (kg >= 1f) return kg.ToString("0.##") + " кг";
        return Mathf.Max(1, Mathf.RoundToInt(kg * 1000f)) + " г";
    }

    /// <summary>Вся рыба из Resources/Fish (корень + River/Sea — LoadAll не рекурсивен).</summary>
    public static FishData[] LoadAll()
    {
        var list = new List<FishData>();
        foreach (string p in new[] { "Fish", "Fish/River", "Fish/Sea" })
            list.AddRange(Resources.LoadAll<FishData>(p));
        return list.ToArray();
    }
}
