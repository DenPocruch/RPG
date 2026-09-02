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

    [Header("Сообщение о слабой кирке")]
    public float weakPickaxeMessageCooldown = 1f;

    private SpriteRenderer sr;
    private int currentHealth;
    private bool isDepleted = false;
    private float lastWeakMessageTime = -99f;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        currentHealth = maxHealth;
        UpdateSprite();
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
        if (isDepleted) return;

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
            StartCoroutine(DepleteVein());
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

        if (veinAnimator != null)
            veinAnimator.SetTrigger("Break");

        yield return new WaitForSeconds(0.4f);

        DropOre();

        if (sr != null) sr.enabled = false;
        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        if (respawns)
        {
            yield return new WaitForSeconds(respawnTime);
            currentHealth = maxHealth;
            isDepleted = false;
            if (sr != null) sr.enabled = true;
            if (col != null) col.enabled = true;
            UpdateSprite();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void DropOre()
    {
        if (lootItemPrefab == null || oreItem == null) return;

        for (int i = 0; i < oreAmount; i++)
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