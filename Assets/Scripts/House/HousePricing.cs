using UnityEngine;

/// <summary>
/// Цены расширения дома (конструктор дома, зафиксировано 03.10):
/// 1-й тайл = 50 дерева + 500 голды, каждые 5 купленных тайлов +25/+250.
/// Кап — 100 тайлов пола (30 стартовых + 70 покупок).
/// </summary>
public static class HousePricing
{
    public const int BaseWood = 50;
    public const int BaseGold = 500;
    public const int WoodStep = 25;
    public const int GoldStep = 250;
    public const int StepEvery = 5;
    public const int MaxFloorTiles = 100;

    /// <param name="purchasedCount">сколько тайлов уже куплено (0 = первый тайл)</param>
    public static void CostFor(int purchasedCount, out int wood, out int gold)
    {
        if (purchasedCount < 0) purchasedCount = 0;
        int step = purchasedCount / StepEvery;
        wood = BaseWood + step * WoodStep;
        gold = BaseGold + step * GoldStep;
    }
}
