using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Таблица «префаб мебели → поверхность установки» (конструктор дома).
/// Лежит в Resources/House/FurnitureMountTable. Билдер Tools → House →
/// Build Furniture Mount Table дописывает только недостающие записи —
/// ручные правки не затираются. Без таблицы работает фолбэк по папке.
/// </summary>
[CreateAssetMenu(fileName = "FurnitureMountTable", menuName = "RPG/House/Furniture Mount Table")]
public class FurnitureMountTable : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("Имя префаба без расширения (например Part 1 copiar_1)")]
        public string prefabName = "";
        public FurnitureMount mount = FurnitureMount.Floor;
    }

    [Tooltip("Ручные исключения поверх дефолта по папкам")]
    public List<Entry> entries = new List<Entry>();

    private Dictionary<string, FurnitureMount> cache;

    const string RESOURCE_PATH = "House/FurnitureMountTable";
    private static FurnitureMountTable loaded;

    public static FurnitureMountTable Load()
    {
        if (loaded == null)
            loaded = Resources.Load<FurnitureMountTable>(RESOURCE_PATH);
        return loaded;
    }

    /// <summary>Дефолт по папке префаба: Part* (настенный декор) → Wall, остальное → Floor.</summary>
    public static FurnitureMount DefaultForFolder(string folder)
    {
        if (!string.IsNullOrEmpty(folder) && folder.StartsWith("Part"))
            return FurnitureMount.Wall;
        return FurnitureMount.Floor;
    }

    /// <summary>Дефолт по имени префаба (папки в рантайме нет): Part 1 copiar_N и т.п. → Wall.</summary>
    public static FurnitureMount DefaultForPrefabName(string prefabName)
    {
        if (!string.IsNullOrEmpty(prefabName) && prefabName.StartsWith("Part "))
            return FurnitureMount.Wall;
        return FurnitureMount.Floor;
    }

    public FurnitureMount GetMount(string prefabName, string folderHint = null)
    {
        if (string.IsNullOrEmpty(prefabName))
            return FurnitureMount.Floor;
        if (cache == null)
            RebuildCache();
        if (cache.TryGetValue(prefabName, out FurnitureMount m))
            return m;
        return DefaultForFolder(folderHint);
    }

    public void RebuildCache()
    {
        cache = new Dictionary<string, FurnitureMount>();
        foreach (Entry e in entries)
        {
            if (e == null || string.IsNullOrEmpty(e.prefabName)) continue;
            if (!cache.ContainsKey(e.prefabName))
                cache[e.prefabName] = e.mount;
        }
    }

    /// <summary>Есть ли явная запись (для билдера: не трогать ручное).</summary>
    public bool HasEntry(string prefabName)
    {
        if (cache == null)
            RebuildCache();
        return !string.IsNullOrEmpty(prefabName) && cache.ContainsKey(prefabName);
    }
}
