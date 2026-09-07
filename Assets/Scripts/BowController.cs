using UnityEngine;
using System.Collections;

public class BowController : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;

    [Header("���")]
    public bool bowEquipped = false;

    [Header("�������� �� �������� (���)")]
    public float shootDelay = 0.2f;

    [Header("4 ����� ��������")]
    public Transform spawnDown;
    public Transform spawnUp;
    public Transform spawnRight;
    public Transform spawnLeft;

    [Tooltip("Оверлей-спрайт отключён: лук рисует слой тела (конструктор персонажа)")]
    public bool overlayDisabled = false;

    [Header("Aim Assist")]
    public float aimAssistRadius = 3f;   // ������ ������ �����
    public float aimAssistStrength = 0.4f; // ���� ���������� 0-1 (0.4 = �����)

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
            Debug.LogWarning("Arrow Prefab �� ����� � ItemData ����!");
            return;
        }

        Vector2 direction = new Vector2(dirX, dirY).normalized;
        if (direction == Vector2.zero) direction = Vector2.down;

        // ��������� aim assist
        direction = ApplyAimAssist(direction);

        StartCoroutine(ShootCoroutine(direction, bowData));
    }

    Vector2 ApplyAimAssist(Vector2 playerDirection)
    {
        // ���� ���������� ����� � �������
        Collider2D[] enemies = Physics2D.OverlapCircleAll(
            transform.position, aimAssistRadius,
            LayerMask.GetMask("Enemy"));

        if (enemies.Length == 0) return playerDirection;

        // ������� ������� ���������:
        // 1. ���� ������ ���� �������� � ��� �� �����������
        // 2. ��� ����� � ������� � ��� �����
        Transform bestTarget = null;
        float bestScore = -1f;

        foreach (Collider2D col in enemies)
        {
            Vector2 toEnemy = (col.transform.position - transform.position);
            Vector2 toEnemyDir = toEnemy.normalized;

            // ���� ����� ������������ ������ � ������
            float dot = Vector2.Dot(playerDirection, toEnemyDir);

            // ���� ������ ������ � ������ 120� ����� ������� (dot > 0.5)
            if (dot < 0.5f) continue;

            // ��� ���� dot (����� � �������) � ��� ����� � ��� �����
            float distance = toEnemy.magnitude;
            float score = dot / (distance + 0.1f);

            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = col.transform;
            }
        }

        if (bestTarget == null) return playerDirection;

        // ����������� � ������� �����
        Vector2 toTargetDir = (bestTarget.position - transform.position).normalized;

        // ����� ��������� ����������� ������ � ������������ � �����
        Vector2 assistedDir = Vector2.Lerp(playerDirection, toTargetDir, aimAssistStrength);
        return assistedDir.normalized;
    }

    IEnumerator ShootCoroutine(Vector2 direction, ItemData bowData)
    {
        if (spriteRenderer != null && !overlayDisabled)
            spriteRenderer.enabled = true;

        yield return new WaitForSeconds(shootDelay);

        Vector3 spawnPos = GetSpawnPoint(direction);

        GameObject arrowObj = Instantiate(
            bowData.arrowPrefab, spawnPos, Quaternion.identity);
        Arrow arrow = arrowObj.GetComponent<Arrow>();
        if (arrow != null)
        {
            float acc = PlayerStats.Instance != null ? PlayerStats.Instance.TotalAccuracy : 0f;
            float pen = PlayerStats.Instance != null ? PlayerStats.Instance.TotalPenetration : 0f;
            arrow.Init(direction, bowData.damage,
                bowData.arrowSpeed, bowData.arrowRange, acc, pen);
        }

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