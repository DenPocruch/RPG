using UnityEngine;

/// <summary>
/// Тюнинг мини-игры рыбалки (по мотивам «Рыбного места»): детерминированная
/// шкала натяжения + фиксированная дистанция. Держишь экран — шкала растёт
/// и рыба ближе; отпустил — шкала падает, дистанция стоит. Края мгновенно
/// проигрывают: 0 — рыба сошла, 1 — леска лопнула. Никаких авто-рывков:
/// шкала отвечает только на палец, а скорость отклика зависит от НАГРУЗКИ
/// (вес рыбы / лимит удочки): мелочь на грани не чувствуется — зажал на
/// 5 секунд и вытащил; рыба под лимит — шкала ракета, только короткие тапы.
/// Перевес (рыба тяжелее лимита удочки) — леска рвётся от любого тапа.
/// Дистанция всегда одна (100м), скорость — метры за секунду держания
/// (20м/с → 5 секунд чистого держания на любую рыбу).
/// Создание: ПКМ в Project → Create → RPG → Fishing Tuning → положить в Resources/Fish/FishingTuning.
/// Если ассета нет — используются встроенные дефолты.
/// </summary>
[CreateAssetMenu(menuName = "RPG/Fishing Tuning", fileName = "FishingTuning")]
public class FishingTuning : ScriptableObject
{
    [Header("Дистанция (одна на всех)")]
    [Tooltip("Дистанция до рыбы в метрах — всегда, для любой рыбы")]
    public float distanceMeters = 100f;
    [Tooltip("Сколько метров сматывается за секунду держания (20 → 5с чистого держания на рыбу)")]
    public float metersPerSecond = 20f;

    [Header("Шкала натяжения (доля шкалы в секунду, движение линейное)")]
    [Tooltip("Рост шкалы пока держишь, когда рыба = лимит удочки (нагрузка 1). Меньше вес — медленнее по кривой")]
    public float holdRiseAtLimit = 1.1f;
    [Tooltip("Степень кривой нагрузки: rise = atLimit × load^curve (2 = мелочь почти не тянет, под лимитом ракета)")]
    public float riseCurve = 2f;
    [Tooltip("Минимальный рост шкалы (чтобы мелочь хоть слегка тянула)")]
    public float holdRiseMin = 0.05f;
    [Tooltip("Спад шкалы когда отпустил (один на всех — пауза между тапами)")]
    public float relaxFall = 0.6f;
    [Tooltip("Бонус к росту шкалы за единицу difficulty (редкая злее при том же весе)")]
    public float diffRiseBonus = 0.15f;

    [Header("Лимиты удочек по весу рыбы (кг), по тиру удочки 1-6")]
    [Tooltip("Дерево/медь/железо/золото/платина/обсидиан. Рыба тяжелее лимита рвёт леску от любого тапа")]
    public float[] rodMaxKgByTier = new float[] { 2f, 5f, 10f, 20f, 50f, 100f };

    [Header("Кнопка атаки с удочкой")]
    [Tooltip("Скин кнопки атаки когда в руках удочка (пусто = только синий оттенок + слот крючка)")]
    public Sprite attackRodSkin;

    [Header("Зоны шкалы (доли 0-1, только визуал)")]
    [Tooltip("0..escapeTop — красная (слабина, рядом сход)")]
    public float escapeTop = 0.12f;
    [Tooltip("escapeTop..greenTop — зелёная (рабочая)")]
    public float greenTop = 0.55f;
    [Tooltip("redStart..1 — красная (перетяг, рядом обрыв). Между ними жёлтая")]
    public float redStart = 0.82f;
    [Tooltip("Бонус удочки (fishingZoneBonus) расширяет безопасную зону с обеих сторон")]
    public bool rodWidensSafeZone = true;

    [Header("Бонусная рыба за редкость удочки (как bonusYield кирок)")]
    [Tooltip("Common/Uncommon/Rare/Epic/Legendary. Сотни = гарант, остаток = шанс: 150 → +1 и 50% на ещё +1")]
    public int[] bonusFishByRarity = new int[] { 0, 25, 50, 100, 150 };

    [Header("Прочее")]
    [Tooltip("Рыба уходит, если бой длится дольше (сек). 0 — без лимита")]
    public float fightTimeout = 0f;

    [Header("Звук поклёвки/обрыва/поимки (можно пусто — будет тихо)")]
    public AudioClip biteClip;
    public AudioClip snapClip;
    public AudioClip catchClip;

    [Header("Вибрация (Android)")]
    public bool vibrateOnBite = true;
    public bool vibrateOnSnap = true;

    [Header("Отладка боя")]
    [Tooltip("Писать в консоль состояние каждые 0.5с")]
    public bool debugLog = false;

    [Header("Цвета")]
    public Color greenColor = new Color(0.25f, 0.85f, 0.35f, 0.55f);
    public Color yellowColor = new Color(0.95f, 0.8f, 0.2f, 0.55f);
    public Color redColor = new Color(0.95f, 0.25f, 0.2f, 0.6f);
    public Color tensionColor = new Color(1f, 1f, 1f, 0.95f);

    [Header("Раскладка (пиксели, панель 420×560 по умолчанию)")]
    public Vector2 panelSize = new Vector2(420f, 560f);
    public Vector2 trackPos = new Vector2(-80f, 10f);
    public Vector2 trackSize = new Vector2(70f, 380f);
    public Vector2 progressPos = new Vector2(0f, -225f);
    public Vector2 progressSize = new Vector2(320f, 26f);
    public int titleFontSize = 34;
    public int hintFontSize = 24;

    [Header("Привязка к панели в сцене (Canvas/<PanelRoot>)")]
    [Tooltip("Корень нарисованной панели — прямой ребёнок Canvas. Нет объекта — строится кодом")]
    public string panelRootName = "FishingTuning";
    [Tooltip("Полоска натяжения: ширина = натяжение (ищется внутри корня)")]
    public string tensionFillName = "HPBarFill";
    [Tooltip("Рыбка-маркер прогресса: едет к финишу (ищется внутри корня)")]
    public string progressMarkerName = "Fishing";
    [Tooltip("Трек маркера. Пусто = родитель маркера")]
    public string progressTrackName = "";
    [Tooltip("Отступы маркера от краёв трека (px)")]
    public float markerInsetLeft = 10f;
    public float markerInsetRight = 10f;

    private static FishingTuning _cached;
    private static bool _lookedUp;

    /// <summary>Ассет из Resources/Fish/FishingTuning либо дефолтный в памяти.</summary>
    public static FishingTuning Instance
    {
        get
        {
            if (_lookedUp) return _cached;
            _lookedUp = true;
            _cached = Resources.Load<FishingTuning>("Fish/FishingTuning");
            if (_cached == null)
                _cached = CreateInstance<FishingTuning>();
            return _cached;
        }
    }

    /// <summary>Лимит веса (кг) для тира удочки 1-6. Нет в таблице — без лимита.
    /// Умножается на перки ветки Fishing (fish_rod_&lt;Тир&gt;_w, +20% за узел).</summary>
    public float RodLimitKg(int rodTier)
    {
        if (rodMaxKgByTier == null || rodMaxKgByTier.Length == 0) return float.MaxValue;
        int i = Mathf.Clamp(rodTier - 1, 0, rodMaxKgByTier.Length - 1);
        float v = rodMaxKgByTier[i];
        if (v <= 0f) return float.MaxValue;
        if (SkillTreeManager.Instance != null)
            v *= SkillTreeManager.Instance.GetRodWeightMult(RodTierId(rodTier));
        return v;
    }

    /// <summary>Тир удочки (toolTier 1-6) → id тира для тегов перков.</summary>
    public static string RodTierId(int rodTier)
    {
        switch (rodTier)
        {
            case 1: return "Wood";
            case 2: return "Copper";
            case 3: return "Iron";
            case 4: return "Gold";
            case 5: return "Platinum";
            default: return "Obsidian";
        }
    }
}
