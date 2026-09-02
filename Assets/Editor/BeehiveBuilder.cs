using UnityEngine;
using UnityEditor;
using System.Linq;

/// <summary>
/// Генератор ассетов улья: предметы «Улей» и «Соты» + префаб Beehive.prefab
/// (спрайты стадий Beehive_0..6, кадры пчелы Bees_0..3, InteractZone).
/// Запуск: Tools → Farm → Create Beehive (улей + пчёлы + соты).
/// Повторный запуск пересоздаёт ассеты (данные улья в сейве не теряются —
/// ключ по имени предмета "Beehive").
/// </summary>
public static class BeehiveBuilder
{
    const string HiveTexturePath = "Assets/Art/Animals/Forest/Bugs/Bee/Beehive.png";
    const string BeeTexturePath = "Assets/Art/Animals/Forest/Bugs/Bee/Bees.png";
    const string PrefabPath = "Assets/Prefab/Beehive.prefab";
    const string HiveItemPath = "Assets/Resources/Items/Animals/Beehive.asset";
    const string HoneycombItemPath = "Assets/Resources/Items/Animal/Honeycomb.asset";

    [MenuItem("Tools/Farm/Create Beehive (улей + пчёлы + соты)")]
    public static void Create()
    {
        Sprite[] hiveStages = LoadSpritesSorted(HiveTexturePath);
        Sprite[] beeFrames = LoadSpritesSorted(BeeTexturePath);
        if (hiveStages.Length == 0 || beeFrames.Length == 0)
        {
            Debug.LogError("[Beehive] Не найдены спрайты: " + HiveTexturePath + " / " + BeeTexturePath);
            return;
        }

        // ── 1) Предмет «Соты» ──
        ItemData honeycomb = CreateHoneycomb(beeFrames[0]);

        // ── 2) Префаб улья ──
        GameObject prefab = CreatePrefab(hiveStages, beeFrames, honeycomb);

        // ── 3) Предмет «Улей» ──
        CreateHiveItem(hiveStages[0], prefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Beehive] Готово: " + PrefabPath + ", " + HiveItemPath + ", " + HoneycombItemPath +
                  ". Улей появится у торговца (вкладка животных) автоматически.");
    }

    static ItemData CreateHoneycomb(Sprite icon)
    {
        ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(HoneycombItemPath);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(item, HoneycombItemPath);
        }

        item.itemName = "Соты";
        item.description = "Пчёлы заполнили ими улей. Можно продать скупщику или переработать.";
        item.icon = icon;
        item.worldSprite = icon;
        item.itemType = ItemType.Material;
        item.rarity = ItemRarity.Common;
        item.isStackable = true;
        item.maxStack = 99;
        EditorUtility.SetDirty(item);
        return item;
    }

    static GameObject CreatePrefab(Sprite[] hiveStages, Sprite[] beeFrames, ItemData honeycomb)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null) AssetDatabase.DeleteAsset(PrefabPath);

        GameObject root = new GameObject("Beehive");

        var sr = root.AddComponent<SpriteRenderer>();
        sr.sprite = hiveStages[0];
        sr.sortingOrder = 0;

        var col = root.AddComponent<BoxCollider2D>();
        col.isTrigger = false; // нельзя ставить предметы друг на друга
        col.offset = new Vector2(0f, 0.1f);
        col.size = new Vector2(0.8f, 0.7f);

        var hive = root.AddComponent<Beehive>();
        hive.stages = hiveStages;
        hive.beeFrames = beeFrames;
        hive.honeycombItem = honeycomb;
        hive.beeCount = 2;
        hive.fillTimeMinutes = 12f; // полный улей ~12 минут
        hive.wanderRadius = 9f;

        root.AddComponent<YSort>();

        // Зона удара (слой Interactable, триггер) — как у кормушки
        int interactLayer = LayerMask.NameToLayer("Interactable");
        var zone = new GameObject("InteractZone");
        zone.transform.SetParent(root.transform, false);
        zone.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        if (interactLayer >= 0) zone.layer = interactLayer;
        var zc = zone.AddComponent<BoxCollider2D>();
        zc.isTrigger = true;
        zc.offset = new Vector2(0f, 0.25f);
        zc.size = new Vector2(1.8f, 1.4f);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void CreateHiveItem(Sprite icon, GameObject prefab)
    {
        ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(HiveItemPath);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(item, HiveItemPath);
        }

        item.itemName = "Улей";
        item.description = "Пчёлы носят в него мёд. Выбери в хотбаре и поставь: призрак поедет за тобой, удар — поставить. Полный улей бей ударом — выпадут соты.";
        item.icon = icon;
        item.worldSprite = icon;
        item.itemType = ItemType.Beehive;
        item.rarity = ItemRarity.Common;
        item.isStackable = true;
        item.maxStack = 5;
        item.placeablePrefab = prefab;
        item.shopPrice = 800;
        EditorUtility.SetDirty(item);
    }

    static Sprite[] LoadSpritesSorted(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>()
            .OrderBy(s => s.name, System.StringComparer.Ordinal)
            .ToArray();
    }
}
