using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Находит ItemData по имени ассета при загрузке сохранения.
/// Загружает ВСЕ ItemData из папок Resources один раз и кеширует.
/// Работает независимо от того в какой подпапке Resources лежит предмет.
/// </summary>
public static class ItemDatabase
{
    private static Dictionary<string, ItemData> cache;

    static void EnsureLoaded()
    {
        if (cache != null) return;

        cache = new Dictionary<string, ItemData>();

        // Загружаем все ItemData отовсюду под любыми папками Resources
        ItemData[] all = Resources.LoadAll<ItemData>("");
        foreach (ItemData item in all)
        {
            if (item == null) continue;
            // Ключ — имя ассета (item.name), НЕ itemName (тот может повторяться/меняться)
            if (!cache.ContainsKey(item.name))
                cache[item.name] = item;
        }

        Debug.Log("[ItemDatabase] Загружено предметов: " + cache.Count);
    }

    /// <summary>Найти предмет по имени ассета. null если не найден.</summary>
    public static ItemData Find(string assetName)
    {
        if (string.IsNullOrEmpty(assetName)) return null;
        EnsureLoaded();
        return cache.TryGetValue(assetName, out ItemData item) ? item : null;
    }

    // На случай если ассеты пересоздавались — можно сбросить кеш
    public static void Reload()
    {
        cache = null;
        EnsureLoaded();
    }
}