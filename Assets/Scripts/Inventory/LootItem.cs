using UnityEngine;
using System.Collections;

public class LootItem : MonoBehaviour
{
    [Header("�������")]
    public ItemData itemData;
    public int amount = 1;

    [Header("Параметры")]
    public float pickupRadius = 0.5f;
    public float lifetime = 30f;
    [Tooltip("Если выключено — предмет лежит пока его не подберут (дроп с животных)")]
    public bool despawnOverTime = true;
    public float bobSpeed = 2f;
    public float bobHeight = 0.1f;
    public float itemScale = 0.5f;

    [Header("���� �� ������")]
    public int craftingXpReward = 2;
    [Tooltip("Вес рыбы в кг — переносится в слот при подборе")]
    public float fishWeightKg = 0f; // ���� � Crafting �� ������ ����������
    public int farmingXpReward = 0;  // XP в Farming при подборе (урожай с грядки)

    [Header("Магнит (пылесос)")]
    [Tooltip("Через столько секунд после выпадения лут начинает тянуться к игроку")]
    public float magnetDelay = 1f;
    [Tooltip("Радиус притяжения (подбор вплотную — pickupRadius)")]
    public float magnetRadius = 2.5f;
    public float magnetSpeed = 6f;

    private SpriteRenderer spriteRenderer;
    private Vector3 startPos;
    private Transform player;
    private bool isPickedUp = false;
    private float spawnTime;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        startPos = transform.position;

        if (spriteRenderer != null && itemData != null)
        {
            spriteRenderer.sprite = itemData.worldSprite != null
                ? itemData.worldSprite
                : itemData.icon;
        }

        transform.localScale = new Vector3(itemScale, itemScale, 1f);

        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
        spawnTime = Time.time;

        if (despawnOverTime)
        {
            Destroy(gameObject, lifetime);
            StartCoroutine(BlinkBeforeDestroy());
        }
    }

    void Update()
    {
        if (isPickedUp) return;

        if (player != null)
        {
            float dist = Vector2.Distance(transform.position, player.position);
            if (dist <= pickupRadius)
            {
                Pickup();
                return;
            }
            // Магнит: полежал секунду — тянемся к игроку, подбирать всё равно учим подходом
            if (Time.time - spawnTime >= magnetDelay && dist <= magnetRadius)
            {
                transform.position = Vector2.MoveTowards(
                    transform.position, player.position, magnetSpeed * Time.deltaTime);
                startPos = transform.position; // боб продолжаем с новой точки
            }
        }

        float newY = startPos.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }

    void Pickup()
    {
        if (isPickedUp) return;
        isPickedUp = true;

        if (itemData == null) { Destroy(gameObject); return; }

        bool added = false;
        if (InventoryUI.Instance != null)
            added = InventoryUI.Instance.AddItem(itemData, amount, fishWeightKg);

        if (added)
        {
            // XP за подбор (крафт/фермерство)
            if (PlayerLevel.Instance != null)
            {
                if (craftingXpReward > 0)
                    PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Crafting, craftingXpReward);
                if (farmingXpReward > 0)
                    PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Farming, farmingXpReward);
            }

            Debug.Log("���������: " + itemData.itemName + " x" + amount);

            // Путеводитель Акт 1: loot — любой подбор; pickup_harvest — только урожай с грядки
            TutorialManager.Notify("loot");
            if (farmingXpReward > 0) TutorialManager.Notify("pickup_harvest");
        }
        else
        {
            Debug.Log("Инвентарь полон! Предмет остаётся на земле.");
            // Не уничтожаем — предмет можно подобрать позже, когда появится место
            isPickedUp = false;
            return;
        }

        Destroy(gameObject);
    }

    IEnumerator BlinkBeforeDestroy()
    {
        yield return new WaitForSeconds(lifetime - 5f);
        while (gameObject != null)
        {
            if (spriteRenderer != null)
                spriteRenderer.enabled = !spriteRenderer.enabled;
            yield return new WaitForSeconds(0.3f);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, pickupRadius);
    }
}