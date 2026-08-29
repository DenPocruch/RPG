using UnityEngine;
using System.Collections;

/// <summary>
/// ИИ врага: патруль у точки спавна → погоня за игроком → касание наносит урон.
/// Анимация — кодом через EnemyAnimator (спрайты из EnemyData), без Animator.
/// Смерть/урон приходят из EnemyHealth (OnDamage/OnDeath), есть респавн на месте спавна.
/// </summary>
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

    [Header("Ближняя атака (если у EnemyData есть attack-кадры)")]
    [Tooltip("С какого расстояния враг останавливается и бьёт с анимацией")]
    public float attackRange = 1.1f;
    [Tooltip("Через сколько секунд после начала анимации наносится удар (момент взмаха)")]
    public float attackHitDelay = 0.35f;
    public float attackCooldown = 1.2f;

    [Header("Респавн")]
    public float respawnTime = 5f;
    public float deathAnimDuration = 1f;

    [Header("Alert State (тревога)")]
    public float alertDetectionRange = 10f; // увеличенный радиус для тревоги
    public float alertDuration = 4f;        // сколько секунд враг в тревоге
    public float alertMoveSpeed = 1.8f;     // скорость при тревоге

    [Header("Данные врага (спрайты, FPS)")]
    public EnemyData enemyData;

    private Transform player;
    private PlayerHealth playerHealth;
    private float lastDamageTime;
    private Vector3 spawnPosition;

    private Vector3 patrolTarget;
    private float patrolWaitTimer;
    private bool isWaiting = false;

    private EnemyAnimator enemyAnimator;
    private float lastMoveX = 0f;
    private float lastMoveY = -1f;

    private bool isDead = false;
    private Vector3 chaseTarget;

    // Ближняя атака
    private bool usingMeleeAttack;
    private float lastAttackTime = -999f;
    private bool attackPending;

    // Alert state
    private bool isAlert = false;
    private float alertTimer = 0f;
    private float currentDetectionRange;
    private float currentMoveSpeed;

    void Awake()
    {
        // Компонент добавляем кодом — руками в инспекторе ничего биндить не надо
        enemyAnimator = GetComponent<EnemyAnimator>();
        if (enemyAnimator == null) enemyAnimator = gameObject.AddComponent<EnemyAnimator>();
    }

    void Start()
    {
        spawnPosition = transform.position;

        currentDetectionRange = detectionRange;
        currentMoveSpeed = moveSpeed;

        if (enemyData != null)
            enemyAnimator.Init(enemyData);
        else
            Debug.LogWarning(gameObject.name + ": не назначен EnemyData — анимации не будет");

        // Есть attack-кадры → враг бьёт с анимацией, урон касанием отключаем
        usingMeleeAttack = EnemyData.Has(enemyData != null ? enemyData.attack : null);

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

        // Обновляем состояние тревоги
        UpdateAlertState();

        float distanceToPlayer = Vector2.Distance(transform.position, player.position);

        // Отложенный удар ближней атаки (момент взмаха в анимации)
        if (attackPending && Time.time - lastAttackTime >= attackHitDelay)
        {
            attackPending = false;
            if (!isDead && playerHealth != null &&
                Vector2.Distance(transform.position, player.position) <= attackRange * 1.4f)
                playerHealth.TakeDamage(damageToPlayer);
        }

        if (distanceToPlayer < currentDetectionRange)
        {
            if (usingMeleeAttack && distanceToPlayer <= attackRange)
                AttackHold();
            else
                ChasePlayer(distanceToPlayer);
        }
        else
            Patrol();

        if (!usingMeleeAttack && distanceToPlayer <= stopDistance + 0.2f)
        {
            if (Time.time - lastDamageTime > damageCooldown)
            {
                lastDamageTime = Time.time;
                if (playerHealth != null)
                    playerHealth.TakeDamage(damageToPlayer);
            }
        }
    }

    // Стоим рядом с игроком и бьём с анимацией (Myconid и прочие с attack-кадрами)
    void AttackHold()
    {
        if (enemyAnimator == null) return;

        Vector2 toPlayer = (player.position - transform.position).normalized;
        SetAnimation(Vector2.zero, toPlayer); // стоим, смотрим на игрока

        if (Time.time - lastAttackTime >= attackCooldown)
        {
            lastAttackTime = Time.time;
            attackPending = true;
            enemyAnimator.PlayState(EnemyAnimState.Attack, DirFromVector(toPlayer), true);
        }
    }

    void UpdateAlertState()
    {
        if (!isAlert) return;

        alertTimer -= Time.deltaTime;
        if (alertTimer <= 0)
        {
            // Тревога закончилась — возвращаем обычные настройки
            isAlert = false;
            currentDetectionRange = detectionRange;
            currentMoveSpeed = moveSpeed;
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
            MoveInDirection(dir, moveSpeed); // патруль всегда в обычной скорости
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
        SetAnimation(dir, dir);
    }

    void SetAnimation(Vector2 dir, Vector2 facing)
    {
        if (enemyAnimator == null) return;

        float speed = dir.magnitude;
        if (speed > 0.1f)
        {
            enemyAnimator.PlayState(EnemyAnimState.Walk, DirFromVector(dir));
            lastMoveX = dir.x;
            lastMoveY = dir.y;
        }
        else
        {
            // Стоим — показываем, куда смотрим (при атаке — на игрока)
            enemyAnimator.PlayState(EnemyAnimState.Idle, DirFromVector(facing));
        }
    }

    EnemyAnimDir DirFromVector(Vector2 v)
    {
        if (Mathf.Abs(v.x) >= Mathf.Abs(v.y))
            return v.x >= 0 ? EnemyAnimDir.Right : EnemyAnimDir.Left;
        return v.y >= 0 ? EnemyAnimDir.Up : EnemyAnimDir.Down;
    }

    public void OnDamage()
    {
        if (isDead) return;
        if (enemyAnimator != null)
            enemyAnimator.PlayState(EnemyAnimState.Damage, enemyAnimator.CurrentDir, true);

        // Поднимаем тревогу и при получении урона
        TriggerAlert();
    }

    public void OnDeath()
    {
        isDead = true;
        isAlert = false;
        attackPending = false;
        if (enemyAnimator != null)
            enemyAnimator.PlayState(EnemyAnimState.Dead, enemyAnimator.CurrentDir, true);

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

        // Сброс состояния
        currentDetectionRange = detectionRange;
        currentMoveSpeed = moveSpeed;
        isAlert = false;
        alertTimer = 0f;

        EnemyHealth health = GetComponent<EnemyHealth>();
        if (health != null)
            health.currentHealth = health.maxHealth;

        if (enemyAnimator != null)
        {
            enemyAnimator.PlayState(EnemyAnimState.Idle, EnemyAnimDir.Down, true);
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
        // Радиус зрения
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
