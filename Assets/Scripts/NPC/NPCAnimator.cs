using UnityEngine;

/// <summary>
/// Покадровая анимация NPC (Idle/Walk) — по образцу AnimalAnimator.
/// Драйвится из NPCController: PlayState(state, direction).
/// Боковые спрайты зеркалятся для «вправо». Незаполненные направления
/// откатываются к down — можно заполнить только часть.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class NPCAnimator : MonoBehaviour
{
    public enum AnimState { Idle, Walk, Work }
    public enum AnimDir { Up, Down, Left, Right }

    [System.Serializable]
    public class DirectionalFrames
    {
        public Sprite[] up;
        public Sprite[] down;
        public Sprite[] side; // лево и право (право = flipX)
    }

    [Header("Кадры анимаций")]
    public DirectionalFrames idle;
    public DirectionalFrames walk;
    public DirectionalFrames work; // ковка/работа (кузнец у станка)

    [Header("Ориентация боковых спрайтов")]
    [Tooltip("Если боковые нарисованы смотрящими влево — true (право зеркалим)")]
    public bool sideFacesLeft = true;

    [Header("Скорость анимации")]
    public float animationFPS = 8f;

    private SpriteRenderer sr;
    private Sprite[] currentFrames;
    private int frameIndex;
    private float frameTimer;

    private AnimState curState = AnimState.Idle;
    private AnimDir curDir = AnimDir.Down;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        PlayState(AnimState.Idle, AnimDir.Down, true);
    }

    public void PlayState(AnimState state, AnimDir dir, bool forceRestart = false)
    {
        if (!forceRestart && state == curState && dir == curDir) return;

        curState = state;
        curDir = dir;

        DirectionalFrames df = state switch
        {
            AnimState.Walk => walk,
            AnimState.Work => HasAny(work) ? work : idle,
            _ => idle,
        };
        if (!HasAny(df)) df = idle; // фолбэк на idle если пусто

        bool flipX = false;
        Sprite[] frames;
        switch (dir)
        {
            case AnimDir.Up: frames = df.up; break;
            case AnimDir.Down: frames = df.down; break;
            case AnimDir.Left: frames = df.side; flipX = !sideFacesLeft; break;
            default: frames = df.side; flipX = sideFacesLeft; break;
        }

        // Фолбэк по направлению
        if (frames == null || frames.Length == 0) frames = df.down;
        if (frames == null || frames.Length == 0) frames = df.side;
        if (frames == null || frames.Length == 0) frames = df.up;

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

    bool HasAny(DirectionalFrames df)
    {
        if (df == null) return false;
        return (df.up != null && df.up.Length > 0) ||
               (df.down != null && df.down.Length > 0) ||
               (df.side != null && df.side.Length > 0);
    }

    void Update()
    {
        if (currentFrames == null || currentFrames.Length <= 1) return;

        frameTimer += Time.deltaTime;
        if (frameTimer >= 1f / animationFPS)
        {
            frameTimer = 0f;
            frameIndex = (frameIndex + 1) % currentFrames.Length;
            sr.sprite = currentFrames[frameIndex];
        }
    }
}