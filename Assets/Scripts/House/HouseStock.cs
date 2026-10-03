using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Склад дома (конструктор дома): отдельный от рюкзака, одинаковые предметы
/// стакаются БЕЗ лимита (счётчик int). Сюда падают снятые полы/стены/мебель
/// и сюда же падают покупки из режима планировки.
/// Ленивый DontDestroyOnLoad-синглтон (как FishingController), ISaveable
/// с глобальным ключом "house_stock" (вне сцены — см. SaveManager.FullKey).
/// </summary>
public class HouseStock : MonoBehaviour, ISaveable
{
    public static HouseStock Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("HouseStock");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<HouseStock>();
                // Синхронно подтягиваем сейв: блобы уже в памяти (SaveManager.Awake раньше всех).
                SaveManager.Instance?.LoadInto(_instance);
            }
            return _instance;
        }
    }
    private static HouseStock _instance;

    // id предмета/стиля → количество. id = имя ассета/префаба (как item.name в InventoryUI).
    private readonly Dictionary<string, int> counts = new Dictionary<string, int>();

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        SaveManager.Instance?.Register(this);
    }

    void Start()
    {
        SaveManager.Instance?.LoadInto(this);
    }

    void OnDestroy() { SaveManager.Instance?.Unregister(this); }

    /// <summary>Сколько лежит на складе (0 = нет).</summary>
    public int Count(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        return counts.TryGetValue(id, out int c) ? c : 0;
    }

    /// <summary>Положить на склад (стакается без лимита).</summary>
    public void Add(string id, int count = 1)
    {
        if (string.IsNullOrEmpty(id) || count <= 0) return;
        counts.TryGetValue(id, out int c);
        counts[id] = c + count;
        SaveManager.Instance?.Save();
    }

    /// <summary>Забрать со склада. false — не хватило, ничего не списано.</summary>
    public bool Remove(string id, int count = 1)
    {
        if (string.IsNullOrEmpty(id) || count <= 0) return false;
        if (!counts.TryGetValue(id, out int c) || c < count) return false;
        c -= count;
        if (c <= 0) counts.Remove(id);
        else counts[id] = c;
        SaveManager.Instance?.Save();
        return true;
    }

    /// <summary>Полная очистка одной позиции (для дебага/миграций).</summary>
    public void ClearId(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (counts.Remove(id))
            SaveManager.Instance?.Save();
    }

    // ─── ISaveable ─────────────────────────────────────────────
    [System.Serializable]
    private class StockEntry { public string id; public int count; }
    [System.Serializable]
    private class StockSave { public List<StockEntry> items = new List<StockEntry>(); }

    public string SaveKey => "house_stock";

    public string CaptureState()
    {
        StockSave save = new StockSave();
        foreach (var kvp in counts)
        {
            if (kvp.Value <= 0) continue;
            save.items.Add(new StockEntry { id = kvp.Key, count = kvp.Value });
        }
        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        StockSave save = JsonUtility.FromJson<StockSave>(json);
        if (save == null) return;
        counts.Clear();
        if (save.items == null) return;
        foreach (StockEntry e in save.items)
        {
            if (e == null || string.IsNullOrEmpty(e.id) || e.count <= 0) continue;
            counts[e.id] = e.count;
        }
    }
}
