using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AttackHitbox : MonoBehaviour
{
    [Header("Базовый урон (если нет PlayerStats)")]
    public float damage = 25f;

    [Header("Длительность хитбокса (сек)")]
    public float hitboxDuration = 0.3f;

    private PolygonCollider2D polygonCollider;
    private List<EnemyHealth> hitEnemies = new List<EnemyHealth>();
    private bool isAttackActive = false; // флаг — атака сейчас активна

    void Awake()
    {
        // Выключаем коллайдер сразу в Awake — до Start других объектов
        polygonCollider = GetComponent<PolygonCollider2D>();
        if (polygonCollider != null)
            polygonCollider.enabled = false;
    }

    void Start()
    {
        // Двойная гарантия
        if (polygonCollider != null)
            polygonCollider.enabled = false;
        isAttackActive = false;
    }

    public void PerformAttack(Vector2 direction)
    {
        StartCoroutine(ActivateHitbox());
    }

    IEnumerator ActivateHitbox()
    {
        hitEnemies.Clear();
        isAttackActive = true;
        if (polygonCollider != null) polygonCollider.enabled = true;
        yield return new WaitForSeconds(hitboxDuration);
        if (polygonCollider != null) polygonCollider.enabled = false;
        isAttackActive = false;
        hitEnemies.Clear();
    }

    void OnTriggerEnter2D(Collider2D col)
    {
        EnemyHealth enemy = col.GetComponent<EnemyHealth>();
        if (enemy == null) enemy = col.GetComponentInParent<EnemyHealth>();
        if (enemy == null) enemy = col.GetComponentInChildren<EnemyHealth>();

        // Игнорируем если атака не активна (защита от ложных срабатываний)
        if (!isAttackActive) return;

        if (enemy != null && !hitEnemies.Contains(enemy))
        {
            hitEnemies.Add(enemy);

            float finalDamage;
            bool isCrit = false;

            if (PlayerStats.Instance != null)
            {
                ItemData activeItem = HotbarManager.Instance?.GetActiveItem();
                PlayerStats.DamageResult result = PlayerStats.Instance.CalculateDamage(activeItem);
                finalDamage = result.damage;
                isCrit = result.isCrit;
            }
            else
            {
                finalDamage = damage;
            }

            enemy.TakeDamage(finalDamage, isCrit);
        }
    }
}