using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class CropSway : MonoBehaviour
{
    [Header("Обнаружение игрока")]
    public float detectRadius = 0.8f;

    [Header("Маятник")]
    public float pushStrength = 120f;  // сила толчка от игрока
    public float maxAngle = 9f;        // максимальный наклон, градусы
    public float stiffness = 55f;      // жёсткость пружины (возврат)
    public float damping = 4.5f;       // затухание

    private SpriteRenderer sr;
    private Transform player;
    private Rigidbody2D playerRb;
    private float sway;         // текущий угол (градусы)
    private float swayVel;      // угловая скорость
    private float lastApplied;  // угол, реально применённый к transform

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    Transform GetPlayer()
    {
        if (player == null)
        {
            GameObject go = GameObject.FindGameObjectWithTag("Player");
            if (go != null)
            {
                player = go.transform;
                playerRb = go.GetComponent<Rigidbody2D>();
            }
        }
        return player;
    }

    void LateUpdate()
    {
        if (sr == null || sr.sprite == null) return;

        float dt = Time.deltaTime;

        Transform p = GetPlayer();
        if (p != null)
        {
            Vector2 diff = (Vector2)transform.position - (Vector2)p.position;
            if (diff.sqrMagnitude <= detectRadius * detectRadius)
            {
                // Куда клонить: от игрока в сторону. Если игрок ровно на растении — по его движению
                float dir = diff.x;
                if (Mathf.Abs(dir) < 0.05f && playerRb != null)
                    dir = playerRb.velocity.x;
                if (Mathf.Abs(dir) > 0.01f)
                    swayVel += Mathf.Sign(dir) * pushStrength * dt;
            }
        }

        // Пружина с затуханием — качание и возврат в вертикаль
        swayVel += (-stiffness * sway - damping * swayVel) * dt;
        sway += swayVel * dt;
        sway = Mathf.Clamp(sway, -maxAngle, maxAngle);

        float delta = sway - lastApplied;
        if (Mathf.Abs(delta) > 0.01f)
        {
            // Вращаем вокруг основания спрайта (низ), а не центра.
            // transform.up — чтобы точка опоры оставалась на месте при наклоне
            float halfHeight = sr.sprite.bounds.extents.y * transform.lossyScale.y;
            Vector3 pivot = transform.position - transform.up * halfHeight;
            transform.RotateAround(pivot, Vector3.forward, delta);
            lastApplied = sway;
        }
    }
}
