using UnityEngine;
using System.Collections.Generic;

public class LootDrop : MonoBehaviour
{
    [Header("Таблица лута")]
    public LootTable lootTable;

    [Header("Префаб лута")]
    public GameObject lootItemPrefab;

    [Header("Радиус разлёта")]
    public float dropRadius = 0.5f;

    [Header("Золото")]
    public int goldMin = 5;   // минимум золота с врага
    public int goldMax = 15;  // максимум золота с врага

    [Header("Позиция попапа золота")]
    public Vector2 goldPopupOffset = new Vector2(0f, 1f);

    // Вызывается из EnemyHealth при смерти
    public void DropLoot()
    {
        DropItems();
        DropGold();
    }

    void DropItems()
    {
        if (lootTable == null || lootItemPrefab == null) return;

        List<(ItemData item, int amount)> loot = lootTable.GenerateLoot();
        foreach (var (item, amount) in loot)
        {
            Vector2 offset = Random.insideUnitCircle * dropRadius;
            Vector3 spawnPos = transform.position + new Vector3(offset.x, offset.y, 0);

            GameObject lootObj = Instantiate(lootItemPrefab, spawnPos, Quaternion.identity);
            LootItem lootItem = lootObj.GetComponent<LootItem>();
            if (lootItem != null)
            {
                lootItem.itemData = item;
                lootItem.amount = amount;
            }
        }
    }

    void DropGold()
    {
        if (goldMax <= 0) return;

        int amount = Random.Range(goldMin, goldMax + 1);
        if (amount <= 0) return;

        // Бонус золота от прокачки
        if (SkillTreeManager.Instance != null)
        {
            float mult = SkillTreeManager.Instance.GetGoldMultiplier();
            amount = Mathf.RoundToInt(amount * mult);
        }

        if (CurrencyManager.Instance != null)
            CurrencyManager.Instance.AddGold(amount);

        if (DamagePopupManager.Instance != null)
        {
            Vector3 popupPos = transform.position + (Vector3)(goldPopupOffset);
            DamagePopupManager.Instance.Spawn(popupPos, amount, DamagePopup.PopupType.Gold);
        }
    }
}