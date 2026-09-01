using UnityEngine;

/// <summary>
/// ИИ вороны (Этап 2 ROADMAP: вороны + пугало).
/// Живёт на ферме одним инстансом: сидит за экраном → раз в N секунд с шансом
/// вылетает → выбирает грядку с растением (предпочитает зрелую) → летит из-за
/// карты → САДИТСЯ и КЛЮЁТ. Игрок близко во время полёта или клёва — испуганная
/// ворона улетает. Склевала растение — растение исчезает
/// (FarmManager.CrowEatCrop) и ворона улетает за карту.
/// Движение полётом по прямой (коллайдер не нужен, ворона в воздухе).
/// Анимация кодом из CrowData, без Animator.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class CrowAI : MonoBehaviour
{
    private enum State { Waiting, FlyToCrop, Peck, FlyAway }

    [Header("Данные (Create → RPG → Crow)")]
    public CrowData data;

    [Header("Вылеты")]
    [Tooltip("Пауза между попытками вылета, сек (мин/макс)")]
    public Vector2 spawnInterval = new Vector2(25f, 70f);
    [Tooltip("Шанс вылета, когда таймер истёк")]
    [Range(0f, 1f)] public float attackChance = 0.7f;
    [Tooltip("Скорость полёта, м/с")]
    public float flySpeed = 4.5f;
    [Tooltip("Запас за краем экрана, метры (спавн/деспавн)")]
    public float offscreenMargin = 2f;

    [Header("Клёв")]
    [Tooltip("Сколько ест, сек (мин/макс) — потом растение исчезает")]
    public Vector2 eatTime = new Vector2(4f, 7f);

    [Header("Боязнь игрока")]
    [Tooltip("Ближе этого расстояния ворона не садится / улетает")]
    public float scareRadius = 3f;

    [Header("Звук (опционально)")]
    [Tooltip("Карканье при испуге. Если пусто — молчит")]
    public AudioClip cawSound;
    [Range(0f, 1f)] public float cawVolume = 0.7f;

    private SpriteRenderer sr;
    private Transform player;
    private AudioSource audioSrc;

    private State state = State.Waiting;
    private CropTile targetCrop;       // выбранная грядка
    private Vector3 cropPos;           // позиция растения (на случай удаления)
    private Vector3 flyTarget;         // куда летим сейчас
    private float stateTimer;
    private float waitTimer;

    // Анимация
    private Sprite[] currentFrames;
    private int frameIndex;
    private float frameTimer;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        if (cawSound != null)
        {
            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;
            audioSrc.spatialBlend = 0f;
            audioSrc.volume = cawVolume;
        }
    }

    void Start()
    {
        // Ворона вне экрана и невидима, пока «ждёт» дома
        waitTimer = Random.Range(spawnInterval.x, spawnInterval.y);
        transform.position = GetOffscreenPoint();
        sr.enabled = false;
        PickFlyFrame(null, false);
    }

    void Update()
    {
        if (data == null) return;
        FindPlayer();

        switch (state)
        {
            case State.Waiting: DoWaiting(); break;
            case State.FlyToCrop: DoFlyToCrop(); break;
            case State.Peck: DoPeck(); break;
            case State.FlyAway: DoFlyAway(); break;
        }

        Animate();
    }

    // ---------- Состояния ----------

    void DoWaiting()
    {
        waitTimer -= Time.deltaTime;
        if (waitTimer > 0f) return;

        // Таймер истёк: бросаем жребий — летим или ждём дальше
        if (Random.value > attackChance)
        {
            waitTimer = Random.Range(spawnInterval.x, spawnInterval.y);
            return;
        }

        CropTile crop = PickCrop();
        if (crop == null)
        {
            // Грядок нет (не ферма / всё пусто) — попробуем позже
            waitTimer = Random.Range(spawnInterval.x, spawnInterval.y);
            return;
        }

        targetCrop = crop;
        cropPos = crop.transform.position;
        flyTarget = cropPos;
        state = State.FlyToCrop;
        sr.enabled = true;
        FaceFly(flyTarget - transform.position);
    }

    void DoFlyToCrop()
    {
        // Пока летели, поставили пугало — улетаем
        if (Scarecrow.IsProtected(cropPos))
        {
            Scare();
            return;
        }

        // Игрок уже стоит у цели — не садиться, улетать
        if (PlayerNear(scareRadius))
        {
            Scare();
            return;
        }

        if (!MoveTowards(flyTarget)) return;

        // Долетели — садимся и клюём
        StartPeck();
    }

    void StartPeck()
    {
        state = State.Peck;
        stateTimer = Random.Range(eatTime.x, eatTime.y);
        sr.flipX = false;
        PlayFrames(data.peck);
    }

    void DoPeck()
    {
        // Игрок подошёл — испуг, улетает голодной
        if (PlayerNear(scareRadius))
        {
            Scare();
            return;
        }

        // Пугало поставили во время клёва — улетает голодной
        if (Scarecrow.IsProtected(cropPos))
        {
            Scare();
            return;
        }

        stateTimer -= Time.deltaTime;

        // Растение могло собрать игрок / исчезнуть — улетаем
        if (targetCrop == null)
        {
            FlyOff();
            return;
        }

        EatTick();
    }

    // Клёв: до конца таймера ворона клюёт (цикл peck), потом съедает растение
    void EatTick()
    {
        if (stateTimer <= 0f)
        {
            // Доели: растение исчезает, ворона улетает довольная
            if (targetCrop != null && FarmManager.Instance != null)
                FarmManager.Instance.CrowEatCrop(cropPos);
            targetCrop = null;
            FlyOff();
            return;
        }

        PlayFrames(data.peck);
    }

    void Scare()
    {
        if (audioSrc != null && cawSound != null) audioSrc.Play();
        FlyOff();
    }

    void FlyOff()
    {
        state = State.FlyAway;
        flyTarget = GetOffscreenPoint();
        FaceFly(flyTarget - transform.position);
    }

    void DoFlyAway()
    {
        if (!MoveTowards(flyTarget)) return;

        // Улетела за карту — спрятаться и ждать следующего вылета
        state = State.Waiting;
        sr.enabled = false;
        waitTimer = Random.Range(spawnInterval.x, spawnInterval.y);
        PickFlyFrame(null, false);
    }

    // ---------- Утилиты ----------

    /// Полёт по прямой. true — долетели (в пределах 0.05м).
    bool MoveTowards(Vector3 target)
    {
        Vector3 delta = target - transform.position;
        float step = flySpeed * Time.deltaTime;
        if (delta.magnitude <= step || delta.magnitude < 0.05f)
        {
            transform.position = target;
            return true;
        }
        transform.position += delta.normalized * step;
        FaceFly(delta);
        return false;
    }

    /// Выбрать кадры полёта и повернуться по направлению движения
    void FaceFly(Vector3 dir)
    {
        bool goingRight = Mathf.Abs(dir.x) > 0.05f && dir.x > 0f;
        PickFlyFrame(data.fly, data.flyFacesLeft ? goingRight : !goingRight);
    }

    /// Поиск цели: зрелые растения в приоритете, иначе любое растущее
    CropTile PickCrop()
    {
        CropTile[] all = FindObjectsByType<CropTile>(FindObjectsSortMode.None);
        CropTile fallback = null;
        foreach (CropTile c in all)
        {
            if (c == null || c.cropData == null) continue;
            if (Scarecrow.IsProtected(c.transform.position)) continue; // под пугалом не клюёт
            if (c.isReady) return c;               // зрелая грядка — лучший улов
            if (fallback == null) fallback = c;    // любое растущее — запасной вариант
        }
        return fallback;
    }

    bool PlayerNear(float radius)
    {
        return player != null && Vector2.Distance(transform.position, player.position) <= radius;
    }

    void FindPlayer()
    {
        if (player == null)
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) player = go.transform;
        }
    }

    /// Точка за краем экрана (случайная сторона) относительно камеры
    Vector3 GetOffscreenPoint()
    {
        Camera cam = Camera.main;
        Vector3 center = cam != null ? cam.transform.position : Vector3.zero;
        float h = (cam != null ? cam.orthographicSize : 6f) + offscreenMargin;
        float w = h * (cam != null ? cam.aspect : 1.7f) + offscreenMargin;

        Vector3 p = center;
        switch (Random.Range(0, 4))
        {
            case 0: p += new Vector3(w, 0f, 0f); break;   // справа
            case 1: p += new Vector3(-w, 0f, 0f); break;  // слева
            case 2: p += new Vector3(0f, h, 0f); break;   // сверху
            default: p += new Vector3(w, h, 0f); break;   // правый верх
        }
        p.z = 0f;
        return p;
    }

    // ---------- Анимация (кодом, как EnemyAnimator) ----------

    void PlayFrames(Sprite[] frames)
    {
        if (!CrowData.Has(frames))
        {
            PickFlyFrame(null, false);
            return;
        }
        if (currentFrames != frames)
        {
            currentFrames = frames;
            frameIndex = 0;
            frameTimer = 0f;
            sr.sprite = frames[0];
        }
    }

    /// Полёт: отдельный цикл с flipX (кадры нарисованы влево).
    /// frames == null → очистка (ворона спрятана).
    void PickFlyFrame(Sprite[] frames, bool flip)
    {
        if (!CrowData.Has(frames))
        {
            currentFrames = null;
            return;
        }
        if (currentFrames != frames)
        {
            currentFrames = frames;
            frameIndex = 0;
            frameTimer = 0f;
            sr.sprite = frames[0];
        }
        sr.flipX = flip;
    }

    void Animate()
    {
        if (currentFrames == null || currentFrames.Length == 0) return;

        frameTimer += Time.deltaTime;
        float fps = data != null ? data.animationFPS : 8f;
        if (currentFrames == data.fly && data != null && data.flyFPS > 0f)
            fps = data.flyFPS;
        float frameTime = 1f / Mathf.Max(1f, fps);
        if (frameTimer < frameTime) return;
        frameTimer -= frameTime;

        frameIndex = (frameIndex + 1) % currentFrames.Length;
        sr.sprite = currentFrames[frameIndex];
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.3f, 0.5f);
        Vector3 c = Application.isPlaying ? (state == State.Waiting ? transform.position : cropPos) : transform.position;
        Gizmos.DrawWireSphere(c, scareRadius);
    }
}
