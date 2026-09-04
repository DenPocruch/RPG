using UnityEngine;
using System.Collections;

/// <summary>
/// Жила руды в мире. Аналог TreeComponent, но для добычи киркой.
/// Требует кирку не ниже нужного уровня (toolTier).
/// </summary>
public class OreVeinComponent : MonoBehaviour
{
    [Header("Данные жилы")]
    public ItemData oreItem;              // что выпадает (руда)
    public int oreAmount = 3;        // сколько руды выпадает при разрушении
    public int maxHealth = 5;        // сколько ударов нужно
    [Tooltip("Минимальный уровень кирки для добычи этой руды")]
    public int requiredToolTier = 1;

    [Header("Спрайты (целая / повреждённая, опционально)")]
    public Sprite fullSprite;
    public Sprite crackedSprite; // например после половины ударов

    [Header("Радиус дропа")]
    public float dropRadius = 0.6f;

    [Header("Возрождение жилы")]
    public bool respawns = true;
    public float respawnTime = 300f; // секунд до восстановления жилы

    [Header("Анимация")]
    public Animator veinAnimator;
    public GameObject lootItemPrefab;

    [Header("Сейв (опционально)")]
    [Tooltip("Ручной ID для сейва. Если пусто — ключ строится из руды и позиции")]
    public string veinId;

    [Header("Сообщение о слабой кирке")]
    public float weakPickaxeMessageCooldown = 1f;

    private SpriteRenderer sr;
    private int currentHealth;
    private bool isDepleted = false;
    private float lastWeakMessageTime = -99f;
    private long respawnAtTicks = 0; // UtcNow.Ticks момента возрождения (0 = не истощена)

    public bool IsDepleted => isDepleted;
    public int CurrentHealth => currentHealth;
    public long RespawnAtTicks => respawnAtTicks;

    /// <summary>
    /// Стабильный ключ жилы для сейва: ручной veinId либо "Руда@X,Y".
    /// Позиции из файла сцены при каждой загрузке одинаковые, так что
    /// перестановка жил юзером просто даст им новые ключи (станут целыми).
    /// </summary>
    public string SaveId()
    {
        if (!string.IsNullOrEmpty(veinId)) return veinId;
        string ore = oreItem != null ? oreItem.name : "Unknown";
        int x = Mathf.RoundToInt(transform.position.x * 100f);
        int y = Mathf.RoundToInt(transform.position.y * 100f);
        return ore + "@" + x + "," + y;
    }

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        currentHealth = maxHealth;
        UpdateSprite();
        // Менеджер сейва уже загрузил блоб (EnsureInScene в sceneLoaded/Start
        // отрабатывает раньше Start'ов сценовых объектов) — подтягиваем состояние
        OreVeinSaveManager.TryApplyTo(this);
    }

    void UpdateSprite()
    {
        if (sr == null) return;
        bool damaged = currentHealth <= maxHealth / 2 && crackedSprite != null;
        sr.sprite = damaged ? crackedSprite : fullSprite;
    }

    // ═══════════════════════════════════════════════════════════
    // ДОБЫЧА — вызывается из PlayerMovement при ударе киркой
    // ═══════════════════════════════════════════════════════════
    public void Mine()
    {
        if (isDepleted || currentHealth <= 0) return;

        if (!HasStrongEnoughPickaxe())
        {
            ShowWeakPickaxeFeedback();
            return;
        }

        currentHealth--;

        if (veinAnimator != null)
            veinAnimator.SetTrigger("Shake");
        else
            StartCoroutine(PunchScale());

        UpdateSprite();

        if (currentHealth <= 0)
        {
            // Флаг СИНХРОННО, до корутины: повторные удары в окно 0.4с
            // анимации разрушения иначе запускали бы второй DepleteVein = двойной дроп
            isDepleted = true;
            StartCoroutine(DepleteVein());
        }
    }

    // Фидбек удара без Animator: короткий пинч-скейл (для мобилки — видно, что бьёшь)
    IEnumerator PunchScale()
    {
        Transform t = transform;
        Vector3 baseScale = t.localScale;
        float time = 0.15f;
        for (float e = 0f; e < time; e += Time.deltaTime)
        {
            float k = 1f + Mathf.Sin(e / time * Mathf.PI) * 0.12f;
            t.localScale = new Vector3(baseScale.x * k, baseScale.y * (2f - k), baseScale.z);
            yield return null;
        }
        t.localScale = baseScale;
    }

    bool HasStrongEnoughPickaxe()
    {
        if (HotbarManager.Instance == null) return true;

        ItemData active = HotbarManager.Instance.GetActiveItem();
        if (active == null || active.itemType != ItemType.Pickaxe) return false;

        return active.toolTier >= requiredToolTier;
    }

    void ShowWeakPickaxeFeedback()
    {
        if (Time.time - lastWeakMessageTime < weakPickaxeMessageCooldown) return;
        lastWeakMessageTime = Time.time;

        ActionLogUI.Show("[Жила руды] Нужна кирка уровня " + requiredToolTier + " или выше!");

        if (veinAnimator != null)
            veinAnimator.SetTrigger("Shake");
    }

    IEnumerator DepleteVein()
    {
        isDepleted = true;
        // Момент возрождения в реальном времени — оффлайн-прогресс сам собой
        respawnAtTicks = respawns
            ? System.DateTime.UtcNow.Ticks + (long)(respawnTime * System.TimeSpan.TicksPerSecond)
            : 0;

        if (veinAnimator != null)
            veinAnimator.SetTrigger("Break");

        yield return new WaitForSeconds(0.4f);

        DropOre();
        SetActiveVisual(false);
        if (SaveManager.Instance != null) SaveManager.Instance.Save();

        if (respawns)
        {
            yield return new WaitForSeconds(respawnTime);
            Respawn();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Respawn()
    {
        currentHealth = maxHealth;
        isDepleted = false;
        respawnAtTicks = 0;
        SetActiveVisual(true);
        UpdateSprite();
        if (SaveManager.Instance != null) SaveManager.Instance.Save();
    }

    // Досыпание после загрузки сцены: жила была истощена, ждём остаток
    IEnumerator ResumeAfterDelay(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        Respawn();
    }

    void SetActiveVisual(bool active)
    {
        if (sr != null) sr.enabled = active;
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = active;
    }

    /// <summary>
    /// Применить состояние из сейва (зовёт OreVeinSaveManager в Start жилы).
    /// sr уже получен, корутины в Start запускать можно.
    /// </summary>
    public void ApplySave(int health, bool depleted, long savedRespawnAt)
    {
        currentHealth = Mathf.Clamp(health, 0, maxHealth);

        if (!depleted)
        {
            ApplyIntactSave();
            return;
        }

        // Истощённая одноразовая жила (respawns=false): в рантайме она бы
        // уничтожилась — повторяем это и после загрузки
        if (!respawns)
        {
            Destroy(gameObject);
            return;
        }

        long now = System.DateTime.UtcNow.Ticks;
        if (savedRespawnAt <= 0 || now >= savedRespawnAt)
        {
            // Время вышло (в т.ч. оффлайн) — жила уже восстановилась
            RespawnSilent();
            return;
        }

        isDepleted = true;
        respawnAtTicks = savedRespawnAt;
        SetActiveVisual(false);
        UpdateSprite();
        float remain = (float)((savedRespawnAt - now) / (double)System.TimeSpan.TicksPerSecond);
        StartCoroutine(ResumeAfterDelay(remain));
    }

    // Целая или побитая (но не истощённая) жила из сейва
    void ApplyIntactSave()
    {
        isDepleted = false;
        respawnAtTicks = 0;
        SetActiveVisual(true);
        UpdateSprite();
    }

    // Восстановление без лишнего сейва (сейв и так актуален — мы из него читаем)
    void RespawnSilent()
    {
        currentHealth = maxHealth;
        isDepleted = false;
        respawnAtTicks = 0;
        SetActiveVisual(true);
        UpdateSprite();
    }

    void DropOre()
    {
        if (lootItemPrefab == null || oreItem == null) return;

        // Бонус добычи от редкости кирки (0/25/50/100/150)
        int total = oreAmount;
        if (HotbarManager.Instance != null)
        {
            ItemData pick = HotbarManager.Instance.GetActiveItem();
            if (pick != null && pick.itemType == ItemType.Pickaxe)
                total += ItemData.RollBonusDrops(pick);
        }

        for (int i = 0; i < total; i++)
        {
            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(dropRadius * 0.5f, dropRadius);
            Vector3 pos = transform.position + new Vector3(offset.x, offset.y, 0);

            GameObject obj = Instantiate(lootItemPrefab, pos, Quaternion.identity);
            LootItem loot = obj.GetComponent<LootItem>();
            if (loot != null)
            {
                loot.itemData = oreItem;
                loot.amount = 1;
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.6f, 0.6f, 0.2f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, dropRadius);
    }
}