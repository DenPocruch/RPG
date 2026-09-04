using UnityEngine;
using System.Collections;

public class PlayerHealth : MonoBehaviour
{
    [Header("Здоровье")]
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Смерть и возрождение")]
    public float deathAnimDuration = 1f;
    public float respawnDelay = 3f;

    [Header("Позиция попапа")]
    public Vector2 popupOffset = new Vector2(0f, 1.2f); // настрой под высоту игрока

    private SpriteRenderer spriteRenderer;
    private Animator animator;
    private Rigidbody2D rb;
    private PlayerMovement movement;
    private Vector3 spawnPosition;
    private bool isDead = false;

    void Start()
    {
        currentHealth = maxHealth;
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        animator = GetComponent<Animator>();
        rb = GetComponent<Rigidbody2D>();
        movement = GetComponent<PlayerMovement>();
        spawnPosition = transform.position;
    }

    /// <summary>
    /// Получить урон с учётом защиты, уворота и блока из PlayerStats.
    /// Точность/пробитие атакующего срезают уворот/защиту (враги со статами).
    /// </summary>
    public void TakeDamage(float incomingDamage, float attackerAccuracy = 0f, float attackerPenetration = 0f)
    {
        if (isDead) return;

        PlayerStats ps = PlayerStats.Instance;

        bool wasBlocked = false;

        if (ps != null)
        {
            // Уворот — попап "Промах!" и выход (точность врага срезает уворот)
            if (ps.TryDodge(attackerAccuracy))
            {
                if (DamagePopupManager.Instance != null)
                    DamagePopupManager.Instance.Spawn(
                        (Vector2)transform.position + popupOffset, 0, DamagePopup.PopupType.Dodge);
                return;
            }

            // Блок — половина урона + попап "Блок"
            if (ps.TryBlock())
            {
                incomingDamage *= 0.5f;
                wasBlocked = true;
            }

            // Защита (пробитие врага игнорирует часть защиты)
            incomingDamage = ps.ApplyDefense(incomingDamage, attackerPenetration);
        }

        currentHealth -= incomingDamage;
        currentHealth = Mathf.Max(currentHealth, 0);

        // Попап урона
        if (DamagePopupManager.Instance != null)
        {
            DamagePopup.PopupType type = wasBlocked
                ? DamagePopup.PopupType.Block
                : DamagePopup.PopupType.Normal;
            DamagePopupManager.Instance.Spawn(
                (Vector2)transform.position + popupOffset, incomingDamage, type);
        }

        StartCoroutine(FlashRed());

        if (currentHealth <= 0) Die();
    }

    IEnumerator FlashRed()
    {
        if (spriteRenderer != null)
            spriteRenderer.color = Color.red;
        yield return new WaitForSeconds(0.15f);
        if (spriteRenderer != null)
            spriteRenderer.color = Color.white;
    }

    void Die()
    {
        if (isDead) return;
        isDead = true;

        if (movement != null) movement.enabled = false;
        if (rb != null) rb.linearVelocity = Vector2.zero;

        if (animator != null)
            animator.SetTrigger("Death");

        StartCoroutine(RespawnCoroutine());
    }

    IEnumerator RespawnCoroutine()
    {
        yield return new WaitForSeconds(deathAnimDuration);

        if (spriteRenderer != null)
            spriteRenderer.enabled = false;

        yield return new WaitForSeconds(respawnDelay);

        transform.position = spawnPosition;
        currentHealth = maxHealth;
        isDead = false;

        if (animator != null)
        {
            animator.ResetTrigger("Death");
            animator.SetFloat("Speed", 0);
            animator.SetFloat("LastMoveX", 0f);
            animator.SetFloat("LastMoveY", -1f);
            animator.Play("Idle", 0, 0f);
        }

        if (spriteRenderer != null)
            spriteRenderer.enabled = true;

        if (movement != null)
            movement.enabled = true;
    }

    /// <summary>Установить HP напрямую (для PlayerStats при смене экипировки).</summary>
    public void SetHealth(float hp)
    {
        currentHealth = Mathf.Clamp(hp, 0, maxHealth);
    }
}