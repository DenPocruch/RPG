using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class LootEntry
{
    public ItemData item;           // предмет
    [Range(0f, 100f)]
    public float dropChance = 50f;  // шанс выпадения %
    public int minAmount = 1;       // минимум
    public int maxAmount = 1;       // максимум
}

[CreateAssetMenu(fileName = "NewLootTable", menuName = "RPG/Loot Table")]
public class LootTable : ScriptableObject
{
    [Header("Предметы лута")]
    public List<LootEntry> lootEntries = new List<LootEntry>();

    [Header("Настройки")]
    public int maxDrops = 3; // максимум предметов за раз

    // Генерирует список выпавших предметов
    public List<(ItemData item, int amount)> GenerateLoot()
    {
        var result = new List<(ItemData, int)>();
        int drops = 0;

        foreach (LootEntry entry in lootEntries)
        {
            if (drops >= maxDrops) break;
            if (entry.item == null) continue;

            float roll = Random.Range(0f, 100f);
            if (roll <= entry.dropChance)
            {
                int amount = Random.Range(entry.minAmount, entry.maxAmount + 1);
                result.Add((entry.item, amount));
                drops++;
            }
        }

        return result;
    }
}