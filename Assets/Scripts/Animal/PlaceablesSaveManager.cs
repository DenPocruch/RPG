using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Сохранение размещаемых объектов фермы (кормушки FeederStorage, поилки WaterTrough).
/// Менеджер создаётся автоматически при загрузке каждой сцены
/// (вызывается из SaveManager.ProcessScene). Ключ — "ИмяСцены/placeables".
/// При загрузке сцены все поставленные объекты спавнятся заново из сейва.
/// </summary>
public class PlaceablesSaveManager : MonoBehaviour, ISaveable
{
    // ═══════════════════════════════════════════════════════════
    // АВТОСОЗДАНИЕ В КАЖДОЙ СЦЕНЕ
    // ═══════════════════════════════════════════════════════════
    public static void EnsureInScene(Scene scene)
    {
        if (!scene.IsValid()) return;

        foreach (PlaceablesSaveManager m in FindObjectsByType<PlaceablesSaveManager>(FindObjectsSortMode.None))
            if (m != null && m.gameObject.scene == scene) return;

        GameObject go = new GameObject("PlaceablesSaveManager");
        SceneManager.MoveGameObjectToScene(go, scene);
        var manager = go.AddComponent<PlaceablesSaveManager>();

        SaveManager.Instance?.LoadInto(manager);
    }

    void Awake() { SaveManager.Instance?.Register(this); }
    void OnDestroy() { SaveManager.Instance?.Unregister(this); }

    // ═══════════════════════════════════════════════════════════
    // ISaveable
    // ═══════════════════════════════════════════════════════════
    [System.Serializable]
    private class PlaceableSave
    {
        public string itemName;      // имя ItemData ("Feeder" / "WaterTrough")
        public float x, y, z;
        public int water;            // поилка
        public string[] feedItems;   // кормушка
        public int[] feedCounts;
    }

    [System.Serializable]
    private class PlaceablesSave
    {
        public List<PlaceableSave> items = new List<PlaceableSave>();
    }

    public string SaveKey => "placeables";

    public string CaptureState()
    {
        PlaceablesSave save = new PlaceablesSave();

        foreach (FeederStorage f in FindObjectsByType<FeederStorage>(FindObjectsSortMode.None))
        {
            if (f == null) continue;
            save.items.Add(new PlaceableSave
            {
                itemName = "Feeder",
                x = f.transform.position.x,
                y = f.transform.position.y,
                z = f.transform.position.z,
                feedItems = f.SaveItems(),
                feedCounts = f.SaveCounts()
            });
        }

        foreach (WaterTrough t in FindObjectsByType<WaterTrough>(FindObjectsSortMode.None))
        {
            if (t == null) continue;
            save.items.Add(new PlaceableSave
            {
                itemName = "WaterTrough",
                x = t.transform.position.x,
                y = t.transform.position.y,
                z = t.transform.position.z,
                water = t.water
            });
        }

        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        PlaceablesSave save = JsonUtility.FromJson<PlaceablesSave>(json);
        if (save == null || save.items == null) return;

        foreach (PlaceableSave p in save.items)
        {
            ItemData data = ItemDatabase.Find(p.itemName);
            if (data == null || data.placeablePrefab == null)
            {
                Debug.LogWarning("[Placeables] Не найден предмет/префаб: " + p.itemName);
                continue;
            }

            Vector3 pos = new Vector3(p.x, p.y, p.z);
            GameObject obj = Instantiate(data.placeablePrefab, pos, Quaternion.identity);
            obj.name = data.placeablePrefab.name;

            var feeder = obj.GetComponent<FeederStorage>();
            if (feeder != null) feeder.ApplySave(p.feedItems, p.feedCounts);

            var trough = obj.GetComponent<WaterTrough>();
            if (trough != null) trough.water = p.water;
        }
    }
}
