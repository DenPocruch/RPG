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

    // Антипинг-понг: после ЛЮБОГО телепорта короткое время новые телепорты игнорируются.
    // Нужен, потому что точки спавна часто стоят вплотную к обратной лестнице —
    // без этого первый же удар/триггер после прибытия сразу выбрасывает игрока обратно.
    private const float RetriggerGraceSeconds = 0.75f;
    private static float lastTeleportTime = -999f;

    // ── IInteractable ──────────────────────────────────────────
    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        Debug.Log("[Телепорт] Удар по " + gameObject.name + " (" + (Vector2)transform.position + ")");
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
        // Только что телепортировались — игнорируем (удар по соседней лестнице
        // или триггер, в который попали точкой спавна)
        if (Time.unscaledTime - lastTeleportTime < RetriggerGraceSeconds)
        {
            Debug.Log("[Телепорт] " + gameObject.name + " — игнор (антипинг-понг)");
            return;
        }

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
        // Отсчёт антипинг-понга — от момента фактического переноса (после фейда)
        lastTeleportTime = Time.unscaledTime;

        // Переносим игрока
        Vector3 newPos = targetSpawn.position;
        newPos.z = player.transform.position.z; // сохраняем Z игрока
        Debug.Log("[Телепорт] " + gameObject.name + " (" + (Vector2)transform.position + ") → " +
                  targetSpawn.name + " (" + (Vector2)targetSpawn.position + ")");
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