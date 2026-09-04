using UnityEngine;
using System.Collections;

public class TreeComponent : MonoBehaviour
{
    [Header("������ ������ (�� ItemData ������)")]
    public ItemData treeData;

    [Header("��� ����������� �������� (��� ������)")]
    public bool isStaticTree = false;
    public Sprite staticAdultSprite;
    public ItemData staticWoodItem;
    public int staticWoodAmount = 3;
    public int staticMaxHealth = 5;

    [Header("������������")]
    public float transparentAlpha = 0.3f;
    public float transparentDistance = 1.5f;

    [Header("������� ����")]
    public float fruitDropRadius = 0.8f;
    public float woodDropRadius = 0.6f;

    [Header("������ ������� ��� �����")]
    public Animator leafAnimator; // Animator ��������� LeafEffect �������

    [Header("�������� ������")]
    public Animator treeAnimator;
    public GameObject lootItemPrefab;

    private SpriteRenderer sr;
    private Transform player;
    private bool isFalling = false;
    private int currentHealth;

    private bool hasFruit = false;
    private bool isDried = false;
    private int fruitHarvestCount = 0;
    private float fruitTimer = 0f;
    private bool fruitTimerActive = false;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
        InitFromData();
    }

    void OnEnable()
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        InitFromData();
    }

    void InitFromData()
    {
        if (isStaticTree)
        {
            currentHealth = staticMaxHealth;
            if (sr != null && staticAdultSprite != null)
                sr.sprite = staticAdultSprite;
            return;
        }

        if (treeData == null) return;

        currentHealth = treeData.treeMaxHealth;

        // Состояние из сохранения важнее начальной инициализации
        if (hasPendingRestore)
        {
            hasPendingRestore = false;
            isDried = pendingDried;
            hasFruit = pendingHasFruit;
            fruitHarvestCount = pendingHarvestCount;
            fruitTimer = pendingFruitTimer;
            fruitTimerActive = pendingTimerActive;
            UpdateSprite();
            return;
        }

        if (treeData.isFruitTree && !isDried)
        {
            hasFruit = false;
            fruitTimer = treeData.treeFruitGrowTime;
            fruitTimerActive = true;
        }

        UpdateSprite();
    }

    void Update()
    {
        UpdateTransparency();

        if (treeData != null && treeData.isFruitTree && !isDried && !hasFruit && fruitTimerActive)
        {
            fruitTimer -= Time.deltaTime;
            if (fruitTimer <= 0f)
            {
                fruitTimerActive = false;
                hasFruit = true;
                UpdateSprite();
                Debug.Log("����� �������: " + gameObject.name);
            }
        }
    }

    void UpdateTransparency()
    {
        if (sr == null || player == null) return;

        bool playerBehind = player.position.y > transform.position.y
            && Vector2.Distance(player.position, transform.position) < transparentDistance;

        float targetAlpha = playerBehind ? transparentAlpha : 1f;
        Color c = sr.color;
        c.a = Mathf.Lerp(c.a, targetAlpha, Time.deltaTime * 8f);
        sr.color = c;
    }

    void UpdateSprite()
    {
        if (sr == null) return;

        if (isStaticTree)
        {
            if (staticAdultSprite != null) sr.sprite = staticAdultSprite;
            return;
        }

        if (treeData == null) return;

        if (!treeData.isFruitTree)
        {
            if (isDried && treeData.treeDriedSprite != null)
                sr.sprite = treeData.treeDriedSprite;
            else if (treeData.treeAdultSprite != null)
                sr.sprite = treeData.treeAdultSprite;
            return;
        }

        if (isDried && treeData.treeDriedSprite != null)
            sr.sprite = treeData.treeDriedSprite;
        else if (hasFruit && treeData.treeFruitSprite != null)
            sr.sprite = treeData.treeFruitSprite;
        else if (!hasFruit && treeData.treeNoFruitSprite != null)
            sr.sprite = treeData.treeNoFruitSprite;
    }

    public void Chop()
    {
        if (isFalling) return;

        currentHealth--;

        // ��������� �������� ������� ����� �������
        if (leafAnimator != null)
            leafAnimator.SetTrigger("Play");

        if (treeAnimator != null)
            treeAnimator.SetTrigger("Shake");

        if (currentHealth <= 0)
            StartCoroutine(FallTree());
    }

    public bool TryHarvestFruit()
    {
        if (!hasFruit || isDried) return false;
        if (treeData == null || treeData.treeFruitItem == null) return false;

        hasFruit = false;
        fruitHarvestCount++;

        DropItemsIndividually(treeData.treeFruitItem, treeData.treeFruitAmount, fruitDropRadius);

        if (fruitHarvestCount >= treeData.treeMaxFruitHarvests)
        {
            isDried = true;
            currentHealth = treeData.treeMaxHealth;
            fruitTimerActive = false;
        }
        else
        {
            fruitTimer = treeData.treeFruitGrowTime;
            fruitTimerActive = true;
        }

        UpdateSprite();
        return true;
    }

    public bool HasFruit() => hasFruit && !isDried;
    public bool IsFalling => isFalling;

    // ─── Сохранение/загрузка (читает и восстанавливает FarmManager) ───
    public bool IsDried => isDried;
    public int FruitHarvestCount => fruitHarvestCount;
    public float FruitTimer => fruitTimer;
    public bool FruitTimerActive => fruitTimerActive;

    // Отложенное восстановление: ApplyRestoredState зовётся ДО того как
    // отработает Start() → InitFromData() (который иначе сбросил бы
    // плодоношение на начальное состояние)
    private bool hasPendingRestore;
    private bool pendingDried;
    private bool pendingHasFruit;
    private bool pendingTimerActive;
    private int pendingHarvestCount;
    private float pendingFruitTimer;

    public void ApplyRestoredState(bool dried, bool withFruit, int harvestCount, float fTimer, bool timerActive)
    {
        pendingDried = dried;
        pendingHasFruit = withFruit;
        pendingHarvestCount = harvestCount;
        pendingFruitTimer = fTimer;
        pendingTimerActive = timerActive;
        hasPendingRestore = true;
    }

    IEnumerator FallTree()
    {
        isFalling = true;

        if (treeAnimator != null)
            treeAnimator.SetTrigger("Fall");

        yield return new WaitForSeconds(0.5f);

        ItemData wood = isStaticTree ? staticWoodItem : treeData?.treeWoodItem;
        int amount = isStaticTree ? staticWoodAmount : (treeData?.treeWoodAmount ?? 3);

        // Бонус добычи от редкости топора (0/25/50/100/150)
        if (HotbarManager.Instance != null)
        {
            ItemData axe = HotbarManager.Instance.GetActiveItem();
            if (axe != null && axe.itemType == ItemType.Axe)
                amount += ItemData.RollBonusDrops(axe);
        }

        if (wood != null)
            DropItemsIndividually(wood, amount, woodDropRadius);

        // Сейв по событию: дерево срублено (исчезло из мира)
        SaveManager.Instance?.Save();

        Destroy(gameObject);
    }

    void DropItemsIndividually(ItemData item, int amount, float radius)
    {
        if (lootItemPrefab == null || item == null) return;

        for (int i = 0; i < amount; i++)
        {
            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(radius * 0.5f, radius);
            Vector3 pos = transform.position + new Vector3(offset.x, offset.y, 0);

            GameObject obj = Instantiate(lootItemPrefab, pos, Quaternion.identity);
            LootItem loot = obj.GetComponent<LootItem>();
            if (loot != null)
            {
                loot.itemData = item;
                loot.amount = 1;
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0, 1, 0, 0.3f);
        Gizmos.DrawWireSphere(transform.position, transparentDistance);
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, fruitDropRadius);
    }
}