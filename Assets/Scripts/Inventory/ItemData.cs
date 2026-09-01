using UnityEngine;

public enum ItemType
{
    Weapon,
    RangedWeapon,
    Hoe,
    Pickaxe,
    BugNet,
    Axe,
    Sickle,
    Sapling,
    Tool,
    Consumable,
    Material,
    Seed,
    Crop,
    WateringCan,
    // Экипировка
    Helmet,
    Armor,
    Pants,
    Boots,
    Gloves,
    Shield,
    Ring,
    Earrings,
    Bracelet,
    Amulet,
    None,
    AnimalBaby,
    Feeder,
    WaterTrough,
    Hammer,
    Scarecrow,
}

public enum ItemRarity
{
    Common,     // Серый — обычный
    Uncommon,   // Зелёный — необычный
    Rare,       // Синий — редкий
    Epic,       // Фиолетовый — эпический
    Legendary   // Оранжевый — легендарный
}

public enum EquipmentSlotType
{
    None,
    Helmet,
    Armor,
    Pants,
    Boots,
    Gloves,
    Weapon,
    Shield,
    Ring,      // используй это в ItemData — лезет в Ring1 и Ring2
    Ring1,     // слот в UI
    Ring2,     // слот в UI
    Earrings,
    Bracelet,
    Amulet
}

public enum FoodBuffType
{
    None,
    Attack,
    Defense,
    AttackSpeed,
    MoveSpeed,
    CritChance,
    CritDamage,
    DodgeChance,
    BlockChance
}

[CreateAssetMenu(fileName = "NewItem", menuName = "RPG/Item")]
public class ItemData : ScriptableObject
{
    [Header("Основное")]
    public string itemName = "Предмет";
    [TextArea(2, 4)]
    public string description = "";
    public Sprite icon;
    public Sprite worldSprite;
    public ItemType itemType = ItemType.None;
    public ItemRarity rarity = ItemRarity.Common;

    [Header("Стак")]
    public bool isStackable = false;
    public int maxStack = 1;

    [Header("Оружие / Инструмент")]
    public float damage = 0f;
    public float attackRange = 1f;
    public float attackSpeed = 1f;

    [Header("GameObject в руках")]
    public GameObject weaponPrefab;

    [Header("Стрела (для лука)")]
    public GameObject arrowPrefab;
    public float arrowSpeed = 10f;
    public float arrowRange = 8f;

    [Header("Семена (для посадки на грядке)")]
    public Sprite[] growthStages;
    public int growthStagesCount = 4;
    public float growthTimeWatered = 300f;
    public float growthTimeNormal = 600f;
    public ItemData harvestItem;
    public int harvestAmount = 1;

    [Header("Лейка")]
    public int maxWater = 10;

    [Header("Детёныш животного (AnimalBaby)")]
    [Tooltip("Префаб животного — спавнится при использовании из хотбара")]
    public GameObject animalPrefab;

    [Header("Размещаемый объект (Feeder / WaterTrough / Scarecrow)")]
    [Tooltip("Префаб размещаемого объекта (кормушка/поилка/пугало) — ставится через ghost-режим")]
    public GameObject placeablePrefab;
    [Tooltip("Цена в магазине (0 = цена задаётся в ShopInteraction)")]
    public int shopPrice = 0;

    [Header("Удобрение")]
    [Tooltip("Если true — использование на грядку с растением ускоряет его рост ×2")]
    public bool isFertilizer = false;

    [Header("Еда (Consumable) — эффект при съедании")]
    [Tooltip("Мгновенное восстановление HP")]
    public int healAmount = 0;
    [Tooltip("Временный бафф (None = только лечение)")]
    public FoodBuffType foodBuffType = FoodBuffType.None;
    public float foodBuffValue = 0f;
    [Tooltip("Длительность баффа в секундах")]
    public float foodBuffDuration = 0f;

    [Header("Росток дерева (Sapling)")]
    public GameObject treePrefab;
    public float treeGrowthTime = 600f;

    [Header("Спрайты дерева")]
    public Sprite[] treeGrowthStages;
    public Sprite treeAdultSprite;
    public Sprite treeFruitSprite;
    public Sprite treeNoFruitSprite;
    public Sprite treeDriedSprite;

    [Header("Данные дерева")]
    public bool isFruitTree = false;
    public ItemData treeFruitItem;
    public int treeFruitAmount = 1;
    public float treeFruitGrowTime = 120f;
    public int treeMaxFruitHarvests = 3;
    public ItemData treeWoodItem;
    public int treeWoodAmount = 3;
    public int treeMaxHealth = 5;

    // ════════════════════════════════════════════════════════════════════
    // КРАФТ — СЛЕДУЮЩАЯ РЕДКОСТЬ
    // ════════════════════════════════════════════════════════════════════
    [Header("Крафт — следующая версия предмета")]
    public ItemData nextRarityVersion; // Common меч → Uncommon меч (заполни в Inspector)

    // ════════════════════════════════════════════════════════════════════
    // ПЕРЕРАБОТКА (лесоруб, шахтёр, повар и т.д.)
    // ════════════════════════════════════════════════════════════════════
    [Header("Категория ресурса (для фильтров складов мастерских)")]
    [Tooltip("Например \"Wood\" для брёвен/досок, \"Ore\" для руды/слитков. Используется чтобы лесопилка не принимала руду и наоборот.")]
    public string resourceCategory = "";

    [Header("Переработка — во что превращается этот предмет")]
    [Tooltip("Например: Бревно берёзы → Доска берёзы")]
    public ItemData convertsToItem;
    [Tooltip("Сколько единиц ЭТОГО предмета нужно на 1 единицу результата")]
    public int conversionRatio = 1;
    [Tooltip("Золота за каждую единицу результата")]
    public int conversionGoldCost = 0;
    [Tooltip("Секунд на переработку 1 единицы результата (0 = мгновенно)")]
    public float conversionTimePerUnit = 0f;

    [Header("Требование инструмента (для деревьев/руды)")]
    [Tooltip("Минимальный уровень топора/кирки чтобы добыть этот ресурс")]
    public int requiredToolTier = 1;

    [Header("Уровень инструмента (для топоров/кирок)")]
    [Tooltip("Уровень ЭТОГО инструмента — должен быть >= requiredToolTier ресурса")]
    public int toolTier = 1;

    // ════════════════════════════════════════════════════════════════════
    // ЭКИПИРОВКА И ХАРАКТЕРИСТИКИ
    // ════════════════════════════════════════════════════════════════════

    [Header("Слот экипировки")]
    public EquipmentSlotType equipSlot = EquipmentSlotType.None;
    public bool IsEquipment => equipSlot != EquipmentSlotType.None;

    [Header("Бонусы характеристик")]
    public int bonusHealth = 0;
    public int bonusAttack = 0;
    public int bonusDefense = 0;
    public float bonusAttackSpeed = 0f;  // в процентах: 0.1 = +10%
    public float bonusMoveSpeed = 0f;
    public float bonusCritChance = 0f;  // в процентах: 5f = 5%
    public float bonusCritDamage = 0f;  // в процентах: 50f = +50% урона от крита
    public float bonusDodgeChance = 0f;
    public float bonusBlockChance = 0f;
}