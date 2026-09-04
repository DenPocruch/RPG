using UnityEngine;
using UnityEditor;
using System.Linq;

/// <summary>
/// Генератор ассетов шахты: предметы руды/самоцветов (Resources/Items/Ore/)
/// и префабы жил (Prefab/Mine/) со скриптом OreVeinComponent.
/// Запуск: Tools → Mine → Create Ores and Veins. Повторный запуск обновляет данные
/// in place (guid префабов и инстансы в сценах целы, crackedSprite не затирается).
/// Тир кирки: камень 1, медь 2, железо 3, серебро/золото 4, самоцветы 5, обсидиан 6.
/// Карты спрайтов (листы уже нарезаны, pivot {0.5,0}). Все стили листа идут
/// 10 колонками минералов в одном порядке: медь, серебро, золото, железо,
/// аметист, рубин, изумруд, сапфир, обсидиан, розовый кварц. Спрайт минерала N
/// = базовый индекс стиля + номер колонки.
///   stone with minerals   1+col — большой камень с самоцветами (обычная жила)
///   stone with minerals  15+col — двойной камень (богатая жила)
///   stone with minerals  39+col — малый камень, крупные вкрапления
///   stone with minerals  50+col — малый камешек
///   stone with minerals  61+col — средний кристалл
///   stone with minerals  72+col — кристальный нарост
///   stone with minerals  83+col — малый кластер кристаллов
///   stone with minerals  94+col — блок-самородок (кубики)
///   stone with minerals 105+col — капля-кристалл (и иконки самоцветов)
///   stone with minerals 116+col — капля-кристалл, вариант 2
///   stone with minerals 127+col — мини-кластер
///   stone with minerals   0/14 — камень без руды (жила камня)
///   Props Mine         182-185 — иконки руды (куски: медь/серебро/золото/железо)
/// Белые заготовки (11-13, 26-28, 34-38, 49, 60, 71, 82, 93, 104, 115, 126, 137,
/// 141-143) и обломки-декор (29-33, 138-140) НЕ используются.
/// </summary>
public static class MineBuilder
{
    const string MineralsTex = "Assets/Art/Objects/Exterior/Mine and Dungeon/stone with minerals.png";
    const string PropsTex = "Assets/Art/Objects/Exterior/Mine and Dungeon/Props Mine.png";
    const string MineralsPrefix = "stone with minerals_";
    const string PropsPrefix = "Props Mine_";
    const string LootPrefabPath = "Assets/Resources/LootItemPrefab.prefab";

    class MineralSpec
    {
        public string asset, title, desc;
        public ItemRarity rarity;
        public int iconIndex;      // индекс иконки предмета
        public string iconTex;     // лист с иконкой (MineralsTex = самоцвет-капля, PropsTex = кусок руды)
        public int hits;           // ударов большой жилой
        public int toolTier;       // уровень кирки: камень 1, медь 2, железо 3, серебро/золото 4, самоцветы 5, обсидиан 6
    }

    // Порядок колонок листа: медь, серебро, золото, железо, аметист, рубин, изумруд, сапфир, обсидиан, кварц
    static readonly MineralSpec[] Minerals = new MineralSpec[]
    {
        new MineralSpec { asset="CopperOre",  title="Медная руда",    rarity=ItemRarity.Common,   iconTex=PropsTex,    iconIndex=182, hits=4, toolTier=2, desc="Обычная руда. Годится для переплавки." },
        new MineralSpec { asset="SilverOre",  title="Серебряная руда",rarity=ItemRarity.Uncommon, iconTex=PropsTex,    iconIndex=183, hits=5, toolTier=4, desc="Редкая руда. Годится для переплавки." },
        new MineralSpec { asset="GoldOre",    title="Золотая руда",   rarity=ItemRarity.Rare,     iconTex=PropsTex,    iconIndex=184, hits=6, toolTier=4, desc="Ценная руда. Годится для переплавки." },
        new MineralSpec { asset="IronOre",    title="Железная руда",  rarity=ItemRarity.Common,   iconTex=PropsTex,    iconIndex=185, hits=5, toolTier=3, desc="Крепкая руда. Годится для переплавки." },
        new MineralSpec { asset="Amethyst",   title="Аметист",        rarity=ItemRarity.Uncommon, iconTex=MineralsTex, iconIndex=109, hits=5, toolTier=5, desc="Фиолетовый самоцвет." },
        new MineralSpec { asset="Ruby",       title="Рубин",          rarity=ItemRarity.Rare,     iconTex=MineralsTex, iconIndex=110, hits=5, toolTier=5, desc="Красный самоцвет." },
        new MineralSpec { asset="Emerald",    title="Изумруд",        rarity=ItemRarity.Rare,     iconTex=MineralsTex, iconIndex=111, hits=5, toolTier=5, desc="Зелёный самоцвет." },
        new MineralSpec { asset="Sapphire",   title="Сапфир",         rarity=ItemRarity.Epic,     iconTex=MineralsTex, iconIndex=112, hits=6, toolTier=5, desc="Синий самоцвет. Дорогая находка." },
        new MineralSpec { asset="Obsidian",   title="Обсидиан",       rarity=ItemRarity.Epic,     iconTex=MineralsTex, iconIndex=113, hits=6, toolTier=6, desc="Чёрный вулканический камень с острыми гранями." },
        new MineralSpec { asset="RoseQuartz", title="Розовый кварц",  rarity=ItemRarity.Uncommon, iconTex=MineralsTex, iconIndex=114, hits=5, toolTier=5, desc="Нежно-розовый самоцвет." },
    };

    class VeinStyle
    {
        public string suffix;   // хвост имени префаба
        public int baseIndex;   // базовый индекс спрайта стиля (спрайт = baseIndex + колонка)
        public int hits;        // ударов до разрушения
        public int amount;      // дроп за 1 жилу
    }

    // Стили жил: (суффикс, база спрайтов, ударов, дроп). Колонка минерала добавляется к базе.
    static readonly VeinStyle[] Styles = new VeinStyle[]
    {
        new VeinStyle { suffix="",        baseIndex=1,   hits=0, amount=0 }, // большая жила — hits/amount из MineralSpec
        new VeinStyle { suffix="_Rich",   baseIndex=15,  hits=0, amount=0 }, // богатая — ×2 дроп
        new VeinStyle { suffix="_Pebble", baseIndex=39,  hits=2, amount=1 },
        new VeinStyle { suffix="_Small",  baseIndex=50,  hits=2, amount=1 },
        new VeinStyle { suffix="_Gem",    baseIndex=61,  hits=4, amount=1 },
        new VeinStyle { suffix="_Crystal",baseIndex=72,  hits=6, amount=2 },
        new VeinStyle { suffix="_Cluster",baseIndex=83,  hits=3, amount=1 },
        new VeinStyle { suffix="_Nugget", baseIndex=94,  hits=5, amount=2 },
        new VeinStyle { suffix="_Spike",  baseIndex=105, hits=4, amount=1 },
        new VeinStyle { suffix="_SpikeAlt",baseIndex=116, hits=4, amount=1 },
        new VeinStyle { suffix="_Mini",   baseIndex=127, hits=3, amount=1 },
    };

    [MenuItem("Tools/Mine/Create Ores and Veins (руда + жилы)")]
    public static void Create()
    {
        GameObject lootPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LootPrefabPath);
        if (lootPrefab == null)
        {
            Debug.LogError("[Mine] Не найден " + LootPrefabPath);
            return;
        }

        // ── 1) Камень — предмет уже существует (Resources/Items/Ore/Stones.asset) ──
        ItemData stones = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Resources/Items/Ore/Stones.asset");
        if (stones == null) Debug.LogWarning("[Mine] Stones.asset не найден — жила камня будет без дропа");

        // ── 2) Предметы всех 10 минералов ──
        foreach (var m in Minerals)
            CreateOre(m);

        // ── 3) Жилы: все стили × 10 минералов + камень без руды ──
        CreateVein("OreVein_Stone", stones, 0, 3, 2, 1, lootPrefab);
        CreateVein("OreVein_Stone_Rich", stones, 14, 3, 4, 1, lootPrefab);

        for (int col = 0; col < Minerals.Length; col++)
        {
            var m = Minerals[col];
            ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(
                "Assets/Resources/Items/Ore/" + m.asset + ".asset");
            bool isGem = m.iconTex == MineralsTex; // самоцветы: базовый дроп 1, руда 2
            int baseAmount = isGem ? 1 : 2;

            foreach (var style in Styles)
            {
                string name = "OreVein_" + m.asset + style.suffix;
                bool isBig = style.baseIndex == 1, isRich = style.baseIndex == 15;
                int hits = isBig || isRich ? m.hits : style.hits;
                int amount = isRich ? baseAmount * 2 : (isBig ? baseAmount : style.amount);
                CreateVein(name, item, style.baseIndex + col, hits, amount, m.toolTier, lootPrefab);
            }
        }

        // ── 4) Кирка в магазин (цена на ассете) ──
        ItemData pickaxe = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Resources/Items/Pickaxe.asset");
        if (pickaxe != null && pickaxe.shopPrice <= 0)
        {
            pickaxe.shopPrice = 300;
            EditorUtility.SetDirty(pickaxe);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Mine] Готово: 10 предметов + 112 жил (11 стилей × 10 минералов + 2 каменные), кирка в магазине.");
    }

    // ═══════════════════════════════════════════════════════════
    // ПРЕДМЕТ РУДЫ / САМОЦВЕТА
    // ═══════════════════════════════════════════════════════════
    static void CreateOre(MineralSpec m)
    {
        EnsureFolder("Assets/Resources/Items/Ore");
        string path = "Assets/Resources/Items/Ore/" + m.asset + ".asset";

        Sprite icon = LoadSprite(m.iconTex, (m.iconTex == MineralsTex ? MineralsPrefix : PropsPrefix) + m.iconIndex);
        if (icon == null) Debug.LogError("[Mine] Нет спрайта иконки " + m.iconIndex + " для " + m.asset);

        ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(item, path);
        }

        item.itemName = m.title;
        item.description = m.desc + " Продай скупщику.";
        item.icon = icon;
        item.worldSprite = icon;
        item.itemType = ItemType.Material;
        item.rarity = m.rarity;
        item.isStackable = true;
        item.maxStack = 99;
        item.resourceCategory = "Ore";
        item.requiredToolTier = m.toolTier;
        EditorUtility.SetDirty(item);
    }

    // ═══════════════════════════════════════════════════════════
    // ПРЕФАБ ЖИЛЫ
    // ═══════════════════════════════════════════════════════════
    // Префаб жилы. ОБНОВЛЯЕТ in place (guid и инстансы в сценах целы,
    // ручные правки crackedSprite не затираются) — перезапуск безопасен.
    static void CreateVein(string prefabName, ItemData ore, int spriteIndex,
        int hits, int amount, int toolTier, GameObject lootPrefab)
    {
        EnsureFolder("Assets/Prefab/Mine");
        string prefabPath = "Assets/Prefab/Mine/" + prefabName + ".prefab";

        Sprite full = LoadSprite(MineralsTex, MineralsPrefix + spriteIndex);
        if (full == null)
        {
            Debug.LogError("[Mine] Нет спрайта " + spriteIndex + " для " + prefabName);
            return;
        }

        GameObject root = null;
        bool isNew = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null;
        if (isNew)
        {
            root = new GameObject(prefabName);
            root.layer = LayerMask.NameToLayer("Tree");
            var srNew = root.AddComponent<SpriteRenderer>();
            srNew.sprite = full;
            var colNew = root.AddComponent<BoxCollider2D>();
            colNew.isTrigger = false;
            root.AddComponent<OreVeinComponent>();
            root.AddComponent<YSort>();
        }
        else
        {
            root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) { Debug.LogError("[Mine] Не открылся префаб: " + prefabPath); return; }
            root.layer = LayerMask.NameToLayer("Tree");
        }

        var sr = root.GetComponent<SpriteRenderer>();
        if (sr != null) sr.sprite = full;

        var col = root.GetComponent<BoxCollider2D>();
        if (col != null)
        {
            bool twoTiles = full.rect.width > 24f; // большие жилы 32px (2 тайла), остальные 16px
            col.isTrigger = false;
            col.offset = new Vector2(0f, 0.25f);
            col.size = new Vector2(twoTiles ? 1.9f : 0.9f, 0.5f);
        }

        var vein = root.GetComponent<OreVeinComponent>();
        if (vein == null) vein = root.AddComponent<OreVeinComponent>();
        vein.oreItem = ore;
        vein.oreAmount = amount;
        vein.maxHealth = hits;
        vein.requiredToolTier = toolTier;
        vein.fullSprite = full;
        // crackedSprite НЕ трогаем — ставится вручную, если найдётся подходящий спрайт
        vein.dropRadius = 0.5f;
        vein.respawns = true;
        vein.respawnTime = 300f;
        vein.lootItemPrefab = lootPrefab;

        if (root.GetComponent<YSort>() == null) root.AddComponent<YSort>();

        if (isNew)
        {
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
        }
        else
        {
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath); // тот же путь = guid цел
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Sprite LoadSprite(string path, string spriteName)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>()
            .FirstOrDefault(s => s.name == spriteName);
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