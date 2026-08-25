using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Управляет деревом навыков.
/// Теперь хранит РАНГИ узлов вместо просто флага разблокировки.
/// Ранг 0 = заблокирован, Ранг 1+ = разблокирован N раз.
/// </summary>
public class SkillTreeManager : MonoBehaviour, ISaveable
{
    public static SkillTreeManager Instance { get; private set; }

    [Header("Все узлы игры")]
    public SkillNode[] allNodes;

    [Header("Стоимость сброса навыков")]
    public int resetCost = 1000;

    public System.Action onSkillTreeChanged;

    // Ключ: узел → текущий ранг (0 = не открыт)
    private Dictionary<SkillNode, int> nodeRanks = new Dictionary<SkillNode, int>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Инициализируем все узлы нулевым рангом
        if (allNodes != null)
            foreach (SkillNode node in allNodes)
                if (node != null) nodeRanks[node] = 0;

        SaveManager.Instance?.Register(this);
    }

    void Start()
    {
        // Восстанавливаем ПОСЛЕ инициализации PlayerStats (та в Awake),
        // чтобы переприменение эффектов легло на чистые базовые статы
        SaveManager.Instance?.LoadInto(this);
    }

    // ─── ISaveable ─────────────────────────────────────────────
    [System.Serializable] private class NodeRankSave { public string nodeName; public int rank; }
    [System.Serializable] private class SkillSave { public List<NodeRankSave> nodes = new List<NodeRankSave>(); }

    public string SaveKey => "skilltree";

    public string CaptureState()
    {
        SkillSave save = new SkillSave();
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0)
                save.nodes.Add(new NodeRankSave { nodeName = kvp.Key.name, rank = kvp.Value });
        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        SkillSave save = JsonUtility.FromJson<SkillSave>(json);
        if (save == null || save.nodes == null) return;

        // Сбрасываем всё в 0
        foreach (SkillNode node in allNodes)
            if (node != null) nodeRanks[node] = 0;

        // Ставим сохранённые ранги (ищем узел по имени ассета)
        foreach (NodeRankSave ns in save.nodes)
        {
            SkillNode node = FindNodeByName(ns.nodeName);
            if (node == null) continue;
            nodeRanks[node] = ns.rank;

            // Переприменяем стат-эффекты по одному разу на каждый ранг
            for (int r = 0; r < ns.rank; r++)
                ApplyEffect(node);
        }

        onSkillTreeChanged?.Invoke();
    }

    SkillNode FindNodeByName(string nodeName)
    {
        foreach (SkillNode node in allNodes)
            if (node != null && node.name == nodeName) return node;
        return null;
    }

    // ═══════════════════════════════════════════════════════════
    // ГЕТТЕРЫ
    // ═══════════════════════════════════════════════════════════
    public int GetRank(SkillNode node) => nodeRanks.ContainsKey(node) ? nodeRanks[node] : 0;
    public bool IsUnlocked(SkillNode node) => GetRank(node) >= 1;
    public bool IsMaxRank(SkillNode node) => node != null && GetRank(node) >= node.maxRanks;

    public bool IsAvailable(SkillNode node)
    {
        if (node == null) return false;

        // Уже максимальный ранг
        if (IsMaxRank(node)) return false;

        // Проверка уровня персонажа
        if (PlayerLevel.Instance != null &&
            PlayerLevel.Instance.TotalLevel < node.requiredLevel) return false;

        // Проверка предыдущих узлов
        if (node.requiredNodes != null)
            foreach (SkillNode req in node.requiredNodes)
                if (req != null && !IsUnlocked(req)) return false;

        return true;
    }

    // ═══════════════════════════════════════════════════════════
    // ПРОКАЧКА УЗЛА
    // ═══════════════════════════════════════════════════════════
    public bool TryUnlock(SkillNode node)
    {
        if (node == null) return false;
        if (!IsAvailable(node))
        {
            if (IsMaxRank(node))
                Debug.Log("[SkillTree] " + node.nodeName + " уже максимального ранга!");
            return false;
        }

        int currentRank = GetRank(node);
        int ptsCost = node.GetSkillPointsCost(currentRank);
        int goldCost = node.GetGoldCost(currentRank);

        // Тратим очки навыков
        if (ptsCost > 0 && PlayerLevel.Instance != null)
            if (!PlayerLevel.Instance.SpendSkillPoints(ptsCost)) return false;

        // Тратим золото
        if (goldCost > 0 && CurrencyManager.Instance != null)
        {
            if (!CurrencyManager.Instance.SpendGold(goldCost))
            {
                if (ptsCost > 0 && PlayerLevel.Instance != null)
                    PlayerLevel.Instance.RefundSkillPoints(ptsCost);
                return false;
            }
        }

        // Повышаем ранг
        nodeRanks[node] = currentRank + 1;
        ApplyEffect(node);

        int newRank = nodeRanks[node];
        Debug.Log("[SkillTree] " + node.nodeName + " → Ранг " + newRank + "/" + node.maxRanks);

        onSkillTreeChanged?.Invoke();
        return true;
    }

    // ═══════════════════════════════════════════════════════════
    // ПРИМЕНЕНИЕ ЭФФЕКТА (за один ранг)
    // ═══════════════════════════════════════════════════════════
    void ApplyEffect(SkillNode node)
    {
        PlayerStats ps = PlayerStats.Instance;

        switch (node.effectType)
        {
            case SkillEffectType.BonusHealth:
                if (ps != null) { ps.baseHealth += (int)node.effectValue; Recalculate(); }
                break;
            case SkillEffectType.BonusAttack:
                if (ps != null) { ps.baseAttack += (int)node.effectValue; Recalculate(); }
                break;
            case SkillEffectType.BonusDefense:
                if (ps != null) { ps.baseDefense += (int)node.effectValue; Recalculate(); }
                break;
            case SkillEffectType.BonusAttackSpeed:
                if (ps != null) { ps.baseAttackSpeed += node.effectValue; Recalculate(); }
                break;
            case SkillEffectType.BonusMoveSpeed:
                if (ps != null) { ps.baseMoveSpeed += node.effectValue; Recalculate(); }
                break;
            case SkillEffectType.BonusCritChance:
                if (ps != null) { ps.baseCritChance += node.effectValue; Recalculate(); }
                break;
            case SkillEffectType.BonusCritDamage:
                if (ps != null) { ps.baseCritDamage += node.effectValue; Recalculate(); }
                break;
            case SkillEffectType.BonusDodgeChance:
                if (ps != null) { ps.baseDodgeChance += node.effectValue; Recalculate(); }
                break;
            case SkillEffectType.BonusBlockChance:
                if (ps != null) { ps.baseBlockChance += node.effectValue; Recalculate(); }
                break;
            case SkillEffectType.ExtraInventorySlot:
                Debug.Log("[SkillTree] +" + (int)node.effectValue + " слот(а) инвентаря");
                break;
            case SkillEffectType.UnlockItem:
                if (node.unlocksItem != null)
                    Debug.Log("[SkillTree] Открыт: " + node.unlocksItem.itemName);
                break;
            case SkillEffectType.UnlockFeature:
                Debug.Log("[SkillTree] Открыта механика: " + node.unlocksFeature);
                break;
            case SkillEffectType.HarvestDouble:
                Debug.Log("[SkillTree] Двойной урожай " + node.effectValue + "%");
                break;
            case SkillEffectType.BonusMaxWater:
                Debug.Log("[SkillTree] Вместимость лейки +" + (int)node.effectValue);
                break;
            case SkillEffectType.XpBonus:
                Debug.Log("[SkillTree] Бонус XP +" + (node.effectValue * 100f) + "%");
                break;
            case SkillEffectType.GoldBonus:
                Debug.Log("[SkillTree] Бонус золота +" + (node.effectValue * 100f) + "%");
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // СБРОС
    // ═══════════════════════════════════════════════════════════
    public bool TryReset()
    {
        if (CurrencyManager.Instance == null ||
            !CurrencyManager.Instance.SpendGold(resetCost))
        {
            Debug.Log("[SkillTree] Нужно " + resetCost + " золота!");
            return false;
        }

        // Возвращаем очки с учётом рангов
        foreach (var kvp in nodeRanks)
        {
            if (kvp.Value <= 0) continue;
            SkillNode node = kvp.Key;
            // Суммируем стоимость всех рангов
            for (int r = 0; r < kvp.Value; r++)
            {
                if (node.skillPointsCost > 0 && PlayerLevel.Instance != null)
                    PlayerLevel.Instance.RefundSkillPoints(node.GetSkillPointsCost(r));
            }
            // Откатываем эффект на все ранги
            for (int r = 0; r < kvp.Value; r++)
                RollbackEffect(node);
        }

        // Сбрасываем все ранги
        foreach (SkillNode node in allNodes)
            if (node != null) nodeRanks[node] = 0;

        onSkillTreeChanged?.Invoke();
        Debug.Log("[SkillTree] Навыки сброшены!");
        return true;
    }

    void RollbackEffect(SkillNode node)
    {
        PlayerStats ps = PlayerStats.Instance;
        if (ps == null) return;

        switch (node.effectType)
        {
            case SkillEffectType.BonusHealth: ps.baseHealth -= (int)node.effectValue; break;
            case SkillEffectType.BonusAttack: ps.baseAttack -= (int)node.effectValue; break;
            case SkillEffectType.BonusDefense: ps.baseDefense -= (int)node.effectValue; break;
            case SkillEffectType.BonusAttackSpeed: ps.baseAttackSpeed -= node.effectValue; break;
            case SkillEffectType.BonusMoveSpeed: ps.baseMoveSpeed -= node.effectValue; break;
            case SkillEffectType.BonusCritChance: ps.baseCritChance -= node.effectValue; break;
            case SkillEffectType.BonusCritDamage: ps.baseCritDamage -= node.effectValue; break;
            case SkillEffectType.BonusDodgeChance: ps.baseDodgeChance -= node.effectValue; break;
            case SkillEffectType.BonusBlockChance: ps.baseBlockChance -= node.effectValue; break;
        }
        Recalculate();
    }

    void Recalculate()
    {
        if (PlayerStats.Instance != null)
            PlayerStats.Instance.RecalculateBonuses(
                EquipmentManager.Instance != null
                    ? EquipmentManager.Instance.GetAllEquipped()
                    : new List<ItemData>());
    }

    public bool IsNodeUnlockedByFeature(string feature)
    {
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.unlocksFeature == feature) return true;
        return false;
    }

    // Стоимость следующего ранга
    public (int pts, int gold) GetNextRankCost(SkillNode node)
    {
        int rank = GetRank(node);
        return (node.GetSkillPointsCost(rank), node.GetGoldCost(rank));
    }

    // ═══════════════════════════════════════════════════════════
    // ГЕТТЕРЫ СПЕЦИАЛЬНЫХ ЭФФЕКТОВ
    // Вызываются из других систем (ферма, инвентарь, лут, колодец)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Шанс двойного урожая в % (суммирует все ранги HarvestDouble)</summary>
    public float GetDoubleHarvestChance()
    {
        float total = 0f;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.HarvestDouble)
                total += kvp.Key.effectValue * kvp.Value;
        return total;
    }

    /// <summary>Количество дополнительных слотов инвентаря</summary>
    public int GetExtraInventorySlots()
    {
        int total = 0;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.ExtraInventorySlot)
                total += (int)(kvp.Key.effectValue * kvp.Value);
        return total;
    }

    /// <summary>Множитель опыта (1.0 = норма, 1.1 = +10%)</summary>
    public float GetXpMultiplier()
    {
        float bonus = 0f;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.XpBonus)
                bonus += kvp.Key.effectValue * kvp.Value;
        return 1f + bonus;
    }

    /// <summary>Множитель золота (1.0 = норма, 1.1 = +10%)</summary>
    public float GetGoldMultiplier()
    {
        float bonus = 0f;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.GoldBonus)
                bonus += kvp.Key.effectValue * kvp.Value;
        return 1f + bonus;
    }

    /// <summary>Бонус вместимости лейки</summary>
    public int GetBonusMaxWater()
    {
        int total = 0;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.BonusMaxWater)
                total += (int)(kvp.Key.effectValue * kvp.Value);
        return total;
    }

    /// <summary>Бонус к лимиту вскопанных грядок</summary>
    public int GetPlotLimitBonus()
    {
        int total = 0;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.BonusPlotLimit)
                total += (int)(kvp.Key.effectValue * kvp.Value);
        return total;
    }

    /// <summary>Множитель скорости роста растений (1.1 = +10%)</summary>
    public float GetCropGrowthMultiplier()
    {
        float bonus = 0f;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.GrowthSpeed)
                bonus += kvp.Key.effectValue * kvp.Value;
        return 1f + bonus;
    }

    /// <summary>Множитель скорости роста/производства животных (1.1 = +10%)</summary>
    public float GetAnimalGrowthMultiplier()
    {
        float bonus = 0f;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.AnimalGrowthSpeed)
                bonus += kvp.Key.effectValue * kvp.Value;
        return 1f + bonus;
    }

    /// <summary>Бонус к шансу серебряного урожая</summary>
    public float GetSilverQualityBonus()
    {
        float total = 0f;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.QualityBonus)
                total += kvp.Key.effectValue * kvp.Value;
        return total;
    }

    /// <summary>Бонус к шансу золотого урожая</summary>
    public float GetGoldQualityBonus()
    {
        float total = 0f;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.GoldQualityBonus)
                total += kvp.Key.effectValue * kvp.Value;
        return total;
    }

    /// <summary>Бонус к шансу пурпурного урожая</summary>
    public float GetPurpleQualityBonus()
    {
        float total = 0f;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.PurpleQualityBonus)
                total += kvp.Key.effectValue * kvp.Value;
        return total;
    }

    /// <summary>Снижение шанса провала крафта в %</summary>
    public float GetFailChanceReduction()
    {
        float total = 0f;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.ReduceFailChance)
                total += kvp.Key.effectValue * kvp.Value;
        return total;
    }

    /// <summary>Снижение стоимости услуг NPC в % (лесоруб, повар, магазин)</summary>
    public float GetServiceCostReduction()
    {
        float total = 0f;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.ReduceServiceCost)
                total += kvp.Key.effectValue * kvp.Value;
        return Mathf.Min(total, 90f); // защита от 100%+ скидки
    }

    /// <summary>Снижение времени переработки в секундах (на единицу результата)</summary>
    public float GetCraftTimeReduction()
    {
        float total = 0f;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.ReduceCraftTime)
                total += kvp.Key.effectValue * kvp.Value;
        return total;
    }

    /// <summary>Бонус вместимости склада мастерских (лесопилка и т.д.)</summary>
    public int GetStorageCapacityBonus()
    {
        int total = 0;
        foreach (var kvp in nodeRanks)
            if (kvp.Value > 0 && kvp.Key.effectType == SkillEffectType.IncreaseStorageCapacity)
                total += (int)(kvp.Key.effectValue * kvp.Value);
        return total;
    }
}