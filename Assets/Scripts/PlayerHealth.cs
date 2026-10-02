using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

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
        PlayerVisualDriver driver = GetComponent<PlayerVisualDriver>();
        if (driver != null)
        {
            driver.SetTint(Color.red);
            yield return new WaitForSeconds(0.15f);
            driver.SetTint(Color.white);
            yield break;
        }
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
        PlayerVisualDriver driver = GetComponent<PlayerVisualDriver>();
        if (driver != null) driver.PlayDeath();

        StartCoroutine(RespawnCoroutine());
    }

    IEnumerator RespawnCoroutine()
    {
        yield return new WaitForSeconds(deathAnimDuration);

        if (spriteRenderer != null)
            spriteRenderer.enabled = false;

        // Кровать-возрождение: сон 5 сек на подушке, подъём рядом, 10% HP
        if (RespawnBed.HasHome)
        {
            yield return StartCoroutine(RespawnOnBed());
            yield break;
        }

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
        PlayerVisualDriver reviveDriver = GetComponent<PlayerVisualDriver>();
        if (reviveDriver != null) reviveDriver.Revive();

        // Старый спрайт включаем только без код-визуала (иначе будет «два персонажа»)
        if (spriteRenderer != null && GetComponent<PlayerVisualDriver>() == null)
            spriteRenderer.enabled = true;

        if (movement != null)
            movement.enabled = true;
    }

    /// <summary>Установить HP напрямую (для PlayerStats при смене экипировки).</summary>
    public void SetHealth(float hp)
    {
        currentHealth = Mathf.Clamp(hp, 0, maxHealth);
    }

    /// <summary>
    /// Возрождение на кровати: грузим сцену дома при нужде, телепорт на подушку,
    /// снэп камеры, скин «занято», 19. Sleep N сек, подъём в свободную точку, 10% HP.
    /// movement остаётся выключенным, isDead=true — урон во сне игнорируется.
    /// </summary>
    IEnumerator RespawnOnBed()
    {
        // 1) Сцена дома
        if (SceneManager.GetActiveScene().name != RespawnBed.HomeSceneName)
        {
            SceneManager.LoadScene(RespawnBed.HomeSceneName);
            float timeout = 6f;
            while (RespawnBed.Active == null && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            yield return null; // кадр на Awake/Start сцены
        }

        RespawnBed bed = RespawnBed.Active;
        if (bed == null) // дом не нашёлся — старый фолбэк
        {
            yield return new WaitForSeconds(respawnDelay);
            transform.position = spawnPosition;
            currentHealth = maxHealth;
            isDead = false;
            if (movement != null) movement.enabled = true;
            yield break;
        }

        // 2) Лечь на подушку (коллайдеры кровати — выкл ДО телепорта,
        // иначе физика за один шаг выдавит игрока с подушки)
        bed.SetSolid(false);
        if (rb != null) rb.linearVelocity = Vector2.zero;
        transform.position = bed.SleepPos;
        if (rb != null) rb.linearVelocity = Vector2.zero;
        RespawnBed.SnapCamera(bed.SleepPos);
        bed.SetOccupied(true);
        currentHealth = 1f; // во сне едва жив — подъём долечит до 10% с попапом

        PlayerVisualDriver driver = GetComponent<PlayerVisualDriver>();
        if (driver != null) driver.PlaySleep(bed.SleepOrder(), bed.sleepAnimSpeed);

        yield return new WaitForSeconds(bed.sleepDuration);

        // 3) Подъём рядом (точка валидируется от стен)
        Vector3 wake = bed.ResolveWakePosition(gameObject);
        transform.position = wake;
        if (rb != null) rb.linearVelocity = Vector2.zero;
        bed.SetSolid(true);
        RespawnBed.SnapCamera(wake);
        bed.SetOccupied(false);

        currentHealth = Mathf.Max(1f, maxHealth * 0.1f);
        isDead = false;

        // Попап исцеления «+N» как от еды
        if (DamagePopupManager.Instance != null)
            DamagePopupManager.Instance.Spawn(
                wake + (Vector3)popupOffset, currentHealth - 1f, DamagePopup.PopupType.Heal);

        if (animator != null)
        {
            animator.ResetTrigger("Death");
            animator.SetFloat("Speed", 0);
            animator.SetFloat("LastMoveX", 0f);
            animator.SetFloat("LastMoveY", -1f);
            animator.Play("Idle", 0, 0f);
        }
        if (driver != null) driver.WakeUp();

        if (spriteRenderer != null && GetComponent<PlayerVisualDriver>() == null)
            spriteRenderer.enabled = true;

        if (movement != null)
            movement.enabled = true;
    }
}