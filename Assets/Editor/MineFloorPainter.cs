#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Художник демо-зоны шахты v3 (пример: MaeveDevs farm-rpg).
/// Blob-пол + пятна земли + пруд на WaterTilemap + рельсы + балки +
/// дверь с лестницей (выход на вход шахты) + гнёзда жил у стен + волт.
/// Спрайты пола/стен — топ вариантов из уже нарисованного.
/// Использование: открой Mine (single!) → Tools/Mine/Paint Demo Zone.
/// </summary>
public static class MineFloorPainter
{
    const string TileDir = "Assets/Art/Tileset/Mine/";
    const string PrefKey = "MinePaintDemo";
    const string PropsSheet = "Assets/Art/Objects/Exterior/Mine and Dungeon/Props Mine.png";
    const string WaterTileBase = "Assets/Art/Tileset Grass Spring/Tiles_Tileset Grass Spring/Cave Water Ground animations tiles_0.asset";

    // Индексы спрайтов Props Mine (по твоей документации; поправь если мимо):
    const int RailStraight = 94;   // прямая рельса
    const int BeamTop = 72;        // верх балки
    const int BeamMid = 73;        // низ балки
    const int DoorDark = 106;      // тёмный вход
    const int Ladder = 180;        // лестница

    static int lastSeed;

    [MenuItem("Tools/Mine/Paint Demo Zone (камень+медь)")]
    public static void Paint()
    {
        lastSeed = Random.Range(1, 999999);
        PaintSeeded(lastSeed);
    }

    [MenuItem("Tools/Mine/Repaint Demo Zone (тот же сид)")]
    public static void Repaint()
    {
        if (lastSeed == 0) lastSeed = EditorPrefs.GetInt(PrefKey + "_seed", 12345);
        PaintSeeded(lastSeed);
    }

    [MenuItem("Tools/Mine/Clear Demo Zone (убрать моё)")]
    public static void ClearDemo()
    {
        var grid = GameObject.Find("Grid");
        if (grid == null) { Debug.LogError("[MinePaint] Нет Grid — открой сцену Mine"); return; }
        var floorMap = grid.transform.Find("InteriorFloor")?.GetComponent<Tilemap>();
        var wallMap = grid.transform.Find("InteriorWalls")?.GetComponent<Tilemap>();
        var waterMap = grid.transform.Find("WaterTilemap")?.GetComponent<Tilemap>();
        var decorT = grid.transform.Find("Decor");
        if (floorMap == null || wallMap == null) return;
        ClearLast(floorMap, wallMap, waterMap, decorT != null ? decorT.GetComponent<Tilemap>() : null);
        var demo = GameObject.Find("DemoZone");
        if (demo != null) Object.DestroyImmediate(demo);
        var dl = GameObject.Find("DemoDownLadder");
        if (dl != null) Object.DestroyImmediate(dl);
        Debug.Log("[MinePaint] Демо убрано. Сохраняй сам (Ctrl+S).");
    }

    static void PaintSeeded(int seed)
    {
        var grid = GameObject.Find("Grid");
        if (grid == null) { Debug.LogError("[MinePaint] Нет Grid — открой сцену Mine"); return; }
        var floorMap = grid.transform.Find("InteriorFloor")?.GetComponent<Tilemap>();
        var wallMap = grid.transform.Find("InteriorWalls")?.GetComponent<Tilemap>();
        var waterMap = grid.transform.Find("WaterTilemap")?.GetComponent<Tilemap>();
        if (floorMap == null || wallMap == null) { Debug.LogError("[MinePaint] Нет InteriorFloor/Walls"); return; }
        var layout = floorMap.layoutGrid;
        if (layout == null) { Debug.LogError("[MinePaint] Нет Grid layout"); return; }
        var decorMap = GetOrCreateDecor(grid.transform, layout);

        var floorVars = TopSprites(floorMap, 3);
        var wallVars = TopSprites(wallMap, 2);
        if (floorVars.Count == 0 || wallVars.Count == 0) { Debug.LogError("[MinePaint] Пустые карты"); return; }
        var floorTiles = GetOrCreateTiles("DemoFloor", floorVars);
        var wallTiles = GetOrCreateTiles("DemoWall", wallVars);

        ClearLast(floorMap, wallMap, waterMap, decorMap);
        var demo = GameObject.Find("DemoZone");
        if (demo != null) Object.DestroyImmediate(demo);

        BoundsInt fb = floorMap.cellBounds, wb = wallMap.cellBounds;
        bool empty = fb.size.x <= 0 && wb.size.x <= 0;
        int originX = empty ? 0 : Mathf.Min(fb.xMin, wb.xMin) - 45;
        int originY = empty ? 0 : (Mathf.Min(fb.yMin, wb.yMin) + Mathf.Max(fb.yMax, wb.yMax)) / 2;

        var rng = new System.Random(seed);
        int rx = 14 + rng.Next(0, 3);
        int ry = 10 + rng.Next(0, 3);

        // Пол: эллипс + шум + карманы
        var floor = new HashSet<Vector2Int>();
        for (int y = -ry - 3; y <= ry + 3; y++)
            for (int x = -rx - 3; x <= rx + 3; x++)
            {
                float nx = (float)x / rx, ny = (float)y / ry;
                float edge = 1f + 0.3f * (Hash2(x * 3 + seed, y * 3 - seed) - 0.5f) * 2f;
                if (nx * nx + ny * ny <= edge) floor.Add(new Vector2Int(x, y));
            }
        AddPocket(floor, rng, rx - 1, 2, 3, 2);
        AddPocket(floor, rng, -rx + 1, -3, 3, 2);

        // Волт в центре: кольцо стен, вход с юга
        Vector2Int vault = Vector2Int.zero;
        var vaultInner = new HashSet<Vector2Int>();
        for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++)
                vaultInner.Add(new Vector2Int(vault.x + x, vault.y + y));
        var vaultRing = new HashSet<Vector2Int>();
        for (int y = -4; y <= 4; y++)
            for (int x = -4; x <= 4; x++)
            {
                float d = Mathf.Sqrt(x * x + y * y);
                if (d <= 3.4f && d >= 2.3f && !(y < -1 && Mathf.Abs(x) <= 1))
                    vaultRing.Add(new Vector2Int(vault.x + x, vault.y + y));
            }

        var walls = new HashSet<Vector2Int>(vaultRing);
        foreach (var c in floor)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var n = new Vector2Int(c.x + dx, c.y + dy);
                    if (!floor.Contains(n) && !vaultInner.Contains(n)) walls.Add(n);
                }
        foreach (var c in vaultInner) floor.Add(c);

        // Пятна тёмной земли (2-й вариант пола)
        var dirt = new HashSet<Vector2Int>();
        for (int i = 0; i < 3; i++)
        {
            var fl = new List<Vector2Int>(floor);
            var c0 = fl[rng.Next(fl.Count)];
            int r = 1 + rng.Next(0, 2);
            for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    var c = new Vector2Int(c0.x + x, c0.y + y);
                    if (floor.Contains(c) && !vaultInner.Contains(c)) dirt.Add(c);
                }
        }

        // Пруд: эллипс справа от центра
        var pond = new HashSet<Vector2Int>();
        Vector2Int pondC = new Vector2Int(rx / 2, 2);
        for (int y = -3; y <= 3; y++)
            for (int x = -4; x <= 4; x++)
            {
                float nx = (float)x / 4, ny = (float)y / 3;
                var c = new Vector2Int(pondC.x + x, pondC.y + y);
                if (nx * nx + ny * ny <= 1f && floor.Contains(c)) pond.Add(c);
            }

        demo = new GameObject("DemoZone");
        Undo.RegisterCreatedObjectUndo(demo, "Mine demo zone");

        // Красим пол (пятна — 2-м вариантом)
        Tile dirtTile = floorTiles.Count > 1 ? floorTiles[1] : floorTiles[0];
        foreach (var c in floor)
            floorMap.SetTile(ToCell(originX, originY, c),
                dirt.Contains(c) ? dirtTile : Pick(floorTiles, rng, 0));
        foreach (var c in walls)
        {
            var cell = ToCell(originX, originY, c);
            if (floorMap.GetTile(cell) == null) wallMap.SetTile(cell, Pick(wallTiles, rng, 0));
        }
        // Вода поверх пола
        TileBase waterTile = AssetDatabase.LoadAssetAtPath<TileBase>(WaterTileBase);
        if (waterTile != null && waterMap != null)
            foreach (var c in pond) waterMap.SetTile(ToCell(originX, originY, c), waterTile);
        else Debug.LogWarning("[MinePaint] Нет воды: " + WaterTileBase);
        floorMap.RefreshAllTiles();
        wallMap.RefreshAllTiles();

        // Рельсы: горизонталь ниже центра по полу
        var railTile = SheetTile("rail", PropsSheet, RailStraight);
        int railY = -3, railsDone = 0;
        if (railTile != null)
            for (int x = -rx + 2; x <= rx - 2; x++)
            {
                var c = new Vector2Int(x, railY);
                if (!floor.Contains(c) || pond.Contains(c) || vaultInner.Contains(c)) continue;
                decorMap.SetTile(ToCell(originX, originY, c), railTile);
                railsDone++;
            }
        // Балки: колонны по 2 через каждые 7 клеток на полу
        var beamT = SheetTile("beamT", PropsSheet, BeamTop);
        var beamM = SheetTile("beamM", PropsSheet, BeamMid);
        int beamsDone = 0;
        if (beamT != null && beamM != null)
            for (int x = -rx + 3; x <= rx - 3; x += 7)
                for (int y = ry; y > -ry; y--)
                {
                    var c = new Vector2Int(x, y);
                    var c2 = new Vector2Int(x, y - 1);
                    if (!floor.Contains(c) || !floor.Contains(c2) || pond.Contains(c)) continue;
                    decorMap.SetTile(ToCell(originX, originY, c), beamT);
                    decorMap.SetTile(ToCell(originX, originY, c2), beamM);
                    beamsDone++;
                    break;
                }
        // Дверь+лестница: южная стена у центра
        Vector2Int doorC = Vector2Int.zero;
        bool foundDoor = false;
        for (int y = -ry - 2; y <= 0 && !foundDoor; y++)
        {
            var wc = new Vector2Int(1, y);
            var fc = new Vector2Int(1, y - 1);
            if (walls.Contains(wc) && floor.Contains(fc) && !pond.Contains(fc)) { doorC = wc; foundDoor = true; }
        }
        if (foundDoor)
        {
            var doorTile = SheetTile("door", PropsSheet, DoorDark);
            var ladderTile = SheetTile("ladder", PropsSheet, Ladder);
            if (doorTile != null) decorMap.SetTile(ToCell(originX, originY, doorC), doorTile);
            var lad = new Vector2Int(doorC.x, doorC.y - 1);
            if (ladderTile != null) decorMap.SetTile(ToCell(originX, originY, lad), ladderTile);
            // Рабочий выход: лестница → на вход шахты
            var go = new GameObject("DemoDownLadder");
            go.transform.position = CellWorld(layout, originX, originY, lad);
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = Vector2.one;
            var dt = go.AddComponent<DoorTeleport>();
            var exit = GameObject.Find("Spawn_FromCity");
            dt.targetSpawn = exit != null ? exit.transform : null;
            dt.triggerOnTouch = false;
            dt.snapCamera = true;
            go.transform.SetParent(demo.transform, true);
            Undo.RegisterCreatedObjectUndo(go, "Demo ladder");
        }
        decorMap.RefreshAllTiles();

        // Жилы: медь гнёздами у стен (без пруда), камень россыпью, богатая — в волт
        var placed = new List<Vector2Int>();
        System.Func<Vector2Int, bool> free = c =>
            floor.Contains(c) && !vaultInner.Contains(c) && !pond.Contains(c)
            && !placed.Contains(c) && !Near(placed, c, 2);
        var nearWall = new List<Vector2Int>();
        foreach (var c in floor)
            if (!vaultInner.Contains(c) && !pond.Contains(c) && WallNeighbours(walls, c) >= 2) nearWall.Add(c);
        Shuffle(nearWall, rng);
        string[] copperKinds = { "OreVein_CopperOre", "OreVein_CopperOre_Small", "OreVein_CopperOre_Pebble" };
        int clusters = 0;
        foreach (var c in nearWall)
        {
            if (clusters >= 3) break;
            if (!free(c)) continue;
            int size = 2 + rng.Next(0, 3), done = 0;
            foreach (var n in Neighbours(c))
            {
                if (done >= size) break;
                if (!free(n)) continue;
                PlaceVein(demo, layout, originX, originY, n, copperKinds[rng.Next(copperKinds.Length)]);
                placed.Add(n);
                done++;
            }
            if (done > 0) { PlaceVein(demo, layout, originX, originY, c, copperKinds[rng.Next(copperKinds.Length)]); placed.Add(c); clusters++; }
        }
        var allFloor = new List<Vector2Int>(floor);
        Shuffle(allFloor, rng);
        int stones = 0;
        string[] stoneKinds = { "OreVein_Stone", "OreVein_Stone", "OreVein_Stone_Rich" };
        foreach (var c in allFloor)
        {
            if (stones >= 7) break;
            if (!free(c)) continue;
            PlaceVein(demo, layout, originX, originY, c, stoneKinds[rng.Next(stoneKinds.Length)]);
            placed.Add(c);
            stones++;
        }
        PlaceVein(demo, layout, originX, originY, vault, "OreVein_CopperOre_Rich");
        placed.Add(vault);

        EditorPrefs.SetInt(PrefKey + "_x", originX);
        EditorPrefs.SetInt(PrefKey + "_y", originY);
        EditorPrefs.SetInt(PrefKey + "_seed", seed);
        Debug.Log("[MinePaint] Демо v3: сид=" + seed + " origin=(" + originX + "," + originY
            + ") гнёзд=" + clusters + " камней=" + stones
            + " рельс=" + railsDone + " балок=" + beamsDone + " дверь=" + foundDoor
            + ". Индексы рельс/балок/двери/лестницы — предположительные, сверь глазами!");
    }

    static Vector3Int ToCell(int ox, int oy, Vector2Int c) => new Vector3Int(ox + c.x, oy + c.y, 0);
    static Vector3 CellWorld(GridLayout layout, int ox, int oy, Vector2Int c) =>
        layout.CellToWorld(ToCell(ox, oy, c)) + new Vector3(0.5f, 0.5f, 0f);

    static float Hash2(int x, int y)
    {
        int h = x * 374761393 + y * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffff) / (float)0xffff;
    }

    static void AddPocket(HashSet<Vector2Int> floor, System.Random rng, int cx, int cy, int rx, int ry)
    {
        for (int y = -ry - 1; y <= ry + 1; y++)
            for (int x = -rx - 1; x <= rx + 1; x++)
            {
                float nx = (float)x / rx, ny = (float)y / ry;
                if (nx * nx + ny * ny <= 1f) floor.Add(new Vector2Int(cx + x, cy + y));
            }
    }

    static IEnumerable<Vector2Int> Neighbours(Vector2Int c)
    {
        yield return c;
        yield return new Vector2Int(c.x + 1, c.y);
        yield return new Vector2Int(c.x - 1, c.y);
        yield return new Vector2Int(c.x, c.y + 1);
        yield return new Vector2Int(c.x, c.y - 1);
    }

    static int WallNeighbours(HashSet<Vector2Int> walls, Vector2Int c)
    {
        int n = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (walls.Contains(new Vector2Int(c.x + dx, c.y + dy))) n++;
        return n;
    }

    static bool Near(List<Vector2Int> list, Vector2Int c, int d)
    {
        foreach (var p in list)
            if (Mathf.Abs(p.x - c.x) + Mathf.Abs(p.y - c.y) < d) return true;
        return false;
    }

    static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    static List<Sprite> TopSprites(Tilemap map, int take)
    {
        var count = new Dictionary<Sprite, int>();
        foreach (var pos in map.cellBounds.allPositionsWithin)
        {
            Sprite s = map.GetSprite(pos);
            if (s == null) continue;
            count[s] = count.TryGetValue(s, out int n) ? n + 1 : 1;
        }
        var sorted = new List<KeyValuePair<Sprite, int>>(count);
        sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
        var res = new List<Sprite>();
        for (int i = 0; i < Mathf.Min(take, sorted.Count); i++) res.Add(sorted[i].Key);
        return res;
    }

    static List<Tile> GetOrCreateTiles(string prefix, List<Sprite> sprites)
    {
        var res = new List<Tile>();
        for (int i = 0; i < sprites.Count; i++)
        {
            string path = TileDir + prefix + "_" + i + ".asset";
            Tile t = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (t == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art/Tileset/Mine"))
                    System.IO.Directory.CreateDirectory("Assets/Art/Tileset/Mine");
                t = ScriptableObject.CreateInstance<Tile>();
                t.sprite = sprites[i];
                AssetDatabase.CreateAsset(t, path);
            }
            else if (t.sprite != sprites[i]) { t.sprite = sprites[i]; EditorUtility.SetDirty(t); }
            res.Add(t);
        }
        AssetDatabase.SaveAssets();
        return res;
    }

    static readonly Dictionary<string, Tile> sheetCache = new Dictionary<string, Tile>();

    static Tile SheetTile(string key, string sheetPath, int index)
    {
        if (sheetCache.TryGetValue(key, out Tile t)) return t;
        foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(sheetPath))
        {
            if (o is Sprite s && s.name.EndsWith("_" + index))
            {
                t = ScriptableObject.CreateInstance<Tile>();
                t.sprite = s;
                sheetCache[key] = t;
                return t;
            }
        }
        Debug.LogWarning("[MinePaint] Нет спрайта _" + index + " в " + sheetPath);
        return null;
    }

    static Tile Pick(List<Tile> tiles, System.Random rng, int _)
    {
        if (tiles.Count == 1) return tiles[0];
        int r = rng.Next(100);
        if (r < 70) return tiles[0];
        return tiles[1 + rng.Next(tiles.Count - 1)];
    }

    static Tilemap GetOrCreateDecor(Transform grid, GridLayout layout)
    {
        var t = grid.Find("Decor");
        if (t == null)
        {
            var go = new GameObject("Decor");
            go.transform.SetParent(grid, false);
            var tm = go.AddComponent<Tilemap>();
            var tr = go.AddComponent<TilemapRenderer>();
            tr.sortingOrder = 5;
            Undo.RegisterCreatedObjectUndo(go, "Decor tilemap");
            return tm;
        }
        return t.GetComponent<Tilemap>();
    }

    static void ClearLast(Tilemap floorMap, Tilemap wallMap, Tilemap waterMap, Tilemap decorMap)
    {
        if (!EditorPrefs.HasKey(PrefKey + "_x"))
        {
            for (int y = -30; y <= -13; y++)
                for (int x = -160; x <= -130; x++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    floorMap.SetTile(cell, null);
                    wallMap.SetTile(cell, null);
                }
            return;
        }
        int ox = EditorPrefs.GetInt(PrefKey + "_x"), oy = EditorPrefs.GetInt(PrefKey + "_y");
        for (int y = -22; y <= 22; y++)
            for (int x = -40; x <= 40; x++)
            {
                var cell = new Vector3Int(ox + x, oy + y, 0);
                floorMap.SetTile(cell, null);
                wallMap.SetTile(cell, null);
                if (waterMap != null) waterMap.SetTile(cell, null);
                if (decorMap != null) decorMap.SetTile(cell, null);
            }
        Debug.Log("[MinePaint] Прошлая зона затёрта");
    }

    static void PlaceVein(GameObject parent, GridLayout layout, int ox, int oy, Vector2Int c, string prefabName)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefab/Mine/" + prefabName + ".prefab");
        if (prefab == null) { Debug.LogWarning("[MinePaint] Нет префаба " + prefabName); return; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.position = CellWorld(layout, ox, oy, c);
        go.transform.SetParent(parent.transform, true);
        Undo.RegisterCreatedObjectUndo(go, "Place " + prefabName);
    }
}
#endif
