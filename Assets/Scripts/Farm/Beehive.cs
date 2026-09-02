using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Улей на ферме. Ставится игроком из хотбара (ghost-режим в PlayerMovement),
/// собирается молотком как кормушка/поилка/пугало.
/// Пчёлы (BeeController, спавнятся кодом) вылетают ПО ОЧЕРЕДИ, делают рейсы:
/// летают по округе → возвращаются в улей → +1 мёд.
/// Визуал: пустой улей = статичный кадр Beehive_0, ПОЛНЫЙ улей = зацикленная
/// анимация Beehive_1..6 (это и есть индикатор готовности к сбору).
/// Удар (атака) по полному улью — дроп сот физическим лутом (LootItem),
/// улей сбрасывается в пустой кадр и пчёлы снова вылетают (по очереди).
/// Перк honey_harvest (будущий): каждый ранг даёт шанс на бонусную соту.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Beehive : MonoBehaviour, IInteractable
{
    public const int Capacity = 6;                 // стадий мёда (спрайтов стадий: Capacity+1)
    public const string HoneyPerkTag = "honey_harvest";
    public const string HoneycombItemName = "Honeycomb";

    [Header("Спрайты (Beehive_0..6)")]
    [Tooltip("Кадр 0 = ПУСТОЙ улей (статика), кадры 1..6 = анимация ПОЛНОГО улья (зацикливается)")]
    public Sprite[] stages;
    [Tooltip("FPS анимации полного улья (кадры 1..6)")]
    public float fullAnimFps = 7f;

    [Header("Пчёлы")]
    [Tooltip("Кадры пчелы (Bees_0..3, летит вправо)")]
    public Sprite[] beeFrames;
    [Tooltip("Сколько пчёл летает с одного улья")]
    public int beeCount = 2;
    [Tooltip("Минут от пустого улья до ПОЛНОГО (реальное время игры). Длительность каждого рейса считается из этого: рейс = fillTime × пчёлы ÷ стадии, ±10% разброса")]
    public float fillTimeMinutes = 12f;
    [Tooltip("Радиус полёта пчёл вокруг улья")]
    public float wanderRadius = 9f;

    [Header("Сбор")]
    [Tooltip("Предмет-соты (дроп при ударе по полному улью)")]
    public ItemData honeycombItem;
    [Tooltip("Шанс бонусной соты за каждый ранг перка honey_harvest")]
    [Range(0f, 1f)] public float bonusChancePerPerkRank = 0.25f;

    // ── Состояние ──
    int honey = 0;
    readonly List<BeeController> bees = new List<BeeController>();
    SpriteRenderer sr;
    Coroutine animRoutine;
    Coroutine spawnRoutine;

    // Восстановление пчёл из сейва (позиция + остаток рейса) — применяется в Start
    readonly List<Vector3> pendingBees = new List<Vector3>(); // x, y, remainingTrip

    public bool IsFull => honey >= Capacity;
    public int Honey => honey;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        var col = GetComponent<BoxCollider2D>();
        if (col != null) col.isTrigger = false;
        ApplyState();
    }

    void Start()
    {
        // Пчёлы из сейва: спавним на сохранённых точках с остатком рейса
        // (иначе при каждом входе в игру пчёлы вылетали бы из улья заново)
        if (pendingBees.Count > 0 && !IsFull)
        {
            foreach (var b in pendingBees)
                SpawnBee(new Vector3(b.x, b.y, 0f), b.z);
            pendingBees.Clear();
        }
        UpdateBees(); // доберёт недостающих по очереди, если нужно
    }

    // ═══════════════════════════════════════════════════════════
    // МЁД
    // ═══════════════════════════════════════════════════════════
    public void AddHoney(int amount = 1)
    {
        if (IsFull) return;
        honey = Mathf.Min(Capacity, honey + Mathf.Max(1, amount));
        ApplyState();
        UpdateBees(); // полный улей — пчёлы больше не вылетают
        if (IsFull)
        {
            ActionLogUI.Show("Улей заполнен! Ударь по нему, чтобы собрать соты.");
            SaveManager.Instance?.Save();
        }
    }

    /// <summary>Восстановление из сейва (PlaceablesSaveManager). Пчёлы дозаполнят в Start.</summary>
    public void ApplySave(int savedHoney)
    {
        honey = Mathf.Clamp(savedHoney, 0, Capacity);
        ApplyState();
    }

    /// <summary>Пустой улей = кадр 0 (статика), полный = анимация кадров 1..6.</summary>
    void ApplyState()
    {
        if (stages == null || stages.Length == 0 || sr == null) return;

        if (animRoutine != null) { StopCoroutine(animRoutine); animRoutine = null; }

        if (IsFull)
            animRoutine = StartCoroutine(PlayFullAnim());
        else if (stages[0] != null)
            sr.sprite = stages[0];
    }

    IEnumerator PlayFullAnim()
    {
        int i = 1;
        float frameTime = 1f / Mathf.Max(1f, fullAnimFps);
        while (true)
        {
            if (stages[i] != null) sr.sprite = stages[i];
            yield return new WaitForSeconds(frameTime);
            i = i >= stages.Length - 1 ? 1 : i + 1;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ПЧЁЛЫ
    // ═══════════════════════════════════════════════════════════
    void UpdateBees()
    {
        bees.RemoveAll(b => b == null);

        if (IsFull)
        {
            // Полный улей: пчёлы залетают внутрь и исчезают
            if (spawnRoutine != null) { StopCoroutine(spawnRoutine); spawnRoutine = null; }
            foreach (var b in bees) b.GoHomeAndStay();
            bees.Clear();
            return;
        }

        // Пчёлы вылетают ПО ОЧЕРЕДИ (корутина добирает недостающих)
        if (spawnRoutine == null)
            spawnRoutine = StartCoroutine(SpawnBeesOverTime());
    }

    IEnumerator SpawnBeesOverTime()
    {
        while (true)
        {
            bees.RemoveAll(b => b == null);
            if (IsFull || bees.Count >= beeCount) break;

            SpawnBee(transform.position + new Vector3(Random.Range(-0.3f, 0.3f), 0.4f, 0f));
            yield return new WaitForSeconds(Random.Range(2f, 4f)); // пауза между вылетами
        }
        spawnRoutine = null;
    }

    void SpawnBee(Vector3 pos, float remainingTrip = -1f)
    {
        var go = new GameObject("Bee");
        go.transform.SetParent(transform, false);
        go.transform.position = pos;

        var beeSr = go.AddComponent<SpriteRenderer>();
        beeSr.sortingOrder = YSort.GetOrder(pos, 1); // точную сортировку ведёт LateUpdate пчелы
        if (beeFrames != null && beeFrames.Length > 0)
            beeSr.sprite = beeFrames[0];

        var bee = go.AddComponent<BeeController>();
        // Рейс = полное время заполнения × пчёлы ÷ стадий (±10% разброса):
        // каждая пчела за рейс приносит 1 мёд → сумма рейсов как раз fillTime
        float baseTrip = fillTimeMinutes * 60f * beeCount / Capacity;
        bee.Init(this, beeFrames, baseTrip * Random.Range(0.9f, 1.1f), wanderRadius, remainingTrip);
        bees.Add(bee);
    }

    // ═══════════════════════════════════════════════════════════
    // СЕЙВ ПЧЁЛ (тянет PlaceablesSaveManager): где пчела и остаток рейса
    // ═══════════════════════════════════════════════════════════
    public float[] SaveBeeX()
    {
        var arr = new float[bees.Count];
        for (int i = 0; i < bees.Count; i++) arr[i] = bees[i].transform.position.x;
        return arr;
    }

    public float[] SaveBeeY()
    {
        var arr = new float[bees.Count];
        for (int i = 0; i < bees.Count; i++) arr[i] = bees[i].transform.position.y;
        return arr;
    }

    public float[] SaveBeeTrip()
    {
        var arr = new float[bees.Count];
        for (int i = 0; i < bees.Count; i++) arr[i] = bees[i].RemainingTrip;
        return arr;
    }

    public void ApplyBeeSave(float[] xs, float[] ys, float[] trips)
    {
        pendingBees.Clear();
        if (xs == null || ys == null || trips == null) return;
        int n = Mathf.Min(xs.Length, Mathf.Min(ys.Length, trips.Length));
        for (int i = 0; i < n; i++)
            pendingBees.Add(new Vector3(xs[i], ys[i], Mathf.Max(0.5f, trips[i])));
    }

    /// <summary>Пчела вернулась с взятком (зовёт BeeController).</summary>
    public void NotifyBeeReturned(BeeController bee)
    {
        bees.Remove(bee);
        if (bee != null) Destroy(bee.gameObject);
        AddHoney(1);
    }

    // ═══════════════════════════════════════════════════════════
    // ВЗАИМОДЕЙСТВИЕ (удар = атака)
    // ═══════════════════════════════════════════════════════════
    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        if (!IsFull)
        {
            ActionLogUI.Show("Улей: " + honey + "/" + Capacity + ". Пчёлы носят мёд — подожди.");
            return;
        }
        Harvest();
    }

    void Harvest()
    {
        if (honeycombItem == null)
            honeycombItem = ItemDatabase.Find(HoneycombItemName);
        if (honeycombItem == null)
        {
            Debug.LogWarning("[Улей] Не найден предмет сот: " + HoneycombItemName);
            return;
        }

        // Базово 1 сота; перк honey_harvest: за каждый ранг шанс бонусной соты
        int amount = 1;
        int rank = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetNodeRankByFeature(HoneyPerkTag) : 0;
        for (int i = 0; i < rank; i++)
            if (Random.value < bonusChancePerPerkRank) amount++;

        var lootPrefab = Resources.Load<GameObject>("LootItemPrefab");
        if (lootPrefab != null)
        {
            for (int i = 0; i < amount; i++)
            {
                Vector2 off = Random.insideUnitCircle * 0.35f;
                GameObject obj = Instantiate(lootPrefab,
                    transform.position + new Vector3(off.x, off.y, 0f), Quaternion.identity);
                LootItem loot = obj.GetComponent<LootItem>();
                if (loot != null)
                {
                    loot.itemData = honeycombItem;
                    loot.amount = 1;
                    loot.despawnOverTime = false; // соты не пропадают
                    loot.craftingXpReward = 0;
                    loot.farmingXpReward = 0;
                }
            }

            if (amount > 1 && DamagePopupManager.Instance != null)
                DamagePopupManager.Instance.Spawn(
                    transform.position + Vector3.up, amount, DamagePopup.PopupType.Heal);
        }
        else
        {
            // Фолбэк: сразу в рюкзак
            InventoryUI.Instance?.AddItem(honeycombItem, amount);
        }

        honey = 0;
        ApplyState();
        UpdateBees(); // пчёлы снова вылетают (по очереди)

        ActionLogUI.Show("Собрано сот: " + amount);
        SaveManager.Instance?.Save();
    }
}
