using UnityEngine;

/// <summary>
/// Покадровая анимация животного. Драйвится из AnimalController:
/// PlayState(state, direction). Циклит кадры и зеркалит бок для «вправо».
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class AnimalAnimator : MonoBehaviour
{
    public enum AnimState { Idle, Walk, Sit, Eat }
    public enum AnimDir { Up, Down, Left, Right }

    private SpriteRenderer sr;
    private AnimalData data;
    private bool isAdult = false;

    private Sprite[] currentFrames;
    private int frameIndex;
    private float frameTimer;
    private bool flipX;

    private AnimState curState = AnimState.Idle;
    private AnimDir curDir = AnimDir.Down;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    public void Init(AnimalData animalData, bool adult)
    {
        data = animalData;
        isAdult = adult;
        PlayState(AnimState.Idle, AnimDir.Down, true);
    }

    public void SetAdult(bool adult) => isAdult = adult;

    public void PlayState(AnimState state, AnimDir dir, bool forceRestart = false)
    {
        if (!forceRestart && state == curState && dir == curDir) return;

        curState = state;
        curDir = dir;

        var stage = isAdult ? data.adult : data.baby;

        // Ищем нужное состояние — если не заполнено, откатываемся на idle
        AnimalData.DirectionalFrames df = state switch
        {
            AnimState.Walk => HasFrames(stage.walk) ? stage.walk : stage.idle,
            AnimState.Sit => HasFrames(stage.sit) ? stage.sit : stage.idle,
            AnimState.Eat => HasFrames(stage.eat) ? stage.eat : stage.idle,
            _ => stage.idle,
        };

        // Финальный фолбэк — если даже idle пустой, берём что найдём
        if (df == null) df = stage.idle ?? stage.walk;
        if (df == null) return; // совсем ничего нет — ничего не делаем

        // Выбираем массив кадров по направлению + определяем зеркалирование
        flipX = false;
        Sprite[] frames;
        switch (dir)
        {
            case AnimDir.Up:
                frames = df.up;
                break;
            case AnimDir.Down:
                frames = df.down;
                break;
            case AnimDir.Left:
                frames = df.side;
                flipX = !data.sideFacesLeft;
                break;
            default: // Right
                frames = df.side;
                flipX = data.sideFacesLeft;
                break;
        }

        // Фолбэк по направлению — берём первое что есть: down → side → up
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

    // Проверяет что хотя бы одно направление в состоянии заполнено
    bool HasFrames(AnimalData.DirectionalFrames df)
    {
        if (df == null) return false;
        return (df.up != null && df.up.Length > 0) ||
               (df.down != null && df.down.Length > 0) ||
               (df.side != null && df.side.Length > 0);
    }

    void Update()
    {
        if (currentFrames == null || currentFrames.Length <= 1 || data == null) return;

        frameTimer += Time.deltaTime;
        if (frameTimer >= 1f / data.animationFPS)
        {
            frameTimer = 0f;
            frameIndex = (frameIndex + 1) % currentFrames.Length;
            sr.sprite = currentFrames[frameIndex];
        }
    }
}