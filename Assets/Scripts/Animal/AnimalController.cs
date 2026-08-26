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

    private Rigidbody2D rb;
    private AnimalAnimator anim;
    private Transform player;

    private enum State { Wander, Idle, Sit, Peck, Eat }
    private State state = State.Idle;
    private float stateTimer;
    private Vector2 moveDir;
    private Vector2 wanderBias;
    private float steerTimer;
    private AnimalAnimator.AnimDir lastFacing = AnimalAnimator.AnimDir.Down;

    // Рост — 3 стадии (Teen пропускается автоматически если не заполнена в AnimalData)
    private AnimalData.GrowthStage growthStage;
    private float growTimer;

    private bool isFed;
    private float productionTimer;
    private float eatTimer;

    void OnEnable() { allAnimals.Add(this); }
    void OnDisable() { allAnimals.Remove(this); }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
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

        // Фолбэк: префаб лута из Resources, если не назначен в инспекторе
        if (lootItemPrefab == null)
            lootItemPrefab = Resources.Load<GameObject>("LootItemPrefab");

        wanderBias = Random.insideUnitCircle.normalized;

        anim.Init(data, growthStage);
        EnterIdle();
    }

    void Update()
    {
        HandleGrowth();
        HandleProduction();
        HandleState();
        HandlePendingProduct();
    }

    // ═══════════════════════════════════════════════════════════
    // СОХРАНЕНИЕ / ЗАГРУЗКА / ОФФЛАЙН-ПРОГРЕСС
    // (читает и восстанавливает AnimalSaveManager)
    // ═══════════════════════════════════════════════════════════
    public AnimalData.GrowthStage CurrentStage => growthStage;
    public float GrowTimerRemaining => growTimer;
    public bool IsFed => isFed;
    public float ProductionTimerRemaining => productionTimer;
    public bool HasPendingProduct => pendingProduct;

    private bool restoredFromSave = false;
    private bool pendingProduct = false; // продукт произведён пока игра была закрыта

    /// <summary>Восстановить состояние животного (позиция, рост, кормление).</summary>
    public void ApplyRestoredState(int stage, float growTimerSec, bool fed, float prodTimer, bool hasPendingProduct, Vector3 pos)
    {
        restoredFromSave = true;
        growthStage = (AnimalData.GrowthStage)stage;
        growTimer = growTimerSec;
        isFed = fed;
        productionTimer = prodTimer;
        pendingProduct = hasPendingProduct;

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

        // ── Продукт (созревает только у взрослого) ──
        if (isFed && left > 0 && (!data.onlyAdultProduces || growthStage == AnimalData.GrowthStage.Adult))
        {
            if (productionTimer <= left)
            {
                // Продукт созрел пока игры не было — животное "держит" его
                isFed = false;
                pendingProduct = true;
            }
            else
            {
                productionTimer -= (float)left;
            }
        }
    }

    /// <summary>Отдать продукт, произведённый оффлайн, когда игрок подошёл.</summary>
    void HandlePendingProduct()
    {
        if (!pendingProduct || player == null || data == null) return;

        if (Vector2.Distance(transform.position, player.position) <= data.playerAttractRadius)
        {
            pendingProduct = false;
            DropProduct();
        }
    }

    void FixedUpdate()
    {
        if (state == State.Wander)
            rb.linearVelocity = moveDir * data.moveSpeed;
        else
            rb.linearVelocity = Vector2.zero;
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
                Debug.Log("[Животное] " + data.animalName + " подрос(ла)!");
            }
            else
            {
                // Стадия "подросток" не заполнена — старое поведение: сразу взрослый
                growthStage = AnimalData.GrowthStage.Adult;
                Debug.Log("[Животное] " + data.animalName + " вырос(ла)!");
            }
        }
        else if (growthStage == AnimalData.GrowthStage.Teen)
        {
            growthStage = AnimalData.GrowthStage.Adult;
            Debug.Log("[Животное] " + data.animalName + " вырос(ла)!");
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

        // Перк ускоряет и производство продукта
        float mult = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetAnimalGrowthMultiplier() : 1f;

        productionTimer -= Time.deltaTime * mult;
        if (productionTimer <= 0f)
        {
            DropProduct();
            isFed = false;
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
        Debug.Log("[Животное] " + data.animalName + " дал(а): " + data.productItem.itemName);
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
            if (eatTimer <= 0f) EnterIdle();
            return;
        }

        // Если игрок рядом с кормом — животное идёт к нему (прерывает покой)
        Vector2 pull = GetPlayerFeedPull(transform.position);
        if (pull != Vector2.zero && state != State.Wander)
        {
            EnterWander();
        }

        // В движении периодически пересчитываем направление (стая + притяжение)
        if (state == State.Wander)
        {
            steerTimer -= Time.deltaTime;
            if (steerTimer <= 0f)
            {
                steerTimer = data.steerInterval;
                moveDir = ComputeSteering();
                anim.PlayState(AnimalAnimator.AnimState.Walk, DirToAnim(moveDir));
            }
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer > 0f) return;

        if (state == State.Wander) EnterIdle();
        else EnterWander();
    }

    void EnterWander()
    {
        state = State.Wander;
        stateTimer = Random.Range(data.minWanderTime, data.maxWanderTime);
        steerTimer = data.steerInterval;

        // немного меняем случайный уклон чтобы блуждание было органичным
        wanderBias = Vector2.Lerp(wanderBias, Random.insideUnitCircle.normalized, 0.5f).normalized;

        moveDir = ComputeSteering();
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

    // Врезались в препятствие — сразу меняем курс (не застреваем)
    void OnCollisionStay2D(Collision2D col)
    {
        if (state != State.Wander) return;
        if (col.gameObject.CompareTag("Player")) return;

        wanderBias = Random.insideUnitCircle.normalized;
        moveDir = ComputeSteering();
        anim.PlayState(AnimalAnimator.AnimState.Walk, DirToAnim(moveDir));
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
            Debug.Log("[Животное] Нужен корм: " + data.feedItem.itemName);
            return;
        }

        if (isFed)
        {
            Debug.Log("[Животное] Уже накормлено");
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

        Debug.Log("[Животное] " + data.animalName + " накормлено! Продукт через " + data.productionTime + "с");
    }
}