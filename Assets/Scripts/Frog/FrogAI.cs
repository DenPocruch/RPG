using UnityEngine;

/// <summary>
/// ИИ жабы: гуляет у точки спавна → иногда сидит → иногда спит → иногда квакает.
/// Игрок подошёл близко — просыпается и УБЕГАЕТ, пока не оторвётся.
/// Движение через Rigidbody2D (коллайдер вешает юзер): жаба не проходит сквозь
/// стены и не застревает — луч вперёд (rb.Cast) + проверка «стоим на месте» →
/// выбирается новое направление. Анимация кодом, без Animator.
/// Референсы не нужны: FrogData назначается в инспекторе префаба.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Rigidbody2D))]
public class FrogAI : MonoBehaviour
{
    private enum State { Walk, Idle, Sleep, Croak, Flee }

    [Header("Данные (Create → RPG → Frog)")]
    public FrogData data;

    [Header("Гуляние")]
    [Tooltip("Радиус ходьбы вокруг точки спавна")]
    public float wanderRadius = 4f;

    [Header("Прыжки (синхронизация с анимацией)")]
    [Tooltip("Длина одного прыжка, метров (ходьба)")]
    public float hopDistance = 0.9f;
    [Tooltip("Пауза между прыжками при ходьбе, сек (мин/макс)")]
    public Vector2 hopRestTime = new Vector2(0.4f, 1f);
    [Tooltip("Множитель скорости прыжка: 1 = движение длится ровно длину walk-анимации")]
    public float hopSpeedMul = 1f;
    [Tooltip("Доля прыжка в начале (замах/присед) — движения нет, только анимация")]
    [Range(0f, 0.4f)] public float hopWindup = 0.15f;
    [Tooltip("Доля прыжка в конце (приземление) — движения нет, только анимация")]
    [Range(0f, 0.4f)] public float hopLanding = 0.15f;
    [Tooltip("Длина прыжка при бегстве, метров")]
    public float fleeHopDistance = 1.3f;
    [Tooltip("Пауза между прыжками при бегстве, сек (мин/макс)")]
    public Vector2 fleeRestTime = new Vector2(0.05f, 0.2f);

    [Header("Отдых / сон / кваканье")]
    [Tooltip("Шанс присесть после прогулки (остальное — снова гулять)")]
    [Range(0f, 1f)] public float idleChance = 0.4f;
    [Tooltip("Шанс уснуть (проверяется после сидения)")]
    [Range(0f, 1f)] public float sleepChance = 0.35f;
    [Tooltip("Шанс заквакать вместо сидения")]
    [Range(0f, 1f)] public float croakChance = 0.25f;
    [Tooltip("Длительность сидения, сек (мин/макс)")]
    public Vector2 idleTime = new Vector2(2f, 5f);
    [Tooltip("Длительность сна, сек (мин/макс)")]
    public Vector2 sleepTime = new Vector2(8f, 20f);
    [Tooltip("Длительность кваканья, сек (мин/макс)")]
    public Vector2 croakTime = new Vector2(2f, 4f);

    [Header("Боязнь игрока")]
    [Tooltip("Ближе этого расстояния жаба просыпается и убегает")]
    public float fleeRadius = 2.5f;
    [Tooltip("Оторвалась на это расстояние — успокаивается и возвращается к делам")]
    public float calmDistance = 6f;

    [Header("Звук (опционально)")]
    [Tooltip("Звук кваканья. Если пусто — молчит")]
    public AudioClip croakSound;
    [Range(0f, 1f)] public float croakVolume = 0.7f;

    private SpriteRenderer sr;
    private Rigidbody2D rb;
    private Transform player;
    private AudioSource audioSrc;

    private Vector3 home;
    private Vector3 target;
    private State state = State.Walk;
    private float stateTimer;

    // Прыжковая машина (Walk и Flee)
    private bool hopping;
    private float hopTimer;
    private float hopDuration;
    private float hopDist;      // длина текущего прыжка
    private Vector2 hopDir;     // направление текущего прыжка
    private float restTimer;
    private float nextRest;     // пауза после текущего прыжка

    // Анимация
    private Sprite[] currentFrames;
    private int frameIndex;
    private float frameTimer;
    private bool flipX;
    private Vector2 facing = new Vector2(0f, -1f); // последнее направление взгляда

    // Антизастревание
    private Vector3 lastCheckPos;
    private float stuckCheckTimer;
    [Header("Антизастревание")]
    [Tooltip("Раз в сколько секунд проверять, не застряла ли")]
    public float stuckCheckInterval = 0.5f;
    [Tooltip("Если за это время прошла меньше — считается застрявшей и меняет цель")]
    public float stuckDistance = 0.05f;
    [Tooltip("Дальность «луча вперёд» для обхода стен")]
    public float obstacleProbe = 0.6f;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        rb = GetComponent<Rigidbody2D>();

        // Жаба ходит сама, гравитация не нужна, вращение запрещаем — иначе застрянет кверху брюхом
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        home = transform.position;
        lastCheckPos = home;

        if (croakSound != null)
        {
            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false;
            audioSrc.spatialBlend = 0f;
            audioSrc.volume = croakVolume;
        }
    }

    void Start()
    {
        FindPlayer();
        PickWalkTarget();
    }

    void FindPlayer()
    {
        if (player == null)
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go != null) player = go.transform;
        }
    }

    void Update()
    {
        if (data == null) return;
        Animate();
    }

    void FixedUpdate()
    {
        if (data == null) return;
        FindPlayer(); // игрок persistent, но на всякий случай обновляем ссылку

        float distToPlayer = float.MaxValue;
        if (player != null)
            distToPlayer = Vector2.Distance(transform.position, player.position);

        // Испуг: игрок близко — мгновенно прерываем сон/сидение/кваканье и бежим
        if (distToPlayer <= fleeRadius)
        {
            if (state != State.Flee) EnterFlee();
        }
        else if (state == State.Flee && distToPlayer >= calmDistance && stateTimer <= 0f)
        {
            PickNextState(); // оторвались — успокоилась
            return;
        }

        switch (state)
        {
            case State.Walk: DoWalk(); break;
            case State.Flee: DoFlee(distToPlayer); break;
            case State.Idle: DoSit(); break;
            case State.Sleep: DoSit(); break;
            case State.Croak: DoSit(); break;
        }

        UnstuckCheck();
    }

    // ---------- Состояния ----------

    void EnterState(State s)
    {
        state = s;
        switch (s)
        {
            case State.Walk:
                PickWalkTarget();
                break;
            case State.Idle:
                stateTimer = Random.Range(idleTime.x, idleTime.y);
                break;
            case State.Sleep:
                stateTimer = Random.Range(sleepTime.x, sleepTime.y);
                break;
            case State.Croak:
                stateTimer = Random.Range(croakTime.x, croakTime.y);
                if (audioSrc != null && croakSound != null) audioSrc.Play();
                break;
        }
    }

    void EnterFlee()
    {
        state = State.Flee;
        stateTimer = Random.Range(1.5f, 2.5f); // минимум бежит, даже если игрок отступил
        restTimer = 0f; // первый прыжок бегства — сразу
    }

    void PickNextState()
    {
        // После гуляния/отдыха бросаем жребий, что делать дальше
        float r = Random.value;
        if (r < idleChance) EnterState(State.Idle);
        else if (r < idleChance + croakChance) EnterState(State.Croak);
        else EnterState(State.Walk);
    }

    void DoWalk()
    {
        if (hopping) { UpdateHop(); return; }

        // Пауза между прыжками
        rb.linearVelocity = Vector2.zero;
        restTimer -= Time.fixedDeltaTime;
        if (restTimer > 0f) return;

        // Дошла до цели — присесть / квакнуть / уснуть / снова гулять
        Vector2 delta = target - transform.position;
        float distToTarget = delta.magnitude;
        if (distToTarget <= 0.15f)
        {
            if (Random.value < idleChance)
            {
                float r = Random.value;
                float denom = Mathf.Max(0.01f, sleepChance + croakChance);
                if (r < sleepChance / denom) EnterState(State.Sleep);
                else if (r < (sleepChance + croakChance) / denom) EnterState(State.Croak);
                else EnterState(State.Idle);
            }
            else PickWalkTarget();
            return;
        }

        // Последний прыжок — ровно на цель (иначе перелетает и мелко прыгает обратно)
        StartHop(delta.normalized, Mathf.Min(hopDistance, distToTarget), hopRestTime);
    }

    void DoFlee(float distToPlayer)
    {
        stateTimer -= Time.fixedDeltaTime; // минимальное время бегства

        if (hopping) { UpdateHop(); return; }

        rb.linearVelocity = Vector2.zero;
        restTimer -= Time.fixedDeltaTime;
        if (restTimer > 0f) return;

        if (player == null) { EnterState(State.Walk); return; }

        // Прыжок прочь от игрока
        Vector2 dir = ((Vector2)(transform.position - player.position)).normalized;
        if (dir.sqrMagnitude < 0.001f) dir = Random.insideUnitCircle.normalized;
        StartHop(dir, fleeHopDistance, fleeRestTime);

        // Оторвалась на calmDistance и минимум отбежала — успокоилась
        if (distToPlayer >= calmDistance && stateTimer <= 0f)
        {
            rb.linearVelocity = Vector2.zero;
            PickNextState();
        }
    }

    // ---------- Прыжковая машина ----------

    /// Начать прыжок в направлении dir длиной dist; после — пауза rest
    void StartHop(Vector2 dir, float dist, Vector2 rest)
    {
        hopDir = FreeDirection(dir, obstacleProbe);
        FaceMovement(hopDir); // facing нужен ДО расчёта длительности (по нужному ряду)
        hopDist = dist;
        hopDuration = CalcHopDuration();
        hopTimer = 0f;
        hopping = true;
        nextRest = Random.Range(rest.x, rest.y);
    }

    /// Длительность прыжка = длина walk-анимации (движение кадр-в-кадр со спрайтами)
    float CalcHopDuration()
    {
        float dur = 0.4f;
        if (FrogData.Has(data.walk) && data.animationFPS > 0f)
        {
            Sprite[] f = ChooseRow(data.walk, out _);
            if (FrogData.Has(f)) dur = f.Length / data.animationFPS;
        }
        return Mathf.Max(0.12f, dur / Mathf.Max(0.1f, hopSpeedMul));
    }

    /// Движение во время прыжка; по завершении — стоп и пауза.
    /// Движение только в средней части прыжка: замах и приземление — стоим.
    void UpdateHop()
    {
        hopTimer += Time.fixedDeltaTime;
        if (hopTimer >= hopDuration)
        {
            rb.linearVelocity = Vector2.zero;
            hopping = false;
            restTimer = nextRest;
            return;
        }

        float t = hopTimer / hopDuration;
        if (t < hopWindup || t > 1f - hopLanding)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        float moveSpan = 1f - hopWindup - hopLanding; // доля прыжка, где жаба летит
        rb.linearVelocity = hopDir * (hopDist / (hopDuration * moveSpan));
    }

    void DoSit()
    {
        // Сидим / спим / квакаем — стоим на месте
        rb.linearVelocity = Vector2.zero;
        stateTimer -= Time.fixedDeltaTime;
        if (stateTimer <= 0f) PickNextState();
    }

    // ---------- Движение ----------

    void PickWalkTarget()
    {
        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        target = home + new Vector3(offset.x, offset.y, 0f);
    }

    /// Если по направлению стена — пробуем соседние направления (обход препятствия)
    Vector2 FreeDirection(Vector2 desired, float probeDist)
    {
        if (!Blocked(desired, probeDist)) return desired;

        // Перебираем отклонения ±35°, ±70°, ±105°, ±140° — первое свободное
        for (int i = 1; i <= 4; i++)
        {
            float a = 35f * i * Mathf.Deg2Rad;
            Vector2 left = Rotate(desired, a);
            if (!Blocked(left, probeDist)) return left;
            Vector2 right = Rotate(desired, -a);
            if (!Blocked(right, probeDist)) return right;
        }
        // Всё заблокировано — разворачиваемся
        Vector2 back = -desired;
        return back;
    }

    static Vector2 Rotate(Vector2 v, float rad)
    {
        float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    bool Blocked(Vector2 dir, float dist)
    {
        // rb.Cast игнорирует собственные коллайдеры жабы — ловит только стены
        RaycastHit2D[] hits = new RaycastHit2D[1];
        int count = rb.Cast(dir, default(ContactFilter2D), hits, dist);
        return count > 0;
    }

    /// Антизастревание: стоим на месте при попытке идти → новая цель
    void UnstuckCheck()
    {
        stuckCheckTimer += Time.fixedDeltaTime;
        if (stuckCheckTimer < stuckCheckInterval) return;
        stuckCheckTimer = 0f;

        bool wantsToMove = (state == State.Walk || state == State.Flee) && hopping;
        float moved = Vector3.Distance(transform.position, lastCheckPos);
        lastCheckPos = transform.position;

        if (wantsToMove && moved < stuckDistance && state == State.Walk)
        {
            PickWalkTarget(); // застряла — цель в другом месте
        }
    }

    // ---------- Ориентация / анимация ----------

    void FaceMovement(Vector2 dir)
    {
        // Запоминаем направление как есть — выбор кадров делает Animate:
        // |y| > |x| → вниз или вверх (по знаку), иначе бок
        facing = dir;
    }

    void Animate()
    {
        FrogData.FrogFrames df = state switch
        {
            // Idle-спрайтов нет: в прыжке играем walk-кадры, в паузе между
            // прыжками и в сидении показываем позу приземления (последний кадр walk).
            // Сон/кваканье играют только если кадры заполнены.
            State.Walk => hopping ? data.walk : null,
            State.Flee => hopping ? data.walk : null,
            State.Sleep => data.sleep,
            State.Croak => data.croak,
            _ => null,
        };
        if (!FrogData.Has(df))
        {
            currentFrames = null; // сброс: следующая анимация начнётся с кадра 0
            ShowRestPose();
            return;
        }

        Sprite[] frames = ChooseRow(df, out bool fx);
        if (!FrogData.Has(frames))
        {
            ShowRestPose();
            return;
        }
        flipX = fx;

        frameTimer += Time.deltaTime;
        float frameTime = 1f / Mathf.Max(1f, data.animationFPS);
        if (currentFrames != frames)
        {
            currentFrames = frames;
            frameIndex = 0;
            frameTimer = 0f;
            sr.flipX = flipX;
            sr.sprite = frames[0];
            return;
        }

        if (frameTimer < frameTime) return;
        frameTimer -= frameTime;
        frameIndex = (frameIndex + 1) % frames.Length;
        sr.flipX = flipX;
        sr.sprite = frames[frameIndex];
    }

    /// Выбор ряда кадров по направлению взгляда (+ нужен ли flipX)
    Sprite[] ChooseRow(FrogData.FrogFrames df, out bool fx)
    {
        fx = false;
        bool vertical = Mathf.Abs(facing.y) > Mathf.Abs(facing.x);
        Sprite[] frames;
        if (vertical)
        {
            frames = facing.y > 0f ? df.up : df.down;
            // Фолбэк: нет верха/низа → другой вертикальный ряд → бок
            if (!FrogData.Has(frames)) frames = facing.y > 0f ? df.down : df.up;
            if (!FrogData.Has(frames)) frames = df.sideRight;
        }
        else
        {
            frames = df.sideRight;
            if (!FrogData.Has(frames)) frames = df.down;
            // Боковые кадры: влево отзеркаливаем только если нарисованы вправо
            else fx = facing.x < 0f ? !data.sideFacesLeft : data.sideFacesLeft;
        }
        return frames;
    }

    /// Поза приземления: последний кадр walk-ряда текущего направления.
    /// Иначе после кваканья/сна спрайт замирает на случайном середине-кадре и «висит».
    void ShowRestPose()
    {
        if (!FrogData.Has(data.walk)) return;
        Sprite[] frames = ChooseRow(data.walk, out bool fx);
        if (!FrogData.Has(frames)) return;

        Sprite rest = frames[frames.Length - 1];
        if (sr.sprite != rest)
        {
            sr.flipX = fx;
            sr.sprite = rest;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.5f);
        Vector3 center = Application.isPlaying ? home : transform.position;
        Gizmos.DrawWireSphere(center, wanderRadius);
        Gizmos.color = new Color(1f, 0.4f, 0.3f, 0.5f);
        Gizmos.DrawWireSphere(center, fleeRadius);
    }
}
