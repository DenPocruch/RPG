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
        st.forkTop = Tile("Tileset House_2");
        st.forkBottom = Tile("Tileset House_69");
        st.forkLeft = Tile("Tileset House_35");
        st.forkRight = Tile("Tileset House_37");
        st.forkCenter = Tile("Tileset House_36");
        st.bandTopL = Tile("Tileset House_197");
        st.bandTopM = Tile("Tileset House_198");
        st.bandTopR = Tile("Tileset House_199");
        st.bandTopEdge = Tile("Tileset House_196");
        st.bandBotL = Tile("Tileset House_217 1");
        st.bandBotM = Tile("Tileset House_218 1");
        st.bandBotR = Tile("Tileset House_219 1");
        st.bandBotEdge = Tile("Tileset House_216 1");
        st.westTurnA = Tile("Tileset House_67");
        st.westTurnB = Tile("Tileset House_37");
        st.columnMid0 = Tile("Tileset House_0");
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
            st.forkTop, st.forkBottom, st.forkLeft, st.forkRight, st.forkCenter,
            st.bandTopL, st.bandTopM, st.bandTopR, st.bandTopEdge,
            st.bandBotL, st.bandBotM, st.bandBotR, st.bandBotEdge,
            st.westTurnA, st.westTurnB, st.columnMid0,
            st.concaveAltNW, st.concaveAltNE, st.concaveAltSW, st.concaveAltSE,
            st.doorJambL, st.doorJambR, st.doorThreshold })
            if (o == null) missing++;

        EditorUtility.SetDirty(st);
        AssetDatabase.SaveAssets();
        Selection.activeObject = st;
        HouseFurniture.InvalidateHangCache(); // лента/фон могли поменяться
        if (missing > 0)
            Debug.LogError("[House] WallStyle_Brick: НЕ НАШЁЛ ассетов: " + missing + " (проверь имена).");
        else
            Debug.Log("[House] WallStyle_Brick готов: все 31 ссылок на месте.");
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
        int painted = HouseAutotiler.Stabilize(walls, floors, region, st, minY, null,
            st.floorTile, unmapped);
        if (painted > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(walls.gameObject.scene);
        // Регион мог подрасти доливом пола — пересчитываем перед сверкой,
        // иначе аудит смотрит stale-набор и показывает вечные остатки.
        region = HouseAutotiler.LargestFloorRegion(floors);
        minY = int.MaxValue;
        foreach (var c in region) if (c.y < minY) minY = c.y;
        var after = HouseAutotiler.Audit(walls,
            HouseAutotiler.ComputeWanted(walls, region, st, minY, null, new List<string>()));
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
            st.bandTopL, st.bandTopM, st.bandTopR, st.bandTopEdge,
            st.bandBotL, st.bandBotM, st.bandBotR, st.bandBotEdge,
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
        st.bandTopEdge = Tile("Tileset House_404");
        st.bandBotL = Tile("Tileset House_457");
        st.bandBotM = Tile("Tileset House_458");
        st.bandBotR = Tile("Tileset House_459");
        st.bandBotEdge = Tile("Tileset House_456");
        st.doorThreshold = Tile("Tileset House_65");

        EditorUtility.SetDirty(st);
        AssetDatabase.SaveAssets();
        Selection.activeObject = st;
        HouseFurniture.InvalidateHangCache(); // лента/фон могли поменяться
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
        if (w + f > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
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
        if (w + f > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[House] Возврат: стены " + w + ", пол " + f + ". Нажми Ctrl+S.");
    }

    // ═══════════════════════════════════════════════════════════
    // ФАЗА 4: тесты расширения (14–16 в редакторе бесплатно, 17 в Play по-честному)
    // ═══════════════════════════════════════════════════════════
    static bool FindRoomForExpand(out UnityEngine.Tilemaps.Tilemap walls,
        out UnityEngine.Tilemaps.Tilemap floors, out HashSet<Vector3Int> region, out int lockY)
    {
        walls = null;
        floors = null;
        region = new HashSet<Vector3Int>();
        lockY = 0;
        GameObject wgo = GameObject.Find("InteriorWalls");
        GameObject fgo = GameObject.Find("InteriorFloor");
        if (wgo == null || fgo == null)
        {
            Debug.LogError("[House] Не нашёл InteriorWalls/InteriorFloor. Открой SampleScene (single).");
            return false;
        }
        walls = wgo.GetComponent<UnityEngine.Tilemaps.Tilemap>();
        floors = fgo.GetComponent<UnityEngine.Tilemaps.Tilemap>();
        if (walls == null || floors == null) return false;
        region = HouseAutotiler.LargestFloorRegion(floors);
        if (region.Count == 0)
        {
            Debug.LogError("[House] Пустая область пола.");
            return false;
        }
        lockY = int.MaxValue;
        foreach (Vector3Int c in region)
            if (c.y < lockY) lockY = c.y;
        return true;
    }

    /// <summary>Первый валидный ghost в сторону: 0=север, 1=запад, 2=восток.</summary>
    static bool PickTarget(int dir, out Vector3Int picked)
    {
        picked = default;
        if (!FindRoomForExpand(out var walls, out var floors, out var region, out int lockY)) return false;
        WallStyle brick = AssetDatabase.LoadAssetAtPath<WallStyle>(WallStylePath);
        if (brick == null)
        {
            Debug.LogError("[House] Нет WallStyle_Brick (пункт 5).");
            return false;
        }
        List<Vector3Int> cands = HouseExpansion.ValidTargets(walls, floors, region, brick, lockY);
        if (cands.Count == 0)
        {
            Debug.LogWarning("[House] Валидных клеток для пристройки нет (кап? всё застроено?).");
            Debug.LogWarning("[House] Разбор: " + HouseExpansion.TargetsDebug(walls, floors, region, brick, lockY));
            return false;
        }
        float cx = 0;
        foreach (Vector3Int c in region) cx += c.x;
        cx /= region.Count;
        Vector3Int best = cands[0];
        foreach (Vector3Int c in cands)
        {
            if (dir == 0 && (c.y > best.y || (c.y == best.y && Mathf.Abs(c.x - cx) < Mathf.Abs(best.x - cx)))) best = c;
            if (dir == 1 && (c.x < best.x || (c.x == best.x && c.y > best.y))) best = c;
            if (dir == 2 && (c.x > best.x || (c.x == best.x && c.y > best.y))) best = c;
            // Края верха: сначала самый верхний ряд, в нём крайний запад/восток.
            if (dir == 3)
            {
                if (c.y > best.y || (c.y == best.y && c.x < best.x)) best = c;
            }
            if (dir == 4)
            {
                if (c.y > best.y || (c.y == best.y && c.x > best.x)) best = c;
            }
        }
        picked = best;
        Debug.Log("[House] Ghost-кандидатов: " + cands.Count + ", строим " + picked + ".");
        return true;
    }

    [MenuItem("Tools/House/00. Ping (проверка кликов)")]
    public static void Ping()
    {
        Debug.Log("[House] PING: клики доходят, код выполняется.");
    }

    [MenuItem("Tools/House/14. Test Expand (Edit): +1 на СЕВЕР (бесплатно)")]
    public static void TestExpandNorth()
    {
        Debug.Log("[House] Клик 14 (север).");
        try
        {
            Debug.Log("[House] 14: проверяю Play...");
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("[House] Тест пристройки — только НЕ в Play. Потом Ctrl+S.");
                return;
            }
            Debug.Log("[House] 14: ищу комнату...");
            Vector3Int t;
            bool picked = PickTargetTraced(0, out t);
            bool built = false;
            if (picked)
            {
                if (FindRoomForExpand(out var jw, out var jf, out var jregion, out int jlock))
                {
                    JournalScope(jregion, jlock, out int x0, out int x1, out int y0, out int y1);
                    HouseJournal.Record(jw, t, x0, x1, y0, y1);
                }
                built = HouseExpansion.TryExpand(t, true);
            }
            if (built)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                Debug.Log("[House] Готово: Ctrl+S, затем смотри в Play (стены, проход, коллайдеры).");
            }
            Debug.Log("[House] 14: конец.");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[House] 14 EXCEPTION: " + e);
        }
    }

    /// <summary>Общая стройка по направлению: выбор → журнал → стройка.</summary>
    static void BuildPicked(int dir, string label)
    {
        Debug.Log("[House] Клик " + label + ".");
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Только НЕ в Play. Потом Ctrl+S.");
            return;
        }
        if (!PickTargetTraced(dir, out Vector3Int t)) return;
        if (FindRoomForExpand(out var jw, out var jf, out var jregion, out int jlock))
        {
            JournalScope(jregion, jlock, out int x0, out int x1, out int y0, out int y1);
            HouseJournal.Record(jw, t, x0, x1, y0, y1);
        }
        if (HouseExpansion.TryExpand(t, true))
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[House] Готово: Ctrl+S, затем смотри в Play.");
        }
    }

    [MenuItem("Tools/House/14b. Test Expand (Edit): СЕВЕРО-ЗАПАДНЫЙ край")]
    public static void TestExpandNorthWest()
    {
        BuildPicked(3, "14b (северо-запад)");
    }

    [MenuItem("Tools/House/14c. Test Expand (Edit): СЕВЕРО-ВОСТОЧНЫЙ край")]
    public static void TestExpandNorthEast()
    {
        BuildPicked(4, "14c (северо-восток)");
    }

    // ═══════════════════════════════════════════════════════════
    // Ручные тесты (в т.ч. параллельно и в 2–3 ряда): пол кладёшь САМ
    // кистью (встык, поверх стены — верхний тайл снимется), код достраивает
    // кольцо/ленту. Свежими считаются клетки пола с покрывающей стеной.
    // ═══════════════════════════════════════════════════════════
    [MenuItem("Tools/House/20. Build Ring For Hand Floors (кольцо вокруг ручного пола)")]
    public static void BuildRingForHandFloors()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Только НЕ в Play. Потом Ctrl+S.");
            return;
        }
        if (!FindRoomForExpand(out var walls, out var floors, out var region, out int lockY)) return;
        WallStyle brick = AssetDatabase.LoadAssetAtPath<WallStyle>(WallStylePath);
        if (brick == null)
        {
            Debug.LogError("[House] Нет WallStyle_Brick (пункт 5).");
            return;
        }
        var fresh = new HashSet<Vector3Int>();
        foreach (Vector3Int c in region)
        {
            if (walls.GetTile(c) != null) fresh.Add(c);
        }
        if (fresh.Count == 0)
        {
            Debug.LogWarning("[House] Ручного пола нет (нужен пол с покрывающей стеной поверх). Положи пол кистью встык и жми снова.");
            return;
        }
        JournalScope(region, lockY, out int jx0, out int jx1, out int jy0, out int jy1);
        foreach (Vector3Int fc in fresh)
            HouseJournal.Record(walls, fc, jx0, jx1, jy0, jy1);
        // Докажи что нарисовал ты: список свежих клеток (пол с покрывающей стеной).
        var freshSorted = new List<Vector3Int>(fresh);
        freshSorted.Sort((a, b) => a.y != b.y ? b.y.CompareTo(a.y) : a.x.CompareTo(b.x));
        string fs = "";
        foreach (Vector3Int fc in freshSorted) fs += fc + " ";
        Debug.Log("[House] 20 fresh (" + fresh.Count + "): " + fs);
        var freshBefore = new HashSet<Vector3Int>(fresh);
        // Снап стен до стройки (скоп журнала) — после сравним что доложил код.
        var beforeWalls = new Dictionary<Vector3Int, string>();
        for (int x = jx0; x <= jx1; x++)
            for (int y = jy0; y <= jy1; y++)
            {
                var c = new Vector3Int(x, y, 0);
                UnityEngine.Tilemaps.TileBase t = walls.GetTile(c);
                beforeWalls[c] = t != null ? t.name : "<пусто>";
            }
        var unmapped = new List<string>();
        int painted = HouseAutotiler.Stabilize(walls, floors, region, brick, lockY,
            fresh, brick.floorTile, unmapped);
        // Долив пола стабилизацией (новые клетки сверх нарисованных).
        string addedF = "";
        foreach (Vector3Int c in fresh)
            if (!freshBefore.Contains(c)) addedF += c + " ";
        if (!string.IsNullOrEmpty(addedF))
            Debug.Log("[House] 20 долив пола: " + addedF);
        // Регион мог подрасти доливом — пересчитываем, иначе вечные остатки.
        // Аудит кольца делаем ДО колонки: строгий мост (1в) permissive-колонку
        // п.23 не ждёт и показал бы её как расхождение.
        region = HouseAutotiler.LargestFloorRegion(floors);
        lockY = int.MaxValue;
        foreach (Vector3Int c in region) if (c.y < lockY) lockY = c.y;
        var after = HouseAutotiler.Audit(walls,
            HouseAutotiler.ComputeWanted(walls, region, brick, lockY, null, new List<string>()));
        // Колонка из п.23 — вместе с коробкой (ручное кольцо тоже её ставит).
        int colBuilt = BuildCorridorColumnCore(walls, floors, region, brick, lockY, out int colSkipped);
        // Декор, из-под которого ушла стена/пол, — снести (фаза 5, склад не трогаем).
        HouseFurniture.RefreshPlaced(walls, floors, false);
        if (painted + colBuilt > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        // Что доложил код: сравнение стен до/после по скопу + маска итога.
        int logged = 0;
        for (int x = jx0; x <= jx1 && logged < 150; x++)
            for (int y = jy0; y <= jy1 && logged < 150; y++)
            {
                var c = new Vector3Int(x, y, 0);
                UnityEngine.Tilemaps.TileBase t = walls.GetTile(c);
                string now = t != null ? t.name : "<пусто>";
                string was = beforeWalls[c];
                if (was != now)
                {
                    string mask = HouseAutotiler.DebugMask(walls, region, brick, c);
                    string role = HouseAutotiler.RoleOf(brick, t);
                    Debug.Log("[House] 20 put " + c + ": " + was + " → " + now + " (" + role + ") | " + mask);
                    logged++;
                }
            }
        // Карта скопа итогом (пол F + короткое имя стены) — видеть контур целиком.
        // Строки сверху вниз, токен: F/пол? + имя (76=фон, .=пусто).
        int scopeArea = (jx1 - jx0 + 1) * (jy1 - jy0 + 1);
        if (scopeArea <= 400)
        {
            for (int y = jy1; y >= jy0; y--)
            {
                string row = "";
                for (int x = jx0; x <= jx1; x++)
                {
                    var c = new Vector3Int(x, y, 0);
                    bool fl = floors.HasTile(c);
                    UnityEngine.Tilemaps.TileBase t = walls.GetTile(c);
                    string wn = t != null ? t.name.Replace("Tileset House_", "").Replace(" 1", "'") : ".";
                    if (wn == "76") wn = "~";
                    row += (fl ? "F" : "_") + wn + " ";
                }
                Debug.Log("[House] 20 map y=" + y + ": " + row);
            }
        }
        Debug.Log("[House] Кольцо: свежих " + fresh.Count + ", записано стен " + painted +
                  ", колонок " + colBuilt + " (пропущено " + colSkipped + ")" +
                  ", расхождений кольца " + after.Count + ", unmapped " + unmapped.Count + ". Потом Ctrl+S.");
        foreach (string d in after)
            Debug.LogWarning("[House] Остаток: " + d);
        foreach (string u in unmapped)
            Debug.LogWarning("[House] UNMAPPED: " + u);
    }

    static bool PickTargetTraced(int dir, out Vector3Int picked)
    {
        picked = default;
        Debug.Log("[House] PickTarget: ищу объекты сцены...");
        GameObject wgo = GameObject.Find("InteriorWalls");
        GameObject fgo = GameObject.Find("InteriorFloor");
        Debug.Log("[House] PickTarget: walls=" + (wgo != null) + " floors=" + (fgo != null));
        if (wgo == null || fgo == null)
        {
            Debug.LogError("[House] Не нашёл InteriorWalls/InteriorFloor. Открой SampleScene (single).");
            return false;
        }
        var walls = wgo.GetComponent<UnityEngine.Tilemaps.Tilemap>();
        var floors = fgo.GetComponent<UnityEngine.Tilemaps.Tilemap>();
        Debug.Log("[House] PickTarget: tilemaps walls=" + (walls != null) + " floors=" + (floors != null));
        if (walls == null || floors == null) return false;
        var region = HouseAutotiler.LargestFloorRegion(floors);
        Debug.Log("[House] PickTarget: клеток пола в регионе=" + region.Count);
        if (region.Count == 0) return false;
        return PickTarget(dir, out picked);
    }

    [MenuItem("Tools/House/15. Test Expand (Edit): +1 на ЗАПАД (бесплатно)")]
    public static void TestExpandWest()
    {
        BuildPicked(1, "15 (запад)");
    }

    [MenuItem("Tools/House/16. Test Expand (Edit): +1 на ВОСТОК (бесплатно)")]
    public static void TestExpandEast()
    {
        BuildPicked(2, "16 (восток)");
    }

    [MenuItem("Tools/House/17. Test Expand (Play): +1 по-честному (дерево+голда)")]
    public static void TestExpandReal()
    {
        Debug.Log("[House] Клик 17 (честно).");
        if (!EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Честная пристройка — только в Play: положи 50+ дерева в рюкзак, запусти из SampleScene.");
            return;
        }
        if (!FindRoomForExpand(out var walls, out var floors, out var region, out int lockY)) return;
        WallStyle brick = WallStyle.Load("Brick");
        if (brick == null)
        {
            Debug.LogError("[House] Нет WallStyle_Brick в Resources.");
            return;
        }
        List<Vector3Int> cands = HouseExpansion.ValidTargets(walls, floors, region, brick, lockY);
        if (cands.Count == 0)
        {
            Debug.LogWarning("[House] Валидных клеток нет.");
            return;
        }
        Debug.Log("[House] Покупаю " + cands[0] + " (дерево в рюкзаке: " + HouseExpansion.CountWood() + ").");
        HouseExpansion.TryExpand(cands[0], false);
    }

    // ═══════════════════════════════════════════════════════════
    // Штамп среднего паттерна (калитка наверх): копирует нарисованный
    // владельцем эталон 1-в-1 на ghost-таргет. Якорь эталона — его верхняя
    // клетка пола P (в сцене: (-120,2)). Совмещение: P → ghost-таргет T,
    // нижний ряд эталона обязан лечь на пол комнаты (проверяем, иначе отказ
    // со списком). Стены копируем только НЕ-фон (пустоту не затираем,
    // замки не трогаем). Пол кладём только недостающий (обычно сам T).
    // ═══════════════════════════════════════════════════════════
    static readonly Vector3Int PatternAnchorP = new Vector3Int(-120, 2, 0);

    [MenuItem("Tools/House/19. Stamp Mid Pattern At Ghost (тест)")]
    public static void StampMidPatternAtGhost()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Только НЕ в Play. Потом Ctrl+S.");
            return;
        }
        if (!FindRoomForExpand(out var walls, out var floors, out var region, out int lockY)) return;
        WallStyle brick = WallStyle.Load(HouseReskin.BaseStyleId);
        if (brick == null)
        {
            Debug.LogError("[House] Нет стиля комнаты.");
            return;
        }
        if (!floors.HasTile(PatternAnchorP))
        {
            Debug.LogError("[House] Нет эталона: в " + PatternAnchorP + " нет пола. Нарисуй средний паттерн сбоку.");
            return;
        }
        List<Vector3Int> cands = HouseExpansion.ValidTargets(walls, floors, region, brick, lockY);
        if (cands.Count == 0)
        {
            Debug.LogWarning("[House] Валидных клеток нет.");
            return;
        }
        float cx = 0;
        foreach (Vector3Int c in region) cx += c.x;
        cx /= region.Count;
        Vector3Int T = cands[0];
        foreach (Vector3Int c in cands)
            if (c.y > T.y || (c.y == T.y && Mathf.Abs(c.x - cx) < Mathf.Abs(T.x - cx))) T = c;
        Vector3Int off = T - PatternAnchorP;
        // Границы эталона: пол региона-якоря ±2.
        var srcRegion = new HashSet<Vector3Int>();
        {
            var seen = new HashSet<Vector3Int> { PatternAnchorP };
            var q = new Queue<Vector3Int>();
            q.Enqueue(PatternAnchorP);
            Vector3Int[] dirs = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
            while (q.Count > 0)
            {
                Vector3Int c = q.Dequeue();
                srcRegion.Add(c);
                foreach (Vector3Int d in dirs)
                {
                    Vector3Int n = c + d;
                    if (seen.Contains(n) || !floors.HasTile(n)) continue;
                    seen.Add(n);
                    q.Enqueue(n);
                }
            }
        }
        int sx0 = int.MaxValue, sx1 = int.MinValue, sy0 = int.MaxValue, sy1 = int.MinValue;
        foreach (Vector3Int c in srcRegion)
        {
            if (c.x < sx0) sx0 = c.x; if (c.x > sx1) sx1 = c.x;
            if (c.y < sy0) sy0 = c.y; if (c.y > sy1) sy1 = c.y;
        }
        // Проверка: пол эталона (кроме P) обязан лечь на пол комнаты.
        var missing = new List<Vector3Int>();
        foreach (Vector3Int c in srcRegion)
        {
            if (c == PatternAnchorP) continue;
            if (!floors.HasTile(c + off)) missing.Add(c + off);
        }
        if (missing.Count > 0)
        {
            string s = "";
            foreach (Vector3Int m in missing) s += m + " ";
            Debug.LogError("[House] Штамп не лёг: нижний ряд эталона требует пол, нет: " + s + "(таргет " + T + ").");
            return;
        }
        JournalScope(region, lockY, out int jx0, out int jx1, out int jy0, out int jy1);
        HouseJournal.Record(walls, T, jx0, jx1, jy0, jy1);
        int putFloor = 0, putWall = 0, skipLock = 0, cleared = 0;
        for (int x = sx0 - 4; x <= sx1 + 4; x++)
        {
            for (int y = sy0 - 4; y <= sy1 + 4; y++)
            {
                var s = new Vector3Int(x, y, 0);
                Vector3Int d = s + off;
                if (floors.HasTile(s) && !floors.HasTile(d))
                {
                    floors.SetTile(d, floors.GetTile(s));
                    putFloor++;
                }
                UnityEngine.Tilemaps.TileBase wt = walls.GetTile(s);
                UnityEngine.Tilemaps.TileBase cur = walls.GetTile(d);
                bool dstLocked = cur != null && brick.lockTileNames != null && brick.lockTileNames.Contains(cur.name);
                if (wt == null || (brick.bgFill != null && wt == brick.bgFill))
                {
                    // В эталоне стены нет: где встал пол — старую стену снести
                    // (стена выше и перекрывает пол). Замки не трогаем.
                    if (floors.HasTile(d) && cur != null && !dstLocked)
                    {
                        walls.SetTile(d, null);
                        cleared++;
                    }
                    continue; // пустоту не копируем
                }
                if (dstLocked) { skipLock++; continue; }
                if (cur != wt)
                {
                    walls.SetTile(d, wt);
                    putWall++;
                }
            }
        }
        if (putFloor + putWall > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[House] Штамп: якорь " + PatternAnchorP + " → " + T + " (сдвиг " + off +
                  "), пола +" + putFloor + ", стен +" + putWall + ", снесено стен под полом " + cleared +
                  ", замков пропущено " + skipLock +
                  ". Смотри глазами в Play. Учти: п.6 будет ругаться (код считал бы иначе) — это ожидаемо.");
    }

    // ═══════════════════════════════════════════════════════════
    // Журнал построек: снапшот стен до стройки (для сноса 21).
    // ═══════════════════════════════════════════════════════════
    static void JournalScope(HashSet<Vector3Int> region, int lockY,
        out int x0, out int x1, out int y0, out int y1)
    {
        int minX = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Vector3Int c in region)
        {
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        x0 = minX - 4; x1 = maxX + 4; y0 = lockY - 1; y1 = maxY + 4;
    }

    [MenuItem("Tools/House/21. Shrink (снести снесённое обратно)")]
    public static void ShrinkRemoved()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Только НЕ в Play.");
            return;
        }
        if (!FindRoomForExpand(out var walls, out var floors, out var region, out int lockY)) return;
        int n = HouseJournal.UndoRemoved(walls, floors);
        if (n == 0)
        {
            Debug.Log("[House] Сносить нечего (все journal-таргеты с полом).");
            return;
        }
        // Оставшиеся стройки пересобираем как свежие: выйдет фигура
        // ровно под оставшиеся полы (как будто столько и ставили).
        WallStyle st = AssetDatabase.LoadAssetAtPath<WallStyle>(WallStylePath);
        if (st == null)
        {
            Debug.LogError("[House] Нет WallStyle_Brick (пункт 5).");
            return;
        }
        var fresh = HouseJournal.RemainingTargets(floors);
        var unmapped = new List<string>();
        int painted = HouseAutotiler.Stabilize(walls, floors, region, st, lockY,
            fresh, st.floorTile, unmapped);
        // Снесённые полы могли держать мебель — проверить (фаза 5, склад не трогаем).
        HouseFurniture.RefreshPlaced(walls, floors, false);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        var after = HouseAutotiler.Audit(walls,
            HouseAutotiler.ComputeWanted(walls, region, st, lockY, null, new List<string>()));
        Debug.Log("[House] Снос: построек снято " + n + ", осталось свежих " + fresh.Count +
                  ", записано стен " + painted + ", расхождений " + after.Count + ". Смотри глазами.");
        foreach (string d in after)
            Debug.LogWarning("[House] Остаток: " + d);
    }

    // ═══════════════════════════════════════════════════════════
    // L-коридор (разделитель комнат): L-уголок из 3 полов в главной
    // комнате → колонка по эталону владельца (x=-113: 34/67/196/216/
    // пол/0/34). Пока N-варианты (рукав вверх) + зеркало E/W; S-варианты —
    // когда нарисуешь. Эскизы (малые регионы) не трогаем.
    // ═══════════════════════════════════════════════════════════
    [MenuItem("Tools/House/23. Build L-Corridor Column (L-уголок → колонка)")]
    public static void BuildCorridorColumn()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Только НЕ в Play.");
            return;
        }
        if (!FindRoomForExpand(out var walls, out var floors, out var region, out int lockY)) return;
        WallStyle brick = AssetDatabase.LoadAssetAtPath<WallStyle>(WallStylePath);
        if (brick == null)
        {
            Debug.LogError("[House] Нет WallStyle_Brick (пункт 5).");
            return;
        }
        int built = BuildCorridorColumnCore(walls, floors, region, brick, lockY, out int skipped);
        if (built > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[House] Колонки: построено клеток " + built + ", пропущено уголков " + skipped +
                  " (готово/не влезло/S-вариант). Потом Ctrl+S.");
    }

    /// <summary>
    /// Общее ядро колонки (п.23): L-уголок 2x2 с 3 полами + N-рукав → колонка
    /// 34/67/196/216/пол/0/34 в сторону E/W-рукава. Используется и в п.20
    /// (ручное кольцо ставит коробку + колонку за один проход). Возвращает
    /// число записанных клеток стен.
    /// Гейты (как строгий мост 1в): низ колонки не ниже ряда двери (lockY),
    /// якорь в регионе, вторая масса за колонкой и отвязана от изгиба
    /// (иначе одиночный выступ — не мост, там обычное кольцо).
    /// </summary>
    static int BuildCorridorColumnCore(UnityEngine.Tilemaps.Tilemap walls,
        UnityEngine.Tilemaps.Tilemap floors, HashSet<Vector3Int> region, WallStyle brick, int lockY, out int skipped)
    {
        skipped = 0;
        if (walls == null || floors == null || region == null || brick == null) return 0;
        UnityEngine.Tilemaps.TileBase tileH0 = brick.columnMid0;
        if (tileH0 == null)
            tileH0 = AssetDatabase.LoadAssetAtPath<UnityEngine.Tilemaps.TileBase>(TileDir + "Tileset House_0.asset");
        // Ряд колонки сверху вниз (эталон): 34, 67, 196, 216, ПОЛ, 0, 34.
        UnityEngine.Tilemaps.TileBase[] col = {
            brick.straightV, brick.westTurnA, brick.bandTopEdge,
            brick.bandBotEdge, null, tileH0, brick.straightV };
        int[] dys = { 4, 3, 2, 1, 0, -1, -2 };
        for (int i = 0; i < col.Length; i++)
        {
            if (i == 4) continue; // пол — проверяем отдельно
            if (col[i] == null)
            {
                Debug.LogError("[House] Нет тайла колонки (№" + i + ") — прогони пункт 5 / проверь Tileset House_0.");
                return 0;
            }
        }
        // L-уголки главной комнаты: блок 2x2 ровно с 3 полами.
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (Vector3Int c in region)
        {
            if (c.x < minX) minX = c.x; if (c.x > maxX) maxX = c.x;
            if (c.y < minY) minY = c.y; if (c.y > maxY) maxY = c.y;
        }
        int built = 0;
        Vector3Int[] card = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        for (int x = minX - 1; x <= maxX + 1; x++)
        {
            for (int y = minY - 1; y <= maxY + 1; y++)
            {
                // 4 поворота блока 2x2: не хватает одной из 4 клеток.
                Vector3Int[] sq = {
                    new Vector3Int(x, y, 0), new Vector3Int(x + 1, y, 0),
                    new Vector3Int(x, y + 1, 0), new Vector3Int(x + 1, y + 1, 0) };
                int missing = -1, have = 0;
                for (int k = 0; k < 4; k++)
                {
                    if (region.Contains(sq[k])) have++;
                    else missing = k;
                }
                if (have != 3) continue;
                // Изгиб = клетка, смежная с обеими другими (напротив пропущенной).
                Vector3Int bend = sq[3 - missing];
                if (!region.Contains(bend)) continue;
                var arms = new List<Vector3Int>();
                foreach (Vector3Int d in card)
                {
                    Vector3Int n = bend + d;
                    if (n == sq[missing]) continue;
                    if (region.Contains(n)) arms.Add(d);
                }
                if (arms.Count != 2) continue;
                // Пока только N-рукав (вверх): колонка вертикально, в сторону E/W-рукава.
                Vector3Int vArm = Vector3Int.zero, hArm = Vector3Int.zero;
                foreach (Vector3Int a in arms)
                {
                    if (a == Vector3Int.up || a == Vector3Int.down) vArm = a;
                    else hArm = a;
                }
                if (vArm != Vector3Int.up || hArm == Vector3Int.zero) continue; // S-варианты позже
                int sx = hArm == Vector3Int.right ? 1 : -1;
                // Низ колонки (bend.y-2) не должен уходить в ряд двери и ниже.
                if (bend.y - 2 < lockY) { skipped++; continue; }
                // Проверка: клетка колонки на уровне изгиба — пол региона есть.
                Vector3Int anchor = bend + new Vector3Int(sx, 0, 0);
                if (!region.Contains(anchor))
                {
                    skipped++;
                    continue;
                }
                // Вторая масса за колонкой, отвязанная от изгиба (иначе это
                // одиночный выступ той же комнаты — мост не строим).
                Vector3Int farCell = default;
                bool farMass = false;
                for (int fx = bend.x + sx * 2; fx != bend.x + sx * 4 && !farMass; fx += sx)
                {
                    for (int fy = bend.y - 1; fy <= bend.y + 1; fy++)
                    {
                        var fc = new Vector3Int(fx, fy, 0);
                        if (region.Contains(fc)) { farMass = true; farCell = fc; break; }
                    }
                }
                if (!farMass) { skipped++; continue; }
                if (HouseAutotiler.ConnectedThruFloor(region, bend, farCell, 400)) { skipped++; continue; }
                // Проверка: остальные клетки колонки без пола.
                bool ok = true;
                for (int i = 0; i < dys.Length; i++)
                {
                    if (i == 4) continue;
                    if (floors.HasTile(new Vector3Int(bend.x + sx, bend.y + dys[i], 0))) { ok = false; break; }
                }
                if (!ok) { skipped++; continue; }
                // Совпадает — пропускаем (идемпотентно).
                bool same = true;
                for (int i = 0; i < dys.Length; i++)
                {
                    if (i == 4) continue;
                    if (walls.GetTile(new Vector3Int(bend.x + sx, bend.y + dys[i], 0)) != col[i]) { same = false; break; }
                }
                if (same) { skipped++; continue; }
                for (int i = 0; i < dys.Length; i++)
                {
                    if (i == 4) continue;
                    Vector3Int d = new Vector3Int(bend.x + sx, bend.y + dys[i], 0);
                    UnityEngine.Tilemaps.TileBase cur = walls.GetTile(d);
                    if (cur != null && brick.lockTileNames != null && brick.lockTileNames.Contains(cur.name)) continue;
                    walls.SetTile(d, col[i]);
                    built++;
                }
            }
        }
        return built;
    }

    [MenuItem("Tools/House/22. Clear Journal (забыть стройки)")]
    public static void ClearJournal()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Только НЕ в Play.");
            return;
        }
        HouseJournal.Clear();
    }

    // ═══════════════════════════════════════════════════════════
    // Эталоны сбоку (паттерны +1 вверх/влево/вправо): сверка каждого
    // маленького региона пола (не главной комнаты) с автотайлером.
    // Дверного ряда у паттернов нет — lock = minY-2 (кольцо снизу видно).
    // ═══════════════════════════════════════════════════════════
    static List<HashSet<Vector3Int>> AllFloorRegions(UnityEngine.Tilemaps.Tilemap floors)
    {
        var all = new List<HashSet<Vector3Int>>();
        if (floors == null) return all;
        var seen = new HashSet<Vector3Int>();
        Vector3Int[] dirs = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        foreach (Vector3Int start in floors.cellBounds.allPositionsWithin)
        {
            if (!floors.HasTile(start) || seen.Contains(start)) continue;
            var region = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(start);
            seen.Add(start);
            while (queue.Count > 0)
            {
                Vector3Int c = queue.Dequeue();
                region.Add(c);
                foreach (Vector3Int d in dirs)
                {
                    Vector3Int n = c + d;
                    if (seen.Contains(n) || !floors.HasTile(n)) continue;
                    seen.Add(n);
                    queue.Enqueue(n);
                }
            }
            all.Add(region);
        }
        all.Sort((a, b) => b.Count.CompareTo(a.Count));
        return all;
    }

    [MenuItem("Tools/House/18. Audit Side Patterns (эталоны сбоку)")]
    public static void AuditSidePatterns()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Только НЕ в Play.");
            return;
        }
        if (!FindRoomMaps(out var walls, out var floors)) return;
        WallStyle st = LoadBrick();
        if (st == null) return;

        var all = AllFloorRegions(floors);
        if (all.Count == 0)
        {
            Debug.LogError("[House] Пола нет вообще.");
            return;
        }
        Debug.Log("[House] Регионов пола: " + all.Count + ", главный " + all[0].Count + " клеток (его смотрят п.6/7).");
        for (int i = 1; i < all.Count; i++)
        {
            var region = all[i];
            if (region.Count > 12) continue; // только маленькие эталоны
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (Vector3Int c in region)
            {
                if (c.x < minX) minX = c.x;
                if (c.x > maxX) maxX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.y > maxY) maxY = c.y;
            }
            // Текущее кольцо вокруг эталона (имена тайлов).
            string ring = "";
            for (int y = maxY + 2; y >= minY - 2; y--)
            {
                for (int x = minX - 2; x <= maxX + 2; x++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (region.Contains(c)) { ring += "[ПОЛ] "; continue; }
                    var t = walls.GetTile(c);
                    ring += (t != null ? t.name.Replace("Tileset House_", "") : ".") + " ";
                }
                ring += "| y=" + y + "\n";
            }
            Debug.Log("[House] Эталон №" + i + ": пол " + region.Count + ", bbox x[" + minX + "," + maxX +
                      "] y[" + minY + "," + maxY + "]. Кольцо (сейчас):\n" + ring);

            var unmapped = new List<string>();
            var wanted = HouseAutotiler.ComputeWanted(walls, region, st, minY - 2, null, unmapped);
            // Своя сверка: пустота→фон вдали (клетки вообще не крашены) — шум, пропускаем.
            var diffs = new List<string>();
            foreach (var kvp in wanted)
            {
                UnityEngine.Tilemaps.TileBase cur = walls.GetTile(kvp.Key);
                if (cur == kvp.Value) continue;
                if (cur == null && kvp.Value == st.bgFill) continue;
                string cn = cur != null ? cur.name : "<пусто>";
                string wn = kvp.Value != null ? kvp.Value.name : "<пусто>";
                diffs.Add(kvp.Key + ": сейчас " + cn + " → надо " + wn);
            }
            foreach (string u in unmapped)
                Debug.LogWarning("[House] Эталон №" + i + " UNMAPPED: " + u);
            if (diffs.Count == 0)
                Debug.Log("[House] Эталон №" + i + ": ЧИСТО (код рисует так же).");
            else
            {
                Debug.LogError("[House] Эталон №" + i + ": расхождений " + diffs.Count + ":");
                foreach (string d in diffs)
                    Debug.LogError("[House] Эталон №" + i + " diff: " + d);
            }
        }
        Debug.Log("[House] Готово: скопируй diff эталонов сюда текстом.");
    }

    // ═══════════════════════════════════════════════════════════
    // ФАЗА 5: мебель (24–27). Сцену не сохраняем: база в файле чистая,
    // тесты в открытой сцене без сохранения (протокол). 24 — набор каталога
    // (только дописывает недостающее + цены где 0, ручное не трёт).
    // ═══════════════════════════════════════════════════════════
    const string CandlePrefabPath = "Assets/Prefab/Interior/Candles/Candle 1_0.prefab";

    static int FurniturePriceFor(string prefabName)
    {
        if (prefabName == "Beds_0") return 300;
        if (prefabName == "Tables_71") return 200;
        if (prefabName == "Chairs_13") return 100;
        if (prefabName == "Candle 1_0") return 50;
        if (prefabName.StartsWith("Part 1 copiar_")) return 150;
        return 100;
    }

    [MenuItem("Tools/House/24. Furniture Test Set (каталог 8 + цены)")]
    public static void EnsureFurnitureTestSet()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Выключи Play-режим.");
            return;
        }
        BuildMountTable(); // недостающие записи таблицы (ручные не трёт)

        // Напольные из папки Part* (увидели глазами): торшер + цветок + предмет у двери.
        string[] floorOverrides = { "Part 1 copiar_29", "Part 1 copiar_114", "Part 1 copiar_116" };
        FurnitureMountTable mtable = AssetDatabase.LoadAssetAtPath<FurnitureMountTable>(MountTablePath);
        if (mtable != null)
        {
            foreach (string ov in floorOverrides)
            {
                foreach (FurnitureMountTable.Entry en in mtable.entries)
                {
                    if (en != null && en.prefabName == ov && en.mount != FurnitureMount.Floor)
                    {
                        en.mount = FurnitureMount.Floor;
                        mtable.RebuildCache();
                        EditorUtility.SetDirty(mtable);
                        Debug.Log("[House] " + ov + " переведён в Floor (был Wall).");
                        break;
                    }
                }
            }
        }

        HouseCatalog catalog = AssetDatabase.LoadAssetAtPath<HouseCatalog>(CatalogPath);
        if (catalog == null)
        {
            Debug.LogError("[House] Нет каталога — сначала пункт 2.");
            return;
        }
        bool hasCandle = false;
        foreach (HouseCatalog.Entry e in catalog.entries)
        {
            if (e != null && e.prefab != null && e.prefab.name == "Candle 1_0") { hasCandle = true; break; }
        }
        if (!hasCandle)
        {
            GameObject candle = AssetDatabase.LoadAssetAtPath<GameObject>(CandlePrefabPath);
            if (candle == null)
                Debug.LogError("[House] Нет префаба свечи: " + CandlePrefabPath);
            else
            {
                catalog.entries.Add(new HouseCatalog.Entry { prefab = candle, price = 50 });
                Debug.Log("[House] В каталог добавлена свеча Candle 1_0 (кандидат мелочёвки).");
            }
        }
        int priced = 0;
        foreach (HouseCatalog.Entry e in catalog.entries)
        {
            if (e == null || e.prefab == null) continue;
            if (e.price == 0)
            {
                e.price = FurniturePriceFor(e.prefab.name);
                priced++;
            }
        }
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        Debug.Log("[House] Набор: записей " + catalog.entries.Count + ", цен проставлено " + priced +
                  " (ненулевые не тронуты, правятся в инспекторе). Часы и ковёр (Both) — " +
                  "покажи какие префабы, добавим следующим шагом.");
    }

    [MenuItem("Tools/House/25. Audit Furniture Catalog (только сверка)")]
    public static void AuditFurnitureCatalog()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Выключи Play-режим.");
            return;
        }
        HouseCatalog catalog = AssetDatabase.LoadAssetAtPath<HouseCatalog>(CatalogPath);
        if (catalog == null)
        {
            Debug.LogError("[House] Нет каталога — сначала пункт 2.");
            return;
        }
        FurnitureMountTable table = AssetDatabase.LoadAssetAtPath<FurnitureMountTable>(MountTablePath);
        Debug.Log("[House] Каталог: записей " + catalog.entries.Count + " (ждём 8: кровать/стол/стул/4 настенных/свеча).");
        foreach (HouseCatalog.Entry e in catalog.entries)
        {
            if (e == null || e.prefab == null)
            {
                Debug.LogError("[House] Пустая запись каталога!");
                continue;
            }
            string nm = e.prefab.name;
            FurnitureMount mount = table != null ? table.GetMount(nm)
                : FurnitureMountTable.DefaultForPrefabName(nm);
            bool hasTable = table != null && table.HasEntry(nm);
            BoxCollider2D col = e.prefab.GetComponent<BoxCollider2D>();
            string colInfo = col != null ? ("футпринт " + col.size.x.ToString("0.00") + "x" + col.size.y.ToString("0.00")) : "без коллайдера";
            SpriteRenderer sr = e.prefab.GetComponent<SpriteRenderer>();
            string sprInfo = (sr != null && sr.sprite != null)
                ? ("спрайт " + sr.sprite.bounds.size.x.ToString("0.00") + "x" + sr.sprite.bounds.size.y.ToString("0.00"))
                : "НЕТ СПРАЙТА";
            bool ysort = false;
            foreach (MonoBehaviour mb in e.prefab.GetComponents<MonoBehaviour>())
            {
                if (mb != null && mb.GetType().Name == "YSort") { ysort = true; break; }
            }
            // Свеча без MonoBehaviour вообще — GetComponents вернёт пусто, это норма.
            Debug.Log("[House] " + nm + ": mount=" + mount + (hasTable ? "" : " (НЕТ В ТАБЛИЦЕ!)") +
                      ", цена=" + e.price + ", " + colInfo + ", " + sprInfo + ", YSort=" + (ysort ? "да" : "нет"));
        }
    }

    /// <summary>Прямоугольники уже расставленной мебели (чтобы тест не клал в одну точку).</summary>
    static List<Rect> OccupiedRects()
    {
        var list = new List<Rect>();
        foreach (HousePlacedFurniture f in HouseFurniture.FindPlaced())
        {
            if (f == null) continue;
            SpriteRenderer sr = f.GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                Bounds b = sr.bounds;
                list.Add(new Rect(b.min.x, b.min.y, b.size.x, b.size.y));
            }
            else
            {
                Vector3 p = f.transform.position;
                list.Add(new Rect(p.x - 0.5f, p.y - 0.5f, 1f, 1f));
            }
        }
        return list;
    }

    static bool HitsOccupied(List<Rect> occ, Vector2 center, Vector2 size)
    {
        // С усадкой 0.8: лёгкие касания разрешаем, полные стаки — нет.
        Rect r = new Rect(center.x - size.x * 0.4f, center.y - size.y * 0.4f,
            size.x * 0.8f, size.y * 0.8f);
        foreach (Rect o in occ)
            if (r.Overlaps(o)) return true;
        return false;
    }

    /// <summary>Первое валидное место: напольным — центры клеток пола, настенным — центры клеток стен.</summary>
    static bool FindSpotFor(UnityEngine.Tilemaps.Tilemap walls, UnityEngine.Tilemaps.Tilemap floors,
        HashSet<Vector3Int> region, GameObject prefab, FurnitureMount mount, out Vector3 spot)
    {
        spot = Vector3.zero;
        List<Rect> occ = OccupiedRects();
        if (mount == FurnitureMount.Wall)
        {
            if (!HouseFurniture.SpriteSize(prefab, out Vector2 ssize, out Vector2 scenter))
                return false;
            // Только кольцо КОМНАТЫ: клетка стены не дальше 2 от пола региона.
            // Иначе поиск хватает боковые паттерны/остатки в пустоте (баг 26: y=12.5).
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (Vector3Int c in region)
            {
                if (c.x < minX) minX = c.x; if (c.x > maxX) maxX = c.x;
                if (c.y < minY) minY = c.y; if (c.y > maxY) maxY = c.y;
            }
            for (int y = maxY + 4; y >= minY - 4; y--)
                for (int x = minX - 4; x <= maxX + 4; x++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (!walls.HasTile(c)) continue;
                    bool near = false;
                    foreach (Vector3Int f in region)
                    {
                        if (Mathf.Abs(f.x - x) <= 2 && Mathf.Abs(f.y - y) <= 2) { near = true; break; }
                    }
                    if (!near) continue;
                    Vector3 p = walls.GetCellCenterWorld(c);
                    if (HitsOccupied(occ, (Vector2)p + scenter, ssize)) continue;
                    if (HouseFurniture.CanPlaceAt(walls, floors, region, prefab, p, mount, out _)) { spot = p; return true; }
                }
            return false;
        }
        // Напольным: габарит для разноса — футпринт, без коллайдера — полспрайта.
        Vector2 gapSize = new Vector2(0.5f, 0.5f);
        Vector2 gapOff = Vector2.zero;
        if (HouseFurniture.Footprint(prefab, out Vector2 fsize, out Vector2 foff))
        {
            gapSize = fsize;
            gapOff = foff;
        }
        else if (HouseFurniture.SpriteSize(prefab, out Vector2 ssize2, out Vector2 scenter2))
        {
            gapSize = ssize2 * 0.5f;
            gapOff = scenter2;
        }
        var cells = new List<Vector3Int>(region);
        cells.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        foreach (Vector3Int c in cells)
        {
            Vector3 p = floors.GetCellCenterWorld(c);
            if (HitsOccupied(occ, (Vector2)p + gapOff, gapSize)) continue;
            if (HouseFurniture.CanPlaceAt(walls, floors, region, prefab, p, mount, out _)) { spot = p; return true; }
        }
        return false;
    }

    [MenuItem("Tools/House/26. Test Place All (Edit, оставить для глаз)")]
    public static void TestPlaceAllFurniture()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Только НЕ в Play. Потом смотри в Play, сцену НЕ сохраняй (п.27 уберёт).");
            return;
        }
        if (!FindRoomForExpand(out var walls, out var floors, out var region, out int lockY)) return;
        HouseCatalog catalog = AssetDatabase.LoadAssetAtPath<HouseCatalog>(CatalogPath);
        if (catalog == null)
        {
            Debug.LogError("[House] Нет каталога — сначала пункт 2.");
            return;
        }
        int placed = 0;
        foreach (HouseCatalog.Entry e in catalog.entries)
        {
            if (e == null || e.prefab == null) continue;
            FurnitureMount mount = HouseFurniture.MountOfPrefab(e.prefab.name);
            if (!FindSpotFor(walls, floors, region, e.prefab, mount, out Vector3 spot))
            {
                Debug.LogError("[House] Не влезло: " + e.prefab.name + " (mount " + mount + ").");
                continue;
            }
            GameObject go = HouseFurniture.TryPlace(walls, floors, e.prefab, spot, false, out string reason);
            if (go == null)
                Debug.LogError("[House] Не встало: " + e.prefab.name + " — " + reason);
            else
            {
                placed++;
                Debug.Log("[House] Стоит: " + e.prefab.name + " в " + spot + " (mount " + mount + ").");
            }
        }
        // Both-путь (ковры): свечи нет в таблице как Both — гоняем правило в памяти.
        GameObject candle = AssetDatabase.LoadAssetAtPath<GameObject>(CandlePrefabPath);
        if (candle != null && region.Count > 0)
        {
            var cells = new List<Vector3Int>(region);
            cells.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            Vector3 p = floors.GetCellCenterWorld(cells[0]);
            if (HouseFurniture.CanPlaceAt(walls, floors, region, candle, p, FurnitureMount.Both, out string br))
                Debug.Log("[House] Both-путь: ОК на " + p + " (ковёр добавим, когда покажешь префаб).");
            else
                Debug.LogError("[House] Both-путь: отказ — " + br);
        }
        if (placed > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[House] Поставлено " + placed + ". Сцену НЕ сохраняй! Смотри в Play глазами, потом п.27.");
    }

    [MenuItem("Tools/House/27. Remove Test Furniture (убрать)")]
    public static void RemoveTestFurniture()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Только НЕ в Play.");
            return;
        }
        HousePlacedFurniture[] all = HouseFurniture.FindPlaced();
        int n = 0;
        foreach (HousePlacedFurniture f in all)
        {
            if (f == null) continue;
            Object.DestroyImmediate(f.gameObject);
            n++;
        }
        Debug.Log("[House] Убрано мебели: " + n + ". Если сцена запачкана тестами — перезагрузи без сохранения.");
    }

    // ═══════════════════════════════════════════════════════════
    // 28. Дамп зоны настенных (ТОЛЬКО ЧТЕНИЕ): сетка тайлов стен,
    // вручную повешенный декор (без маркера) + тайлы под ним.
    // Сцену не пачкает, склад не трогает. Лог — сюда текстом.
    // ═══════════════════════════════════════════════════════════
    [MenuItem("Tools/House/28. Dump Wall Zone (только чтение)")]
    public static void DumpWallZone()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Только НЕ в Play.");
            return;
        }
        if (!FindRoomForExpand(out var walls, out var floors, out var region, out int lockY)) return;

        // Сетка стен x[-90,-77] y[13..7]: короткие имена, ~ = фон 76, . = пусто, F = пол.
        for (int y = 13; y >= 7; y--)
        {
            string row = "";
            for (int x = -90; x <= -77; x++)
            {
                var c = new Vector3Int(x, y, 0);
                UnityEngine.Tilemaps.TileBase t = walls.GetTile(c);
                string wn = t != null ? t.name.Replace("Tileset House_", "").Replace(" 1", "'") : ".";
                if (wn == "76") wn = "~";
                string fl = floors.HasTile(c) ? "F" : "_";
                row += fl + wn + " ";
            }
            Debug.Log("[House] 28 map y=" + y + ": " + row);
        }

        // Ручной декор: дети Interior без маркера HousePlacedFurniture (тестовые с маркером — отдельно).
        GameObject interior = GameObject.Find("Interior");
        if (interior == null)
        {
            Debug.LogWarning("[House] 28: нет объекта Interior.");
            return;
        }
        foreach (Transform tr in interior.transform)
        {
            if (tr == null) continue;
            SpriteRenderer sr = tr.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) continue;
            bool isTest = tr.GetComponent<HousePlacedFurniture>() != null;
            Vector3 p = tr.position;
            Vector2 center = (Vector2)p + (Vector2)sr.sprite.bounds.center;
            Vector2 size = sr.sprite.bounds.size;
            Vector3Int anchor = walls.WorldToCell(p);
            UnityEngine.Tilemaps.TileBase at = walls.GetTile(anchor);
            // Клетки спрайта + тайлы стен под ними.
            Vector3Int c0 = walls.WorldToCell(new Vector3(center.x - size.x * 0.5f, center.y - size.y * 0.5f, 0f));
            Vector3Int c1 = walls.WorldToCell(new Vector3(center.x + size.x * 0.5f, center.y + size.y * 0.5f, 0f));
            string cover = "";
            for (int x = c0.x; x <= c1.x; x++)
                for (int y = c0.y; y <= c1.y; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    UnityEngine.Tilemaps.TileBase t = walls.GetTile(c);
                    cover += c + "=" + (t != null ? t.name.Replace("Tileset House_", "") : ".") + " ";
                }
            Debug.Log("[House] 28 " + (isTest ? "ТЕСТ" : "РУЧНОЙ") + " " + tr.name +
                      " в " + p.ToString("0.00") + ", якорь " + anchor +
                      "=(" + (at != null ? at.name.Replace("Tileset House_", "") : ".") + ")" +
                      ", спрайт " + size.x.ToString("0.00") + "x" + size.y.ToString("0.00") +
                      " | " + cover);
        }
        Debug.Log("[House] 28 готово: скопируй весь лог сюда текстом.");
    }

    // ═══════════════════════════════════════════════════════════
    // ФАЗА 7: карточка входа в планировку (чертёж, многоразовый).
    // Удар с чертежом в хотбаре = HouseEditMode.Toggle. Иконку проставь
    // в инспекторе ассета. Цена на ассете (правится там же).
    // ═══════════════════════════════════════════════════════════
    [MenuItem("Tools/House/30. Build Blueprint Card (вход в планировку)")]
    public static void BuildBlueprintCard()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[House] Выключи Play-режим.");
            return;
        }
        const string dir = "Assets/Resources/Items/House";
        EnsureFolder(dir);
        const string path = "Assets/Resources/Items/House/Blueprint.asset";
        ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(item, path);
        }
        item.itemName = "Чертёж дома";
        item.description = "Многоразовый чертёж. Удар с ним в руках — режим планировки дома.";
        item.itemType = ItemType.Blueprint;
        item.rarity = ItemRarity.Common;
        item.isStackable = false;
        item.maxStack = 1;
        if (item.shopPrice <= 0) item.shopPrice = 500;
        EditorUtility.SetDirty(item);
        AssetDatabase.SaveAssets();
        Selection.activeObject = item;
        if (item.icon == null)
        {
            // Тестовая иконка (владелец заменит артом): спрайт плитки кирпича.
            ItemData brick = AssetDatabase.LoadAssetAtPath<ItemData>(
                "Assets/Resources/Items/House/FloorTile_Brick.asset");
            if (brick != null && brick.icon != null)
            {
                item.icon = brick.icon;
                item.worldSprite = brick.worldSprite;
                Debug.Log("[House] Чертёж готов (в магазине появится сам). Иконка ВРЕМЕННАЯ (от плитки) — замени артом в инспекторе.");
            }
            else
            {
                Debug.LogWarning("[House] Чертёж готов, но без иконки — проставь спрайт в инспекторе (иначе в хотбаре пусто).");
            }
        }
        else
        {
            Debug.Log("[House] Чертёж готов (в магазине появится сам, кодом).");
        }
    }
}
