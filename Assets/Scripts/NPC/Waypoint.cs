using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Узел сети навигации. Ставишь такие точки по проходимым местам и связываешь
/// соседние (neighbors). NPC ходит от точки к точке по прямой (в обход заборов,
/// т.к. ты сам прокладываешь сеть по дорожкам) и сам находит путь через сеть
/// к любой точке — алгоритм A*.
/// </summary>
public class Waypoint : MonoBehaviour
{
    [Header("Соседние точки (куда можно пройти напрямую)")]
    public List<Waypoint> neighbors = new List<Waypoint>();

    [Header("Авто-связь")]
    [Tooltip("Если true — при связывании добавит и обратную связь у соседа")]
    public bool bidirectional = true;

    public Vector2 Position => transform.position;

    // ═══════════════════════════════════════════════════════════
    // ПОИСК ПУТИ ПО СЕТИ (A*)
    // ═══════════════════════════════════════════════════════════
    public static List<Waypoint> FindPath(Waypoint start, Waypoint goal)
    {
        if (start == null || goal == null) return null;
        if (start == goal) return new List<Waypoint> { start };

        var open = new List<Waypoint> { start };
        var cameFrom = new Dictionary<Waypoint, Waypoint>();
        var gScore = new Dictionary<Waypoint, float> { { start, 0f } };
        var fScore = new Dictionary<Waypoint, float> { { start, Heuristic(start, goal) } };

        while (open.Count > 0)
        {
            // Узел с наименьшим fScore
            Waypoint current = open[0];
            foreach (Waypoint n in open)
                if (fScore.TryGetValue(n, out float fn) && fn < fScore[current])
                    current = n;

            if (current == goal)
                return Reconstruct(cameFrom, current);

            open.Remove(current);

            foreach (Waypoint nb in current.neighbors)
            {
                if (nb == null) continue;

                float tentative = gScore[current] + Vector2.Distance(current.Position, nb.Position);
                if (!gScore.ContainsKey(nb) || tentative < gScore[nb])
                {
                    cameFrom[nb] = current;
                    gScore[nb] = tentative;
                    fScore[nb] = tentative + Heuristic(nb, goal);
                    if (!open.Contains(nb)) open.Add(nb);
                }
            }
        }
        return null; // путь не найден (сеть разорвана)
    }

    static float Heuristic(Waypoint a, Waypoint b) => Vector2.Distance(a.Position, b.Position);

    static List<Waypoint> Reconstruct(Dictionary<Waypoint, Waypoint> cameFrom, Waypoint current)
    {
        var path = new List<Waypoint> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Insert(0, current);
        }
        return path;
    }

    /// <summary>Ближайшая точка сети к позиции (для старта NPC).</summary>
    public static Waypoint FindNearest(Vector2 pos, Waypoint[] all)
    {
        Waypoint best = null;
        float bestDist = float.MaxValue;
        foreach (Waypoint w in all)
        {
            if (w == null) continue;
            float d = Vector2.Distance(pos, w.Position);
            if (d < bestDist) { bestDist = d; best = w; }
        }
        return best;
    }

    // ═══════════════════════════════════════════════════════════
    // ВИЗУАЛИЗАЦИЯ В РЕДАКТОРЕ
    // ═══════════════════════════════════════════════════════════
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 1f);
        Gizmos.DrawSphere(transform.position, 0.15f);

        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
        if (neighbors != null)
            foreach (Waypoint nb in neighbors)
                if (nb != null)
                    Gizmos.DrawLine(transform.position, nb.transform.position);
    }

    // Авто-добавление обратной связи в редакторе
    void OnValidate()
    {
        if (!bidirectional || neighbors == null) return;
        foreach (Waypoint nb in neighbors)
        {
            if (nb == null) continue;
            if (nb.neighbors == null) nb.neighbors = new List<Waypoint>();
            if (!nb.neighbors.Contains(this))
                nb.neighbors.Add(this);
        }
    }
}