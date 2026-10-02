using UnityEngine;
using System.Collections;

/// <summary>
/// Кровать-возрождение. Вешается на инстанс кровати (пустой вид, напр. Beds_0).
/// Точек две, ставятся вручную дочерними объектами:
/// SleepPoint — куда ложится игрок (совместить с подушкой, голова из анимации сна);
/// WakePoint — куда встаёт после сна (валидируется, при стене уезжает на свободную сторону).
/// Связка: PlayerHealth при смерти ищет RespawnBed.Active (одна кровать на игру),
/// при нужде грузит её сцену, 5 сек сон (19. Sleep), смена скина на занятый (Beds_1),
/// подъём рядом, 10% HP. Камера — снэп на кровать/точку подъёма.
/// </summary>
public class RespawnBed : MonoBehaviour
{
    public static RespawnBed Active { get; private set; }
    public static string HomeSceneName { get; private set; }
    public static bool HasHome => !string.IsNullOrEmpty(HomeSceneName);

    [Header("Скины: пустая / занятая (Beds_0 / Beds_1)")]
    public Sprite occupiedSprite;

    [Header("Точки (дочерние объекты, ставятся вручную)")]
    public Transform sleepPoint; // подушка
    public Transform wakePoint;  // подъём

    [Header("Сон")]
    public float sleepDuration = 5f;
    [Range(0.1f, 2f)] public float sleepAnimSpeed = 0.6f;
    [Range(0.05f, 0.5f)] public float wakeCheckRadius = 0.35f;

    SpriteRenderer bedSr;
    Sprite emptySprite;
    Collider2D[] solidCols;

    void Awake()
    {
        bedSr = GetComponent<SpriteRenderer>();
        if (bedSr == null) bedSr = GetComponentInChildren<SpriteRenderer>();
        if (bedSr != null) emptySprite = bedSr.sprite;

        // Твёрдые коллайдеры кровати — гасятся на время сна, иначе физика
        // выдавит телепортированного на подушку игрока
        solidCols = GetComponentsInChildren<Collider2D>();
        if (solidCols != null)
            solidCols = System.Array.FindAll(solidCols, c => c != null && !c.isTrigger);

        if (Active != null && Active != this)
            Debug.LogWarning("[RespawnBed] В сцене уже есть кровать-возрождение — активна последняя (" + name + ").");
        Active = this;
        HomeSceneName = gameObject.scene.name;
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
        // HomeSceneName НЕ чистим — по нему PlayerHealth грузит сцену дома с того света
    }

    /// <summary>Смена скина пустая/занятая.</summary>
    public void SetOccupied(bool occupied)
    {
        if (bedSr == null) return;
        bedSr.sprite = occupied && occupiedSprite != null ? occupiedSprite : emptySprite;
    }

    /// <summary>Вкл/выкл твёрдых коллайдеров кровати (на сон — выкл).</summary>
    public void SetSolid(bool on)
    {
        if (solidCols == null) return;
        foreach (var c in solidCols)
            if (c != null) c.enabled = on;
    }

    public Vector3 SleepPos => sleepPoint != null ? sleepPoint.position : transform.position;

    /// <summary>
    /// Итоговая точка подъёма: ручная WakePoint, если свободна; иначе свободная
    /// сторона вокруг кровати (ноги → бока → изголовье).
    /// </summary>
    public Vector3 ResolveWakePosition(GameObject playerToIgnore)
    {
        if (wakePoint != null && IsFree((Vector2)wakePoint.position, playerToIgnore))
            return wakePoint.position;

        // Габариты кровати для кандидатов по сторонам
        float halfW = 0.6f, halfH = 1.1f;
        var box = GetComponentInChildren<BoxCollider2D>();
        if (box != null) { halfW = box.size.x * 0.5f; halfH = box.size.y * 0.5f; }
        else if (bedSr != null && bedSr.sprite != null)
        {
            halfW = bedSr.sprite.bounds.size.x * 0.5f;
            halfH = bedSr.sprite.bounds.size.y * 0.5f;
        }
        Vector2 c = transform.position;
        float step = 0.6f;
        Vector2[] candidates = new Vector2[]
        {
            c + new Vector2(0, -(halfH + step)), // ноги (предпочтительно)
            c + new Vector2(-(halfW + step), 0), // левый бок
            c + new Vector2(halfW + step, 0),    // правый бок
            c + new Vector2(0, halfH + step),    // изголовье
        };
        if (wakePoint != null)
        {
            // Ручную точку тоже пробуем сдвинуть по кругу, вдруг стена только впритык
            var extra = new Vector2[]
            {
                (Vector2)wakePoint.position + Vector2.down * step,
                (Vector2)wakePoint.position + Vector2.left * step,
                (Vector2)wakePoint.position + Vector2.right * step,
                (Vector2)wakePoint.position + Vector2.up * step,
            };
            var all = new Vector2[candidates.Length + extra.Length + 1];
            all[0] = wakePoint.position;
            extra.CopyTo(all, 1);
            candidates.CopyTo(all, 1 + extra.Length);
            candidates = all;
        }
        foreach (var p in candidates)
            if (IsFree(p, playerToIgnore)) return p;

        return wakePoint != null ? wakePoint.position : transform.position + Vector3.down;
    }

    bool IsFree(Vector2 p, GameObject playerToIgnore)
    {
        var hits = Physics2D.OverlapCircleAll(p, wakeCheckRadius);
        foreach (var h in hits)
        {
            if (h == null || h.isTrigger) continue;
            Transform t = h.transform;
            if (t == transform || t.IsChildOf(transform)) continue; // сама кровать
            if (playerToIgnore != null && (t == playerToIgnore.transform || t.IsChildOf(playerToIgnore.transform))) continue;
            return false; // стена/мебель/коллайдер — занято
        }
        return true;
    }

    /// <summary>Порядок, поверх которого рисуем спящего (кровать + 2).</summary>
    public int SleepOrder()
    {
        if (bedSr != null) return bedSr.sortingOrder + 2;
        return YSort.GetOrder(transform.position, 2);
    }

    public static void SnapCamera(Vector3 target)
    {
        if (Camera.main == null) return;
        Vector3 cam = Camera.main.transform.position;
        cam.x = target.x;
        cam.y = target.y;
        Camera.main.transform.position = cam;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (sleepPoint != null)
        {
            Gizmos.DrawWireSphere(sleepPoint.position, 0.25f);
            Gizmos.DrawLine(transform.position, sleepPoint.position);
        }
        Gizmos.color = Color.green;
        if (wakePoint != null) Gizmos.DrawWireSphere(wakePoint.position, 0.25f);
    }
}
