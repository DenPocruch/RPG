using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEditor;
using System.Collections.Generic;

// Журнал построек конструктора дома (только редактор/тесты).
// Каждая стройка (14–16, 19, 20) запоминает стены до неё; снос (21) находит
// journal-таргеты, под которыми пола уже нет, и возвращает ИХ стены
// (пропуская клетки где пол есть). Потом обычный пересчёт дотягивает концы.
// Хранится в EditorPrefs — переживает перезапуски, в билд не едет.
public static class HouseJournal
{
    const string PrefsKey = "HouseBuildJournal_SampleScene";

    [System.Serializable]
    class Cell
    {
        public int x;
        public int y;
        public string guid; // "" = пусто (стереть), guid тайла иначе
    }

    [System.Serializable]
    class Entry
    {
        public int tx;
        public int ty;
        public List<Cell> walls = new List<Cell>();
    }

    [System.Serializable]
    class Data
    {
        public List<Entry> entries = new List<Entry>();
    }

    static Data Load()
    {
        string json = EditorPrefs.GetString(PrefsKey, "");
        if (string.IsNullOrEmpty(json)) return new Data();
        try { return JsonUtility.FromJson<Data>(json) ?? new Data(); }
        catch { return new Data(); }
    }

    static void Save(Data d)
    {
        EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(d));
    }

    /// <summary>Таргеты построек, под которыми пол ещё есть (остатки).</summary>
    public static HashSet<Vector3Int> RemainingTargets(Tilemap floors)
    {
        var set = new HashSet<Vector3Int>();
        Data d = Load();
        foreach (Entry e in d.entries)
        {
            var t = new Vector3Int(e.tx, e.ty, 0);
            if (floors.HasTile(t)) set.Add(t);
        }
        return set;
    }

    public static void Clear()
    {
        EditorPrefs.DeleteKey(PrefsKey);
        Debug.Log("[House] Журнал построек очищен.");
    }

    static string GuidOf(TileBase t)
    {
        if (t == null) return "";
        string path = AssetDatabase.GetAssetPath(t);
        if (string.IsNullOrEmpty(path)) return "";
        return AssetDatabase.AssetPathToGUID(path);
    }

    static TileBase TileOf(string guid)
    {
        if (string.IsNullOrEmpty(guid)) return null;
        string path = AssetDatabase.GUIDToAssetPath(guid);
        if (string.IsNullOrEmpty(path)) return null;
        return AssetDatabase.LoadAssetAtPath<TileBase>(path);
    }

    /// <summary>Запомнить стены скопа до стройки (таргет — новая клетка пола).</summary>
    public static void Record(Tilemap walls, Vector3Int target, int x0, int x1, int y0, int y1)
    {
        Data d = Load();
        for (int i = d.entries.Count - 1; i >= 0; i--)
        {
            if (d.entries[i].tx == target.x && d.entries[i].ty == target.y)
                d.entries.RemoveAt(i);
        }
        var e = new Entry { tx = target.x, ty = target.y };
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                var c = new Vector3Int(x, y, 0);
                e.walls.Add(new Cell { x = x, y = y, guid = GuidOf(walls.GetTile(c)) });
            }
        }
        d.entries.Add(e);
        Save(d);
    }

    /// <summary>
    /// Снос: для записей, чей таргет остался без пола, вернуть стены
    /// (только где пола нет сейчас). Возвращает число снятых записей.
    /// </summary>
    public static int UndoRemoved(Tilemap walls, Tilemap floors)
    {
        Data d = Load();
        int undone = 0;
        int restored = 0;
        for (int i = d.entries.Count - 1; i >= 0; i--)
        {
            Entry e = d.entries[i];
            var t = new Vector3Int(e.tx, e.ty, 0);
            if (floors.HasTile(t)) continue; // пол на месте — стройка жива
            foreach (Cell cell in e.walls)
            {
                var c = new Vector3Int(cell.x, cell.y, 0);
                if (floors.HasTile(c)) continue; // под полом стены не трогаем
                TileBase want = TileOf(cell.guid);
                if (walls.GetTile(c) != want)
                {
                    walls.SetTile(c, want);
                    restored++;
                }
            }
            d.entries.RemoveAt(i);
            undone++;
        }
        if (undone > 0) Save(d);
        Debug.Log("[House] Снос: снято построек " + undone + ", возвращено стен " + restored + ".");
        return undone;
    }
}
