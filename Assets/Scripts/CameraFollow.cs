using UnityEngine;

/// <summary>
/// —ледует за игроком с плавностью + ограничивает камеру границами “≈ ”ў≈…
/// "комнаты" (CameraBoundsZone), в которой сейчас находитс€ игрок.
///
/// –аботает дл€ сцен с разным размером карты и дл€ домов-интерьеров,
/// физически сто€щих далеко за пределами основной карты (телепорт) Ч
/// камера сама подхватывает нужные границы по позиции игрока, без ручной
/// прив€зки к двер€м/порталам.
///
/// ≈сли под игроком нет ни одной CameraBoundsZone Ч используетс€ фолбэк
/// (Fallback Min/Max), чтобы ничего не ломалось пока не расставил зоны.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float smoothSpeed = 5f;

    [Header("‘олбэк-границы (если под игроком нет CameraBoundsZone)")]
    public bool useFallbackBounds = true;
    public Vector2 fallbackMin = new Vector2(-20f, -20f);
    public Vector2 fallbackMax = new Vector2(20f, 20f);

    private Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = new Vector3(
            target.position.x,
            target.position.y,
            transform.position.z
        );

        Vector3 smoothed = Vector3.Lerp(
            transform.position, desired,
            smoothSpeed * Time.deltaTime
        );

        // »щем зону границ под игроком (улица / конкретный дом) Ч переключаетс€
        // автоматически при телепорте, без лишних св€зей с DoorTeleport
        CameraBoundsZone zone = CameraBoundsZone.FindContaining(target.position);

        if (zone != null)
        {
            smoothed = ClampToBounds(smoothed, zone.WorldBounds.min, zone.WorldBounds.max);
        }
        else if (useFallbackBounds)
        {
            smoothed = ClampToBounds(smoothed, fallbackMin, fallbackMax);
        }

        transform.position = smoothed;
    }

    Vector3 ClampToBounds(Vector3 pos, Vector2 minB, Vector2 maxB)
    {
        if (cam == null) return pos;

        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        float minX = minB.x + halfWidth;
        float maxX = maxB.x - halfWidth;
        float minY = minB.y + halfHeight;
        float maxY = maxB.y - halfHeight;

        // ≈сли комната меньше экрана по какой-то оси Ч не дЄргаем, ставим в центр
        float x = (minX <= maxX) ? Mathf.Clamp(pos.x, minX, maxX) : (minB.x + maxB.x) * 0.5f;
        float y = (minY <= maxY) ? Mathf.Clamp(pos.y, minY, maxY) : (minB.y + maxB.y) * 0.5f;

        return new Vector3(x, y, pos.z);
    }

    void OnDrawGizmosSelected()
    {
        if (!useFallbackBounds) return;
        Gizmos.color = Color.gray;
        Vector3 center = new Vector3((fallbackMin.x + fallbackMax.x) * 0.5f, (fallbackMin.y + fallbackMax.y) * 0.5f, 0);
        Vector3 size = new Vector3(fallbackMax.x - fallbackMin.x, fallbackMax.y - fallbackMin.y, 0);
        Gizmos.DrawWireCube(center, size);
    }
}