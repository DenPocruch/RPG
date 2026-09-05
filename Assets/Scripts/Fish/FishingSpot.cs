using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Зона ловли (триггер на воде). Таблицу рыбы задают в инспекторе.
/// Ролл: 5% легендарная (difficulty 2), 25% редкая (1), иначе обычная (0).
/// </summary>
public class FishingSpot : MonoBehaviour
{
    public string spotName = "Точка ловли";
    public FishData[] fishTable;

    /// <summary>Игрок внутри зоны?</summary>
    public bool Contains(Vector3 pos)
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col == null) return false;
        return col.OverlapPoint(pos);
    }

    public static FishingSpot SpotAt(Vector3 pos)
    {
        foreach (FishingSpot s in FindObjectsByType<FishingSpot>(FindObjectsSortMode.None))
            if (s != null && s.Contains(pos)) return s;
        return null;
    }

    public FishData RollFish()
    {
        return RollFish(0f, float.MaxValue);
    }

    /// <summary>Ролл с фильтром крючка: клюют только виды, чей весовой диапазон
    /// пересекается с [minKg, maxKg]. null — на этот крючок здесь не клюёт.</summary>
    public FishData RollFish(float minKg, float maxKg)
    {
        if (fishTable == null || fishTable.Length == 0) return null;

        float roll = Random.Range(0f, 100f);
        int want = roll < 5f ? 2 : (roll < 30f ? 1 : 0);

        var fit = new List<FishData>();
        foreach (FishData f in fishTable)
            if (f != null && f.difficulty == want && Overlaps(f, minKg, maxKg)) fit.Add(f);
        if (fit.Count == 0)
            foreach (FishData f in fishTable)
                if (f != null && Overlaps(f, minKg, maxKg)) fit.Add(f);
        if (fit.Count == 0) return null;
        return fit[Random.Range(0, fit.Count)];
    }

    /// <summary>Хоть один вид в точке пересекается с диапазоном крючка?</summary>
    public bool HasOverlap(float minKg, float maxKg)
    {
        if (fishTable == null) return false;
        foreach (FishData f in fishTable)
            if (f != null && Overlaps(f, minKg, maxKg)) return true;
        return false;
    }

    static bool Overlaps(FishData f, float minKg, float maxKg)
    {
        return f.maxWeightKg >= minKg && f.minWeightKg <= maxKg;
    }

    // Подсветка зоны в Scene View (в игре триггеры невидимы)
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.4f);
        var poly = GetComponent<PolygonCollider2D>();
        if (poly != null && poly.points.Length > 1)
        {
            Vector2[] pts = poly.points;
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 a = transform.TransformPoint(pts[i]);
                Vector3 b = transform.TransformPoint(pts[(i + 1) % pts.Length]);
                Gizmos.DrawLine(a, b);
            }
            return;
        }
        var box = GetComponent<BoxCollider2D>();
        if (box != null)
        {
            Gizmos.DrawWireCube((Vector2)transform.position + box.offset, box.size);
        }
    }
}
