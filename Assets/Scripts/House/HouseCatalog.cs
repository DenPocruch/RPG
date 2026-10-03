using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Каталог планировки дома (конструктор дома): что видно во вкладке «Мебель».
/// Лежит в Resources/House/HouseCatalog. Создаётся билдером Tools → House →
/// Create House Catalog Base ПУСТЫМ — владелец сам перетаскивает нужные
/// префабы в инспекторе (префаб = ассет-ссылка). Поверхность установки
/// тянется из FurnitureMountTable по имени префаба, здесь не дублируется.
/// </summary>
[CreateAssetMenu(fileName = "HouseCatalog", menuName = "RPG/House/House Catalog")]
public class HouseCatalog : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("Префаб из Assets/Prefab/Interior/... — перетащить сюда")]
        public GameObject prefab;
        [Tooltip("Цена покупки в режиме планировки (голда, падает в склад дома)")]
        public int price = 100;
    }

    [Tooltip("Тестовый набор v1: ~8–10 предметов (кровать, стол, стул, картина, часы, свеча)")]
    public List<Entry> entries = new List<Entry>();

    const string RESOURCE_PATH = "House/HouseCatalog";
    private static HouseCatalog loaded;

    public static HouseCatalog Load()
    {
        if (loaded == null)
            loaded = Resources.Load<HouseCatalog>(RESOURCE_PATH);
        return loaded;
    }

    /// <summary>Поверхность установки записи (через таблицу, фолбэк по папке).</summary>
    public static FurnitureMount MountOf(Entry e)
    {
        if (e == null || e.prefab == null)
            return FurnitureMount.Floor;
        FurnitureMountTable table = FurnitureMountTable.Load();
        if (table != null)
            return table.GetMount(e.prefab.name);
        // Таблицы нет — дефолт по имени префаба (Part 1 copiar_N → Wall).
        return FurnitureMountTable.DefaultForPrefabName(e.prefab.name);
    }
}
