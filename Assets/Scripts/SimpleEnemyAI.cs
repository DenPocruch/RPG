using UnityEngine;
using System.Collections;

public class SimpleEnemyAI : MonoBehaviour
{
    [Header("Настройки движения")]
    public float moveSpeed = 1f;
    public float detectionRange = 5f;
    public float stopDistance = 0.5f;

    [Header("Патруль")]
    public float patrolRadius = 2f;
    public float patrolWaitTime = 2f;

    [Header("Урон игроку")]
    public float damageToPlayer = 10f;
    public float damageCooldown = 1f;

    [Header("Респавн")]
    public float respawnTime = 5f;
    public float deathAnimDuration = 1f;

    [Header("Alert State (тревога)")]
    public float alertDetectionRange = 10f; // увеличенный радиус при тревоге
    public float alertDuration = 4f;         // сколько секунд в тревоге
    public float alertMoveSpeed = 1.8f;      // скорость при тревоге

    private Transform player;
    private PlayerHealth playerHealth;
    private float lastDamageTime;
    private Vector3 spawnPosition;

    private Vector3 patrolTarget;
    private float patrolWaitTimer;
    private bool isWaiting = false;

    private Animator animator;
    private float lastMoveX = 0f;
    private float lastMoveY = -1f;

    private bool isDead = false;
    private Vector3 chaseTarget;

    // Alert state
    private bool isAlert = false;
    private float alertTimer = 0f;
    private float currentDetectionRange;
    private float currentMoveSpeed;

    void Start()
    {
        spawnPosition = transform.position;
        animator = GetComponent<Animator>();

        currentDetectionRange = detectionRange;
        currentMoveSpeed = moveSpeed;

        if (animator != null)
        {
            animator.SetFloat("LastMoveX", lastMoveX);
            animator.SetFloat("LastMoveY", lastMoveY);
        }

        GameObject p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
            if (playerHealth == null)
                playerHealth = p.GetComponentInChildren<PlayerHealth>();
        }

        SetNewPatrolTarget();
    }

    void Update()
    {
        if (isDead) return;
        if (player == null) return;

        // Обновляем таймер тревоги
        UpdateAlertState();

        float distanceToPlayer = Vector2.Distance(transform.position, player.position);

        if (distanceToPlayer < currentDetectionRange)
            ChasePlayer(distanceToPlayer);
        else
            Patrol();

        if (distanceToPlayer <= stopDistance + 0.2f)
        {
            if (Time.time - lastDamageTime > damageCooldown)
            {
                lastDamageTime = Time.time;
                if (playerHealth != null)
                    playerHealth.TakeDamage(damageToPlayer);
            }
        }
    }

    void UpdateAlertState()
    {
        if (!isAlert) return;

        alertTimer -= Time.deltaTime;
        if (alertTimer <= 0)
        {
            // Тревога закончилась — возвращаем стандартные параметры
            isAlert = false;
            currentDetectionRange = detectionRange;
            currentMoveSpeed = moveSpeed;
            Debug.Log(gameObject.name + " успокоился");
        }
    }

    // Вызывается из EnemyHealth при получении урона
    public void TriggerAlert()
    {
        if (isDead) return;

        isAlert = true;
        alertTimer = alertDuration;
        currentDetectionRange = alertDetectionRange;
        currentMoveSpeed = alertMoveSpeed;
        Debug.Log(gameObject.name + " в тревоге! Радиус: " + alertDetectionRange);
    }

    void ChasePlayer(float distanceToPlayer)
    {
        UpdateChaseTarget();

        float distToTarget = Vector2.Distance(transform.position, chaseTarget);

        if (distToTarget > 0.1f)
        {
            Vector2 dir = (chaseTarget - transform.position).normalized;
            MoveInDirection(dir, currentMoveSpeed);
        }
        else
        {
            SetAnimation(Vector2.zero);
        }
    }

    void UpdateChaseTarget()
    {
        Vector2 dirFromPlayer = (transform.position - player.position).normalized;
        if (dirFromPlayer == Vector2.zero)
            dirFromPlayer = new Vector2(lastMoveX, lastMoveY).normalized;
        chaseTarget = player.position + (Vector3)(dirFromPlayer * stopDistance);
    }

    void Patrol()
    {
        if (isWaiting)
        {
            patrolWaitTimer -= Time.deltaTime;
            SetAnimation(Vector2.zero);
            if (patrolWaitTimer <= 0)
            {
                isWaiting = false;
                SetNewPatrolTarget();
            }
            return;
        }

        float distToTarget = Vector2.Distance(transform.position, patrolTarget);

        if (distToTarget > 0.2f)
        {
            Vector2 dir = (patrolTarget - transform.position).normalized;
            MoveInDirection(dir, moveSpeed); // патруль всегда с обычной скоростью
        }
        else
        {
            isWaiting = true;
            patrolWaitTimer = patrolWaitTime;
        }
    }

    void MoveInDirection(Vector2 dir, float speed)
    {
        transform.position += (Vector3)dir * speed * Time.deltaTime;
        SetAnimation(dir);
        lastMoveX = dir.x;
        lastMoveY = dir.y;
    }

    void SetAnimation(Vector2 dir)
    {
        if (animator == null) return;

        float speed = dir.magnitude;
        animator.SetFloat("Speed", speed);

        if (speed > 0.1f)
        {
            animator.SetFloat("MoveX", dir.x);
            animator.SetFloat("MoveY", dir.y);
        }

        animator.SetFloat("LastMoveX", lastMoveX);
        animator.SetFloat("LastMoveY", lastMoveY);
    }

    public void OnDamage()
    {
        if (isDead) return;
        if (animator != null)
            animator.SetTrigger("Damage");

        // Всегда входим в тревогу при получении урона
        TriggerAlert();
    }

    public void OnDeath()
    {
        isDead = true;
        isAlert = false;
        if (animator != null)
            animator.SetTrigger("Dead");

        StartCoroutine(RespawnCoroutine());
    }

    IEnumerator RespawnCoroutine()
    {
        yield return new WaitForSeconds(deathAnimDuration);

        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>();
        foreach (var r in renderers) r.enabled = false;

        Collider2D[] cols = GetComponents<Collider2D>();
        foreach (var c in cols) c.enabled = false;

        yield return new WaitForSeconds(respawnTime);

        transform.position = spawnPosition;

        // Сбрасываем состояние
        currentDetectionRange = detectionRange;
        currentMoveSpeed = moveSpeed;
        isAlert = false;
        alertTimer = 0f;

        EnemyHealth health = GetComponent<EnemyHealth>();
        if (health != null)
            health.currentHealth = health.maxHealth;

        if (animator != null)
        {
            animator.ResetTrigger("Dead");
            animator.ResetTrigger("Damage");
            animator.SetFloat("Speed", 0);
            animator.SetFloat("LastMoveX", 0f);
            animator.SetFloat("LastMoveY", -1f);
            animator.Play("Idle", 0, 0f);
        }

        foreach (var r in renderers) r.enabled = true;
        foreach (var c in cols) c.enabled = true;

        isDead = false;
        SetNewPatrolTarget();
        this.enabled = true;
    }

    void SetNewPatrolTarget()
    {
        Vector2 randomOffset = Random.insideUnitCircle * patrolRadius;
        patrolTarget = spawnPosition + new Vector3(randomOffset.x, randomOffset.y, 0);
    }

    void OnDrawGizmosSelected()
    {
        // Обычный радиус
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        // Радиус тревоги
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, alertDetectionRange);
        // Радиус атаки
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, stopDistance);
        // Радиус патруля
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(spawnPosition == Vector3.zero ?
            transform.position : spawnPosition, patrolRadius);
        if (Application.isPlaying && !isDead && player != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(chaseTarget, 0.2f);
        }
    }
}