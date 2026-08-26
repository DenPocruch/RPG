using UnityEngine;

/// <summary>
/// Простой автономный тестер анимации для мобов. Не связан с боевой системой
/// или AI — только показывает как выглядит анимация. Кидаешь кадры по трём
/// направлениям (up/down/side — side зеркалится для право/лево), переключаешь
/// состояние (Idle/Walk/Attack/Death) через выпадающий список ПРЯМО ВО ВРЕМЯ
/// Play, чекбоксами отмечаешь какие анимации у тебя готовы, и решаешь —
/// двигается моб по карте или стоит на месте проигрывая анимацию.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class MobAnimationTester : MonoBehaviour
{
    public enum TestState { Idle, Walk, Attack, Death }
    public enum Dir { Up, Down, Left, Right }

    [System.Serializable]
    public class DirectionalFrames
    {
        public Sprite[] up;
        public Sprite[] down;
        public Sprite[] side; // одна анимация вбок — право/лево получаются зеркалированием
    }

    [Header("═══ ЧТО ТЕСТИРУЕМ СЕЙЧАС ═══")]
    [Tooltip("Меняй прямо во время Play чтобы переключать анимацию")]
    public TestState currentState = TestState.Idle;

    [Header("Какие анимации готовы (включить/выключить)")]
    public bool enableIdle = true;
    public bool enableWalk = true;
    public bool enableAttack = false;
    public bool enableDeath = false;

    [Header("═══ ТЕСТ АТАКИ ПО НАПРАВЛЕНИЯМ ═══")]
    [Tooltip("Меняй прямо в Inspector во время Play — атака сразу перезапустится в эту сторону. " +
             "Либо жми стрелки ↑↓←→ на клавиатуре — тоже сразу проиграют атаку в нужном направлении.")]
    public Dir attackDirection = Dir.Down;

    [Header("═══ КАДРЫ АНИМАЦИЙ ═══")]
    public DirectionalFrames idleFrames;
    public DirectionalFrames walkFrames;
    public DirectionalFrames attackFrames;
    public DirectionalFrames deathFrames;

    [Header("Ориентация бокового спрайта")]
    [Tooltip("Если боковой спрайт нарисован смотрящим ВЛЕВО — оставь true (право = отзеркалить)")]
    public bool sideFacesLeft = true;

    [Header("Скорость анимации (кадров в секунду)")]
    public float fps = 8f;

    [Header("═══ ДВИЖЕНИЕ ═══")]
    [Tooltip("Включено — моб реально ходит туда-сюда во время Walk. Выключено — стоит на месте и просто проигрывает анимацию.")]
    public bool moveAroundMap = true;
    public float moveSpeed = 1.5f;
    [Tooltip("На какое расстояние от старта ходить туда-сюда")]
    public float patrolRadius = 2f;

    public enum PatrolAxis { Horizontal, Vertical, Both }
    [Tooltip("Horizontal — влево/вправо. Vertical — вверх/вниз. Both — квадратом по всем 4 направлениям.")]
    public PatrolAxis patrolAxis = PatrolAxis.Horizontal;

    private SpriteRenderer sr;
    private Sprite[] currentFrames;
    private int frameIndex;
    private float frameTimer;

    private TestState playingState;
    private Dir facing = Dir.Down;
    private Dir lastAttackDirection = Dir.Down;
    private bool oneShotFinished; // для Attack/Death (не зациклены)

    private Vector3 startPos;
    private int patrolDir = 1; // 1 = вправо/вверх, -1 = влево/вниз

    // Для режима Both — движение квадратом: Right → Up → Left → Down → повтор
    private int patrolLeg = 0; // 0=право,1=верх,2=лево,3=низ

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    void Start()
    {
        startPos = transform.position;
        ApplyState(currentState, force: true);
    }

    void Update()
    {
        HandleAttackHotkeys();

        // Переключение состояния (можно менять currentState прямо в Inspector во время Play)
        if (currentState != playingState)
        {
            ApplyState(currentState, force: true);
        }
        else if (playingState == TestState.Attack && attackDirection != lastAttackDirection)
        {
            // Направление атаки сменили прямо в Inspector (не стрелками) — перезапускаем
            ApplyState(TestState.Attack, force: true);
        }

        HandleMovement();
        HandleFrameAdvance();
    }

    // Стрелки ↑↓←→ сразу проигрывают атаку в нужную сторону, даже если
    // уже атакуем в другом направлении (каждое нажатие — свежий запуск)
    void HandleAttackHotkeys()
    {
        if (!enableAttack) return;

        if (Input.GetKeyDown(KeyCode.UpArrow)) TriggerAttack(Dir.Up);
        else if (Input.GetKeyDown(KeyCode.DownArrow)) TriggerAttack(Dir.Down);
        else if (Input.GetKeyDown(KeyCode.LeftArrow)) TriggerAttack(Dir.Left);
        else if (Input.GetKeyDown(KeyCode.RightArrow)) TriggerAttack(Dir.Right);
    }

    void TriggerAttack(Dir dir)
    {
        attackDirection = dir;
        currentState = TestState.Attack;
        ApplyState(TestState.Attack, force: true);
    }

    // ═══════════════════════════════════════════════════════════
    // ПЕРЕКЛЮЧЕНИЕ СОСТОЯНИЯ
    // ═══════════════════════════════════════════════════════════
    void ApplyState(TestState state, bool force)
    {
        // Если состояние выключено чекбоксом — не даём его тестировать, откатываемся на Idle
        if (!IsEnabled(state))
        {
            Debug.LogWarning("[MobTester] " + state + " выключен чекбоксом — переключаю на Idle");
            state = TestState.Idle;
            currentState = TestState.Idle;
        }

        playingState = state;
        oneShotFinished = false;

        // Атака использует СВОЁ направление (attackDirection), независимое от
        // направления ходьбы/покоя — можно бить в любую сторону вне зависимости
        // от того куда моб "смотрел" до этого
        Dir playDir = state == TestState.Attack ? attackDirection : facing;
        if (state == TestState.Attack) lastAttackDirection = attackDirection;

        DirectionalFrames df = GetFramesFor(state);
        PlayFrames(df, playDir, force);
    }

    bool IsEnabled(TestState state) => state switch
    {
        TestState.Idle => enableIdle,
        TestState.Walk => enableWalk,
        TestState.Attack => enableAttack,
        TestState.Death => enableDeath,
        _ => true,
    };

    DirectionalFrames GetFramesFor(TestState state) => state switch
    {
        TestState.Walk => walkFrames,
        TestState.Attack => attackFrames,
        TestState.Death => deathFrames,
        _ => idleFrames,
    };

    bool IsLooping(TestState state) => state == TestState.Idle || state == TestState.Walk;

    // ═══════════════════════════════════════════════════════════
    // ДВИЖЕНИЕ (только в Walk, только если включено)
    // ═══════════════════════════════════════════════════════════
    void HandleMovement()
    {
        if (playingState != TestState.Walk || !moveAroundMap)
            return;

        switch (patrolAxis)
        {
            case PatrolAxis.Horizontal: MoveHorizontal(); break;
            case PatrolAxis.Vertical: MoveVertical(); break;
            case PatrolAxis.Both: MoveSquare(); break;
        }
    }

    void MoveHorizontal()
    {
        Vector3 pos = transform.position;
        float traveled = pos.x - startPos.x;

        if (traveled >= patrolRadius) patrolDir = -1;
        else if (traveled <= -patrolRadius) patrolDir = 1;

        transform.position += Vector3.right * patrolDir * moveSpeed * Time.deltaTime;

        SetFacing(patrolDir > 0 ? Dir.Right : Dir.Left);
    }

    void MoveVertical()
    {
        Vector3 pos = transform.position;
        float traveled = pos.y - startPos.y;

        if (traveled >= patrolRadius) patrolDir = -1;
        else if (traveled <= -patrolRadius) patrolDir = 1;

        transform.position += Vector3.up * patrolDir * moveSpeed * Time.deltaTime;

        SetFacing(patrolDir > 0 ? Dir.Up : Dir.Down);
    }

    // Ходит квадратом по всем 4 направлениям — удобно проверить сразу всё
    void MoveSquare()
    {
        Vector3 corner0 = startPos;                                              // низ-лево (старт)
        Vector3 corner1 = startPos + new Vector3(patrolRadius, 0, 0);            // низ-право
        Vector3 corner2 = startPos + new Vector3(patrolRadius, patrolRadius, 0); // верх-право
        Vector3 corner3 = startPos + new Vector3(0, patrolRadius, 0);            // верх-лево

        Vector3 target = patrolLeg switch
        {
            0 => corner1, // едем вправо
            1 => corner2, // едем вверх
            2 => corner3, // едем влево
            _ => corner0, // едем вниз
        };

        Vector3 pos = transform.position;
        Vector3 toTarget = target - pos;

        if (toTarget.magnitude <= 0.05f)
        {
            patrolLeg = (patrolLeg + 1) % 4;
            return;
        }

        Vector3 dir = toTarget.normalized;
        transform.position += dir * moveSpeed * Time.deltaTime;

        // Направление анимации по текущему участку пути
        Dir facingDir = patrolLeg switch
        {
            0 => Dir.Right,
            1 => Dir.Up,
            2 => Dir.Left,
            _ => Dir.Down,
        };
        SetFacing(facingDir);
    }

    void SetFacing(Dir newFacing)
    {
        if (newFacing == facing) return;
        facing = newFacing;
        PlayFrames(GetFramesFor(playingState), facing, true);
    }

    // ═══════════════════════════════════════════════════════════
    // ПОКАДРОВАЯ АНИМАЦИЯ
    // ═══════════════════════════════════════════════════════════
    void PlayFrames(DirectionalFrames df, Dir dir, bool force)
    {
        if (df == null) return;

        bool flipX = false;
        Sprite[] frames;
        switch (dir)
        {
            case Dir.Up: frames = df.up; break;
            case Dir.Down: frames = df.down; break;
            case Dir.Left: frames = df.side; flipX = !sideFacesLeft; break;
            default: frames = df.side; flipX = sideFacesLeft; break;
        }

        // Фолбэк если для направления нет кадров
        if (frames == null || frames.Length == 0) frames = df.down;
        if (frames == null || frames.Length == 0) frames = df.side;
        if (frames == null || frames.Length == 0) frames = df.up;

        if (!force && frames == currentFrames) return;

        currentFrames = frames;
        frameIndex = 0;
        frameTimer = 0f;

        if (sr != null)
        {
            sr.flipX = flipX;
            if (currentFrames != null && currentFrames.Length > 0)
                sr.sprite = currentFrames[0];
        }
    }

    void HandleFrameAdvance()
    {
        if (currentFrames == null || currentFrames.Length <= 1) return;
        if (oneShotFinished) return; // Attack/Death доиграли — держим последний кадр

        frameTimer += Time.deltaTime;
        if (frameTimer < 1f / Mathf.Max(1f, fps)) return;
        frameTimer = 0f;

        frameIndex++;

        if (frameIndex >= currentFrames.Length)
        {
            if (IsLooping(playingState))
            {
                frameIndex = 0;
            }
            else
            {
                // Одноразовая анимация (Attack/Death) доиграла
                frameIndex = currentFrames.Length - 1;
                oneShotFinished = true;

                if (playingState == TestState.Attack && enableIdle)
                {
                    // Атака доиграла — авто-возврат в Idle для удобства теста
                    currentState = TestState.Idle;
                }
                return;
            }
        }

        sr.sprite = currentFrames[frameIndex];
    }

    void OnDrawGizmosSelected()
    {
        if (!moveAroundMap) return;
        Vector3 basePos = Application.isPlaying ? startPos : transform.position;
        Gizmos.color = Color.cyan;

        switch (patrolAxis)
        {
            case PatrolAxis.Horizontal:
                Gizmos.DrawLine(basePos + Vector3.left * patrolRadius, basePos + Vector3.right * patrolRadius);
                break;
            case PatrolAxis.Vertical:
                Gizmos.DrawLine(basePos + Vector3.down * patrolRadius, basePos + Vector3.up * patrolRadius);
                break;
            case PatrolAxis.Both:
                Vector3 c0 = basePos;
                Vector3 c1 = basePos + new Vector3(patrolRadius, 0, 0);
                Vector3 c2 = basePos + new Vector3(patrolRadius, patrolRadius, 0);
                Vector3 c3 = basePos + new Vector3(0, patrolRadius, 0);
                Gizmos.DrawLine(c0, c1);
                Gizmos.DrawLine(c1, c2);
                Gizmos.DrawLine(c2, c3);
                Gizmos.DrawLine(c3, c0);
                break;
        }
    }
}