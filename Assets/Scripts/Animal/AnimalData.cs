using UnityEngine;

/// <summary>
/// Данные вида животного (курица, корова...). Создаётся через
/// Assets → Create → RPG → Animal.
/// Спрайты «вправо» не нужны — зеркалим «вбок» через flipX.
///
/// РОСТ: 3 стадии — Baby → Teen → Adult. Стадия Teen ОПЦИОНАЛЬНА —
/// если её спрайты не заполнены (как у курицы), она просто пропускается
/// и рост идёт напрямую Baby → Adult за время growTime, как раньше.
/// Для животных с 3 стадиями (корова) заполни ещё и teen + growTimeToAdult.
/// </summary>
[CreateAssetMenu(fileName = "NewAnimal", menuName = "RPG/Animal")]
public class AnimalData : ScriptableObject
{
    [Header("Основное")]
    public string animalName = "Животное";

    // Стадия роста — общая для AnimalController и AnimalAnimator
    public enum GrowthStage { Baby, Teen, Adult }

    // ── Кадры для одного направления ─────────────────────────
    [System.Serializable]
    public class DirectionalFrames
    {
        public Sprite[] up;
        public Sprite[] down;
        public Sprite[] side; // используется и для лево, и для право (право = flipX)
    }

    // ── Все состояния одной стадии роста ─────────────────────
    [System.Serializable]
    public class StageSprites
    {
        public DirectionalFrames walk; // движение
        public DirectionalFrames idle; // стоит
        public DirectionalFrames sit;  // сидит на земле
        public DirectionalFrames eat;  // ест
    }

    [Header("Спрайты — детёныш")]
    public StageSprites baby;

    [Header("Спрайты — подросток (ОПЦИОНАЛЬНО)")]
    [Tooltip("Если оставить пустым — стадия пропускается, рост идёт сразу Baby → Adult (как у животных с 2 стадиями)")]
    public StageSprites teen;

    [Header("Спрайты — взрослый")]
    public StageSprites adult;

    [Header("Ориентация боковых спрайтов")]
    [Tooltip("Если боковые спрайты нарисованы смотрящими ВЛЕВО — оставь true (право = отзеркалить)")]
    public bool sideFacesLeft = true;

    [Header("Анимация")]
    public float animationFPS = 6f;

    [Header("Рост")]
    [Tooltip("Секунд: Baby→Teen (если стадия Teen заполнена), иначе Baby→Adult напрямую")]
    public float growTime = 120f;
    [Tooltip("Секунд: Teen→Adult. Используется ТОЛЬКО если стадия Teen заполнена")]
    public float growTimeToAdult = 120f;

    [Header("Движение")]
    public float moveSpeed = 1.2f;

    [Header("Поведение (тайминги в секундах)")]
    public float minWanderTime = 1.5f;
    public float maxWanderTime = 3.5f;
    public float minIdleTime = 2f;
    public float maxIdleTime = 5f;
    [Range(0f, 1f)]
    [Tooltip("Шанс что в состоянии покоя животное сядет (иначе просто стоит)")]
    public float sitChance = 0.3f;
    [Range(0f, 1f)]
    [Tooltip("Шанс что в покое животное вместо стояния поклюёт (анимация еды)")]
    public float peckChance = 0.4f;
    [Tooltip("Сколько секунд длится клевок (короткий — пару раз клюнул и всё)")]
    public float minPeckTime = 1f;
    public float maxPeckTime = 2f;

    [Header("Сидение (отдельно от стояния)")]
    [Tooltip("Когда животное садится — сидит дольше чем стоит")]
    public float minSitTime = 5f;
    public float maxSitTime = 10f;

    [Header("Стая (боидное поведение)")]
    [Tooltip("Радиус в котором животное видит сородичей своего вида")]
    public float flockRadius = 3f;
    [Tooltip("Дистанция ниже которой животные расталкиваются (чтобы не толпились)")]
    public float separationRadius = 0.8f;
    [Range(0f, 2f)]
    [Tooltip("Сила притяжения к центру стаи (0 = не собираются)")]
    public float cohesionWeight = 0.35f;
    [Range(0f, 3f)]
    [Tooltip("Сила расталкивания (больше = меньше толпятся)")]
    public float separationWeight = 1.2f;
    [Tooltip("Как часто пересчитывать направление в движении (сек)")]
    public float steerInterval = 0.4f;

    [Header("Притяжение к игроку с едой")]
    [Tooltip("Радиус в котором животное чует корм в руках игрока")]
    public float playerAttractRadius = 4f;
    [Tooltip("Дистанция на которой останавливается (не набегает вплотную)")]
    public float playerStopDistance = 1.2f;
    [Range(0f, 3f)]
    public float playerAttractWeight = 1.5f;

    [Header("Кормление и продукт")]
    public ItemData feedItem;     // чем кормить (например зерно)
    public ItemData productItem;  // что даёт (яйцо/молоко)
    public int productAmount = 1;
    [Tooltip("Секунд после кормления до появления продукта")]
    public float productionTime = 60f;
    [Tooltip("Только взрослые дают продукт")]
    public bool onlyAdultProduces = true;

    // ═══════════════════════════════════════════════════════════
    // ПРОВЕРКА ЗАПОЛНЕННОСТИ СТАДИИ (для пропуска Teen если пустая)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Заполнена ли стадия "подросток" хоть каким-то спрайтом.</summary>
    public bool HasTeenStage() => StageHasAnyFrames(teen);

    public static bool StageHasAnyFrames(StageSprites s)
    {
        if (s == null) return false;
        return DirectionHasAnyFrames(s.walk) || DirectionHasAnyFrames(s.idle) ||
               DirectionHasAnyFrames(s.sit) || DirectionHasAnyFrames(s.eat);
    }

    public static bool DirectionHasAnyFrames(DirectionalFrames df)
    {
        if (df == null) return false;
        return (df.up != null && df.up.Length > 0) ||
               (df.down != null && df.down.Length > 0) ||
               (df.side != null && df.side.Length > 0);
    }

    /// <summary>Спрайты нужной стадии с фолбэком: пустой Teen → берём Adult.</summary>
    public StageSprites GetStageSprites(GrowthStage stage)
    {
        switch (stage)
        {
            case GrowthStage.Baby: return baby;
            case GrowthStage.Teen: return HasTeenStage() ? teen : adult;
            default: return adult;
        }
    }
}