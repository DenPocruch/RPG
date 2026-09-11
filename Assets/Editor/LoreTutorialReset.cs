using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Tools → Lore → Reset Tutorial: сброс только обучения (ключ lore_tutorial),
/// ферма/золото/инвентарь не трогаются. Жми при остановленном Play.
/// </summary>
public static class LoreTutorialReset
{
    [System.Serializable] private class Entry { public string key; public string json; }
    [System.Serializable] private class SaveFile { public int version; public List<Entry> entries = new List<Entry>(); }

    [MenuItem("Tools/Lore/Reset Tutorial (только обучение)")]
    public static void ResetTutorial()
    {
        string dir = Application.persistentDataPath;
        string[] files = {
            Path.Combine(dir, "save.json"),
            Path.Combine(dir, "save_backup.json")
        };
        int fixedCount = 0;
        foreach (string path in files)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var sf = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path));
                if (sf == null || sf.entries == null) continue;
                int removed = sf.entries.RemoveAll(e => e != null && e.key == "lore_tutorial");
                if (removed > 0)
                {
                    File.WriteAllText(path, JsonUtility.ToJson(sf));
                    fixedCount++;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Lore] Не смог почистить " + path + ": " + e.Message);
            }
        }
        Debug.Log("[Lore] Обучение сброшено (" + fixedCount + " файл.). Жми Play из SampleScene — откроется книга.");
    }
}
