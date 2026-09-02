using UnityEngine;
using UnityEditor;
using System.Linq;

/// <summary>
/// Генератор ассетов станков-переработчиков (CraftMachine):
/// предметы-продукты (Вино/Сыр/Масло/Джем), предметы-станки и префабы
/// (SpriteRenderer + BoxCollider2D + CraftMachine + YSort + InteractZone + ReadyIcon).
/// ИКОНКА ГОТОВНОСТИ — дочерний объект ReadyIcon в префабе: правится РУКАМИ
/// (размер/позиция/спрайт), скрипт только включает/выключает.
/// Запуск: Tools → Farm → Create Machines. Повторный запуск пересоздаёт ассеты
/// (станки в сейве не теряются — ключ по имени предмета).
/// </summary>
public static class MachineBuilder
{
    // ── Спрайты станков ──
    const string BarrelTex = "Assets/Art/Objects/Work Benches/fermentation barrel.png";
    const string CheeseTex = "Assets/Art/Objects/Work Benches/Cheese Press.png";
    const string ChurnTex = "Assets/Art/Objects/Work Benches/Butter Churn.png";
    const string JamTex = "Assets/Art/Objects/Work Benches/Jam Maker.png";

    // ── Иконки продуктов ──
    const string WineIconTex = "Assets/Art/Icons/Food Icons/Juices.png"; // кадр Juices_1 (красный бокал)
    const string CheeseIconTex = "Assets/Art/Icons/Food Icons/Cheese.png";
    const string ButterIconTex = "Assets/Art/Icons/Food Icons/Butter.png";
    const string JamIconTex = "Assets/Art/Icons/Food Icons/Jam.png";

    [MenuItem("Tools/Farm/Create Machines (бочка/пресс/маслобойка/джем)")]
    public static void Create()
    {
        // ── 1) Предметы-продукты ──
        ItemData wine = CreateProduct("Wine", "Вино", "Брожение винограда в бочке. Продай скупщику.",
            LoadSprite(WineIconTex, "Juices_1"), 0);
        ItemData cheese = CreateProduct("Cheese", "Сыр", "Из коровьего молока. Продай скупщику.",
            LoadSprite(CheeseIconTex, null), 25);
        ItemData butter = CreateProduct("Butter", "Масло", "Взбитое из молока. Продай скупщику.",
            LoadSprite(ButterIconTex, null), 15);
        ItemData jam = CreateProduct("Jam", "Джем", "Сварен из ягод. Продай скупщику.",
            LoadSprite(JamIconTex, null), 20);

        // ── 2) Входные предметы (уже есть в игре) ──
        ItemData grapes = ItemDatabase.Find("Grapes");
        ItemData milk = ItemDatabase.Find("Milk");
        ItemData strawberry = ItemDatabase.Find("Strawberry");
        ItemData blueberry = ItemDatabase.Find("Blueberry");
        if (grapes == null || milk == null || strawberry == null || blueberry == null)
            Debug.LogWarning("[Machines] Не найдены входные предметы (Grapes/Milk/Strawberry/Blueberry) — рецепты будут пустыми");

        // ── 3) Префабы + предметы-станки ──
        CreateMachine("WineBarrel", "Бочка брожения", BarrelTex,
            new[] { MakeRecipe(grapes, wine, 1, 480f) },
            0, 1, 3, -1, 800);
        CreateMachine("CheesePress", "Сырный пресс", CheeseTex,
            new[] { MakeRecipe(milk, cheese, 1, 360f) },
            0, 1, 4, 5, 1200);
        CreateMachine("ButterChurn", "Маслобойка", ChurnTex,
            new[] { MakeRecipe(milk, butter, 1, 300f) },
            0, 1, 3, -1, 1000);
        CreateMachine("JamMaker", "Джем-мейкер", JamTex,
            new[] { MakeRecipe(strawberry, jam, 1, 420f), MakeRecipe(blueberry, jam, 1, 420f) },
            0, 1, 4, 5, 1200);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Machines] Готово. Станки появятся у торговца (вкладка животных) автоматически.");
    }

    static CraftMachine.MachineRecipe MakeRecipe(ItemData input, ItemData output, int ratio, float seconds)
    {
        if (input == null || output == null) return null;
        return new CraftMachine.MachineRecipe
        {
            input = input,
            output = output,
            inputPerOutput = ratio,
            processSeconds = seconds
        };
    }

    // ═══════════════════════════════════════════════════════════
    // ПРЕДМЕТ-ПРОДУКТ
    // ═══════════════════════════════════════════════════════════
    static ItemData CreateProduct(string assetName, string title, string desc, Sprite icon, int heal)
    {
        EnsureFolder("Assets/Resources/Items/Crafted");
        string path = "Assets/Resources/Items/Crafted/" + assetName + ".asset";

        ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(item, path);
        }

        item.itemName = title;
        item.description = desc + (heal > 0 ? " Лечит " + heal + " HP." : "");
        item.icon = icon;
        item.worldSprite = icon;
        item.itemType = heal > 0 ? ItemType.Consumable : ItemType.Material;
        item.rarity = ItemRarity.Common;
        item.isStackable = true;
        item.maxStack = 99;
        item.healAmount = heal;
        EditorUtility.SetDirty(item);
        return item;
    }

    // ═══════════════════════════════════════════════════════════
    // ПРЕФАБ + ПРЕДМЕТ-СТАНОК
    // ═══════════════════════════════════════════════════════════
    static void CreateMachine(string assetName, string titleRu, string texturePath,
        CraftMachine.MachineRecipe[] recipes, int idleIndex, int workFirst, int workLast,
        int readyIndex, int price)
    {
        Sprite[] frames = LoadSpritesSorted(texturePath);
        if (frames.Length == 0)
        {
            Debug.LogError("[Machines] Не найдены спрайты: " + texturePath);
            return;
        }

        EnsureFolder("Assets/Prefab/Machines");
        EnsureFolder("Assets/Resources/Items/Machines");

        string prefabPath = "Assets/Prefab/Machines/" + assetName + ".prefab";
        string itemPath = "Assets/Resources/Items/Machines/" + assetName + ".asset";

        // Выходной продукт из первого рецепта — его иконка пойдёт в ReadyIcon
        ItemData outProduct = recipes != null && recipes.Length > 0 && recipes[0] != null
            ? recipes[0].output : null;

        GameObject prefab = CreatePrefab(assetName, titleRu, frames, recipes,
            idleIndex, workFirst, workLast, readyIndex, outProduct, prefabPath);
        CreateMachineItem(assetName, titleRu, frames[Mathf.Clamp(idleIndex, 0, frames.Length - 1)],
            prefab, itemPath, price);
    }

    static GameObject CreatePrefab(string goName, string titleRu, Sprite[] frames,
        CraftMachine.MachineRecipe[] recipes, int idleIndex, int workFirst, int workLast,
        int readyIndex, ItemData readyIconSprite, string prefabPath)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null) AssetDatabase.DeleteAsset(prefabPath);

        GameObject root = new GameObject(goName);

        var sr = root.AddComponent<SpriteRenderer>();
        sr.sprite = frames[Mathf.Clamp(idleIndex, 0, frames.Length - 1)];
        sr.sortingOrder = 0;

        var col = root.AddComponent<BoxCollider2D>();
        col.isTrigger = false; // нельзя ставить предметы друг на друга
        col.offset = new Vector2(0f, 0.1f);
        col.size = new Vector2(0.9f, 0.7f);

        var machine = root.AddComponent<CraftMachine>();
        machine.selfItemName = goName;
        machine.displayNameRu = titleRu;
        machine.recipes = recipes;
        machine.idleSprite = frames[Mathf.Clamp(idleIndex, 0, frames.Length - 1)];
        machine.workingFrames = Slice(frames, workFirst, workLast);
        machine.workingFps = 6f;
        machine.readySprite = readyIndex >= 0 && readyIndex < frames.Length ? frames[readyIndex] : null;
        machine.batchCapacity = 5;

        root.AddComponent<YSort>();

        // Зона удара (слой Interactable, триггер) — как у улья
        int interactLayer = LayerMask.NameToLayer("Interactable");
        var zone = new GameObject("InteractZone");
        zone.transform.SetParent(root.transform, false);
        zone.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        if (interactLayer >= 0) zone.layer = interactLayer;
        var zc = zone.AddComponent<BoxCollider2D>();
        zc.isTrigger = true;
        zc.offset = new Vector2(0f, 0.25f);
        zc.size = new Vector2(1.8f, 1.4f);

        // ИКОНКА ГОТОВНОСТИ: правится РУКАМИ в префабе (позиция/размер/спрайт).
        // Скрипт только включает/выключает этот объект.
        var icon = new GameObject("ReadyIcon");
        icon.transform.SetParent(root.transform, false);
        icon.transform.localPosition = new Vector3(0f, 0.95f, 0f);
        icon.transform.localScale = new Vector3(0.6f, 0.6f, 1f);
        var iconSr = icon.AddComponent<SpriteRenderer>();
        iconSr.sprite = readyIconSprite != null ? readyIconSprite.icon : null;
        iconSr.sortingOrder = 10;
        icon.SetActive(false);

        machine.readyIcon = icon;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void CreateMachineItem(string assetName, string titleRu, Sprite icon, GameObject prefab,
        string itemPath, int price)
    {
        ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(itemPath);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(item, itemPath);
        }

        item.itemName = titleRu;
        item.description = "Ставь через хотбар: призрак поедет за тобой, удар — поставить. Удар с продуктом в руках — загрузить. Готовый станок бей ударом — забрать продукцию.";
        item.icon = icon;
        item.worldSprite = icon;
        item.itemType = ItemType.Processor;
        item.rarity = ItemRarity.Common;
        item.isStackable = true;
        item.maxStack = 5;
        item.placeablePrefab = prefab;
        item.shopPrice = price;
        EditorUtility.SetDirty(item);
    }

    static Sprite[] Slice(Sprite[] frames, int first, int last)
    {
        if (frames == null || first < 0 || last >= frames.Length || first > last) return null;
        Sprite[] slice = new Sprite[last - first + 1];
        for (int i = first; i <= last; i++) slice[i - first] = frames[i];
        return slice;
    }

    static Sprite[] LoadSpritesSorted(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>()
            .OrderBy(s => s.name, System.StringComparer.Ordinal)
            .ToArray();
    }

    static Sprite LoadSprite(string path, string spriteName)
    {
        return LoadSpritesSorted(path).FirstOrDefault(s => s.name == spriteName);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
