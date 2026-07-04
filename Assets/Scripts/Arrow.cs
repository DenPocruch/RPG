using UnityEngine;

public class Arrow : MonoBehaviour
{
    private float damage;
    private float speed;
    private float range;
    private Vector2 direction;
    private Vector3 startPosition;
    private bool hasHit = false;

    public void Init(Vector2 dir, float dmg, float spd, float rng)
    {
        direction = dir;
        damage = dmg;
        speed = spd;
        range = rng;
        startPosition = transform.position;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
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

        if (col.CompareTag("Player")) return;
        if (col.GetComponentInParent<PlayerMovement>() != null) return;

        if (col.CompareTag("Enemy"))
        {
            hasHit = true;
            EnemyHealth enemy = col.GetComponent<EnemyHealth>();
            if (enemy == null) enemy = col.GetComponentInParent<EnemyHealth>();
            if (enemy != null)
            {
                // Используем PlayerStats для крита
                if (PlayerStats.Instance != null && HotbarManager.Instance != null)
                {
                    ItemData bow = HotbarManager.Instance.GetActiveItem();
                    PlayerStats.DamageResult result = PlayerStats.Instance.CalculateDamage(bow);
                    enemy.TakeDamage(result.damage, result.isCrit);
                }
                else
                {
                    enemy.TakeDamage(damage);
                }
            }
            Destroy(gameObject);
            return;
        }
    }
}