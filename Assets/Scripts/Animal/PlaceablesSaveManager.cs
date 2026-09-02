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
        public int honey;            // улей (стадий мёда)
        public float[] beeX;         // улей: где пчёлы сейчас
        public float[] beeY;
        public float[] beeTrip;      // остаток рейса каждой пчелы, сек
        public string[] feedItems;   // кормушка
        public int[] feedCounts;
        public string machineItem;   // станок: что загружено (имя ассета)
        public int machineInput;     // сколько единиц входа лежит
        public int machineOutput;    // сколько готового ждёт забора
        public long machineFinish;   // UtcNow.Ticks момента готовности (0 = не работает)
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

        foreach (Scarecrow s in FindObjectsByType<Scarecrow>(FindObjectsSortMode.None))
        {
            if (s == null) continue;
            save.items.Add(new PlaceableSave
            {
                itemName = "Scarecrow", // пугало: состояния нет, важна только позиция
                x = s.transform.position.x,
                y = s.transform.position.y,
                z = s.transform.position.z
            });
        }

        foreach (Beehive h in FindObjectsByType<Beehive>(FindObjectsSortMode.None))
        {
            if (h == null) continue;
            save.items.Add(new PlaceableSave
            {
                itemName = "Beehive",
                x = h.transform.position.x,
                y = h.transform.position.y,
                z = h.transform.position.z,
                honey = h.Honey,
                beeX = h.SaveBeeX(),
                beeY = h.SaveBeeY(),
                beeTrip = h.SaveBeeTrip()
            });
        }

        foreach (CraftMachine m in FindObjectsByType<CraftMachine>(FindObjectsSortMode.None))
        {
            if (m == null) continue;
            save.items.Add(new PlaceableSave
            {
                itemName = m.SelfItemName,
                x = m.transform.position.x,
                y = m.transform.position.y,
                z = m.transform.position.z,
                machineItem = m.SaveInputItem(),
                machineInput = m.SaveInputCount(),
                machineOutput = m.SaveOutput(),
                machineFinish = m.SaveFinishTicks()
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

            var hive = obj.GetComponent<Beehive>();
            if (hive != null)
            {
                hive.ApplySave(p.honey);
                hive.ApplyBeeSave(p.beeX, p.beeY, p.beeTrip);
            }

            var machine = obj.GetComponent<CraftMachine>();
            if (machine != null)
                machine.ApplySave(p.machineItem, p.machineInput, p.machineOutput, p.machineFinish);
        }
    }
}
