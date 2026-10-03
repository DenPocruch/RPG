using UnityEngine;
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

        if (floorTilemap == null || wallTilemap == null)
        {
            Debug.LogWarning("[House] Нет тайлмапов — менеджер интерьера отключён до фазы UI.");
            return;
        }
        SaveManager.Instance?.Register(this);
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
        if (string.IsNullOrEmpty(json)) return;
        HouseSave save = JsonUtility.FromJson<HouseSave>(json);
        if (save == null) return;
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
                    wallTilemap.SetTile(new Vector3Int(r.x, r.y, 0), next);
            }
        }
        // Мебель спавнится из записей — фаза 5.
        if (save.furniture != null && save.furniture.Count > 0)
            Debug.LogWarning("[House] В сейве мебель (" + save.furniture.Count + ") — спавн будет в фазе 5.");
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
