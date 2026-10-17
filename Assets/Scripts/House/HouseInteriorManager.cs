using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

/// <summary>
/// Менеджер интерьера дома игрока (конструктор дома), ISaveable "house".
/// Сценовый синглтон (как FarmManager): живёт в сцене с тайлмапами,
/// дубликат в той же сцене уничтожается. Ссылки на InteriorFloor/InteriorWalls
/// назначит владелец в инспекторе на фазе UI — без них менеджер молча
/// отключён (сейв не пишется, как FarmManager без тайлмапов).
/// Фаза 1: счётчик купленных тайлов (лесенка цен) + снимок клеток пола.
/// Перестройка геометрии — фаза 2 (автотайлер), спавн мебели — фаза 5.
/// </summary>
public class HouseInteriorManager : MonoBehaviour, ISaveable
{
    public static HouseInteriorManager Instance;

    [Header("Тайлмапы интерьера (назначит владелец на фазе UI)")]
    public Tilemap floorTilemap;
    public Tilemap wallTilemap;

    // Менеджер сценовый (умирает со сценой) — флаг на инстанс, от двойного
    // восстановления защищает (EnsureInScene грузит сразу + Start грузит ещё раз).
    private bool restored;

    // Клетки, прирощенные честными стройками (билет/меню 17, НЕ free-тесты):
    // на restore прогоняем по ним Stabilize — кольцо вокруг них живёт только
    // в рантайме и в сейве полов его нет. Переживает сессии через блоб grown.
    private readonly HashSet<Vector3Int> grownRuntime = new HashSet<Vector3Int>();

    // Рантайм-журнал сноса (аналог HouseJournal п.21, но в блобе, не в
    // EditorPrefs): таргет стройки → стены скопа ДО неё (только изменившиеся).
    // Снос = откат этих клеток + Stabilize дотягивает концы.
    private readonly Dictionary<Vector3Int, Dictionary<Vector3Int, string>> journalRuntime =
        new Dictionary<Vector3Int, Dictionary<Vector3Int, string>>();
    private readonly Dictionary<Vector3Int, Dictionary<Vector3Int, string>> journalPending =
        new Dictionary<Vector3Int, Dictionary<Vector3Int, string>>();

    /// <summary>Записать прирощенную клетку (зовут TryExpand/TryExpandTicket).</summary>
    public static void NoteGrown(Vector3Int cell)
    {
        var m = Live();
        if (m == null) return;
        m.grownRuntime.Add(cell);
    }

    /// <summary>Живой менеджер сцены (для стройки/сноса из кода).</summary>
    public static HouseInteriorManager Live()
    {
        return FindFirstObjectByType<HouseInteriorManager>();
    }

    /// <summary>Клетка — пристроенная (трекнута)? Основу дома снести нельзя.</summary>
    public bool IsGrown(Vector3Int cell) => grownRuntime.Contains(cell);

    /// <summary>Забыть прирощенную клетку (после сноса пола).</summary>
    public void ForgetGrown(Vector3Int cell) { grownRuntime.Remove(cell); }

    /// <summary>Снапшот стен скопа ДО честной стройки (вторая половина — CommitJournal).</summary>
    public void BeginJournal(Vector3Int target, Tilemap walls, HashSet<Vector3Int> scope)
    {
        var snap = new Dictionary<Vector3Int, string>();
        if (walls != null && scope != null)
        {
            foreach (Vector3Int c in scope)
            {
                TileBase t = walls.GetTile(c);
                snap[c] = t != null ? t.name : "";
            }
        }
        journalPending[target] = snap;
    }

    /// <summary>Зафиксировать в журнал только изменившиеся стройкой клетки.</summary>
    public void CommitJournal(Vector3Int target, Tilemap walls)
    {
        if (!journalPending.TryGetValue(target, out var snap))
            return;
        journalPending.Remove(target);
        var changed = new Dictionary<Vector3Int, string>();
        if (walls != null && snap != null)
        {
            foreach (var kvp in snap)
            {
                TileBase now = walls.GetTile(kvp.Key);
                string nowName = now != null ? now.name : "";
                if (nowName != kvp.Value)
                    changed[kvp.Key] = kvp.Value; // было ДО стройки
            }
        }
        journalRuntime.Remove(target);
        if (changed.Count > 0)
            journalRuntime[target] = changed;
    }

    /// <summary>
    /// Откат стен по журналу (перед сносом пола): вернуть "как было до стройки"
    /// там, где сейчас нет пола. Возвращает, была ли запись.
    /// </summary>
    public bool UndoJournal(Vector3Int target, Tilemap walls, Tilemap floors)
    {
        if (!journalRuntime.TryGetValue(target, out var entry))
            return false;
        journalRuntime.Remove(target);
        int restored = 0, missing = 0;
        foreach (var kvp in entry)
        {
            Vector3Int c = kvp.Key;
            if (floors != null && floors.HasTile(c)) continue; // под полом не трогаем
            TileBase want = HouseReskin.TileByName(kvp.Value);
            if (want == null && !string.IsNullOrEmpty(kvp.Value)) { missing++; continue; }
            if (walls.GetTile(c) != want)
            {
                walls.SetTile(c, want);
                restored++;
            }
        }
        if (missing > 0)
            Debug.LogWarning("[House] Журнал сноса: неизвестных тайлов " + missing + " (пропущены).");
        Debug.Log("[House] Журнал сноса " + target + ": возвращено стен " + restored + ".");
        return true;
    }

    /// <summary>Скоп журнального снапшота: bbox комнаты ±4 (как JournalScope п.20).</summary>
    public static HashSet<Vector3Int> JournalScopeCells(HashSet<Vector3Int> region, int lockY)
    {
        var scope = new HashSet<Vector3Int>();
        if (region == null || region.Count == 0) return scope;
        int minX = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Vector3Int c in region)
        {
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        for (int x = minX - 4; x <= maxX + 4; x++)
            for (int y = lockY - 1; y <= maxY + 4; y++)
                scope.Add(new Vector3Int(x, y, 0));
        return scope;
    }
    /// <summary>
    /// Пересомкнуть кольцо после сноса пола (рантайм-аналог п.21): свежие =
    /// оставшиеся пристройки с полом; мебель, потерявшая опору, — на склад.
    /// Возвращает число записанных стен.
    /// </summary>
    public int RebuildAfterDemolish(out int unmappedCount, out int diffsCount)
    {
        unmappedCount = 0;
        diffsCount = 0;
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
            return 0;
        WallStyle brick = WallStyle.Load(HouseReskin.BaseStyleId);
        if (brick == null) return 0;
        var floorLive = HouseAutotiler.LargestFloorRegion(floors);
        if (floorLive.Count == 0) return 0;
        string styleId = HouseExpansion.DominantFloorStyle(floors, floorLive);
        WallStyle fst = WallStyle.Load(styleId);
        TileBase floorTile = fst != null && fst.floorTile != null ? fst.floorTile : brick.floorTile;
        var fresh = new HashSet<Vector3Int>();
        foreach (Vector3Int c in grownRuntime)
            if (floors.HasTile(c)) fresh.Add(c);
        var unmapped = new List<string>();
        int painted = HouseAutotiler.Stabilize(walls, floors, floorLive, brick, lockY,
            fresh, floorTile, unmapped);
        foreach (Vector3Int c in fresh) grownRuntime.Add(c);
        HouseFurniture.RefreshPlaced(walls, floors, true);
        unmappedCount = unmapped.Count;
        var checkWanted = HouseAutotiler.ComputeWanted(walls, floorLive, brick, lockY,
            null, new List<string>());
        var diffs = HouseAutotiler.Audit(walls, checkWanted);
        diffsCount = diffs.Count;
        foreach (string d in diffs)
            Debug.LogWarning("[House] Кольцо после сноса: " + d);
        foreach (string u in unmapped)
            Debug.LogWarning("[House] Снос UNMAPPED: " + u);
        return painted;
    }

    /// <summary>Автосоздание в сцене (паттерн PlaceablesSaveManager): зовётся из
    /// SaveManager.ProcessScene. Тайлмапы резолвятся сами по именам.</summary>
    public static void EnsureInScene(Scene scene)
    {
        if (!scene.IsValid()) return;

        foreach (HouseInteriorManager m in FindObjectsByType<HouseInteriorManager>(FindObjectsSortMode.None))
            if (m != null && m.gameObject.scene == scene) return;

        GameObject go = new GameObject("HouseInteriorManager");
        SceneManager.MoveGameObjectToScene(go, scene);
        var manager = go.AddComponent<HouseInteriorManager>();

        SaveManager.Instance?.LoadInto(manager);
    }

    // Сколько тайлов пола докуплено сверх стартовых (лесенка HousePricing).
    private int expandedCount = 0;

    void Awake()
    {
        if (Instance != null && Instance != this && Instance.gameObject.scene == gameObject.scene)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Сцену руками не трогаем: ссылки резолвим сами (паттерн проекта).
        if (floorTilemap == null)
        {
            GameObject fgo = GameObject.Find("InteriorFloor");
            if (fgo != null) floorTilemap = fgo.GetComponent<Tilemap>();
        }
        if (wallTilemap == null)
        {
            GameObject wgo = GameObject.Find("InteriorWalls");
            if (wgo != null) wallTilemap = wgo.GetComponent<Tilemap>();
        }
        if (floorTilemap == null || wallTilemap == null)
        {
            Debug.LogWarning("[House] Нет тайлмапов — менеджер интерьера отключён до фазы UI.");
            return;
        }
        SaveManager.Instance?.Register(this);
        Debug.Log("[House] Менеджер интерьера: тайлмапы найдены, сейв подключён.");
    }

    void Start()
    {
        if (floorTilemap == null || wallTilemap == null) return;
        SaveManager.Instance?.LoadInto(this);
    }

    void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
        if (Instance == this) Instance = null;
    }

    /// <summary>Сколько тайлов пола сейчас (для проверки капа 100).</summary>
    public int FloorTileCount()
    {
        if (floorTilemap == null) return 0;
        int n = 0;
        BoundsInt b = floorTilemap.cellBounds;
        foreach (Vector3Int c in b.allPositionsWithin)
            if (floorTilemap.HasTile(c)) n++;
        return n;
    }

    /// <summary>Сколько тайлов уже докуплено (шаг лесенки цен).</summary>
    public int ExpandedCount => expandedCount;

    /// <summary>Вызывается из TryExpand (фаза 4) после успешной пристройки.</summary>
    public void RegisterExpandedTile()
    {
        expandedCount++;
        SaveManager.Instance?.Save();
    }

    // ─── ISaveable ─────────────────────────────────────────────
    [System.Serializable]
    private class CellRec { public int x; public int y; public string style; }
    [System.Serializable]
    private class PlacedRec { public string prefabPath; public float x; public float y; }
    [System.Serializable]
    private class HouseSave
    {
        public int expandedCount = 0;
        public List<CellRec> floor = new List<CellRec>();   // стиль пола на клетку ("", Brick/Barn...)
        public List<CellRec> walls = new List<CellRec>();   // стиль стен на клетку (лента/порог)
        public List<PlacedRec> furniture = new List<PlacedRec>();
        public List<CellRec> grown = new List<CellRec>();   // прирощенные стройками клетки (кольцо)
        public List<JournalEntry> journal = new List<JournalEntry>(); // снапшоты стен до строек
    }

    [System.Serializable]
    private class JournalCell
    {
        public int x; public int y;
        public string tile; // имя тайла, "" = было пусто
    }

    [System.Serializable]
    private class JournalEntry
    {
        public int tx; public int ty;
        public List<JournalCell> cells = new List<JournalCell>();
    }

    public string SaveKey => "house";

    public string CaptureState()
    {
        HouseSave save = new HouseSave { expandedCount = expandedCount };
        if (floorTilemap != null)
        {
            BoundsInt b = floorTilemap.cellBounds;
            foreach (Vector3Int c in b.allPositionsWithin)
            {
                TileBase t = floorTilemap.GetTile(c);
                if (t == null) continue;
                save.floor.Add(new CellRec { x = c.x, y = c.y, style = HouseReskin.WhichFloorStyle(t) });
            }
        }
        if (wallTilemap != null)
        {
            BoundsInt b = wallTilemap.cellBounds;
            foreach (Vector3Int c in b.allPositionsWithin)
            {
                TileBase t = wallTilemap.GetTile(c);
                if (t == null) continue;
                string id = HouseReskin.WhichWallStyle(t);
                // Пишем только не-базу и только значимые слоты (лента/порог этого стиля).
                if (id != HouseReskin.BaseStyleId && IsStyleSlot(WallStyle.Load(id), t))
                    save.walls.Add(new CellRec { x = c.x, y = c.y, style = id });
            }
        }
        foreach (HousePlacedFurniture f in FindObjectsByType<HousePlacedFurniture>(FindObjectsSortMode.None))
        {
            if (f == null) continue;
            save.furniture.Add(new PlacedRec
            {
                prefabPath = f.prefabName,
                x = f.transform.position.x,
                y = f.transform.position.y
            });
        }
        // Детерминированный порядок — дешевле dirty-flag сейва.
        save.floor.Sort((a, b) => a.x != b.x ? a.x - b.x : a.y - b.y);
        save.walls.Sort((a, b) => a.x != b.x ? a.x - b.x : a.y - b.y);
        foreach (Vector3Int c in grownRuntime)
            save.grown.Add(new CellRec { x = c.x, y = c.y });
        save.grown.Sort((a, b) => a.x != b.x ? a.x - b.x : a.y - b.y);
        foreach (var je in journalRuntime)
        {
            var entry = new JournalEntry { tx = je.Key.x, ty = je.Key.y };
            foreach (var kvp in je.Value)
                entry.cells.Add(new JournalCell { x = kvp.Key.x, y = kvp.Key.y, tile = kvp.Value });
            entry.cells.Sort((a, b) => a.x != b.x ? a.x - b.x : a.y - b.y);
            save.journal.Add(entry);
        }
        save.journal.Sort((a, b) => a.tx != b.tx ? a.tx - b.tx : a.ty - b.ty);
        return JsonUtility.ToJson(save);
    }

    /// <summary>Тайл — значимый слот стиля (лента/порог), а не общее кольцо/фон?</summary>
    static bool IsStyleSlot(WallStyle st, TileBase t)
    {
        if (st == null || t == null) return false;
        return t == st.bandTopL || t == st.bandTopM || t == st.bandTopR
            || t == st.bandBotL || t == st.bandBotM || t == st.bandBotR
            || t == st.doorThreshold;
    }

    public void RestoreState(string json)
    {
        if (restored) return; // EnsureInScene + Start грузят дважды — мебель бы задублировалась
        if (string.IsNullOrEmpty(json)) return;
        HouseSave save = JsonUtility.FromJson<HouseSave>(json);
        if (save == null) return;
        restored = true;
        int floorsApplied = 0, wallsApplied = 0;
        if (save.grown != null)
        {
            foreach (CellRec g in save.grown)
            {
                if (g == null) continue;
                grownRuntime.Add(new Vector3Int(g.x, g.y, 0));
            }
        }
        if (save.journal != null)
        {
            foreach (JournalEntry je in save.journal)
            {
                if (je == null || je.cells == null) continue;
                var t = new Vector3Int(je.tx, je.ty, 0);
                var map = new Dictionary<Vector3Int, string>();
                foreach (JournalCell jc in je.cells)
                {
                    if (jc == null) continue;
                    map[new Vector3Int(jc.x, jc.y, 0)] = jc.tile ?? "";
                }
                if (map.Count > 0)
                    journalRuntime[t] = map;
            }
        }
        expandedCount = save.expandedCount;
        // Стили применяем поверх сцены (рантайм-перекраска фазы 7; в редакторе
        // истина — сама сцена). Расхождение сцена/сейв лечится в пользу сейва.
        if (save.floor != null)
        {
            foreach (CellRec r in save.floor)
            {
                WallStyle st = WallStyle.Load(r.style);
                if (st == null || st.floorTile == null || floorTilemap == null) continue;
                floorTilemap.SetTile(new Vector3Int(r.x, r.y, 0), st.floorTile);
                floorsApplied++;
            }
        }
        if (save.walls != null)
        {
            foreach (CellRec r in save.walls)
            {
                WallStyle st = WallStyle.Load(r.style);
                if (st == null || wallTilemap == null) continue;
                TileBase cur = wallTilemap.GetTile(new Vector3Int(r.x, r.y, 0));
                WallStyle from = WallStyle.Load(HouseReskin.BaseStyleId);
                // Перекладываем слот базы в слот стиля (кольцо общее — совпадёт само).
                TileBase next = MapBaseSlot(cur, from, st);
                if (next != null && next != cur)
                {
                    wallTilemap.SetTile(new Vector3Int(r.x, r.y, 0), next);
                    wallsApplied++;
                }
            }
        }
        int furnitureTotal = save.furniture != null ? save.furniture.Count : 0;
        // Перестройка кольца вокруг пристроек (ДОМ-ПАНЕЛЬ-01): сейв хранит полы
        // и список прирощенных клеток; кольцо вокруг них живёт только в рантайме,
        // поэтому прогоняем ту же операцию стройки (Stabilize с fresh=grown).
        // База при честных стройках не страдает (аудит 0), скоп — окрестность пристроек.
        int ringPainted = RebuildRing(out int ringUnmapped, out int ringDiffs, out int ringLegacy);
        Debug.Log("[House] Дом из сейва: полов " + floorsApplied + ", стен " + wallsApplied +
                  ", кольцо: +" + ringPainted + " стен (legacy " + ringLegacy +
                  ", unmapped " + ringUnmapped +
                  ", расхождений " + ringDiffs + "), пристроек: " + grownRuntime.Count +
                  ", мебели " + furnitureTotal + " (покупок тайлов: " + save.expandedCount + ").");
        // Мебель спавнится из записей (фаза 5): каталог = реестр префабов.
        if (save.furniture != null && save.furniture.Count > 0)
        {
            if (floorTilemap == null || wallTilemap == null)
            {
                Debug.LogWarning("[House] В сейве мебель (" + save.furniture.Count +
                                 ") — нет тайлмапов, спавн пропущен.");
            }
            else
            {
                var pieces = new System.Collections.Generic.List<HouseFurniture.SavedPiece>();
                foreach (PlacedRec r in save.furniture)
                {
                    if (r == null || string.IsNullOrEmpty(r.prefabPath)) continue;
                    pieces.Add(new HouseFurniture.SavedPiece
                    {
                        prefabName = r.prefabPath,
                        x = r.x,
                        y = r.y
                    });
                }
                int spawned = HouseFurniture.SpawnSaved(pieces, wallTilemap, floorTilemap,
                    out var missing);
                Debug.Log("[House] Мебель из сейва: " + spawned + "/" + pieces.Count + ".");
                foreach (string m in missing)
                    Debug.LogWarning("[House] Нет префаба в каталоге: " + m + ".");
            }
        }
    }

    /// <summary>
    /// Перестроить кольцо вокруг пристроек: та же операция, что при стройке
    /// (Stabilize с fresh). fresh = трекнутые grown + legacy-детект как в
    /// п.20: пол комнаты с покрывающей стеной поверх (у базы над полами пусто,
    /// у старых пристроек осталась несъеденная лента). Найденный legacy тоже
    /// трекаем — со следующего рестарта он уже grown.
    /// </summary>
    int RebuildRing(out int unmappedCount, out int diffsCount, out int legacyCount)
    {
        unmappedCount = 0;
        diffsCount = 0;
        legacyCount = 0;
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
            return 0;
        WallStyle brick = WallStyle.Load(HouseReskin.BaseStyleId);
        if (brick == null) return 0;
        var floorLive = HouseAutotiler.LargestFloorRegion(floors);
        if (floorLive.Count == 0) return 0;
        string styleId = HouseExpansion.DominantFloorStyle(floors, floorLive);
        WallStyle fst = WallStyle.Load(styleId);
        TileBase floorTile = fst != null && fst.floorTile != null ? fst.floorTile : brick.floorTile;
        var fresh = new HashSet<Vector3Int>();
        foreach (Vector3Int c in grownRuntime)
            if (floors.HasTile(c)) fresh.Add(c); // только живой пол (страховка от мусора)
        // Legacy-детект (п.20): пол комнаты, над которым стена (несъеденная лента
        // старых пристроек). Замки/порог правилами не трогаются, как при стройке.
        foreach (Vector3Int c in floorLive)
        {
            if (fresh.Contains(c)) continue;
            if (!floors.HasTile(c)) continue;
            if (walls.GetTile(c) == null) continue;
            fresh.Add(c);
            grownRuntime.Add(c);
            legacyCount++;
        }
        if (fresh.Count == 0) return 0;
        var unmapped = new List<string>();
        int painted = HouseAutotiler.Stabilize(walls, floors, floorLive, brick, lockY,
            fresh, floorTile, unmapped);
        foreach (Vector3Int c in fresh) grownRuntime.Add(c); // долив тоже трекаем
        unmappedCount = unmapped.Count;
        // Проверка тем же критерием, что при стройке (аудит с fresh=null —
        // пункты 6/20 и TryExpand меряют так же; строгий fresh=all шумит и на
        // принятых сборках, он здесь не oracle).
        var checkWanted = HouseAutotiler.ComputeWanted(walls, floorLive, brick, lockY,
            null, new List<string>());
        var diffs = HouseAutotiler.Audit(walls, checkWanted);
        diffsCount = diffs.Count;
        foreach (string d in diffs)
            Debug.LogWarning("[House] Кольцо после restore: " + d);
        foreach (string u in unmapped)
            Debug.LogWarning("[House] Кольцо UNMAPPED: " + u);
        // Карта поменялась прямо при загрузке — сейв отложенно (мгновенный
        // поймал бы полусозданные системы, чей Start ещё не отработал).
        if (painted > 0) SaveManager.Instance?.ScheduleDelayedSave(3f);
        return painted;
    }

    static TileBase MapBaseSlot(TileBase cur, WallStyle from, WallStyle to)
    {
        if (cur == null || from == null || to == null) return null;
        if (cur == from.bandTopL) return to.bandTopL;
        if (cur == from.bandTopM) return to.bandTopM;
        if (cur == from.bandTopR) return to.bandTopR;
        if (cur == from.bandBotL) return to.bandBotL;
        if (cur == from.bandBotM) return to.bandBotM;
        if (cur == from.bandBotR) return to.bandBotR;
        if (cur == from.doorThreshold) return to.doorThreshold;
        return cur; // кольцо/фон — общие, без изменений
    }
}
