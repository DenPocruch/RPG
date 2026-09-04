using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ����� ��������� ������ � �����. � ������ ���� spawnId.
/// ������ (SceneTransition) ��������� � ����� ����� ��������� ������
/// ����� �������� ����� �����.
/// </summary>
public class SceneSpawnPoint : MonoBehaviour
{
    [Tooltip("���������� ��� �����, ����. 'FromFarm', 'CityEntrance'")]
    public string spawnId = "Default";

    private static readonly List<SceneSpawnPoint> all = new List<SceneSpawnPoint>();

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() { all.Remove(this); }

    public static SceneSpawnPoint Find(string id)
    {
        foreach (SceneSpawnPoint s in all)
            if (s != null && s.spawnId == id) return s;
        // если не нашли по id — вернуть первую попавшуюся (чтобы не застрять у портала)
        SceneSpawnPoint fallback = all.Count > 0 ? all[0] : null;
        Debug.LogWarning("[Спавн] Точка '" + id + "' не найдена! Использую первую попавшуюся: " +
                         (fallback != null ? fallback.name + " (" + fallback.spawnId + ")" : "<нет точек в сцене>"));
        return fallback;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, 0.4f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.6f);
    }
}