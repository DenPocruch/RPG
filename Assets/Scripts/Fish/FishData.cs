using UnityEngine;

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

    [Header("Экономика (Морек)")]
    public int price = 10;          // скупка
    public int firstCatchBonus = 20; // бонус за первый улов вида
}
