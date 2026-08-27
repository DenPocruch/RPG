using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Мозг животного: боидное стадное поведение (держатся вместе, но не толпятся),
/// притяжение к игроку с кормом, случайные клевки, рост (3 стадии), кормление
/// и продукт. Движение через Rigidbody2D — в препятствиях не застревает.
///
/// РОСТ: Baby → Teen → Adult. Если у AnimalData стадия Teen не заполнена
/// спрайтами (см. AnimalData.HasTeenStage) — она автоматически пропускается,
/// и животное растёт напрямую Baby → Adult за data.growTime (старое поведение,
/// как было у курицы — ничего не ломается).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(AnimalAnimator))]
public class AnimalController : MonoBehaviour, IInteractable
{
    // Реестр всех животных для боидного поведения (дешевле чем Physics-запросы)
    private static readonly List<AnimalController> allAnimals = new List<AnimalController>();

    [Header("Данные")]
    public AnimalData data;

    [Header("Старт")]
    public bool startAsAdult = false;

    [Header("Дроп продукта")]
    public GameObject lootItemPrefab;
    public float productDropRadius = 0.5f;

    [Header("Голод/жажда (кормушка/поилка)")]
    [Tooltip("Радиус поиска кормушки/поилки")]
    public float feedSearchRadius = 15f;
    [Tooltip("Как часто животное хочет пить (сек)")]
    public float drinkInterval = 180f;
    [Tooltip("С какой дистанции животное ест/пьёт")]
    public float facilityStopDistance = 1.45f;

    [Header("ИИ движения (плавность и антизастревание)")]
    [Tooltip("Ускорение разгона/торможения (ед/сек²) — убирает рывки скорости")]
    public float accelSpeed = 8f;
    [Tooltip("Максимальная скорость поворота курса (градусов/сек) — убирает резкие дёрганья влево-вправо")]
    public float maxTurnDegPerSec = 250f;
    [Tooltip("Дальность 'уса' вперёд для обхода препятствий (ед.)")]
    public float avoidProbeDist = 0.75f;
    [Tooltip("Радиус 'уса' (толщина луча обхода, ед.)")]
    public float avoidProbeRadius = 0.18f;
    [Tooltip("Сколько секунд копится подозрение на застревание до аварийного манёвра (сек)")]
    public float stuckDetectTime = 0.7f;
    [Tooltip("Сколько секунд держать аварийное направление выхода (сек)")]
    public float stuckEscapeTime = 0.7f;

    [Header("Дистанция до забора/стен (блуждание)")]
    [Tooltip("На сколько метров впереди 'чует' препятствие и выталкивается от него")]
    public float wallKeepDist = 2.0f;

    [Header("Притяжение к центру загона")]
    [Tooltip("Имя объекта-маркера в центре загона. Если объект есть в сцене — животные держатся ближе к центру, а не жмутся к заборам")]
    public string roamCenterName = "RoamCenter";
    [Tooltip("Сила притяжения к центру (сравнима с wallAvoidWeight)")]
    public float roamCenterWeight = 0.6f;
    Transform roamCenter;
    [Tooltip("Сила отталкивания от стен при блуждании (0 = можно жаться к забору)")]
    [Range(0f, 3f)]
    public float wallAvoidWeight = 1.3f;

    [Header("Оффлайн-прогресс")]
    [Tooltip("Максимум продуктов, которое животное накопит за оффлайн (остальное пропадает)")]
    public int offlineProductCap = 5;

    private Rigidbody2D rb;
    private Collider2D selfCol;
    private AnimalAnimator anim;
    private Transform player;

    private enum State { Wander, Idle, Sit, Peck, Eat }
    private State state = State.Idle;
    private float stateTimer;
    private Vector2 moveDir;
    private Vector2 desiredDir;   // куда хотим идти (до сглаживания поворота)
    private Vector2 wanderBias;
    private float steerTimer;
    private AnimalAnimator.AnimDir lastFacing = AnimalAnimator.AnimDir.Down;

    // Антизастревание / антидёрганье
    private Vector2 lastContactNormal; // нормаль стены из последнего касания
    private float avoidCooldown;       // пауза между подворотами от стены
    private int skirtSide;             // сторона текущего огибания препятствия (+1/-1, 0 = чисто)
    private float stuckTimer;          // копится, пока идём но не двигаемся
    private float posCheckTimer;
    private Vector2 lastStuckCheckPos;
    private Vector2 escapeDir;         // принудительное направление выхода
    private float escapeTimer;

    // Контроль прогресса на пути к кормушке/поилке
    private Vector3 lastNeedTarget;
    private Vector2 lastNeedCheckPos;
    private float needCheckTimer;

    // Рост — 3 стадии (Teen пропускается автоматически если не заполнена в AnimalData)
    private AnimalData.GrowthStage growthStage;
    private float growTimer;

    private bool isFed;
    private float productionTimer;
    private float eatTimer;

    // Оффлайн-прогресс (корм/вода/продукция). Симуляция отложенная:
    // животные восстанавливаются из сейва РАНЬШЕ кормушек/поилок,
    // поэтому расход ресурсов считаем после RunPendingOfflineSim()
    private static readonly List<AnimalController> offlineQueue = new List<AnimalController>();
    private double pendingOfflineSeconds;
    private bool offlineSimQueued;

    // Голод/жажда. Голод = !isFed (не производит продукт — значит ищет кормушку сам)
    private bool wantsWater;
    private float thirstTimer;
    private FeederStorage targetFeeder;
    private WaterTrough targetTrough;
    private Vector2 leaveDir;
    private bool hasLeaveDir;

    // Индикатор голода над головой
    private SpriteRenderer hungerIcon;

    void OnEnable() { allAnimals.Add(this); }
    void OnDisable()
    {
        allAnimals.Remove(this);
        offlineQueue.Remove(this);
        offlineSimQueued = false;
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        selfCol = GetComponent<Collider2D>();
        anim = GetComponent<AnimalAnimator>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    void Start()
    {
        // Восстановленное из сохранения состояние важнее стартового
        if (!restoredFromSave)
        {
            growthStage = startAsAdult ? AnimalData.GrowthStage.Adult : AnimalData.GrowthStage.Baby;
            growTimer = data != null ? data.growTime : 120f;
        }

        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;

        // Маркер центра загона (если расставлен в сцене)
        GameObject rc = GameObject.Find(roamCenterName);
        if (rc != null) roamCenter = rc.transform;

        // Фолбэк: префаб лута из Resources, если не назначен в инспекторе
        if (lootItemPrefab == null)
            lootItemPrefab = Resources.Load<GameObject>("LootItemPrefab");

        wanderBias = Random.insideUnitCircle.normalized;

        // Восстановленному животному таймер жажды ставит оффлайн-симуляция
        if (!restoredFromSave)
            thirstTimer = drinkInterval * Random.Range(0.6f, 1f);

        anim.Init(data, growthStage);
        EnsureHungerIcon();
        EnterIdle();
    }

    void Update()
    {
        if (escapeTimer > 0f) escapeTimer -= Time.deltaTime;
        if (avoidCooldown > 0f) avoidCooldown -= Time.deltaTime;

        HandleGrowth();
        HandleProduction();
        HandleState();
        HandlePendingProduct();
        UpdateHungerIcon();
    }

    // ═══════════════════════════════════════════════════════════
    // СОХРАНЕНИЕ / ЗАГРУЗКА / ОФФЛАЙН-ПРОГРЕСС
    // (читает и восстанавливает AnimalSaveManager)
    // ═══════════════════════════════════════════════════════════
    public AnimalData.GrowthStage CurrentStage => growthStage;
    public float GrowTimerRemaining => growTimer;
    public bool IsFed => isFed;
    public float ProductionTimerRemaining => productionTimer;
    public bool HasPendingProduct => pendingProducts > 0;

    private bool restoredFromSave = false;
    private int pendingProducts = 0; // сколько продуктов держит при себе (в т.ч. накопил за оффлайн)

    /// <summary>Восстановить состояние животного (позиция, рост, кормление).</summary>
    public void ApplyRestoredState(int stage, float growTimerSec, bool fed, float prodTimer, bool hasPendingProduct, Vector3 pos)
    {
        restoredFromSave = true;
        growthStage = (AnimalData.GrowthStage)stage;
        growTimer = growTimerSec;
        isFed = fed;
        productionTimer = prodTimer;
        pendingProducts = hasPendingProduct ? 1 : 0;

        // Через Rigidbody2D — иначе физика может перебить телепорт
        if (rb != null) rb.position = pos;
        else transform.position = pos;

        // Обновляем спрайты под восстановленную стадию.
        // Если это вызовется ДО Start — Start повторит Init с тем же результатом.
        if (anim != null && data != null)
            anim.Init(data, growthStage);
    }

    /// <summary>
    /// Применить время, прошедшее с момента сохранения (игра была закрыта):
    /// животное подросло, продукт созрел. Вызывать ПОСЛЕ ApplyRestoredState.
    /// </summary>
    public void ApplyOfflineTime(double seconds)
    {
        if (data == null || seconds <= 0) return;

        // ── Рост (может перескочить несколько стадий за долгое отсутствие) ──
        double left = seconds;
        while (growthStage != AnimalData.GrowthStage.Adult && left > 0)
        {
            float stageTime = Mathf.Max(growTimer, 0.01f); // защита от деления на ноль
            if (left < stageTime)
            {
                growTimer -= (float)left;
                left = 0;
                break;
            }

            left -= stageTime;

            if (growthStage == AnimalData.GrowthStage.Baby)
            {
                if (data.HasTeenStage())
                {
                    growthStage = AnimalData.GrowthStage.Teen;
                    growTimer = data.growTimeToAdult;
                }
                else
                {
                    growthStage = AnimalData.GrowthStage.Adult;
                }
            }
            else
            {
                growthStage = AnimalData.GrowthStage.Adult;
            }
        }

        if (growthStage == AnimalData.GrowthStage.Adult) growTimer = 0f;

        // ── Расход корма/воды и выработка продукции ──
        // Сразу НЕ считаем: кормушки/поилки ещё не заспавнены из сейва
        // (животные восстанавливаются раньше PlaceablesSaveManager).
        // Ставим в очередь — SaveManager.ProcessScene вызовет RunPendingOfflineSim()
        pendingOfflineSeconds = seconds;
        if (!offlineSimQueued)
        {
            offlineSimQueued = true;
            offlineQueue.Add(this);
        }
    }

    /// <summary>Запустить отложенную оффлайн-симуляцию у всех животных сцены.
    /// Вызывается из SaveManager.ProcessScene ПОСЛЕ спавна кормушек/поилок.
    /// Возвращает true — было что симулировать (расход корма/воды уже применён,
    /// стоит сохраниться).</summary>
    public static bool RunPendingOfflineSim()
    {
        bool any = false;
        foreach (AnimalController a in offlineQueue)
            if (a != null) { a.SimulateOfflineNeeds(a.pendingOfflineSeconds); any = true; }
        offlineQueue.Clear();
        return any;
    }

    /// <summary>
    /// Оффлайн-симуляция потребностей: животное ест из кормушек, пьёт из поилок
    /// и производит продукт за время отсутствия игрока. Корм/вода РЕАЛЬНО
    /// списываются из кормушек и поилок. Если ресурсов нет — ждёт (как в игре).
    /// </summary>
    void SimulateOfflineNeeds(double seconds)
    {
        if (data == null || data.productItem == null || seconds <= 0) return;

        double left = seconds;

        while (left > 0)
        {
            // ── ЖАЖДА: производство стоит, пока не попьёт ──
            if (wantsWater)
            {
                WaterTrough trough = WaterTrough.AnyInWorld
                    ? WaterTrough.FindNearest(transform.position, feedSearchRadius) : null;
                if (trough == null || !trough.TryDrink()) break; // воды нет — простаивает
                wantsWater = false;
                thirstTimer = drinkInterval;
                continue;
            }

            // ── ГОЛОД: ищем кормушку со своим кормом ──
            if (!isFed)
            {
                FeederStorage feeder = (data.feedItem != null && FeederStorage.AnyInWorld)
                    ? FeederStorage.FindNearest(transform.position, feedSearchRadius, data.feedItem) : null;
                if (feeder == null || !feeder.TryConsume(data.feedItem)) break; // корм кончился
                isFed = true;
                productionTimer = data.productionTime;
                continue;
            }

            // ── Производство (только взрослые, если так задумано) ──
            if (data.onlyAdultProduces && growthStage != AnimalData.GrowthStage.Adult) break;

            // Жажда наступит раньше конца производства?
            if (thirstTimer <= left)
            {
                left -= thirstTimer;
                thirstTimer = 0f;
                wantsWater = true;
                continue;
            }
            thirstTimer -= (float)left;

            // Продукт созреет раньше жажды?
            if (productionTimer <= left)
            {
                left -= productionTimer;
                productionTimer = 0f;
                isFed = false; // съел свой корм, следующий цикл требует новый

                // Лимит удержания достигнут — дальше не копим и корм не сжигаем
                if (!ProduceOne()) left = 0;
            }
            else
            {
                productionTimer -= (float)left;
                left = 0;
            }
        }
    }

    /// <summary>Один продукт произведён — копим "при себе". false = лимит достигнут.</summary>
    bool ProduceOne()
    {
        int cap = Mathf.Max(1, offlineProductCap);
        if (pendingProducts >= cap) return false;
        pendingProducts++;
        return true;
    }

    /// <summary>Отдать продукты (в т.ч. накопленные за оффлайн), когда игрок подошёл.</summary>
    void HandlePendingProduct()
    {
        if (!HasPendingProduct || player == null || data == null) return;

        if (Vector2.Distance(transform.position, player.position) <= data.playerAttractRadius)
        {
            int count = pendingProducts;
            pendingProducts = 0;
            for (int i = 0; i < count; i++)
                DropProduct();
        }
    }

    void FixedUpdate()
    {
        // Плавный разгон/торможение вместо мгновенной установки скорости
        if (state == State.Wander)
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, moveDir * data.moveSpeed,
                accelSpeed * Time.fixedDeltaTime);
        else
            rb.linearVelocity = Vector2.MoveTowards(rb.linearVelocity, Vector2.zero,
                accelSpeed * Time.fixedDeltaTime);

        DetectStuck();
    }

    // ═══════════════════════════════════════════════════════════
    // ЗАСТРЕВАНИЕ: если идём (Wander), но позиция почти не меняется —
    // принудительно выезжаем вдоль стены. Работает и на пути к кормушке.
    // ═══════════════════════════════════════════════════════════
    void DetectStuck()
    {
        if (escapeTimer > 0f)
        {
            lastStuckCheckPos = rb.position;
            return;
        }

        posCheckTimer -= Time.fixedDeltaTime;
        if (posCheckTimer > 0f) return;
        posCheckTimer = 0.3f;

        bool actuallyMoving = state == State.Wander &&
                              rb.linearVelocity.sqrMagnitude > Mathf.Pow(data.moveSpeed * 0.35f, 2f);
        if (!actuallyMoving)
        {
            lastStuckCheckPos = rb.position;
            stuckTimer = 0f;
            return;
        }

        float moved = (rb.position - lastStuckCheckPos).magnitude;
        lastStuckCheckPos = rb.position;

        // За 0.3с должны проходить хотя бы ~35% ожидаемого пути
        if (moved < data.moveSpeed * 0.3f * 0.35f) stuckTimer += 0.3f;
        else stuckTimer = 0f;

        if (stuckTimer >= stuckDetectTime) StartEscape();
    }

    /// <summary>Принудительный выезд из застревания: вдоль нормали стены,
    /// по возможности в сторону progressBias (цели).</summary>
    void StartEscape(float duration = -1f, Vector2 progressBias = default)
    {
        stuckTimer = 0f;

        Vector2 dir;
        if (lastContactNormal.sqrMagnitude > 0.01f)
        {
            // Скользим вдоль стены: перпендикуляр к нормали в сторону движения/цели
            Vector2 n = lastContactNormal.normalized;
            Vector2 perp = new Vector2(-n.y, n.x);
            Vector2 refDir;
            if (progressBias.sqrMagnitude > 0.001f) refDir = progressBias;      // к цели
            else if (moveDir.sqrMagnitude > 0.01f) refDir = moveDir;            // куда шли
            else refDir = wanderBias;
            dir = Vector2.Dot(perp, refDir) >= 0f ? perp : -perp;
            dir = (dir + n * 0.6f).normalized; // чуть отталкиваемся от стены
        }
        else
        {
            dir = Random.insideUnitCircle.normalized;
        }

        escapeDir = dir;
        escapeTimer = duration > 0f ? duration : stuckEscapeTime;
        wanderBias = dir;
        avoidCooldown = Mathf.Max(avoidCooldown, escapeTimer);
        moveDir = dir;
        anim.PlayState(AnimalAnimator.AnimState.Walk, DirToAnim(moveDir));
    }

    // ═══════════════════════════════════════════════════════════
    // РОСТ (Baby → Teen → Adult, Teen опционален)
    // ═══════════════════════════════════════════════════════════
    void HandleGrowth()
    {
        if (growthStage == AnimalData.GrowthStage.Adult || data == null) return;

        // Перк на скорость роста животных
        float mult = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetAnimalGrowthMultiplier() : 1f;

        growTimer -= Time.deltaTime * mult;
        if (growTimer <= 0f)
            AdvanceGrowthStage();
    }

    void AdvanceGrowthStage()
    {
        if (growthStage == AnimalData.GrowthStage.Baby)
        {
            if (data.HasTeenStage())
            {
                // Есть стадия "подросток" — переходим в неё
                growthStage = AnimalData.GrowthStage.Teen;
                growTimer = data.growTimeToAdult;
                ActionLogUI.Show("[Животное] " + data.animalName + " подрос(ла)!");
            }
            else
            {
                // Стадия "подросток" не заполнена — старое поведение: сразу взрослый
                growthStage = AnimalData.GrowthStage.Adult;
                ActionLogUI.Show("[Животное] " + data.animalName + " вырос(ла)!");
            }
        }
        else if (growthStage == AnimalData.GrowthStage.Teen)
        {
            growthStage = AnimalData.GrowthStage.Adult;
            ActionLogUI.Show("[Животное] " + data.animalName + " вырос(ла)!");
        }

        anim.SetGrowthStage(growthStage);
        anim.PlayState(AnimalAnimator.AnimState.Idle, DirToAnim(moveDir), true);
    }

    // ═══════════════════════════════════════════════════════════
    // ПРОДУКТ
    // ═══════════════════════════════════════════════════════════
    void HandleProduction()
    {
        if (data == null || data.productItem == null) return;
        if (data.onlyAdultProduces && growthStage != AnimalData.GrowthStage.Adult) return;
        if (!isFed) return;
        if (wantsWater) return; // не пьёт — не производит

        // Перк ускоряет и производство продукта
        float mult = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetAnimalGrowthMultiplier() : 1f;

        productionTimer -= Time.deltaTime * mult;
        if (productionTimer <= 0f)
        {
            DropProduct();
            isFed = false;
            // Сытость кончилась — животное само пойдёт к кормушке (см. HandleNeeds).
            // Нет кормушки с едой — просто гуляет, ждёт (кормушку проверяет каждый апдейт)
        }
    }

    void DropProduct()
    {
        if (lootItemPrefab == null)
        {
            Debug.LogWarning("[Животное] " + data.animalName + ": не назначен Loot Item Prefab в инспекторе — продукт не выпадает!");
            return;
        }
        if (data.productItem == null) return;
        for (int i = 0; i < data.productAmount; i++)
        {
            Vector2 offset = Random.insideUnitCircle * productDropRadius;
            Vector3 pos = transform.position + new Vector3(offset.x, offset.y, 0);
            GameObject obj = Instantiate(lootItemPrefab, pos, Quaternion.identity);
            LootItem loot = obj.GetComponent<LootItem>();
            if (loot != null)
            {
                loot.itemData = data.productItem;
                loot.amount = 1;
                loot.despawnOverTime = false; // продукт животного не пропадает
            }
        }
        ActionLogUI.Show("[Животное] " + data.animalName + " дал(а): " + data.productItem.itemName);
    }

    // ═══════════════════════════════════════════════════════════
    // КОНЕЧНЫЙ АВТОМАТ
    // ═══════════════════════════════════════════════════════════
    void HandleState()
    {
        // Анимация еды/клевка идёт по таймеру
        if (state == State.Eat || state == State.Peck)
        {
            eatTimer -= Time.deltaTime;
            if (eatTimer <= 0f)
            {
                // После еды/питья у кормушки — уходим прочь (не толпимся)
                if (hasLeaveDir)
                {
                    hasLeaveDir = false;
                    wanderBias = leaveDir;
                    EnterWander();
                    return;
                }
                EnterIdle();
            }
            return;
        }

        // ── Голод/жажда: сами идём к кормушке/поилке ──
        if (HandleNeeds()) return;

        // Если игрок рядом с кормом — животное идёт к нему (прерывает покой)
        Vector2 pull = GetPlayerFeedPull(transform.position);
        if (pull != Vector2.zero && state != State.Wander)
        {
            EnterWander();
        }

        // В движении периодически пересчитываем цель (стая + притяжение),
        // но КУРС сглаживаем каждый кадр: обход стен + ограничение скорости поворота
        if (state == State.Wander)
        {
            steerTimer -= Time.deltaTime;
            if (steerTimer <= 0f)
            {
                steerTimer = data.steerInterval;
                desiredDir = ComputeSteering();
            }

            moveDir = ComputeMoveDir(desiredDir);
            anim.PlayState(AnimalAnimator.AnimState.Walk, DirToAnim(moveDir));
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        if (state == State.Wander) EnterIdle();
        else EnterWander();
    }

    // ═══════════════════════════════════════════════════════════
    // ГОЛОД И ЖАЖДА (кормушка / поилка)
    // ═══════════════════════════════════════════════════════════
    /// <summary> true — животное занято нуждой (идёт/ест/пьёт), обычное поведение отключено. </summary>
    bool HandleNeeds()
    {
        if (data == null || data.feedItem == null) return false;

        // ── Тик жажды (только если в мире есть поилки) ──
        if (!wantsWater && WaterTrough.AnyInWorld)
        {
            thirstTimer -= Time.deltaTime;
            if (thirstTimer <= 0f)
            {
                wantsWater = true;
                targetTrough = null;
            }
        }

        // ── ГОЛОД: не производит продукт → ищем кормушку С МОИМ кормом.
        // Кормушек с едой нет — просто гуляет (каждый апдейт перепроверяем: как только
        // в кормушке появится еда — сразу пойдём есть)
        if (!isFed)
        {
            if (targetFeeder == null || !targetFeeder.HasFeedFor(data.feedItem))
                targetFeeder = FeederStorage.FindNearest(transform.position, feedSearchRadius, data.feedItem);

            if (targetFeeder != null)
            {
                Vector3 fpos = targetFeeder.transform.position;
                if (Vector2.Distance(transform.position, fpos) > facilityStopDistance)
                {
                    WalkTowards(fpos);
                    return true;
                }

                if (targetFeeder.TryConsume(data.feedItem))
                {
                    isFed = true;
                    productionTimer = data.productionTime;
                    StartEatingAt(fpos, 2f);
                    SaveManager.Instance?.Save();
                }
                else
                {
                    targetFeeder = null; // корм разобрали — ищем другую кормушку
                }
                return true;
            }
        }

        // ── ЖАЖДА: идти к поилке с водой ──
        if (wantsWater)
        {
            if (targetTrough == null || !targetTrough.HasWater)
                targetTrough = WaterTrough.FindNearest(transform.position, feedSearchRadius);

            if (targetTrough != null)
            {
                Vector3 tpos = targetTrough.transform.position;
                if (Vector2.Distance(transform.position, tpos) > facilityStopDistance)
                {
                    WalkTowards(tpos);
                    return true;
                }

                if (targetTrough.TryDrink())
                {
                    wantsWater = false;
                    thirstTimer = drinkInterval;
                    StartEatingAt(tpos, 1.2f); // анимация "пьёт"
                }
                else
                {
                    targetTrough = null;
                }
                return true;
            }
            // Поилок с водой нет — ждём (производство стоит, см. HandleProduction)
        }

        return false;
    }

    void WalkTowards(Vector3 target)
    {
        if (state != State.Wander) EnterWander();
        Vector2 toTarget = ((Vector2)target - rb.position).normalized;
        // Путь к цели: без ограничения поворота (чтобы не кружить вокруг цели),
        // но с огибанием стен и выходом из застревания
        moveDir = ComputeMoveDir(toTarget, clampTurn: false);
        anim.PlayState(AnimalAnimator.AnimState.Walk, DirToAnim(moveDir));

        // ── Страховка прогресса: толкаемся в забор и не сдвигаемся? ──
        // «Усы» могут скользить вдоль длинной стены, но если за секунду
        // продвижения к цели НЕТ вообще — принудительный манёвр
        if ((target - lastNeedTarget).sqrMagnitude > 0.001f)
        {
            // новая цель — начинаем отсчёт заново
            lastNeedTarget = target;
            lastNeedCheckPos = rb.position;
            needCheckTimer = 1.0f;
            return;
        }

        needCheckTimer -= Time.deltaTime;
        if (needCheckTimer > 0f) return;
        needCheckTimer = 0.8f;

        float advanced = (rb.position - lastNeedCheckPos).magnitude;
        lastNeedCheckPos = rb.position;

        if (advanced < 0.12f && escapeTimer <= 0f)
            StartEscape(0.6f, (Vector2)target - rb.position); // сдвиг вдоль стены В СТОРОНУ ЦЕЛИ
    }

    /// <summary>Анимация поедания у объекта + после — уйти в сторону.</summary>
    void StartEatingAt(Vector3 facilityPos, float duration)
    {
        targetFeeder = null;
        targetTrough = null;
        leaveDir = ((Vector2)transform.position - (Vector2)facilityPos).normalized;
        if (leaveDir.sqrMagnitude < 0.01f) leaveDir = Random.insideUnitCircle.normalized;
        hasLeaveDir = true;

        state = State.Eat;
        eatTimer = duration;
        anim.PlayState(AnimalAnimator.AnimState.Eat, lastFacing, true);
    }

    // ═══════════════════════════════════════════════════════════
    // ИНДИКАТОР ГОЛОДА (иконка корма над головой)
    // ═══════════════════════════════════════════════════════════
    void EnsureHungerIcon()
    {
        if (hungerIcon != null) return;
        if (data == null || data.feedItem == null || data.feedItem.icon == null) return;

        var go = new GameObject("HungerIcon");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        go.transform.localScale = Vector3.one * 0.5f; // вдвое меньше
        hungerIcon = go.AddComponent<SpriteRenderer>();
        hungerIcon.sprite = data.feedItem.icon;
        hungerIcon.sortingOrder = YSort.GetOrder(transform.position, 1);
        hungerIcon.enabled = false;
    }

    void UpdateHungerIcon()
    {
        if (hungerIcon == null) return;
        bool hungry = !isFed;
        hungerIcon.enabled = hungry;
        if (hungry)
        {
            float t = Time.time * 4f;
            hungerIcon.transform.localPosition = new Vector3(0f, 1.4f + Mathf.Sin(t) * 0.08f, 0f);
            hungerIcon.color = wantsWater ? new Color(0.6f, 0.8f, 1f) : Color.white;
            // Сортировка как у животного (Y-sort), +1 — поверх тела
            hungerIcon.sortingOrder = YSort.GetOrder(transform.position, 1);
        }
    }

    void EnterWander()
    {
        state = State.Wander;
        stateTimer = Random.Range(data.minWanderTime, data.maxWanderTime);
        steerTimer = data.steerInterval;

        // немного меняем случайный уклон чтобы блуждание было органичным
        wanderBias = Vector2.Lerp(wanderBias, Random.insideUnitCircle.normalized, 0.5f).normalized;

        desiredDir = ComputeSteering();
        // moveDir НЕ сбрасываем — курс плавно довернётся ограничением поворота
        anim.PlayState(AnimalAnimator.AnimState.Walk, DirToAnim(moveDir));
    }

    void EnterIdle()
    {
        float roll = Random.value;

        if (roll < data.peckChance)
        {
            // Поклевать (анимация еды) — короткий, пару раз клюнул и всё
            state = State.Peck;
            eatTimer = Random.Range(data.minPeckTime, data.maxPeckTime);
            anim.PlayState(AnimalAnimator.AnimState.Eat, lastFacing, true);
            return;
        }

        if (roll < data.peckChance + data.sitChance)
        {
            // Сесть — сидит дольше чем стоит
            state = State.Sit;
            anim.PlayState(AnimalAnimator.AnimState.Sit, lastFacing);
            stateTimer = Random.Range(data.minSitTime, data.maxSitTime);
        }
        else
        {
            state = State.Idle;
            anim.PlayState(AnimalAnimator.AnimState.Idle, lastFacing);
            stateTimer = Random.Range(data.minIdleTime, data.maxIdleTime);
        }
    }

    // ═══════════════════════════════════════════════════════════
    // БОИДНОЕ РУЛЕНИЕ (стая + расталкивание + притяжение к игроку)
    // ═══════════════════════════════════════════════════════════
    Vector2 ComputeSteering()
    {
        Vector2 pos = transform.position;
        Vector2 steer = wanderBias; // базовое блуждание

        Vector2 cohesion = Vector2.zero;
        Vector2 separation = Vector2.zero;
        int neighbours = 0;

        foreach (AnimalController other in allAnimals)
        {
            if (other == this || other.data != data) continue; // только свой вид
            Vector2 opos = other.transform.position;
            float d = Vector2.Distance(pos, opos);

            if (d < data.flockRadius)
            {
                cohesion += opos;
                neighbours++;

                if (d < data.separationRadius && d > 0.001f)
                    separation += (pos - opos) / d; // ближе → сильнее отталкивание
            }
        }

        if (neighbours > 0)
        {
            cohesion = (cohesion / neighbours) - pos;      // вектор к центру стаи
            if (cohesion.sqrMagnitude > 0.0001f)
                steer += cohesion.normalized * data.cohesionWeight;
            steer += separation * data.separationWeight;
        }

        // Притяжение к игроку с кормом
        steer += GetPlayerFeedPull(pos);

        // Держим дистанцию от забора/стен: пучок лучей по кругу,
        // направления к препятствию выталкиваются. Чем ближе — тем сильнее.
        Vector2 wallPush = Vector2.zero;
        float nearestFrac = 1f;
        const int wallRays = 8;
        for (int i = 0; i < wallRays; i++)
        {
            Vector2 d = Rotate(Vector2.up, i * (360f / wallRays));
            float frac = ObstacleFraction(d);
            if (frac < 1f)
            {
                wallPush -= d * (1f - frac);
                if (frac < nearestFrac) nearestFrac = frac;
            }
        }
        if (wallPush.sqrMagnitude > 0.0001f)
        {
            // Мягко на дистанции, сильно у самого забора — подойти можно, но редко хочется
            float strength = wallAvoidWeight * Mathf.Lerp(0.4f, 1.6f, 1f - nearestFrac);
            steer += wallPush.normalized * strength;
        }

        // ── Тяга к центру загона: чем дальше от маркера, тем сильнее тянет обратно ──
        // Компенсирует то, что кормушки/поилки у забора собирают всех по краям
        if (roamCenter != null)
        {
            Vector2 toCenter = (Vector2)roamCenter.position - pos;
            float dc = toCenter.magnitude;
            if (dc > 2.5f)
            {
                float pull = roamCenterWeight * Mathf.Min(1f, (dc - 2.5f) / 6f);
                steer += (toCenter / dc) * pull;
            }
        }

        if (steer.sqrMagnitude < 0.0001f) steer = wanderBias;
        return steer.normalized;
    }

    Vector2 GetPlayerFeedPull(Vector2 pos)
    {
        if (player == null || data.feedItem == null) return Vector2.zero;
        if (HotbarManager.Instance?.GetActiveItem() != data.feedItem) return Vector2.zero;

        float d = Vector2.Distance(pos, player.position);
        if (d > data.playerAttractRadius) return Vector2.zero;
        if (d < data.playerStopDistance) return Vector2.zero; // близко — не набегаем

        Vector2 dir = ((Vector2)player.position - pos).normalized;
        return dir * data.playerAttractWeight;
    }

    // ═══════════════════════════════════════════════════════════
    // ОБХОД ПРЕПЯТСТВИЙ И СГЛАЖИВАНИЕ КУРСА
    // ═══════════════════════════════════════════════════════════

    // Углы постепенного огибания препятствия (первый свободный побеждает)
    static readonly float[] SkirtAngles = { 30f, 65f, 100f, 135f, 170f };

    /// <summary>
    /// Финальное направление движения: огибание стены «усами» +
    /// ограничение скорости поворота (нет мгновенных разворотов влево-вправо).
    /// </summary>
    Vector2 ComputeMoveDir(Vector2 desired, bool clampTurn = true)
    {
        if (desired.sqrMagnitude < 0.001f) desired = wanderBias;

        // Аварийный выход из застревания — принудительно держим курс
        if (escapeTimer > 0f) return escapeDir;

        // Впереди стена → огибаем её постепенно, начиная с малого угла.
        // Сторона обхода выбирается ОДИН РАЗ и держится до конца манёвра —
        // иначе животное мечется между «вперёд» и «назад» каждый кадр
        if (BlockedInDir(desired))
        {
            if (skirtSide == 0) skirtSide = Random.value < 0.5f ? 1 : -1;

            bool found = false;
            foreach (float ang in SkirtAngles)
            {
                Vector2 cand = Rotate(desired, ang * skirtSide);
                if (!BlockedInDir(cand)) { desired = cand; found = true; break; }

                cand = Rotate(desired, -ang * skirtSide);
                if (!BlockedInDir(cand)) { desired = cand; found = true; break; }
            }

            // Все направления наглухо закрыты — только разворот
            if (!found) desired = Rotate(desired, 180f);
        }
        else
        {
            skirtSide = 0; // путь снова чист — память обхода сброшена
        }

        // Ограничение скорости поворота: не дёргаемся, а плавно доверяемся
        if (clampTurn && moveDir.sqrMagnitude > 0.001f)
        {
            float maxStep = maxTurnDegPerSec * Time.deltaTime;
            float ang = Vector2.SignedAngle(moveDir, desired);
            if (Mathf.Abs(ang) > maxStep)
                desired = Rotate(moveDir, Mathf.Sign(ang) * maxStep);
        }

        return desired.normalized;
    }

    /// <summary>Есть ли препятствие по направлению dir от животного.</summary>
    bool BlockedInDir(Vector2 dir)
    {
        RaycastHit2D[] hits = Physics2D.CircleCastAll(rb.position, avoidProbeRadius, dir,
            avoidProbeDist, Physics2D.DefaultRaycastLayers);

        foreach (RaycastHit2D h in hits)
            if (IsBlockingCollider(h.collider)) return true;
        return false;
    }

    /// <summary>Ближайшее препятствие по dir: 1 = чисто, меньше = доля дистанции до препятствия.</summary>
    float ObstacleFraction(Vector2 dir)
    {
        RaycastHit2D[] hits = Physics2D.CircleCastAll(rb.position, avoidProbeRadius, dir,
            wallKeepDist, Physics2D.DefaultRaycastLayers);

        float best = 1f;
        foreach (RaycastHit2D h in hits)
        {
            if (!IsBlockingCollider(h.collider)) continue;
            if (h.fraction < best) best = h.fraction;
        }
        return best;
    }

    /// <summary>Считается ли коллайдер препятствием (не мы сами, не игрок, не животные, не лут).</summary>
    bool IsBlockingCollider(Collider2D c)
    {
        if (c == null || c == selfCol || c.isTrigger) return false;
        if (c.attachedRigidbody == rb) return false;
        if (c.CompareTag("Player")) return false;
        if (c.GetComponentInParent<AnimalController>() != null) return false;
        if (c.GetComponent<LootItem>() != null) return false;
        return true;
    }

    static Vector2 Rotate(Vector2 v, float deg) => Quaternion.Euler(0f, 0f, deg) * v;

    // Выбор направления анимации с ПРИОРИТЕТОМ ГОРИЗОНТАЛИ.
    // Любая диагональ с левым уклоном → влево, с правым → вправо.
    // Вверх/вниз играет только при почти чисто вертикальном движении (x ≈ 0).
    AnimalAnimator.AnimDir DirToAnim(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return lastFacing;

        const float horizontalDeadzone = 0.30f; // |x| меньше этого = чистая вертикаль

        AnimalAnimator.AnimDir dir2;
        if (Mathf.Abs(dir.x) > horizontalDeadzone)
            // Есть заметная горизонтальная составляющая → лево/право
            dir2 = dir.x > 0 ? AnimalAnimator.AnimDir.Right : AnimalAnimator.AnimDir.Left;
        else
            // Почти вертикально → вверх/вниз
            dir2 = dir.y > 0 ? AnimalAnimator.AnimDir.Up : AnimalAnimator.AnimDir.Down;

        lastFacing = dir2;
        return dir2;
    }

    // Касание с препятствием: запоминаем нормаль стены и ИЗРЕДКА (с кулдауном)
    // подворачиваем курс ВДОЛЬ стены. Раньше здесь было случайное направление
    // КАЖДЫЙ физический кадр — отсюда дёрганья влево-вправо.
    void OnCollisionStay2D(Collision2D col)
    {
        if (col.contactCount > 0)
            lastContactNormal = col.GetContact(0).normal;

        if (state != State.Wander) return;
        if (col.gameObject.CompareTag("Player")) return;

        // Путь к кормушке/поилке рулится «усами» (ComputeMoveDir) — не мешаем,
        // но нормаль запомнили: она используется при аварийном выезде StartEscape
        bool goingToFeeder = !isFed && targetFeeder != null;
        bool goingToTrough = wantsWater && targetTrough != null;
        if (goingToFeeder || goingToTrough) return;
        if (escapeTimer > 0f || avoidCooldown > 0f) return;

        // Подворот вдоль стены: перпендикуляр к нормали в сторону текущего движения
        Vector2 n = lastContactNormal.sqrMagnitude > 0.01f ? lastContactNormal.normalized : Random.insideUnitCircle.normalized;
        Vector2 perp = new Vector2(-n.y, n.x);
        Vector2 refDir = moveDir.sqrMagnitude > 0.01f ? moveDir : wanderBias;
        wanderBias = (Vector2.Dot(perp, refDir) >= 0f ? perp : -perp).normalized;

        avoidCooldown = 0.45f; // пауза — даём курсу примениться, не мельтешим
    }

    // ═══════════════════════════════════════════════════════════
    // КОРМЛЕНИЕ
    // ═══════════════════════════════════════════════════════════
    public Transform GetTransform() => transform;

    public void Interact(GameObject playerObj)
    {
        if (data == null || data.feedItem == null) return;

        ItemData active = HotbarManager.Instance?.GetActiveItem();
        if (active != data.feedItem)
        {
            ActionLogUI.Show("[Животное] Нужен корм: " + data.feedItem.itemName);
            return;
        }

        if (isFed)
        {
            ActionLogUI.Show("[Животное] Уже накормлено");
            return;
        }

        InventorySlot slot = HotbarManager.Instance.GetActiveSlot();
        if (slot != null && !slot.IsEmpty())
        {
            if (slot.quantity > 1) { slot.quantity--; slot.UpdateUI(); }
            else slot.ClearSlot();
            HotbarManager.Instance.NotifyActiveItemChanged();
        }

        isFed = true;
        productionTimer = data.productionTime;

        // Сейв по событию: животное накормлено (изменилось его состояние)
        SaveManager.Instance?.Save();

        state = State.Eat;
        eatTimer = 2f;
        anim.PlayState(AnimalAnimator.AnimState.Eat, DirToAnim(Vector2.down), true);

        ActionLogUI.Show("[Животное] " + data.animalName + " накормлено! Продукт через " + data.productionTime + "с");
    }
}