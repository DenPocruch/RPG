using UnityEngine;

/// <summary>
/// Один узел дерева навыков. ScriptableObject — создаётся через
/// Assets → Create → RPG → Skill Node
/// </summary>
[CreateAssetMenu(fileName = "NewSkillNode", menuName = "RPG/Skill Node")]
public class SkillNode : ScriptableObject
{
    [Header("Основное")]
    public string nodeName = "Узел";
    [TextArea(2, 4)]
    public string description = "";
    public Sprite icon;
    public PlayerLevel.SkillBranch branch = PlayerLevel.SkillBranch.Combat;

    [Header("Требования для разблокировки")]
    public int requiredLevel = 1;    // минимальный уровень персонажа
    public int skillPointsCost = 1;    // очки навыков (базовая стоимость)
    public int goldCost = 0;    // золото (базовая стоимость)
    public SkillNode[] requiredNodes;      // предыдущие узлы которые нужно открыть

    [Header("Система рангов")]
    public int maxRanks = 1;    // макс количество прокачек (1 = нет рангов)
    public float rankCostMultiplier = 1.5f; // множитель стоимости за каждый ранг

    [Header("Эффект узла")]
    public SkillEffectType effectType = SkillEffectType.None;
    public float effectValue = 0f;  // числовое значение эффекта ЗА ОДИН РАНГ

    [Header("Что открывает (опционально)")]
    public ItemData unlocksItem;     // открывает оружие/инструмент
    public string unlocksFeature;  // текстовый тег фичи ("DoubleHarvest", "ExtraSlot" и т.д.)

    // ─────────────────────────────────────────────────────────────────
    // Описание эффекта для UI
    // ─────────────────────────────────────────────────────────────────
    // Стоимость для следующего ранга
    public int GetSkillPointsCost(int currentRank)
    {
        return Mathf.RoundToInt(skillPointsCost * Mathf.Pow(rankCostMultiplier, currentRank));
    }

    public int GetGoldCost(int currentRank)
    {
        return Mathf.RoundToInt(goldCost * Mathf.Pow(rankCostMultiplier, currentRank));
    }

    public string GetEffectDescription(int rank = 1)
    {
        float val = effectValue * rank;
        switch (effectType)
        {
            case SkillEffectType.BonusHealth:
                return "+" + val + " HP";
            case SkillEffectType.BonusAttack:
                return "+" + val + " Атака";
            case SkillEffectType.BonusDefense:
                return "+" + val + " Защита";
            case SkillEffectType.BonusAttackSpeed:
                return "+" + (val * 100f).ToString("0") + "% Скорость атаки";
            case SkillEffectType.BonusMoveSpeed:
                return "+" + val + " Скорость";
            case SkillEffectType.BonusCritChance:
                return "+" + val + "% Шанс крита";
            case SkillEffectType.BonusCritDamage:
                return "+" + val + "% Урон крита";
            case SkillEffectType.BonusDodgeChance:
                return "+" + val + "% Уворот";
            case SkillEffectType.BonusBlockChance:
                return "+" + val + "% Блок";
            case SkillEffectType.ExtraInventorySlot:
                return "+" + (int)val + " слот(а) инвентаря";
            case SkillEffectType.UnlockItem:
                return unlocksItem != null ? "Открыть: " + unlocksItem.itemName : "Открыть предмет";
            case SkillEffectType.UnlockFeature:
                return "Открыть: " + unlocksFeature;
            case SkillEffectType.XpBonus:
                return "+" + (val * 100f).ToString("0") + "% к опыту";
            case SkillEffectType.GoldBonus:
                return "+" + (val * 100f).ToString("0") + "% к золоту";
            case SkillEffectType.HarvestDouble:
                return "Шанс двойного урожая: " + val + "%";
            case SkillEffectType.BonusMaxWater:
                return "+" + (int)val + " вместимость лейки";
            case SkillEffectType.ReduceFailChance:
                return "-" + val + "% шанс провала крафта";
            case SkillEffectType.ReduceServiceCost:
                return "-" + val + "% стоимость услуг NPC";
            case SkillEffectType.ReduceCraftTime:
                return "-" + val + "с к времени переработки";
            case SkillEffectType.IncreaseStorageCapacity:
                return "+" + (int)val + " мест на складе мастерской";
        }
        return "";
    }
}

// ─────────────────────────────────────────────────────────────────
// Все возможные типы эффектов
// ─────────────────────────────────────────────────────────────────
public enum SkillEffectType
{
    None,

    // ── Боевые (Combat) ──────────────────────────────────
    BonusHealth,        // +HP
    BonusAttack,        // +Атака
    BonusDefense,       // +Защита
    BonusAttackSpeed,   // +Скорость атаки
    BonusMoveSpeed,     // +Скорость движения
    BonusCritChance,    // +Шанс крита
    BonusCritDamage,    // +Урон крита
    BonusDodgeChance,   // +Уворот
    BonusBlockChance,   // +Блок
    UnlockItem,         // Открыть оружие/инструмент

    // ── Фермерские (Farming) ─────────────────────────────
    HarvestDouble,      // Шанс двойного урожая
    BonusMaxWater,      // +Вместимость лейки
    XpBonus,            // +Бонус к получаемому опыту

    // ── Ремесленные (Crafting) ───────────────────────────
    ExtraInventorySlot, // +Слот инвентаря
    GoldBonus,          // +Бонус к получаемому золоту
    UnlockFeature,      // Открыть особую механику
    ReduceFailChance,    // -% шанс провала при крафте
    ReduceServiceCost,   // -% стоимость услуг NPC (лесоруб, повар, магазин)
    ReduceCraftTime,       // -секунд ко времени переработки за единицу (лесопилка и т.д.)
    IncreaseStorageCapacity, // +мест на складе мастерской (лесопилка и т.д.)
}