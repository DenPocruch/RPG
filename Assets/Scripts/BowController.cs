using UnityEngine;
using System.Collections;

public class BowController : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;

    [Header("Лук")]
    public bool bowEquipped = false;

    [Header("Задержка до выстрела (сек)")]
    public float shootDelay = 0.2f;

    [Header("4 точки выстрела")]
    public Transform spawnDown;
    public Transform spawnUp;
    public Transform spawnRight;
    public Transform spawnLeft;

    [Header("Aim Assist")]
    public float aimAssistRadius = 3f;   // радиус поиска врага
    public float aimAssistStrength = 0.4f; // сила притяжения 0-1 (0.4 = мягко)

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            spriteRenderer.enabled = false;
    }

    public void ToggleBow()
    {
        bowEquipped = !bowEquipped;
    }

    public void ForceUnequip()
    {
        bowEquipped = false;
        if (spriteRenderer != null)
            spriteRenderer.enabled = false;
    }

    public void Shoot(float dirX, float dirY, ItemData bowData)
    {
        if (!bowEquipped) return;
        if (bowData.arrowPrefab == null)
        {
            Debug.LogWarning("Arrow Prefab не задан в ItemData лука!");
            return;
        }

        Vector2 direction = new Vector2(dirX, dirY).normalized;
        if (direction == Vector2.zero) direction = Vector2.down;

        // Применяем aim assist
        direction = ApplyAimAssist(direction);

        StartCoroutine(ShootCoroutine(direction, bowData));
    }

    Vector2 ApplyAimAssist(Vector2 playerDirection)
    {
        // Ищем ближайшего врага в радиусе
        Collider2D[] enemies = Physics2D.OverlapCircleAll(
            transform.position, aimAssistRadius,
            LayerMask.GetMask("Enemy"));

        if (enemies.Length == 0) return playerDirection;

        // Находим лучшего кандидата:
        // 1. Враг должен быть примерно в том же направлении
        // 2. Чем ближе к прицелу — тем лучше
        Transform bestTarget = null;
        float bestScore = -1f;

        foreach (Collider2D col in enemies)
        {
            Vector2 toEnemy = (col.transform.position - transform.position);
            Vector2 toEnemyDir = toEnemy.normalized;

            // Угол между направлением игрока и врагом
            float dot = Vector2.Dot(playerDirection, toEnemyDir);

            // Берём только врагов в конусе 120° перед игроком (dot > 0.5)
            if (dot < 0.5f) continue;

            // Чем выше dot (ближе к прицелу) и чем ближе — тем лучше
            float distance = toEnemy.magnitude;
            float score = dot / (distance + 0.1f);

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = col.transform;
            }
        }

        if (bestTarget == null) return playerDirection;

        // Направление к лучшему врагу
        Vector2 toTargetDir = (bestTarget.position - transform.position).normalized;

        // Мягко смешиваем направление игрока с направлением к врагу
        Vector2 assistedDir = Vector2.Lerp(playerDirection, toTargetDir, aimAssistStrength);
        return assistedDir.normalized;
    }

    IEnumerator ShootCoroutine(Vector2 direction, ItemData bowData)
    {
        if (spriteRenderer != null)
            spriteRenderer.enabled = true;

        yield return new WaitForSeconds(shootDelay);

        Vector3 spawnPos = GetSpawnPoint(direction);

        GameObject arrowObj = Instantiate(
            bowData.arrowPrefab, spawnPos, Quaternion.identity);
        Arrow arrow = arrowObj.GetComponent<Arrow>();
        if (arrow != null)
            arrow.Init(direction, bowData.damage,
                bowData.arrowSpeed, bowData.arrowRange);

        if (spriteRenderer != null)
            spriteRenderer.enabled = false;
    }

    Vector3 GetSpawnPoint(Vector2 direction)
    {
        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
        {
            if (direction.x > 0)
                return spawnRight != null ? spawnRight.position : transform.position;
            else
                return spawnLeft != null ? spawnLeft.position : transform.position;
        }
        else
        {
            if (direction.y > 0)
                return spawnUp != null ? spawnUp.position : transform.position;
            else
                return spawnDown != null ? spawnDown.position : transform.position;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, aimAssistRadius);
    }
}