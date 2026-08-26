using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Точка появления игрока в сцене. У каждой свой spawnId.
/// Портал (SceneTransition) указывает в какую точку поставить игрока
/// после загрузки новой сцены.
/// </summary>
public class SceneSpawnPoint : MonoBehaviour
{
    [Tooltip("Уникальное имя точки, напр. 'FromFarm', 'CityEntrance'")]
    public string spawnId = "Default";

    private static readonly List<SceneSpawnPoint> all = new List<SceneSpawnPoint>();

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() { all.Remove(this); }

    public static SceneSpawnPoint Find(string id)
    {
        foreach (SceneSpawnPoint s in all)
            if (s != null && s.spawnId == id) return s;
        // Если не нашли по id — вернём первую попавшуюся (чтобы не потерять игрока)
        return all.Count > 0 ? all[0] : null;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, 0.4f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.6f);
    }
}