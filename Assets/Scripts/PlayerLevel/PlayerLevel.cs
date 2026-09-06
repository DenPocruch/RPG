using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Управляет уровнями игрока: общий уровень + опыт трёх профессий.
/// Доступ через PlayerLevel.Instance.
/// </summary>
public class PlayerLevel : MonoBehaviour, ISaveable
{
    public static PlayerLevel Instance { get; private set; }

    public enum SkillBranch { Combat, Farming, Crafting, Equipment, Fishing }

    [Header("Очки навыков за уровень")]
    public int skillPointsPerLevel = 3;

    [Header("Кривая опыта")]
    public int baseXpToLevel = 100;        // опыт для 2-го уровня
    public float xpMultiplier = 1.5f;      // множитель для следующих уровней

    [Header("Текущее состояние (для просмотра)")]
    [SerializeField] private int totalLevel = 1;
    [SerializeField] private int totalXp = 0;
    [SerializeField] private int availableSkillPoints = 0;

    [Header("Опыт профессий")]
    [SerializeField] private int combatXp;
    [SerializeField] private int farmingXp;
    [SerializeField] private int craftingXp;

    // События — UI и другие системы подписываются
    public System.Action<int> onLevelUp;             // (newLevel)
    public System.Action<SkillBranch, int> onBranchXpChanged;     // (branch, currentXp)
    public System.Action<int> onSkillPointsChanged;  // (available)

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SaveManager.Instance?.Register(this);
    }

    void Start()
    {
        // Уведомляем UI при старте
        onLevelUp?.Invoke(totalLevel);
        onSkillPointsChanged?.Invoke(availableSkillPoints);
        onBranchXpChanged?.Invoke(SkillBranch.Combat, combatXp);
        onBranchXpChanged?.Invoke(SkillBranch.Farming, farmingXp);
        onBranchXpChanged?.Invoke(SkillBranch.Crafting, craftingXp);

        SaveManager.Instance?.LoadInto(this);
    }

    // ─── ISaveable ─────────────────────────────────────────────
    [System.Serializable]
    private class LevelSave
    {
        public int totalLevel;
        public int totalXp;
        public int availableSkillPoints;
        public int combatXp;
        public int farmingXp;
        public int craftingXp;
    }

    public string SaveKey => "level";

    public string CaptureState()
    {
        return JsonUtility.ToJson(new LevelSave
        {
            totalLevel = totalLevel,
            totalXp = totalXp,
            availableSkillPoints = availableSkillPoints,
            combatXp = combatXp,
            farmingXp = farmingXp,
            craftingXp = craftingXp
        });
    }

    public void RestoreState(string json)
    {
        LevelSave s = JsonUtility.FromJson<LevelSave>(json);
        if (s == null) return;

        totalLevel = s.totalLevel;
        totalXp = s.totalXp;
        availableSkillPoints = s.availableSkillPoints;
        combatXp = s.combatXp;
        farmingXp = s.farmingXp;
        craftingXp = s.craftingXp;

        // Обновляем UI восстановленными значениями
        onLevelUp?.Invoke(totalLevel);
        onSkillPointsChanged?.Invoke(availableSkillPoints);
        onBranchXpChanged?.Invoke(SkillBranch.Combat, combatXp);
        onBranchXpChanged?.Invoke(SkillBranch.Farming, farmingXp);
        onBranchXpChanged?.Invoke(SkillBranch.Crafting, craftingXp);
    }

    // ═══════════════════════════════════════════════════════════
    // ДОБАВЛЕНИЕ ОПЫТА
    // ═══════════════════════════════════════════════════════════
    public void AddXp(SkillBranch branch, int amount)
    {
        if (amount <= 0) return;

        // Бонус опыта от прокачки
        if (SkillTreeManager.Instance != null)
        {
            float mult = SkillTreeManager.Instance.GetXpMultiplier();
            amount = Mathf.RoundToInt(amount * mult);
        }

        // Опыт в профессию
        switch (branch)
        {
            case SkillBranch.Combat: combatXp += amount; break;
            case SkillBranch.Farming: farmingXp += amount; break;
            case SkillBranch.Crafting: craftingXp += amount; break;
        }

        onBranchXpChanged?.Invoke(branch, GetBranchXp(branch));

        // Общий опыт = сумма опыта всех профессий
        totalXp += amount;

        // Проверяем апы уровня
        CheckLevelUp();

        Debug.Log("[Level] +" + amount + " XP в " + branch + " | Общий: " + totalXp + "/" + GetXpForNextLevel());
    }

    void CheckLevelUp()
    {
        while (totalXp >= GetXpForNextLevel())
        {
            totalXp -= GetXpForNextLevel();
            totalLevel++;
            availableSkillPoints += skillPointsPerLevel;

            Debug.Log("[Level] ПОВЫШЕНИЕ! Уровень: " + totalLevel + " | Очки навыков: " + availableSkillPoints);

            onLevelUp?.Invoke(totalLevel);
            onSkillPointsChanged?.Invoke(availableSkillPoints);

            // Попап над игроком
            if (DamagePopupManager.Instance != null)
            {
                Vector3 pos = transform.position + new Vector3(0, 1.2f, 0);
                DamagePopupManager.Instance.Spawn(pos, totalLevel, DamagePopup.PopupType.Heal);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ОЧКИ НАВЫКОВ
    // ═══════════════════════════════════════════════════════════
    public bool SpendSkillPoints(int amount)
    {
        if (availableSkillPoints < amount)
        {
            Debug.Log("[Level] Недостаточно очков навыков!");
            return false;
        }
        availableSkillPoints -= amount;
        onSkillPointsChanged?.Invoke(availableSkillPoints);
        return true;
    }

    public void RefundSkillPoints(int amount)
    {
        availableSkillPoints += amount;
        onSkillPointsChanged?.Invoke(availableSkillPoints);
    }

    // ═══════════════════════════════════════════════════════════
    // ГЕТТЕРЫ
    // ═══════════════════════════════════════════════════════════
    public int TotalLevel => totalLevel;
    public int TotalXp => totalXp;
    public int AvailableSkillPoints => availableSkillPoints;

    public int GetBranchXp(SkillBranch branch)
    {
        switch (branch)
        {
            case SkillBranch.Combat: return combatXp;
            case SkillBranch.Farming: return farmingXp;
            case SkillBranch.Crafting: return craftingXp;
        }
        return 0;
    }

    // Сколько опыта нужно для следующего уровня
    public int GetXpForNextLevel()
    {
        return Mathf.RoundToInt(baseXpToLevel * Mathf.Pow(xpMultiplier, totalLevel - 1));
    }

    // Прогресс до следующего уровня (0..1)
    public float GetProgress()
    {
        return (float)totalXp / GetXpForNextLevel();
    }
}