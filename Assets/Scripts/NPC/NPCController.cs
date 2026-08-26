using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// NPC на ОБЩЕЙ сети дорог (Waypoint) с поиском пути A*.
/// Статичные препятствия (стены/заборы) обходятся самой сеткой — ты
/// прокладываешь дороги по проходимым местам. Райкастом/расталкиванием
/// обходятся только ЖИВЫЕ агенты (другие NPC, игрок) — плавно, без дрожания.
/// Если NPC сбился с пути (застрял, оттолкнули) — идёт к ближайшей точке
/// сети и строит маршрут заново.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(NPCAnimator))]
public class NPCController : MonoBehaviour
{
    public enum PatrolMode { Ordered, Random }

    [Header("Сеть точек")]
    public Waypoint[] route;
    public Waypoint homeWaypoint;

    [Header("Режим патруля")]
    public PatrolMode patrolMode = PatrolMode.Random;

    [Header("Движение")]
    public float moveSpeed = 1.5f;
    public float arriveDistance = 0.15f;

    [Header("Ожидание в точке (сек)")]
    public float minWaitTime = 2f;
    public float maxWaitTime = 5f;

    [Header("Случайное блуждание рядом")]
    [Range(0f, 1f)]
    public float wanderChance = 0.3f;
    public float wanderRadius = 1.5f;

    [Header("Расталкивание живых агентов (NPC, игрок)")]
    [Tooltip("Слои живых агентов которых нужно плавно обходить (Player + NPC)")]
    public LayerMask agentMask;
    [Tooltip("Радиус в котором чувствует других агентов")]
    public float avoidRadius = 1.0f;
    [Tooltip("Сила расталкивания")]
    public float avoidWeight = 1.3f;
    [Tooltip("Смещение вправо при встрече лоб-в-лоб (чтобы разъезжались)")]
    public float passRightBias = 0.35f;

    [Header("Восстановление пути")]
    [Tooltip("Если прогресса к точке нет столько секунд — перестроить маршрут")]
    public float repathTime = 2f;

    [Header("Застревание (крайний случай)")]
    public float stuckTimeout = 10f;

    [HideInInspector] public bool manualControl = false;
    [HideInInspector] public bool aiPaused = false;
    // Когда true — контроллер НЕ трогает аниматор (внешний компонент рулит,
    // например BlacksmithNPC проигрывает анимацию ковки)
    [HideInInspector] public bool externalAnimation = false;

    private Rigidbody2D rb;
    private NPCAnimator anim;
    private Waypoint[] allWaypoints;

    private enum State { Move, Wait, Wander }
    private State state = State.Wait;

    private List<Waypoint> currentPath;
    private int pathIndex;
    private Waypoint currentWaypoint;
    private Waypoint finalGoal;
    private int orderedIndex = 0;
    private Vector2 wanderTarget;
    private float stateTimer;
    private float stuckTimer;

    // Отслеживание прогресса для перестроения маршрута
    private float bestDistToTarget;
    private float noProgressTimer;

    // Кэш ближайших агентов (обновляем не каждый кадр)
    private float avoidRefreshTimer;
    private Vector2 cachedAvoid;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<NPCAnimator>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    void Start()
    {
        allWaypoints = FindObjectsByType<Waypoint>(FindObjectsSortMode.None);
        currentWaypoint = Waypoint.FindNearest(transform.position, allWaypoints);
        EnterWait();
    }

    void Update()
    {
        if (aiPaused)
        {
            rb.linearVelocity = Vector2.zero;
            if (!externalAnimation)
                anim.PlayState(NPCAnimator.AnimState.Idle, GetFacing());
            return;
        }

        switch (state)
        {
            case State.Wait: TickWait(); break;
            case State.Move: TickFollowPath(); break;
            case State.Wander: TickWander(); break;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ОЖИДАНИЕ
    // ═══════════════════════════════════════════════════════════
    void TickWait()
    {
        rb.linearVelocity = Vector2.zero;
        if (!externalAnimation)
            anim.PlayState(NPCAnimator.AnimState.Idle, GetFacing());

        if (manualControl) return;

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
            PickNextAction();
    }

    void PickNextAction()
    {
        if (route == null || route.Length == 0) { EnterWait(); return; }

        if (patrolMode == PatrolMode.Ordered)
        {
            // Обход маршрута ПО ОЧЕРЕДИ: перебираем точки пока не найдём достижимую.
            // Никакого случайного блуждания — иначе NPC "топчется" и не доходит.
            for (int i = 0; i < route.Length; i++)
            {
                orderedIndex = (orderedIndex + 1) % route.Length;
                Waypoint w = route[orderedIndex];
                if (w != null && w != currentWaypoint && GoTo(w))
                    return;
            }
            EnterWait(); // ни одна точка не достижима — подождём
        }
        else
        {
            // Случайно, но чаще к ДАЛЬНИМ (берём дальнюю из двух случайных) —
            // чтобы не топтался у ближних точек
            if (Random.value < wanderChance) { EnterWander(); return; }

            Waypoint w = PickFarRandom();
            if (w == null || !GoTo(w)) EnterWait();
        }
    }

    Waypoint PickFarRandom()
    {
        if (route.Length == 1) return route[0];
        Waypoint a = route[Random.Range(0, route.Length)];
        Waypoint b = route[Random.Range(0, route.Length)];
        if (a == null) return b;
        if (b == null) return a;
        float da = Vector2.Distance(transform.position, a.Position);
        float db = Vector2.Distance(transform.position, b.Position);
        return da > db ? a : b; // берём ту что дальше
    }

    // ═══════════════════════════════════════════════════════════
    // ПУБЛИЧНЫЕ КОМАНДЫ
    // ═══════════════════════════════════════════════════════════
    public bool GoTo(Waypoint goal)
    {
        if (goal == null) { return false; }
        if (currentWaypoint == null)
            currentWaypoint = Waypoint.FindNearest(transform.position, allWaypoints);

        finalGoal = goal;
        currentPath = Waypoint.FindPath(currentWaypoint, goal);
        if (currentPath == null || currentPath.Count == 0) { return false; }

        pathIndex = 0;
        state = State.Move;
        stuckTimer = stuckTimeout;
        ResetProgress();
        return true;
    }

    public void ReturnHome() => GoTo(homeWaypoint);
    public bool IsAtWaypoint(Waypoint w) => currentWaypoint == w && state == State.Wait;
    public bool IsMoving() => state == State.Move || state == State.Wander;

    public void TeleportTo(Vector2 pos, Waypoint atWaypoint)
    {
        transform.position = pos;
        rb.linearVelocity = Vector2.zero;
        currentWaypoint = atWaypoint;
        currentPath = null;
        EnterWait();
    }

    // ═══════════════════════════════════════════════════════════
    // ДВИЖЕНИЕ ПО ПУТИ
    // ═══════════════════════════════════════════════════════════
    void TickFollowPath()
    {
        if (currentPath == null || pathIndex >= currentPath.Count) { EnterWait(); return; }

        Waypoint node = currentPath[pathIndex];
        if (node == null) { pathIndex++; return; }

        Vector2 pos = transform.position;
        Vector2 toTarget = node.Position - pos;
        float dist = toTarget.magnitude;

        if (dist <= arriveDistance)
        {
            currentWaypoint = node;
            pathIndex++;
            stuckTimer = stuckTimeout;
            ResetProgress();
            if (pathIndex >= currentPath.Count) EnterWait();
            return;
        }

        MoveTowards(toTarget.normalized, pos);

        // Прогресс к текущей точке
        if (dist < bestDistToTarget - 0.02f)
        {
            bestDistToTarget = dist;
            noProgressTimer = 0f;
        }
        else
        {
            noProgressTimer += Time.deltaTime;
            if (noProgressTimer >= repathTime)
            {
                Repath(); // застрял/оттолкнули → на ближайшую точку и заново
                return;
            }
        }

        stuckTimer -= Time.deltaTime;
        if (stuckTimer <= 0f) Repath();
    }

    void TickWander()
    {
        Vector2 pos = transform.position;
        Vector2 toTarget = wanderTarget - pos;
        if (toTarget.magnitude <= arriveDistance) { EnterWait(); return; }

        MoveTowards(toTarget.normalized, pos, 0.7f);

        stuckTimer -= Time.deltaTime;
        if (stuckTimer <= 0f) EnterWait();
    }

    // ═══════════════════════════════════════════════════════════
    // ПЛАВНОЕ РАСТАЛКИВАНИЕ ЖИВЫХ АГЕНТОВ (без дрожания)
    // ═══════════════════════════════════════════════════════════
    void MoveTowards(Vector2 desiredDir, Vector2 pos, float speedMul = 1f)
    {
        Vector2 avoid = GetAvoidance(pos, desiredDir);

        Vector2 moveDir = (desiredDir + avoid * avoidWeight);
        if (moveDir.sqrMagnitude < 0.0001f) moveDir = desiredDir;
        moveDir.Normalize();

        rb.linearVelocity = moveDir * moveSpeed * speedMul;
        anim.PlayState(NPCAnimator.AnimState.Walk, DirToAnim(moveDir));
    }

    Vector2 GetAvoidance(Vector2 pos, Vector2 desiredDir)
    {
        // Обновляем не каждый кадр — стабильнее и дешевле
        avoidRefreshTimer -= Time.deltaTime;
        if (avoidRefreshTimer > 0f) return cachedAvoid;
        avoidRefreshTimer = 0.1f;

        Vector2 avoid = Vector2.zero;
        bool someoneAhead = false;

        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, avoidRadius, agentMask);
        foreach (Collider2D hit in hits)
        {
            if (hit.gameObject == gameObject) continue; // не себя

            Vector2 away = pos - (Vector2)hit.transform.position;
            float d = away.magnitude;
            if (d < 0.01f) { away = Random.insideUnitCircle.normalized; d = 0.01f; }

            // Сильнее отталкивает когда ближе
            float strength = 1f - Mathf.Clamp01(d / avoidRadius);
            avoid += away.normalized * strength;

            // Агент примерно на пути? → расходимся правым боком
            if (Vector2.Dot(((Vector2)hit.transform.position - pos).normalized, desiredDir) > 0.3f)
                someoneAhead = true;
        }

        // Правило «расходиться правым боком» — ломает симметрию лоб-в-лоб
        if (someoneAhead)
        {
            Vector2 right = new Vector2(desiredDir.y, -desiredDir.x); // поворот на -90°
            avoid += right * passRightBias;
        }

        cachedAvoid = avoid;
        return avoid;
    }

    // ═══════════════════════════════════════════════════════════
    // ВОССТАНОВЛЕНИЕ МАРШРУТА
    // ═══════════════════════════════════════════════════════════
    void Repath()
    {
        // Идём к ближайшей точке сети, оттуда — заново к цели
        Waypoint nearest = Waypoint.FindNearest(transform.position, allWaypoints);
        currentWaypoint = nearest;

        if (finalGoal != null)
        {
            currentPath = Waypoint.FindPath(nearest, finalGoal);
            if (currentPath != null && currentPath.Count > 0)
            {
                pathIndex = 0;
                state = State.Move;
                stuckTimer = stuckTimeout;
                ResetProgress();
                return;
            }
        }
        EnterWait(); // не вышло — отдохнём, потом попробуем снова
    }

    void ResetProgress()
    {
        bestDistToTarget = float.MaxValue;
        noProgressTimer = 0f;
    }

    // ═══════════════════════════════════════════════════════════
    void EnterWander()
    {
        if (manualControl) { EnterWait(); return; }
        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        wanderTarget = (Vector2)transform.position + offset;
        state = State.Wander;
        stuckTimer = stuckTimeout;
    }

    void EnterWait()
    {
        state = State.Wait;
        stateTimer = Random.Range(minWaitTime, maxWaitTime);
        rb.linearVelocity = Vector2.zero;
    }

    NPCAnimator.AnimDir DirToAnim(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return NPCAnimator.AnimDir.Down;
        const float horizontalDeadzone = 0.30f;
        if (Mathf.Abs(dir.x) > horizontalDeadzone)
            return dir.x > 0 ? NPCAnimator.AnimDir.Right : NPCAnimator.AnimDir.Left;
        return dir.y > 0 ? NPCAnimator.AnimDir.Up : NPCAnimator.AnimDir.Down;
    }

    NPCAnimator.AnimDir GetFacing()
    {
        if (rb.linearVelocity.sqrMagnitude < 0.0001f) return NPCAnimator.AnimDir.Down;
        return DirToAnim(rb.linearVelocity);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, avoidRadius);
    }
}