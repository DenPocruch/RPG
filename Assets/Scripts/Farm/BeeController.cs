using UnityEngine;

/// <summary>
/// Пчела улья: вылетает, летает по случайным точкам вокруг улья заданное время,
/// затем возвращается в улей (улей получает +1 мёд). Анимация кодом (4 кадра),
/// спрайт нарисован летящим ВПРАВО — при полёте влево flipX.
/// Создаётся кодом (Beehive.SpawnBee), референсы не нужны.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class BeeController : MonoBehaviour
{
    enum State { Flying, Returning, StayHome }

    Beehive hive;
    Sprite[] frames;
    SpriteRenderer sr;

    Vector3 home;
    Vector3 target;
    State state = State.Flying;

    [Header("Полёт")]
    public float flySpeed = 2.2f;
    public float bobAmplitude = 0.1f;
    public float bobSpeed = 9f;

    // Рейс
    float tripTimer;
    float tripDuration;
    float wanderRadius;

    /// <summary>Сколько секунд осталось до возврата в улей (для сейва).</summary>
    public float RemainingTrip => Mathf.Max(0.5f, tripDuration - tripTimer);

    // Анимация
    int frameIndex;
    float frameTimer;
    float fpsMul;
    float bobPhase;

    public void Init(Beehive hive, Sprite[] frames, float tripDuration, float wanderRadius, float remainingTrip = -1f)
    {
        this.hive = hive;
        this.frames = frames;
        // remainingTrip >= 0 — восстановление из сейва: долетает остаток рейса
        this.tripDuration = remainingTrip > 0f ? remainingTrip : Mathf.Max(5f, tripDuration);
        this.wanderRadius = Mathf.Max(2f, wanderRadius);
        home = hive.transform.position;
        sr = GetComponent<SpriteRenderer>();
        fpsMul = Random.Range(0.9f, 1.15f);
        bobPhase = Random.value * Mathf.PI * 2f;
        PickNewTarget();
    }

    void PickNewTarget()
    {
        Vector2 off = Random.insideUnitCircle * wanderRadius;
        target = home + new Vector3(off.x, off.y, 0f);
    }

    /// <summary>Улей полон: вернуться и сидеть внутри (объект уничтожится).</summary>
    public void GoHomeAndStay()
    {
        state = State.StayHome;
        target = home;
    }

    void Update()
    {
        if (frames == null || frames.Length == 0 || sr == null) return;
        Animate();
        Move();
    }

    /// <summary>Пчела сортируется по Y как весь мир (YSort): не залетает ПОД улей
    /// и другие объекты — на равных Y рисуется поверх (+1).</summary>
    void LateUpdate()
    {
        if (sr != null) sr.sortingOrder = YSort.GetOrder(transform.position, 1);
    }

    void Animate()
    {
        frameTimer += Time.deltaTime * fpsMul;
        float frameTime = 1f / 10f; // 10 FPS — быстрое жужжащее крыло
        if (frameTimer >= frameTime)
        {
            frameTimer -= frameTime;
            frameIndex = (frameIndex + 1) % frames.Length;
            sr.sprite = frames[frameIndex];
        }
    }

    void Move()
    {
        bobPhase += Time.deltaTime * bobSpeed;
        float bob = Mathf.Sin(bobPhase) * bobAmplitude * Time.deltaTime;

        if (state == State.Flying)
        {
            tripTimer += Time.deltaTime;
            if (tripTimer >= tripDuration)
            {
                tripTimer = 0f;
                state = State.Returning;
                target = home;
            }
        }

        Vector3 delta = target - transform.position;
        // Скорость возврата чуть выше — пчела «спешит домой»
        float speed = flySpeed * (state == State.Flying ? 1f : 1.4f);
        float step = speed * Time.deltaTime;

        if (delta.magnitude <= step)
        {
            transform.position = target + new Vector3(0f, bob, 0f);
            Arrived();
        }
        else
        {
            Vector3 dir = delta.normalized;
            transform.position += dir * step + new Vector3(0f, bob, 0f);
            sr.flipX = dir.x < -0.05f; // спрайт летит вправо
        }
    }

    void Arrived()
    {
        if (state == State.Returning || state == State.StayHome)
        {
            // Вернулась в улей: +1 мёд (StayHome = улей уже полон, просто исчезаем)
            hive?.NotifyBeeReturned(this);
            return;
        }

        // Прилетела в случайную точку — зависла на мгновение и дальше
        if (Random.value < 0.3f)
        {
            // короткая пауза: просто выбираем новую точку рядом
            target = transform.position + new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.3f, 0.3f), 0f);
        }
        else PickNewTarget();
    }

    void OnDrawGizmosSelected()
    {
        if (hive == null) return;
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(Application.isPlaying ? home : hive.transform.position, wanderRadius);
    }
}
