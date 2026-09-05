using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Вся рыба игры: FishData (Resources/Fish/River|Sea) + предметы (Resources/Items/Fish/River|Sea).
/// Иконка = спрайт *_0, worldSprite = *_1 (нарезка 16×16 уже сделана руками).
/// Повторный запуск ОБНОВЛЯЕТ in place: у существующих правит спрайты, ссылку
/// fishItem, тир (TIERS) и вес (WEIGHTS). Цены/сложность/имена/описания/бонусы —
/// ручные, НЕ трёт. Новым ставит всё из таблиц ниже.
/// </summary>
public static class FishBuilder
{
    const string SPR_RIVER = "Assets/Art/Icons/Fish/River";
    const string SPR_SEA = "Assets/Art/Icons/Fish/Sea";
    const string DATA_RIVER = "Assets/Resources/Fish/River";
    const string DATA_SEA = "Assets/Resources/Fish/Sea";
    const string ITEM_RIVER = "Assets/Resources/Items/Fish/River";
    const string ITEM_SEA = "Assets/Resources/Items/Fish/Sea";

    struct FishDef
    {
        public string file;   // имя PNG без расширения, КАК В ПАПКЕ (Shad с пробелом!)
        public string key;    // суффикс ассета Fish_<key>
        public string ru;     // русское имя
        public string desc;
        public int price;
        public int diff;      // 0 обычная / 1 редкая / 2 легендарная
        public FishDef(string f, string k, string r, string d, int p, int df)
        { file = f; key = k; ru = r; desc = d; price = p; diff = df; }
    }

    // ── РЕКА ─────────────────────────────────────────────────
    static readonly FishDef[] RIVER = new FishDef[]
    {
        new FishDef("Bone Fish", "BoneFish", "Костяная рыба", "Обычная речная рыба. Одни кости.", 25, 0),
        new FishDef("Bullhead Catfish", "BullheadCatfish", "Сом-бычок", "Обычная речная рыба.", 35, 0),
        // Уже есть руками — цифры не тронем (обновятся только спрайты *_0/*_1)
        new FishDef("Carp", "Carp", "Карп", "Крепкий речной боец.", 40, 1),
        new FishDef("Chub", "Chub", "Голавль", "Обычная речная рыба.", 20, 0),
        new FishDef("Dorado", "Dorado", "Дорадо", "Редкая речная рыба.", 120, 1),
        new FishDef("Dynamite Fish", "DynamiteFish", "Рыба-динамит", "Редкая речная рыба. Бахает.", 100, 1),
        new FishDef("Faeries Fish", "FaeriesFish", "Волшебная рыбка", "Редкая речная рыба.", 130, 1),
        new FishDef("Ghost Catfish", "GhostCatfish", "Сом-призрак", "Редкая речная рыба.", 90, 1),
        // Уже есть руками (Fish_Gold) — цифры не тронем
        new FishDef("Golden Fish", "Gold", "Золотая рыбка", "Легенда пресных вод.", 120, 2),
        new FishDef("Large Mouth Bass", "LargeMouthBass", "Большеротый окунь", "Обычная речная рыба.", 45, 0),
        new FishDef("Perch", "Perch", "Окунь", "Обычная речная рыба.", 15, 0),
        new FishDef("Pike Fish", "PikeFish", "Щука", "Редкая речная рыба.", 55, 1),
        new FishDef("Shad ", "Shad", "Шед", "Обычная речная рыба.", 25, 0),
        new FishDef("Sturgeon", "Sturgeon", "Осётр", "Редкая речная рыба.", 140, 1),
        new FishDef("Sunfish", "Sunfish", "Солнечный окунь", "Обычная речная рыба.", 30, 0),
        new FishDef("Tiger Trout", "TigerTrout", "Тигровая форель", "Редкая речная рыба.", 85, 1),
        new FishDef("Walleye", "Walleye", "Судак", "Обычная речная рыба.", 45, 0),
        new FishDef("Zombie Fish", "ZombieFish", "Рыба-зомби", "Редкая речная рыба.", 110, 1),
    };

    // ── МОРЕ ─────────────────────────────────────────────────
    static readonly FishDef[] SEA = new FishDef[]
    {
        new FishDef("Albacore", "Albacore", "Альбакор", "Редкая морская рыба.", 70, 1),
        new FishDef("Anchovy", "Anchovy", "Анчоус", "Обычная морская рыба.", 15, 0),
        new FishDef("Anglerfish", "Anglerfish", "Удильщик", "Редкая морская рыба.", 130, 1),
        new FishDef("BlobFish", "BlobFish", "Рыба-капля", "Обычная морская рыба.", 65, 0),
        new FishDef("Bream", "Bream", "Морской лещ", "Обычная морская рыба.", 30, 0),
        new FishDef("Clownfish", "Clownfish", "Рыба-клоун", "Обычная морская рыба.", 45, 0),
        new FishDef("Crimson Snapper", "CrimsonSnapper", "Багряный луциан", "Редкая морская рыба.", 95, 1),
        new FishDef("Devil Fish", "DevilFish", "Манта", "Редкая морская рыба.", 120, 1),
        new FishDef("Dolphin", "Dolphin", "Дельфин", "Легенда морских вод. Это вообще не рыба!", 200, 2),
        new FishDef("Flounder", "Flounder", "Камбала", "Обычная морская рыба.", 35, 0),
        new FishDef("Glacier Fish", "GlacierFish", "Ледяная рыба", "Легенда морских вод.", 160, 2),
        new FishDef("Goby", "Goby", "Бычок", "Обычная морская рыба.", 15, 0),
        new FishDef("Halibut", "Halibut", "Палтус", "Редкая морская рыба.", 80, 1),
        new FishDef("Herring", "Herring", "Сельдь", "Обычная морская рыба.", 15, 0),
        new FishDef("Lingcod", "Lingcod", "Терпуг", "Обычная морская рыба.", 55, 0),
        new FishDef("LionFish", "LionFish", "Крылатка", "Редкая морская рыба.", 105, 1),
        new FishDef("pufferfish", "Pufferfish", "Иглобрюх", "Редкая морская рыба.", 90, 1),
        new FishDef("Red Mullet", "RedMullet", "Барабулька", "Обычная морская рыба.", 40, 0),
        new FishDef("Red Snapper", "RedSnapper", "Красный луциан", "Редкая морская рыба.", 75, 1),
        new FishDef("Regal Blue Tang", "RegalBlueTang", "Голубой хирург", "Редкая морская рыба.", 85, 1),
        new FishDef("Salmon", "Salmon", "Лосось", "Редкая морская рыба.", 70, 1),
        // Уже есть руками — цифры не тронем
        new FishDef("Sardine", "Sardine", "Сардина", "Обычная морская рыба.", 15, 0),
        new FishDef("Sea bullhead", "SeaBullhead", "Морской бычок", "Обычная морская рыба.", 20, 0),
        new FishDef("Smallmouth Bass", "SmallmouthBass", "Малоротый окунь", "Обычная морская рыба.", 40, 0),
        new FishDef("Tuna", "Tuna", "Тунец", "Редкая морская рыба.", 110, 1),
    };

    // ── Тир силы 1-6 по ключу ассета (силовая модель: gap = fishTier - toolTier удочки).
    // t1 мелочь (дерево тащит легко), t3 деревом почти нереально, t5-6 — только топ-удочки ──
    static readonly Dictionary<string, int> TIERS = new Dictionary<string, int>
    {
        { "Perch", 1 }, { "Chub", 1 }, { "Goby", 1 }, { "Herring", 1 }, { "Anchovy", 1 },
        { "SeaBullhead", 1 }, { "Sunfish", 1 }, { "Sardine", 1 }, { "Shad", 1 },
        { "RedMullet", 1 }, { "Bream", 1 }, { "BlobFish", 1 },
        { "Carp", 2 }, { "BullheadCatfish", 2 }, { "BoneFish", 2 }, { "LargeMouthBass", 2 },
        { "SmallmouthBass", 2 }, { "Walleye", 2 }, { "Flounder", 2 }, { "Clownfish", 2 },
        { "Lingcod", 2 },
        { "PikeFish", 3 }, { "TigerTrout", 3 }, { "Salmon", 3 }, { "GhostCatfish", 3 },
        { "ZombieFish", 3 }, { "FaeriesFish", 3 }, { "RedSnapper", 3 }, { "RegalBlueTang", 3 },
        { "Halibut", 3 }, { "Albacore", 3 },
        { "Sturgeon", 4 }, { "Dorado", 4 }, { "LionFish", 4 }, { "Pufferfish", 4 },
        { "CrimsonSnapper", 4 }, { "DevilFish", 4 }, { "Anglerfish", 4 }, { "DynamiteFish", 4 },
        { "Gold", 5 }, { "Tuna", 5 },
        { "GlacierFish", 6 }, { "Dolphin", 6 },
    };

    static int TierOf(string key) => TIERS.TryGetValue(key, out int v) ? Mathf.Clamp(v, 1, 6) : 1;

    // ── Вес улова в кг (мин/макс по ключу; ориентиры из реальных данных) ──
    static readonly Dictionary<string, float[]> WEIGHTS = new Dictionary<string, float[]>
    {
        // река
        { "BoneFish", new[]{ 0.3f, 1.5f } }, { "BullheadCatfish", new[]{ 0.2f, 1f } },
        { "Carp", new[]{ 1f, 8f } }, { "Chub", new[]{ 0.2f, 1.5f } },
        { "Dorado", new[]{ 3f, 15f } }, { "DynamiteFish", new[]{ 0.5f, 2f } },
        { "FaeriesFish", new[]{ 0.1f, 0.5f } }, { "GhostCatfish", new[]{ 0.5f, 3f } },
        { "Gold", new[]{ 0.2f, 1f } }, { "LargeMouthBass", new[]{ 0.5f, 4f } },
        { "Perch", new[]{ 0.1f, 0.8f } }, { "PikeFish", new[]{ 1f, 10f } },
        { "Shad", new[]{ 0.5f, 2.5f } }, { "Sturgeon", new[]{ 5f, 60f } },
        { "Sunfish", new[]{ 0.1f, 0.5f } }, { "TigerTrout", new[]{ 0.5f, 4f } },
        { "Walleye", new[]{ 0.5f, 5f } }, { "ZombieFish", new[]{ 0.5f, 2f } },
        // море
        { "Albacore", new[]{ 5f, 30f } }, { "Anchovy", new[]{ 0.01f, 0.05f } },
        { "Anglerfish", new[]{ 1f, 10f } }, { "BlobFish", new[]{ 2f, 10f } },
        { "Bream", new[]{ 0.3f, 2f } }, { "Clownfish", new[]{ 0.05f, 0.2f } },
        { "CrimsonSnapper", new[]{ 1f, 8f } }, { "DevilFish", new[]{ 10f, 60f } },
        { "Dolphin", new[]{ 30f, 150f } }, { "Flounder", new[]{ 0.3f, 3f } },
        { "GlacierFish", new[]{ 2f, 12f } }, { "Goby", new[]{ 0.01f, 0.05f } },
        { "Halibut", new[]{ 5f, 50f } }, { "Herring", new[]{ 0.05f, 0.3f } },
        { "Lingcod", new[]{ 1f, 15f } }, { "LionFish", new[]{ 0.2f, 1.5f } },
        { "Pufferfish", new[]{ 0.3f, 3f } }, { "RedMullet", new[]{ 0.1f, 0.8f } },
        { "RedSnapper", new[]{ 1f, 10f } }, { "RegalBlueTang", new[]{ 0.2f, 1f } },
        { "Salmon", new[]{ 1f, 12f } }, { "Sardine", new[]{ 0.02f, 0.1f } },
        { "SeaBullhead", new[]{ 0.1f, 0.6f } }, { "SmallmouthBass", new[]{ 0.3f, 3f } },
        { "Tuna", new[]{ 10f, 120f } },
    };

    static float[] WeightOf(string key)
        => WEIGHTS.TryGetValue(key, out float[] v) ? v : new float[] { 0.1f, 1f };

    // Старые плоские ассеты → в подпапки (guid целы, ссылки чинятся сами)
    static readonly string[][] LEGACY_MOVES = new string[][]
    {
        new[] { "Assets/Resources/Fish/Fish_Sardine.asset", DATA_SEA + "/Fish_Sardine.asset" },
        new[] { "Assets/Resources/Fish/Fish_Carp.asset", DATA_RIVER + "/Fish_Carp.asset" },
        new[] { "Assets/Resources/Fish/Fish_Gold.asset", DATA_RIVER + "/Fish_Gold.asset" },
        new[] { "Assets/Resources/Items/Fish/Fish_Sardine.asset", ITEM_SEA + "/Fish_Sardine.asset" },
        new[] { "Assets/Resources/Items/Fish/Fish_Carp.asset", ITEM_RIVER + "/Fish_Carp.asset" },
        new[] { "Assets/Resources/Items/Fish/Fish_Gold.asset", ITEM_RIVER + "/Fish_Gold.asset" },
    };

    [MenuItem("Tools/Fish/7. Build All Fish (река+море)")]
    public static void BuildAll()
    {
        EnsureFolders();
        int moved = 0;
        foreach (var mv in LEGACY_MOVES)
        {
            if (AssetDatabase.LoadMainAssetAtPath(mv[0]) != null
                && AssetDatabase.LoadMainAssetAtPath(mv[1]) == null)
            {
                string err = AssetDatabase.MoveAsset(mv[0], mv[1]);
                if (string.IsNullOrEmpty(err)) moved++;
                else Debug.LogWarning("[Fish] Не перенёс " + mv[0] + ": " + err);
            }
        }

        int created = 0, updated = 0, skipped = 0;
        foreach (var grp in new[] { (RIVER, SPR_RIVER, DATA_RIVER, ITEM_RIVER, "River"),
                                   (SEA, SPR_SEA, DATA_SEA, ITEM_SEA, "Sea") })
        {
            foreach (FishDef f in grp.Item1)
            {
                string png = grp.Item2 + "/" + f.file + ".png";
                Sprite icon = null, world = null;
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(png))
                {
                    Sprite sp = o as Sprite;
                    if (sp == null) continue;
                    if (sp.name == f.file + "_0") icon = sp;
                    else if (sp.name == f.file + "_1") world = sp;
                }
                if (icon == null)
                {
                    Debug.LogWarning("[Fish] Нет спрайта " + f.file + "_0 — пропускаю (" + png + ")");
                    skipped++;
                    continue;
                }
                if (world == null) world = icon;

                string dataPath = grp.Item3 + "/Fish_" + f.key + ".asset";
                string itemPath = grp.Item4 + "/Fish_" + f.key + ".asset";

                ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(itemPath);
                if (item == null)
                {
                    item = ScriptableObject.CreateInstance<ItemData>();
                    FillNewItem(item, f, icon, world);
                    AssetDatabase.CreateAsset(item, itemPath);
                }
                else
                {
                    item.icon = icon;
                    item.worldSprite = world;
                    // Рыба не стакается (1 слот = 1 вес) — правим и существующим
                    if (item.maxStack != 1) item.maxStack = 1;
                    EditorUtility.SetDirty(item);
                }

                FishData fd = AssetDatabase.LoadAssetAtPath<FishData>(dataPath);
                if (fd == null)
                {
                    fd = ScriptableObject.CreateInstance<FishData>();
                    fd.fishName = f.ru;
                    fd.icon = icon;
                    fd.description = f.desc;
                    fd.fishItem = item;
                    fd.difficulty = Mathf.Clamp(f.diff, 0, 2);
                    fd.fishTier = TierOf(f.key);
                    float[] wr = WeightOf(f.key);
                    fd.minWeightKg = wr[0];
                    fd.maxWeightKg = wr[1];
                    fd.price = f.price;
                    fd.firstCatchBonus = f.price * 3 / 4;
                    AssetDatabase.CreateAsset(fd, dataPath);
                    created++;
                }
                else
                {
                    // Существующим: спрайты, ссылку + тир/вес из таблиц билда
                    // (это данные билда, не ручные). Цены/сложность/имена/бонусы не трогаем
                    fd.icon = icon;
                    if (fd.fishItem == null) fd.fishItem = item;
                    fd.fishTier = TierOf(f.key);
                    float[] wr = WeightOf(f.key);
                    fd.minWeightKg = wr[0];
                    fd.maxWeightKg = wr[1];
                    EditorUtility.SetDirty(fd);
                    updated++;
                }
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Fish] Готово: создано " + created + ", обновлены спрайты " + updated
            + ", пропущено " + skipped + ", перенесено старых " + moved
            + ". Зоны (таблицы точек) и спрос скупщика заполни руками.");
    }

    static void FillNewItem(ItemData a, FishDef f, Sprite icon, Sprite world)
    {
        a.itemName = f.ru;
        a.description = f.desc;
        a.icon = icon;
        a.worldSprite = world;
        a.itemType = ItemType.Consumable;
        a.rarity = ItemRarity.Common;
        a.isStackable = true;
        a.maxStack = 1; // 1 слот = 1 рыба = 1 вес
        a.healAmount = f.diff == 0 ? 10 : (f.diff == 1 ? 18 : 30);
        a.shopPrice = 0;
        EditorUtility.SetDirty(a);
    }

    static void EnsureFolders()
    {
        foreach (string p in new[] { DATA_RIVER, DATA_SEA, ITEM_RIVER, ITEM_SEA })
        {
            if (AssetDatabase.IsValidFolder(p)) continue;
            string parent = p.Substring(0, p.LastIndexOf('/'));
            string name = p.Substring(p.LastIndexOf('/') + 1);
            EnsureFoldersParent(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    static void EnsureFoldersParent(string p)
    {
        if (AssetDatabase.IsValidFolder(p)) return;
        string parent = p.Substring(0, p.LastIndexOf('/'));
        EnsureFoldersParent(parent);
        AssetDatabase.CreateFolder(parent, p.Substring(p.LastIndexOf('/') + 1));
    }
}
