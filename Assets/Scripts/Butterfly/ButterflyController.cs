using UnityEngine;

/// <summary>
/// Декоративная бабочка: патрульный полёт в радиусе вокруг точки спавна.
 /// Сам выбирает случайную точку, летит к ней, иногда зависает на месте.
 /// Анимация кодом (подмена спрайтов), без Animator-компонента — как у врагов/животных.
 /// Референсы не нужны: ButterflyData назначается в инспекторе префаба.
 /// Коллайдер не нужен — бабочка чисто декоративная.
 /// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class ButterflyController : MonoBehaviour
{
    [Header("Данные (Create → RPG → Butterfly)")]
    public ButterflyData data;

    [Header("Патрулирование")]
    [Tooltip("Радиус полёта вокруг точки спавна")]
    public float patrolRadius = 5f;
    public float flySpeed = 1.5f;
    [Range(0f, 1f)]
    [Tooltip("Шанс зависнуть на месте по прибытии в точку")]
    public float hoverChance = 0.5f;
    [Tooltip("Мин/макс время зависания, сек")]
    public Vector2 hoverTime = new Vector2(0.6f, 2f);

    [Header("Полёт")]
    [Tooltip("Амплитуда покачивания вверх-вниз")]
    public float bobAmplitude = 0.15f;
    [Tooltip("Скорость покачивания")]
    public float bobSpeed = 6f;

    [Header("Отрисовка")]
    [Tooltip("Sorting order — бабочка летает ПОВЕРХ мира")]
    public int sortingOrder = 50;

    private SpriteRenderer sr;
    private Vector3 home;
    private Vector3 target;
    private bool hovering;
    private float hoverTimer;
    private float bobPhase;
    private float speedMul;
    private float fpsMul;

    // Анимация
    private int frameIndex;
    private float frameTimer;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        home = transform.position;
        target = home;
        speedMul = Random.Range(0.8f, 1.25f);
        fpsMul = Random.Range(0.9f, 1.15f);
        bobPhase = Random.value * Mathf.PI * 2f;
    }

    void Start()
    {
        if (sr != null) sr.sortingOrder = sortingOrder;
        PickNewTarget();
    }

    void PickNewTarget()
    {
        Vector2 offset = Random.insideUnitCircle * patrolRadius;
        target = home + new Vector3(offset.x, offset.y, 0f);
        hovering = false;
    }

    void Update()
    {
        if (data == null || !data.HasFrames || sr == null) return;

        Animate();
        Move();
    }

    void Animate()
    {
        frameTimer += Time.deltaTime * fpsMul;
        float frameTime = 1f / Mathf.Max(1f, data.animationFPS);
        if (frameTimer >= frameTime)
        {
            frameTimer -= frameTime;
            frameIndex = (frameIndex + 1) % data.frames.Length;
            sr.sprite = data.frames[frameIndex];
        }
    }

    void Move()
    {
        bobPhase += Time.deltaTime * bobSpeed;
        float bob = Mathf.Sin(bobPhase) * bobAmplitude * Time.deltaTime;

        if (hovering)
        {
            hoverTimer -= Time.deltaTime;
            transform.position += new Vector3(0f, bob, 0f);
            if (hoverTimer <= 0f) PickNewTarget();
            return;
        }

        Vector3 delta = target - transform.position;
        float step = flySpeed * speedMul * Time.deltaTime;

        if (delta.magnitude <= step)
        {
            transform.position = target + new Vector3(0f, bob, 0f);
            if (Random.value < hoverChance)
            {
                hovering = true;
                hoverTimer = Random.Range(hoverTime.x, hoverTime.y);
            }
            else
            {
                PickNewTarget();
            }
        }
        else
        {
            Vector3 dir = delta.normalized;
            transform.position += dir * step + new Vector3(0f, bob, 0f);

            // Зеркало: спрайт нарисован летящим «вперёд», при полёте влево отзеркаливаем
            if (data.mirrorSprite)
                sr.flipX = dir.x < -0.05f;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.5f);
        Vector3 center = Application.isPlaying ? home : transform.position;
        Gizmos.DrawWireSphere(center, patrolRadius);
    }
}
