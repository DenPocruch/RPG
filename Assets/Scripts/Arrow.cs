using UnityEngine;

public class Arrow : MonoBehaviour
{
    private float damage;
    private float speed;
    private float range;
    private Vector2 direction;
    private Vector3 startPosition;
    private bool hasHit = false;
    private bool fromEnemy = false;
    private float attackerAccuracy = 0f;
    private float attackerPenetration = 0f;

    public void Init(Vector2 dir, float dmg, float spd, float rng, float accuracy = 0f, float penetration = 0f)
    {
        direction = dir;
        damage = dmg;
        speed = spd;
        range = rng;
        attackerAccuracy = accuracy;
        attackerPenetration = penetration;
        startPosition = transform.position;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
    }

    // Стрела врага: игнорирует врагов, бьёт игрока
    public void InitEnemy(Vector2 dir, float dmg, float spd, float rng, float accuracy = 0f, float penetration = 0f)
    {
        fromEnemy = true;
        Init(dir, dmg, spd, rng, accuracy, penetration);
    }

    void Update()
    {
        if (hasHit) return;

        transform.position += (Vector3)direction * speed * Time.deltaTime;

        float distanceTraveled = Vector3.Distance(startPosition, transform.position);
        if (distanceTraveled >= range)
            Destroy(gameObject);
    }

    void OnTriggerEnter2D(Collider2D col)
    {
        if (hasHit) return;

        if (fromEnemy)
        {
            // Пропускаем ВСЁ, что принадлежит врагам (тег Enemy + их дочерние
            // хитбоксы без тега, напр. Hurtbox) — иначе стрела умирает прямо
            // в коллайдере стрелявшего гоблина
            if (col.CompareTag("Enemy") || col.GetComponentInParent<EnemyHealth>() != null) return;
            hasHit = true;
            PlayerHealth ph = col.GetComponentInParent<PlayerHealth>();
            if (ph != null)
                ph.TakeDamage(damage, attackerAccuracy, attackerPenetration);
            Destroy(gameObject);
            return;
        }

        if (col.CompareTag("Player")) return;
        if (col.GetComponentInParent<PlayerMovement>() != null) return;

        if (col.CompareTag("Enemy"))
        {
            hasHit = true;
            EnemyHealth enemy = col.GetComponent<EnemyHealth>();
            if (enemy == null) enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy != null)
            {
                // ���������� PlayerStats ��� �����
                if (PlayerStats.Instance != null && HotbarManager.Instance != null)
                {
                    ItemData bow = HotbarManager.Instance.GetActiveItem();
                    PlayerStats.DamageResult result = PlayerStats.Instance.CalculateDamage(bow);
                    enemy.TakeDamage(result.damage, result.isCrit, result.accuracy, result.penetration);
                }
                else
                {
                    enemy.TakeDamage(damage, false, attackerAccuracy, attackerPenetration);
                }
            }
            Destroy(gameObject);
            return;
        }
    }
}