using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Сейв жил руды (OreVeinComponent) — работает в любой сцене с жилами.
/// Менеджер создаётся автоматически при загрузке каждой сцены
/// (SaveManager.ProcessScene → EnsureInScene). Ключ — "ИмяСцены/veins".
///
/// Отличие от деревьев/поилок: жилы НЕ спавнятся из сейва, они часть дизайна
/// сцены и всегда есть на загрузке. Менеджер только хранит блоб, а каждая
/// жила сама подтягивает своё состояние в Start через TryApplyTo.
///
/// В блоб пишутся ТОЛЬКО побитые/истощённые жилы (целые не храним — файл
/// меньше, dirty-флаг SaveManager реже дёргается). Порядок записей
/// сортируется по ключу, чтобы JSON был детерминированным.
///
/// Добитая одноразовая жила (respawns=false) уничтожается в рантайме — её
/// запись переносим из прошлого блоба, иначе после перезахода она воскреснет.
/// </summary>
public class OreVeinSaveManager : MonoBehaviour, ISaveable
{
    // ═══════════════════════════════════════════════════════════
    // АВТОСОЗДАНИЕ В КАЖДОЙ СЦЕНЕ
    // ═══════════════════════════════════════════════════════════
    public static void EnsureInScene(Scene scene)
    {
        if (!scene.IsValid()) return;

        foreach (OreVeinSaveManager m in FindObjectsByType<OreVeinSaveManager>(FindObjectsSortMode.None))
            if (m != null && m.gameObject.scene == scene) return;

        GameObject go = new GameObject("OreVeinSaveManager");
        SceneManager.MoveGameObjectToScene(go, scene);
        var manager = go.AddComponent<OreVeinSaveManager>();

        SaveManager.Instance?.LoadInto(manager);
    }

    void Awake() { SaveManager.Instance?.Register(this); }
    void OnDestroy() { SaveManager.Instance?.Unregister(this); }

    // ═══════════════════════════════════════════════════════════
    // ISaveable
    // ═══════════════════════════════════════════════════════════
    [System.Serializable]
    private class VeinSave
    {
        public string key;
        public int health;
        public bool depleted;
        public long respawnAt; // UtcNow.Ticks момента возрождения (0 = не истощена)
        public bool respawns;
    }

    [System.Serializable]
    private class VeinsSave
    {
        public List<VeinSave> veins = new List<VeinSave>();
    }

    // Последний загруженный/записанный блоб — нужен для TryApplyTo и для
    // переноса записей уничтоженных одноразовых жил при CaptureState
    private List<VeinSave> loaded = new List<VeinSave>();

    public string SaveKey => "veins";

    public string CaptureState()
    {
        VeinsSave save = new VeinsSave();
        var live = new HashSet<string>();

        foreach (OreVeinComponent v in FindObjectsByType<OreVeinComponent>(FindObjectsSortMode.None))
        {
            if (v == null || v.gameObject.scene != gameObject.scene) continue;
            string key = v.SaveId();
            live.Add(key);
            if (v.IsDepleted || v.CurrentHealth < v.maxHealth)
            {
                save.veins.Add(new VeinSave
                {
                    key = key,
                    health = v.CurrentHealth,
                    depleted = v.IsDepleted,
                    respawnAt = v.RespawnAtTicks,
                    respawns = v.respawns
                });
            }
        }

        // Уничтоженных одноразовых жил среди живых нет — переносим их записи,
        // иначе после перезахода в сцену они появятся снова целыми
        foreach (VeinSave old in loaded)
        {
            if (old == null || string.IsNullOrEmpty(old.key)) continue;
            if (live.Contains(old.key)) continue;
            if (!old.depleted || old.respawns) continue;
            bool dup = false;
            foreach (VeinSave s in save.veins)
                if (s.key == old.key) { dup = true; break; }
            if (!dup) save.veins.Add(old);
        }

        // Детерминированный порядок — иначе dirty-флаг SaveManager будет
        // считать файл изменившимся при каждом сейве
        save.veins.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
        loaded = save.veins;
        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        loaded.Clear();
        if (string.IsNullOrEmpty(json)) return;
        VeinsSave save = JsonUtility.FromJson<VeinsSave>(json);
        if (save == null || save.veins == null) return;
        loaded = save.veins;
    }

    // ═══════════════════════════════════════════════════════════
    // Жила зовёт это в своём Start — блоб уже загружен, т.к. EnsureInScene
    // отрабатывает в sceneLoaded (до Start'ов) либо в SaveManager.Start
    // (порядок -1000, раньше обычных Start'ов)
    // ═══════════════════════════════════════════════════════════
    public static void TryApplyTo(OreVeinComponent vein)
    {
        if (vein == null) return;

        OreVeinSaveManager mgr = null;
        foreach (OreVeinSaveManager m in FindObjectsByType<OreVeinSaveManager>(FindObjectsSortMode.None))
        {
            if (m == null) continue;
            if (m.gameObject.scene == vein.gameObject.scene) { mgr = m; break; }
            if (mgr == null) mgr = m;
        }
        if (mgr == null) return;

        string key = vein.SaveId();
        foreach (VeinSave s in mgr.loaded)
        {
            if (s != null && s.key == key)
            {
                vein.ApplySave(s.health, s.depleted, s.respawnAt);
                return;
            }
        }
    }
}
