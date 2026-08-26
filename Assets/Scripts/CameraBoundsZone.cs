using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Зона границ камеры — одна на "комнату": весь уличный уровень целиком,
/// и отдельно КАЖДЫЙ интерьер дома (даже если он физически стоит далеко
/// за пределами основной карты, как у тебя сделано через телепорт).
///
/// Камера каждый кадр находит зону под игроком (по позиции) и ограничивается
/// именно её границами. Переход в дом подхватывается автоматически — ничего
/// дополнительно связывать с DoorTeleport не нужно.
///
/// Настройка: повесь на пустой объект, отрегулируй BoxCollider2D (Is Trigger)
/// по размеру ИМЕННО ЭТОЙ комнаты — от края до края видимой области.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class CameraBoundsZone : MonoBehaviour
{
    private static readonly List<CameraBoundsZone> all = new List<CameraBoundsZone>();

    private BoxCollider2D box;

    void OnEnable()
    {
        box = GetComponent<BoxCollider2D>();
        box.isTrigger = true; // не должен физически мешать игроку
        if (!all.Contains(this)) all.Add(this);
    }

    void OnDisable()
    {
        all.Remove(this);
    }

    public Bounds WorldBounds => box.bounds;

    /// <summary>Найти зону, в которой сейчас находится точка (позиция игрока).</summary>
    public static CameraBoundsZone FindContaining(Vector2 worldPos)
    {
        foreach (CameraBoundsZone z in all)
            if (z != null && z.WorldBounds.Contains(worldPos))
                return z;
        return null;
    }

    void OnDrawGizmos()
    {
        BoxCollider2D b = GetComponent<BoxCollider2D>();
        if (b == null) return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 1f, 0f, 0.12f);
        Gizmos.DrawCube(b.offset, b.size);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(b.offset, b.size);
    }
}