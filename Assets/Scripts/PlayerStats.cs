using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Центральная система характеристик игрока.
/// Хранит базовые статы + считает итоговые с учётом экипировки.
/// Доступ через PlayerStats.Instance.
/// </summary>
public class PlayerStats : MonoBehaviour
{
    public static PlayerStats Instance { get; private set; }

    // ═══════════════════════════════════════════════════════════
    // БАЗОВЫЕ ХАРАКТЕРИСТИКИ (без экипировки)
    // ═══════════════════════════════════════════════════════════
    [Header("Базовые характеристики")]
    public int baseHealth = 100;
    public int baseAttack = 10;
    public int baseDefense = 0;
    public float baseAttackSpeed = 1f;   // множитель: 1.0 = норма
    public float baseMoveSpeed = 3f;   // юниты/сек
    public float baseCritChance = 5f;   // %
    public float baseCritDamage = 50f;  // % к урону крита (50 = +50%, итого x1.5)
    public float baseDodgeChance = 0f;   // %
    public float baseBlockChance = 0f;   // %
    public float baseAccuracy = 0f;      // %: срезает уворот цели 1к1
    public float basePenetration = 0f;   // плоское: игнорирует защиту цели

    // ═══════════════════════════════════════════════════════════
    // СОБРАННЫЕ БОНУСЫ ОТ ЭКИПИРОВКИ (обновляются автоматически)
    // ═══════════════════════════════════════════════════════════
    [Header("Бонусы от экипировки (только для просмотра)")]
    [SerializeField] private int bonusHealth;
    [SerializeField] private int bonusAttack;
    [SerializeField] private int bonusDefense;
    [SerializeField] private float bonusAttackSpeed;
    [SerializeField] private float bonusMoveSpeed;
    [SerializeField] private float bonusCritChance;
    [SerializeField] private float bonusCritDamage;
    [SerializeField] private float bonusDodgeChance;
    [SerializeField] private float bonusBlockChance;
    [SerializeField] private float bonusAccuracy;
    [SerializeField] private float bonusPenetration;

    // ═══════════════════════════════════════════════════════════
    // ИТОГОВЫЕ ХАРАКТЕРИСТИКИ (база + бонусы)
    // ═══════════════════════════════════════════════════════════
    public int TotalHealth => baseHealth + bonusHealth;
    public int TotalAttack => baseAttack + bonusAttack;
    public int TotalDefense => baseDefense + bonusDefense;
    public float TotalAttackSpeed => baseAttackSpeed + bonusAttackSpeed;
    public float TotalMoveSpeed => baseMoveSpeed + bonusMoveSpeed;
    public float TotalCritChance => baseCritChance + bonusCritChance;
    public float TotalCritDamage => baseCritDamage + bonusCritDamage;
    public float TotalDodgeChance => baseDodgeChance + bonusDodgeChance;
    public float TotalBlockChance => baseBlockChance + bonusBlockChance;
    public float TotalAccuracy => baseAccuracy + bonusAccuracy;
    public float TotalPenetration => basePenetration + bonusPenetration;

    // Событие — стат изменились (UI подписывается чтобы обновлять отображение)
    public System.Action onStatsChanged;

    // Активное оружие из хотбара
    private ItemData activeWeapon = null;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Подписываемся на смену активного предмета в хотбаре
        if (HotbarManager.Instance != null)
            HotbarManager.Instance.onActiveItemChanged += OnActiveWeaponChanged;
    }

    void OnDestroy()
    {
        if (HotbarManager.Instance != null)
            HotbarManager.Instance.onActiveItemChanged -= OnActiveWeaponChanged;
    }

    /// <summary>Вызывается из HotbarManager когда меняется активный предмет.</summary>
    public void OnActiveWeaponChanged(ItemData item)
    {
        // Запоминаем активное оружие (Weapon/RangedWeapon/Axe дают бонусы;
        // топор — ещё и оружие, кирка — только инструмент).
        // Закрытое перком оружие — как будто пустые руки (бонусов нет).
        if (item != null &&
            (item.itemType == ItemType.Weapon || item.itemType == ItemType.RangedWeapon
             || item.itemType == ItemType.Axe)
            && EquipmentLocks.IsUnlocked(item))
            activeWeapon = item;
        else
            activeWeapon = null;

        // Пересчитываем характеристики с новым оружием
        if (EquipmentManager.Instance != null)
            RecalculateBonuses(EquipmentManager.Instance.GetAllEquipped());
        else
            RecalculateBonuses(new System.Collections.Generic.List<ItemData>());
    }

    /// <summary>Перечитать активное оружие из хотбара (после покупки перка —
    /// закрытый меч в руках мог стать легальным). Зовёт SkillTreeManager.</summary>
    public void RefreshActiveWeapon()
    {
        if (HotbarManager.Instance != null)
            OnActiveWeaponChanged(HotbarManager.Instance.GetActiveItem());
    }

    // ═══════════════════════════════════════════════════════════
    // ПЕРЕСЧЁТ БОНУСОВ ОТ ЭКИПИРОВКИ
    // Вызывается из EquipmentManager при изменении слотов
    // ═══════════════════════════════════════════════════════════
    public void RecalculateBonuses(List<ItemData> equippedItems)
    {
        // Сбрасываем все бонусы
        bonusHealth = 0;
        bonusAttack = 0;
        bonusDefense = 0;
        bonusAttackSpeed = 0f;
        bonusMoveSpeed = 0f;
        bonusCritChance = 0f;
        bonusCritDamage = 0f;
        bonusDodgeChance = 0f;
        bonusBlockChance = 0f;
        bonusAccuracy = 0f;
        bonusPenetration = 0f;

        // Суммируем бонусы от всех надетых предметов (броня, кольца...)
        foreach (ItemData item in equippedItems)
        {
            if (item == null) continue;
            bonusHealth += item.bonusHealth;
            bonusAttack += item.bonusAttack;
            bonusDefense += item.bonusDefense;
            bonusAttackSpeed += item.bonusAttackSpeed;
            bonusMoveSpeed += item.bonusMoveSpeed;
            bonusCritChance += item.bonusCritChance;
            bonusCritDamage += item.bonusCritDamage;
            bonusDodgeChance += item.bonusDodgeChance;
            bonusBlockChance += item.bonusBlockChance;
            bonusAccuracy += item.bonusAccuracy;
            bonusPenetration += item.bonusPenetration;
        }

        // Добавляем бонусы от активного оружия в хотбаре
        if (activeWeapon != null)
        {
            bonusAttack += activeWeapon.bonusAttack;
            bonusCritChance += activeWeapon.bonusCritChance;
            bonusCritDamage += activeWeapon.bonusCritDamage;
            bonusAttackSpeed += activeWeapon.bonusAttackSpeed;
            bonusAccuracy += activeWeapon.bonusAccuracy;
            bonusPenetration += activeWeapon.bonusPenetration;
        }

        // Бонусы от активного баффа еды (PlayerBuffs)
        if (PlayerBuffs.Instance != null)
        {
            bonusAttack += (int)PlayerBuffs.Instance.GetBuffValue(FoodBuffType.Attack);
            bonusDefense += (int)PlayerBuffs.Instance.GetBuffValue(FoodBuffType.Defense);
            bonusAttackSpeed += PlayerBuffs.Instance.GetBuffValue(FoodBuffType.AttackSpeed);
            bonusMoveSpeed += PlayerBuffs.Instance.GetBuffValue(FoodBuffType.MoveSpeed);
            bonusCritChance += PlayerBuffs.Instance.GetBuffValue(FoodBuffType.CritChance);
            bonusCritDamage += PlayerBuffs.Instance.GetBuffValue(FoodBuffType.CritDamage);
            bonusDodgeChance += PlayerBuffs.Instance.GetBuffValue(FoodBuffType.DodgeChance);
            bonusBlockChance += PlayerBuffs.Instance.GetBuffValue(FoodBuffType.BlockChance);
        }

        // Применяем итоговые статы к другим системам
        ApplyToSystems();

        // Уведомляем UI
        onStatsChanged?.Invoke();
    }

    // Применяем характеристики к существующим системам (HP, движение)
    void ApplyToSystems()
    {
        // Скорость движения
        PlayerMovement pm = GetComponent<PlayerMovement>();
        if (pm != null)
            pm.moveSpeed = TotalMoveSpeed;

        // Максимальное HP — меняем только потолок, текущее HP не трогаем
        PlayerHealth ph = GetComponent<PlayerHealth>();
        if (ph != null)
        {
            ph.maxHealth = TotalHealth;
            // Только ограничиваем если текущее HP больше нового максимума
            ph.currentHealth = Mathf.Min(ph.currentHealth, ph.maxHealth);
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ИТОГОВЫЙ УРОН с учётом оружия и крита
    // ═══════════════════════════════════════════════════════════
    /// <summary>Результат расчёта урона — для попапов.</summary>
    public struct DamageResult
    {
        public float damage;
        public bool isCrit;
        public float accuracy;    // точность атакующего (срезает уворот цели)
        public float penetration; // пробитие атакующего (игнорирует защиту цели)
    }

    public DamageResult CalculateDamage(ItemData weapon)
    {
        float baseDmg = TotalAttack;
        ItemData w = weapon ?? activeWeapon;
        if (w != null) baseDmg += w.damage;

        DamageResult result = new DamageResult();
        result.damage = baseDmg;
        result.isCrit = false;
        result.accuracy = TotalAccuracy;
        result.penetration = TotalPenetration;

        // Крит?
        if (Random.Range(0f, 100f) < TotalCritChance)
        {
            float critMultiplier = 1f + (TotalCritDamage / 100f);
            result.damage = baseDmg * critMultiplier;
            result.isCrit = true;
        }

        return result;
    }

    // Шанс уклониться от атаки (точность атакующего срезает уворот 1к1, минимум 0)
    public bool TryDodge(float attackerAccuracy = 0f)
    {
        float effective = Mathf.Max(0f, TotalDodgeChance - attackerAccuracy);
        return Random.Range(0f, 100f) < effective;
    }

    // Шанс заблокировать атаку
    public bool TryBlock()
    {
        return Random.Range(0f, 100f) < TotalBlockChance;
    }

    // Итоговый урон после защиты (пробитие игнорирует часть защиты, минимум 0)
    public float ApplyDefense(float incomingDamage, float attackerPenetration = 0f)
    {
        float effectiveDefense = Mathf.Max(0f, TotalDefense - attackerPenetration);
        float reduced = incomingDamage - effectiveDefense;
        return Mathf.Max(reduced, 1f); // минимум 1 урона
    }
}