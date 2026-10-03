using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Билдеры конструктора дома. Запуск: Tools → House → пункты меню (Play выключен,
/// кроме 3/4-тестов склада!). Сцену для пунктов 5–7 держать ОТКРЫТОЙ в редакторе
/// (SampleScene, single) — кнопки читают/пишут её тайлмапы, потом Ctrl+S.
/// 1. Строит/обновляет FurnitureMountTable: сканирует Assets/Prefab/Interior,
///    дописывает ТОЛЬКО недостающие записи (ручные правки не трёт).
///    Дефолт: папки Part* → Wall, остальные → Floor.
/// 2. Создаёт ПУСТОЙ HouseCatalog (если его нет) — владелец сам перетаскивает
///    ~8–10 тестовых префабов в инспекторе. Существующий НЕ трогает.
/// 3/4. Дебаг склада (только в Play): +5 тестовых обоев / убрать их.
/// 5. Строит/обновляет WallStyle_Brick (набор кирпича, роли из аудита фазы 2).
/// 6. Audit Player Room: сверка автотайлера с текущей комнатой (только чтение,
///    ожидаем 0 расхождений — это проверка таблицы маппинга).
/// 7. Rebuild Player Room: пересобрать стены комнаты автотайлером + контрольная
///    сверка (ожидаем painted 0 — побайтовое совпадение).
/// </summary>
public static class HouseBuilders
{
    const string HouseResDir = "Assets/Resources/House";
    const string MountTablePath = "Assets/Resources/House/FurnitureMountTable.asset";
    const string CatalogPath = "Assets/Resources/House/HouseCatalog.asset";
    const string WallStylePath = "Assets/Resources/House/WallStyle_Brick.asset";
    const string PrefabRoot = "Assets/Prefab/Interior";
    const string TileDir = "Assets/Art/Tileset Grass Spring/Tileset House/";
    const string TestId = "TestWallpaper";

    [MenuItem("Tools/House/1. Build Furniture Mount Table")]
    public static void BuildMountTable()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Выключи Play-режим перед генерацией таблицы.");
            return;
        }

        EnsureFolder(HouseResDir);

        FurnitureMountTable table = AssetDatabase.LoadAssetAtPath<FurnitureMountTable>(MountTablePath);
        if (table == null)
        {
            table = ScriptableObject.CreateInstance<FurnitureMountTable>();
            AssetDatabase.CreateAsset(table, MountTablePath);
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
        int added = 0, wall = 0, floor = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string prefabName = Path.GetFileNameWithoutExtension(path);
            string folder = Path.GetFileName(Path.GetDirectoryName(path));
            if (table.HasEntry(prefabName)) continue; // ручное не трогаем

            FurnitureMount mount = FurnitureMountTable.DefaultForFolder(folder);
            table.entries.Add(new FurnitureMountTable.Entry { prefabName = prefabName, mount = mount });
            added++;
            if (mount == FurnitureMount.Wall) wall++; else floor++;
        }

        table.RebuildCache();
        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
        Debug.Log("[House] MountTable: всего " + table.entries.Count + ", добавлено " + added +
                  " (Wall " + wall + ", Floor " + floor + "). Ручные правки не тронуты.");
    }

    [MenuItem("Tools/House/2. Create House Catalog Base (пустой)")]
    public static void CreateCatalogBase()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Выключи Play-режим перед созданием каталога.");
            return;
        }

        EnsureFolder(HouseResDir);

        HouseCatalog existing = AssetDatabase.LoadAssetAtPath<HouseCatalog>(CatalogPath);
        if (existing != null)
        {
            Debug.Log("[House] Каталог уже есть (" + existing.entries.Count + " записей) — не трогаю. " +
                      "Перетащи тестовые префабы в инспекторе сам.");
            Selection.activeObject = existing;
            return;
        }

        HouseCatalog catalog = ScriptableObject.CreateInstance<HouseCatalog>();
        AssetDatabase.CreateAsset(catalog, CatalogPath);
        AssetDatabase.SaveAssets();
        Selection.activeObject = catalog;
        Debug.Log("[House] Создан пустой HouseCatalog. Открой его в инспекторе и перетащи " +
                  "~8–10 тестовых префабов из Assets/Prefab/Interior (кровать, стол, стул, картина, часы, свеча), проставь цены.");
    }

    [MenuItem("Tools/House/3. Test (Play): склад +5 Тест-обои")]
    public static void TestStockAdd()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Тест склада — только в Play (запусти из SampleScene).");
            return;
        }
        if (HouseStock.Instance == null) return;
        HouseStock.Instance.Add(TestId, 5);
        Debug.Log("[House] Тест: " + TestId + " на складе = " + HouseStock.Instance.Count(TestId) +
                  " (со СКЛАДА, не рюкзака). Выйди из Play — сохранится в save.json.");
    }

    [MenuItem("Tools/House/4. Test (Play): убрать Тест-обои со склада")]
    public static void TestStockClear()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Тест склада — только в Play (запусти из SampleScene).");
            return;
        }
        if (HouseStock.Instance == null) return;
        HouseStock.Instance.ClearId(TestId);
        Debug.Log("[House] Тест: " + TestId + " убран, остаток = " + HouseStock.Instance.Count(TestId) + ".");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string leaf = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    // ═══════════════════════════════════════════════════════════
    // ФАЗА 2: стиль кирпича + аудит/перестройка комнаты игрока
    // ═══════════════════════════════════════════════════════════
    static UnityEngine.Tilemaps.TileBase Tile(string assetName)
    {
        return AssetDatabase.LoadAssetAtPath<UnityEngine.Tilemaps.TileBase>(TileDir + assetName + ".asset");
    }

    [MenuItem("Tools/House/5. Build WallStyle Brick")]
    public static void BuildWallStyleBrick()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Выключи Play-режим перед генерацией стиля.");
            return;
        }

        EnsureFolder(HouseResDir);

        WallStyle st = AssetDatabase.LoadAssetAtPath<WallStyle>(WallStylePath);
        if (st == null)
        {
            st = ScriptableObject.CreateInstance<WallStyle>();
            AssetDatabase.CreateAsset(st, WallStylePath);
        }

        // Имена ассетов — ровно как файлы (у 217/218/219 в имени пробел и " 1").
        st.bgFill = Tile("Tileset House_76");
        st.floorTile = Tile("Tileset House_12");
        st.cornerTL = Tile("Tileset House_1");
        st.cornerTR = Tile("Tileset House_3");
        st.cornerBL = Tile("Tileset House_68");
        st.cornerBR = Tile("Tileset House_70");
        st.straightH = Tile("Tileset House_117");
        st.straightV = Tile("Tileset House_34");
        st.bandTopL = Tile("Tileset House_197");
        st.bandTopM = Tile("Tileset House_198");
        st.bandTopR = Tile("Tileset House_199");
        st.bandBotL = Tile("Tileset House_217 1");
        st.bandBotM = Tile("Tileset House_218 1");
        st.bandBotR = Tile("Tileset House_219 1");
        st.concaveNW = Tile("Tileset House_1");
        st.concaveNE = Tile("Tileset House_3");
        st.concaveSW = Tile("Tileset House_68");
        st.concaveSE = Tile("Tileset House_70");
        st.concaveAltNW = Tile("Tileset House_77");
        st.concaveAltNE = Tile("Tileset House_75");
        st.concaveAltSW = Tile("Tileset House_40");
        st.concaveAltSE = Tile("Tileset House_38");
        st.useConcaveAlts = false;
        st.doorJambL = Tile("Tileset House_118");
        st.doorJambR = Tile("Tileset House_116");
        st.doorThreshold = Tile("Tileset House_12");

        int missing = 0;
        foreach (Object o in new Object[] {
            st.bgFill, st.floorTile,
            st.cornerTL, st.cornerTR, st.cornerBL, st.cornerBR,
            st.straightH, st.straightV,
            st.bandTopL, st.bandTopM, st.bandTopR,
            st.bandBotL, st.bandBotM, st.bandBotR,
            st.concaveAltNW, st.concaveAltNE, st.concaveAltSW, st.concaveAltSE,
            st.doorJambL, st.doorJambR, st.doorThreshold })
            if (o == null) missing++;

        EditorUtility.SetDirty(st);
        AssetDatabase.SaveAssets();
        Selection.activeObject = st;
        if (missing > 0)
            Debug.LogError("[House] WallStyle_Brick: НЕ НАШЁЛ ассетов: " + missing + " (проверь имена).");
        else
            Debug.Log("[House] WallStyle_Brick готов: все 21 ссылки на месте.");
    }

    static bool FindRoomMaps(out UnityEngine.Tilemaps.Tilemap walls, out UnityEngine.Tilemaps.Tilemap floors)
    {
        walls = null;
        floors = null;
        GameObject wgo = GameObject.Find("InteriorWalls");
        GameObject fgo = GameObject.Find("InteriorFloor");
        if (wgo == null || fgo == null)
        {
            Debug.LogError("[House] Не нашёл InteriorWalls/InteriorFloor в открытой сцене. Открой SampleScene (single).");
            return false;
        }
        walls = wgo.GetComponent<UnityEngine.Tilemaps.Tilemap>();
        floors = fgo.GetComponent<UnityEngine.Tilemaps.Tilemap>();
        if (walls == null || floors == null)
        {
            Debug.LogError("[House] У объектов нет Tilemap-компонентов.");
            return false;
        }
        return true;
    }

    static WallStyle LoadBrick()
    {
        WallStyle st = AssetDatabase.LoadAssetAtPath<WallStyle>(WallStylePath);
        if (st == null)
            Debug.LogError("[House] Нет WallStyle_Brick — сначала пункт 5.");
        return st;
    }

    [MenuItem("Tools/House/6. Audit Player Room (только сверка)")]
    public static void AuditPlayerRoom()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Выключи Play-режим перед аудитом.");
            return;
        }
        if (!FindRoomMaps(out var walls, out var floors)) return;
        WallStyle st = LoadBrick();
        if (st == null) return;

        var region = HouseAutotiler.LargestFloorRegion(floors);
        if (region.Count == 0)
        {
            Debug.LogError("[House] Пустая область пола — нечего сверять.");
            return;
        }
        int minY = int.MaxValue;
        foreach (var c in region) if (c.y < minY) minY = c.y;
        Debug.Log("[House] Комната игрока: клеток пола " + region.Count + ", нижний ряд пола y=" + minY + " (дверь и всё ниже не трогаем).");

        var unmapped = new List<string>();
        var wanted = HouseAutotiler.ComputeWanted(walls, region, st, minY, null, unmapped);
        var diffs = HouseAutotiler.Audit(walls, wanted);
        foreach (string u in unmapped)
            Debug.LogWarning("[House] UNMAPPED: " + u);
        if (diffs.Count == 0 && unmapped.Count == 0)
        {
            Debug.Log("[House] Аудит ЧИСТО: автотайлер 1-в-1 повторяет комнату (" + wanted.Count + " клеток). Можно жать пункт 7.");
        }
        else
        {
            Debug.LogError("[House] Расхождений: " + diffs.Count + ", unmapped: " + unmapped.Count + ". Первые 20:");
            for (int i = 0; i < Mathf.Min(20, diffs.Count); i++)
                Debug.LogError("[House] diff: " + diffs[i]);
        }
    }

    [MenuItem("Tools/House/7. Rebuild Player Room (запись + Ctrl+S)")]
    public static void RebuildPlayerRoom()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Выключи Play-режим перед перестройкой.");
            return;
        }
        if (!FindRoomMaps(out var walls, out var floors)) return;
        WallStyle st = LoadBrick();
        if (st == null) return;

        var region = HouseAutotiler.LargestFloorRegion(floors);
        if (region.Count == 0)
        {
            Debug.LogError("[House] Пустая область пола — нечего перестраивать.");
            return;
        }
        int minY = int.MaxValue;
        foreach (var c in region) if (c.y < minY) minY = c.y;

        var unmapped = new List<string>();
        var wanted = HouseAutotiler.ComputeWanted(walls, region, st, minY, null, unmapped);
        int painted = HouseAutotiler.Paint(walls, wanted);
        var after = HouseAutotiler.Audit(walls, wanted);
        Debug.Log("[House] Перестройка: записано клеток " + painted + ", остаточных расхождений " + after.Count +
                  ", unmapped " + unmapped.Count + ". Нажми Ctrl+S (сохранить сцену), затем проверь комнату глазами в Play.");
        foreach (string d in after)
            Debug.LogError("[House] Остаток: " + d);
    }

    // ═══════════════════════════════════════════════════════════
    // ФАЗА 3: второй стиль (сарай) + предметы + тесты перекраски
    // ═══════════════════════════════════════════════════════════
    const string BarnStylePath = "Assets/Resources/House/WallStyle_Barn.asset";
    const string HouseItemsDir = "Assets/Resources/Items/House";

    static void FillBrickShared(WallStyle st)
    {
        // Кольцо/фон/косяки общие с кирпичом (проверено аудитом: сарай на том же кольце).
        st.bgFill = Tile("Tileset House_76");
        st.cornerTL = Tile("Tileset House_1");
        st.cornerTR = Tile("Tileset House_3");
        st.cornerBL = Tile("Tileset House_68");
        st.cornerBR = Tile("Tileset House_70");
        st.straightH = Tile("Tileset House_117");
        st.straightV = Tile("Tileset House_34");
        st.concaveNW = Tile("Tileset House_1");
        st.concaveNE = Tile("Tileset House_3");
        st.concaveSW = Tile("Tileset House_68");
        st.concaveSE = Tile("Tileset House_70");
        st.concaveAltNW = Tile("Tileset House_77");
        st.concaveAltNE = Tile("Tileset House_75");
        st.concaveAltSW = Tile("Tileset House_40");
        st.concaveAltSE = Tile("Tileset House_38");
        st.useConcaveAlts = false;
        st.doorJambL = Tile("Tileset House_118");
        st.doorJambR = Tile("Tileset House_116");
    }

    static bool CheckRefs(WallStyle st, string label)
    {
        int missing = 0;
        foreach (Object o in new Object[] {
            st.bgFill, st.floorTile,
            st.cornerTL, st.cornerTR, st.cornerBL, st.cornerBR,
            st.straightH, st.straightV,
            st.bandTopL, st.bandTopM, st.bandTopR,
            st.bandBotL, st.bandBotM, st.bandBotR,
            st.doorJambL, st.doorJambR, st.doorThreshold })
            if (o == null) missing++;
        if (missing > 0)
            Debug.LogError("[House] " + label + ": НЕ НАШЁЛ ассетов: " + missing + ".");
        else
            Debug.Log("[House] " + label + ": все ссылки на месте.");
        return missing == 0;
    }

    [MenuItem("Tools/House/8. Build WallStyle Barn (стиль №2)")]
    public static void BuildWallStyleBarn()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Выключи Play-режим перед генерацией стиля.");
            return;
        }

        EnsureFolder(HouseResDir);

        WallStyle st = AssetDatabase.LoadAssetAtPath<WallStyle>(BarnStylePath);
        if (st == null)
        {
            st = ScriptableObject.CreateInstance<WallStyle>();
            AssetDatabase.CreateAsset(st, BarnStylePath);
        }

        FillBrickShared(st);
        st.floorTile = Tile("Tileset House_65");
        st.bandTopL = Tile("Tileset House_405");
        st.bandTopM = Tile("Tileset House_406");
        st.bandTopR = Tile("Tileset House_407");
        st.bandBotL = Tile("Tileset House_457");
        st.bandBotM = Tile("Tileset House_458");
        st.bandBotR = Tile("Tileset House_459");
        st.doorThreshold = Tile("Tileset House_65");

        EditorUtility.SetDirty(st);
        AssetDatabase.SaveAssets();
        Selection.activeObject = st;
        CheckRefs(st, "WallStyle_Barn");
    }

    struct StyleItemSpec
    {
        public string asset;
        public string titleRU;
        public ItemType type;
        public string styleId;
        public string tileAsset;
        public int price;
        public string desc;
        public StyleItemSpec(string asset, string titleRU, ItemType type, string styleId,
            string tileAsset, int price, string desc)
        {
            this.asset = asset; this.titleRU = titleRU; this.type = type;
            this.styleId = styleId; this.tileAsset = tileAsset;
            this.price = price; this.desc = desc;
        }
    }

    [MenuItem("Tools/House/9. Build House Style Items (обои+плитка)")]
    public static void BuildHouseStyleItems()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Выключи Play-режим перед генерацией предметов.");
            return;
        }

        EnsureFolder(HouseItemsDir);

        StyleItemSpec[] specs = new StyleItemSpec[]
        {
            new StyleItemSpec("Wallpaper_Brick", "Обои «Кирпич»", ItemType.Wallpaper, "Brick",
                "Tileset House_198", 200, "Стены под кирпич. Красит 1 тайл, старый возвращается на склад дома."),
            new StyleItemSpec("Wallpaper_Barn", "Обои «Сарай»", ItemType.Wallpaper, "Barn",
                "Tileset House_406", 200, "Стены под сарай. Красит 1 тайл, старый возвращается на склад дома."),
            new StyleItemSpec("FloorTile_Brick", "Плитка «Кирпич»", ItemType.FloorTile, "Brick",
                "Tileset House_12", 150, "Пол под кирпич. Красит 1 тайл, старый возвращается на склад дома."),
            new StyleItemSpec("FloorTile_Barn", "Плитка «Сарай»", ItemType.FloorTile, "Barn",
                "Tileset House_65", 150, "Пол под сарай. Красит 1 тайл, старый возвращается на склад дома."),
        };

        foreach (StyleItemSpec s in specs)
        {
            string path = HouseItemsDir + "/" + s.asset + ".asset";
            ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemData>();
                AssetDatabase.CreateAsset(item, path);
            }
            item.itemName = s.titleRU;
            item.description = s.desc;
            item.itemType = s.type;
            item.rarity = ItemRarity.Common;
            item.isStackable = true;
            item.maxStack = 99;
            item.shopPrice = s.price;
            item.houseStyleId = s.styleId;
            UnityEngine.Tilemaps.Tile tile = AssetDatabase.LoadAssetAtPath<UnityEngine.Tilemaps.Tile>(
                TileDir + s.tileAsset + ".asset");
            if (tile != null && tile.sprite != null)
            {
                item.icon = tile.sprite;
                item.worldSprite = tile.sprite;
            }
            else
            {
                Debug.LogWarning("[House] Нет спрайта для " + s.asset + " (тайл " + s.tileAsset + ").");
            }
            EditorUtility.SetDirty(item);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[House] Предметы стилей готовы: 4 шт в " + HouseItemsDir + " (цены на ассетах, правятся в инспекторе).");
    }

    [MenuItem("Tools/House/10. Test (Play): склад +30 сарай-стен/полов")]
    public static void TestStockGiveStyles()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Тест склада — только в Play (запусти из SampleScene).");
            return;
        }
        if (HouseStock.Instance == null) return;
        HouseStock.Instance.Add(HouseReskin.WallKey("Barn"), 30);
        HouseStock.Instance.Add(HouseReskin.FloorKey("Barn"), 30);
        Debug.Log("[House] Тест: wall_barn=" + HouseStock.Instance.Count(HouseReskin.WallKey("Barn")) +
                  ", floor_barn=" + HouseStock.Instance.Count(HouseReskin.FloorKey("Barn")) + ".");
    }

    [MenuItem("Tools/House/11. Test (Edit): покрасить комнату в сарай (бесплатно)")]
    public static void TestPaintBarnFree()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Бесплатная покраска — только НЕ в Play (иначе затрется сейвом сцены).");
            return;
        }
        int w = HouseReskin.PaintRoomWalls("Barn", false);
        int f = HouseReskin.PaintRoomFloor("Barn", false);
        Debug.Log("[House] Тест покраски: стены " + w + ", пол " + f +
                  ". Нажми Ctrl+S, проверь глазами в Play, потом верни кирпич пунктом 13.");
    }

    [MenuItem("Tools/House/12. Test (Play): своп 1+1 тайла со склада (экономика)")]
    public static void TestSwapOneTile()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Тест свопа — только в Play (запусти из SampleScene).");
            return;
        }
        if (HouseStock.Instance == null) return;
        int wb0 = HouseStock.Instance.Count(HouseReskin.WallKey("Barn"));
        int fb0 = HouseStock.Instance.Count(HouseReskin.FloorKey("Barn"));
        int w = HouseReskin.PaintRoomWalls("Barn", true, 1);
        int f = HouseReskin.PaintRoomFloor("Barn", true, 1);
        Debug.Log("[House] Своп: стен " + w + " (было wall_barn " + wb0 + ", стало " +
                  HouseStock.Instance.Count(HouseReskin.WallKey("Barn")) + "), полов " + f +
                  " (было floor_barn " + fb0 + ", стало " +
                  HouseStock.Instance.Count(HouseReskin.FloorKey("Barn")) + ").");
    }

    [MenuItem("Tools/House/13. Test (Edit): вернуть кирпич (бесплатно)")]
    public static void TestPaintBrickFree()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Бесплатная покраска — только НЕ в Play.");
            return;
        }
        int w = HouseReskin.PaintRoomWalls("Brick", false);
        int f = HouseReskin.PaintRoomFloor("Brick", false);
        Debug.Log("[House] Возврат: стены " + w + ", пол " + f + ". Нажми Ctrl+S.");
    }
}
