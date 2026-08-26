using UnityEngine;

/// <summary>
/// Дверь-телепорт. Вешается и на входную дверь (снаружи → в дом),
/// и на выходную (в доме → наружу). Реализует IInteractable —
/// срабатывает кнопкой атаки рядом с дверью.
/// При срабатывании: затемнение → игрок переносится в targetSpawn → осветление.
/// </summary>
public class DoorTeleport : MonoBehaviour, IInteractable
{
    [Header("Куда телепортировать игрока")]
    public Transform targetSpawn; // пустой объект — точка появления по ту сторону

    [Header("Камера")]
    [Tooltip("Мгновенно переместить камеру к игроку (чтобы не было панорамы через всю карту)")]
    public bool snapCamera = true;

    [Header("Автовход по касанию (опционально)")]
    [Tooltip("Если true — телепорт срабатывает при входе в триггер, без нажатия атаки")]
    public bool triggerOnTouch = false;

    // ── IInteractable ──────────────────────────────────────────
    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        DoTeleport(player);
    }
    // ───────────────────────────────────────────────────────────

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!triggerOnTouch) return;
        if (!other.CompareTag("Player")) return;
        DoTeleport(other.gameObject);
    }

    void DoTeleport(GameObject player)
    {
        if (targetSpawn == null)
        {
            Debug.LogWarning("[Дверь] Не задан targetSpawn на " + gameObject.name);
            return;
        }

        if (ScreenFader.Instance == null)
        {
            // Нет фейдера — телепортируем без эффекта
            MovePlayer(player);
            return;
        }

        // Затемнение → перенос в тёмный момент → осветление
        ScreenFader.Instance.Transition(() => MovePlayer(player));
    }

    void MovePlayer(GameObject player)
    {
        // Переносим игрока
        Vector3 newPos = targetSpawn.position;
        newPos.z = player.transform.position.z; // сохраняем Z игрока
        player.transform.position = newPos;

        // Сбрасываем скорость (чтобы не «уехал» по инерции)
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        // Мгновенно ставим камеру на игрока
        if (snapCamera && Camera.main != null)
        {
            Vector3 camPos = Camera.main.transform.position;
            camPos.x = newPos.x;
            camPos.y = newPos.y;
            Camera.main.transform.position = camPos;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (targetSpawn != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, targetSpawn.position);
            Gizmos.DrawWireSphere(targetSpawn.position, 0.3f);
        }
    }
}