using UnityEngine;

/// <summary>
/// Универсальная покадровая анимация для декоративных объектов
/// (факелы, вода, флаги, костры, мельницы и т.д.).
/// Повесь на объект со SpriteRenderer, закинь кадры — оно циклит.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteAnimator : MonoBehaviour
{
    [Header("Кадры анимации (по порядку)")]
    public Sprite[] frames;

    [Header("Скорость")]
    [Tooltip("Кадров в секунду")]
    public float framesPerSecond = 8f;

    [Header("Проигрывание")]
    [Tooltip("Зациклить (обычная петля). Выкл — проиграть один раз.")]
    public bool loop = true;
    [Tooltip("Туда-обратно (пинг-понг): 1→2→3→2→1... Полезно для качания/дыхания.")]
    public bool pingPong = false;
    [Tooltip("Играть автоматически при старте")]
    public bool playOnStart = true;

    [Header("Разнобой (чтобы одинаковые объекты не мигали синхронно)")]
    [Tooltip("Случайный сдвиг старта — факелы/вода не будут дёргаться в такт")]
    public bool randomStartOffset = true;

    private SpriteRenderer sr;
    private int frameIndex;
    private float timer;
    private int direction = 1; // для пинг-понга
    private bool playing;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    void Start()
    {
        if (frames != null && frames.Length > 0)
        {
            // Случайный стартовый кадр — чтобы клоны не синхронились
            frameIndex = randomStartOffset ? Random.Range(0, frames.Length) : 0;
            sr.sprite = frames[frameIndex];

            if (randomStartOffset)
                timer = Random.Range(0f, 1f / Mathf.Max(1f, framesPerSecond));
        }

        playing = playOnStart;
    }

    void Update()
    {
        if (!playing || frames == null || frames.Length <= 1) return;

        timer += Time.deltaTime;
        float frameTime = 1f / Mathf.Max(1f, framesPerSecond);

        if (timer < frameTime) return;
        timer -= frameTime;

        Advance();
    }

    void Advance()
    {
        if (pingPong)
        {
            frameIndex += direction;
            if (frameIndex >= frames.Length - 1) { frameIndex = frames.Length - 1; direction = -1; }
            else if (frameIndex <= 0) { frameIndex = 0; direction = 1; }
        }
        else
        {
            frameIndex++;
            if (frameIndex >= frames.Length)
            {
                if (loop) frameIndex = 0;
                else { frameIndex = frames.Length - 1; playing = false; return; }
            }
        }

        sr.sprite = frames[frameIndex];
    }

    // ── Управление из кода (опционально) ───────────────────────
    public void Play() => playing = true;
    public void Stop() => playing = false;
    public void Restart()
    {
        frameIndex = 0;
        direction = 1;
        timer = 0f;
        playing = true;
        if (frames != null && frames.Length > 0) sr.sprite = frames[0];
    }
}