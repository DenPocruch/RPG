using UnityEngine;

public enum EnemyAnimState { Idle, Walk, Damage, Dead, Attack }
public enum EnemyAnimDir { Up, Down, Left, Right }

/// <summary>
/// Код-аниматор врага — по образцу AnimalAnimator: подмена спрайтов по
/// состоянию (Idle/Walk/Damage/Dead) и направлению (Up/Down/Left/Right),
/// «право» = отзеркаленный боковой ряд. Без Animator-компонента.
/// Idle/Walk циклятся, Damage — один раз и возврат к циклу, Dead — один раз
/// и замирает. Компонент добавляет SimpleEnemyAI сам, референсы не нужны.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class EnemyAnimator : MonoBehaviour
{
    private SpriteRenderer sr;
    private EnemyData data;

    private Sprite[] currentFrames;
    private int frameIndex;
    private float frameTimer;
    private bool flipX;

    private EnemyAnimState curState = EnemyAnimState.Idle;
    private EnemyAnimDir curDir = EnemyAnimDir.Down;

    // Куда вернуться после one-shot (Damage)
    private EnemyAnimState lastLoopState = EnemyAnimState.Idle;
    private EnemyAnimDir lastLoopDir = EnemyAnimDir.Down;

    private bool oneShotPlaying;

    public EnemyAnimState CurrentState => curState;
    public EnemyAnimDir CurrentDir => curDir;
    public bool IsOneShotPlaying => oneShotPlaying;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    public void Init(EnemyData enemyData)
    {
        data = enemyData;
        PlayState(EnemyAnimState.Idle, EnemyAnimDir.Down, true);
    }

    public void PlayState(EnemyAnimState state, EnemyAnimDir dir, bool forceRestart = false)
    {
        if (data == null) return;

        if (!forceRestart)
        {
            if (oneShotPlaying) return; // Damage/Dead не перебиваем
            if (state == curState && dir == curDir) return;
        }

        bool isLoop = state == EnemyAnimState.Idle || state == EnemyAnimState.Walk;
        if (isLoop)
        {
            lastLoopState = state;
            lastLoopDir = dir;
        }
        oneShotPlaying = !isLoop;

        // Фолбэк по состоянию: нет walk/damage/dead → откатываемся на то, что есть
        EnemyData.DirectionalFrames df = state switch
        {
            EnemyAnimState.Walk => EnemyData.Has(data.walk) ? data.walk : data.idle,
            EnemyAnimState.Attack => FirstNonEmpty(data.attack, data.damage, data.walk, data.idle),
            EnemyAnimState.Damage => FirstNonEmpty(data.damage, data.walk, data.idle),
            EnemyAnimState.Dead => FirstNonEmpty(data.dead, data.damage, data.walk, data.idle),
            _ => data.idle,
        };
        if (!EnemyData.Has(df)) return; // спрайтов нет вообще

        // Выбор кадров по направлению + зеркалирование (как в AnimalAnimator)
        flipX = false;
        Sprite[] frames;
        switch (dir)
        {
            case EnemyAnimDir.Up:
                frames = df.up;
                break;
            case EnemyAnimDir.Down:
                frames = df.down;
                break;
            case EnemyAnimDir.Left:
                frames = df.side;
                flipX = !data.sideFacesLeft;
                break;
            default: // Right
                if (EnemyData.Has(df.sideRight))
                {
                    frames = df.sideRight; // есть отдельно нарисованные «вправо» — зеркалить не надо
                    flipX = false;
                }
                else
                {
                    frames = df.side;
                    flipX = data.sideFacesLeft;
                }
                break;
        }

        // Фолбэк по направлению: down → side → up
        if (!EnemyData.Has(frames)) frames = df.down;
        if (!EnemyData.Has(frames)) frames = df.side;
        if (!EnemyData.Has(frames)) frames = df.up;
        if (!EnemyData.Has(frames)) return;

        curState = state;
        curDir = dir;
        currentFrames = frames;
        frameIndex = 0;
        frameTimer = 0f;

        sr.flipX = flipX;
        sr.sprite = frames[0];
    }

    static EnemyData.DirectionalFrames FirstNonEmpty(params EnemyData.DirectionalFrames[] list)
    {
        foreach (var df in list)
            if (EnemyData.Has(df)) return df;
        return null;
    }

    void Update()
    {
        if (currentFrames == null || currentFrames.Length == 0) return;

        frameTimer += Time.deltaTime;
        float frameTime = 1f / Mathf.Max(1f, data != null ? data.animationFPS : 8f);
        if (frameTimer < frameTime) return;
        frameTimer -= frameTime;

        frameIndex++;

        if (frameIndex >= currentFrames.Length)
        {
            if (curState == EnemyAnimState.Dead)
            {
                frameIndex = currentFrames.Length - 1; // замираем на последнем кадре
            }
            else if (curState == EnemyAnimState.Damage || curState == EnemyAnimState.Attack)
            {
                PlayState(lastLoopState, lastLoopDir, true); // урон/атака закончились — назад к циклу
                return;
            }
            else
            {
                frameIndex = 0;
            }
        }

        sr.sprite = currentFrames[frameIndex];
    }
}
