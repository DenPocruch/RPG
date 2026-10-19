using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Режим планировки дома (конструктор дома, фаза 7): вход ударом с чертежом
/// в хотбаре, выход крестиком панели. На входе игрок замирает
/// (паттерн CharacterConstructorUI), панель открывается; на выходе игрок
/// оживает + сейв. Действия применяются сразу с сейвом (стека Готово/Отмены нет).
/// Панель верстает владелец (HouseEditBinder); без биндера вход отклоняется.
/// Камера на время планировки не следует за игроком (карту можно тащить,
/// HouseDragController); физика игрока отключена, а после сноса пола под ногами
/// он переносится на ближайшую свободную клетку комнаты (не остаётся за стенами).
/// </summary>
public class HouseEditMode : MonoBehaviour
{
    public static HouseEditMode Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("HouseEditMode");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<HouseEditMode>();
            }
            return _instance;
        }
    }
    private static HouseEditMode _instance;

    public static bool IsOpen { get; private set; }

    PlayerMovement lockedMovement;
    Rigidbody2D lockedBody;
    bool lockedBodyWasSimulated;
    CameraFollow lockedCameraFollow;
    bool lockedCameraWasEnabled;
    bool playerInsideHouse;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance == this) _instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Панель сценовая: при смене сцены выходим молча (игрок не должен зависнуть).
        // Игрока не переносим — сцена уже другая, дом и координаты там свои.
        if (IsOpen)
        {
            playerInsideHouse = false;
            Exit(silent: true);
        }
    }

    public static void Toggle()
    {
        if (IsOpen) Instance.Exit();
        else Instance.Enter();
    }

    public void Enter()
    {
        if (IsOpen) return;
        if (HouseEditUI.Instance == null || !HouseEditUI.Instance.IsReady)
        {
            Debug.LogWarning("[House] Нет панели планировки (собери Canvas + HouseEditBinder).");
            return;
        }
        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            lockedMovement = player.GetComponent<PlayerMovement>();
            lockedBody = player.GetComponent<Rigidbody2D>();
            playerInsideHouse = IsInsideHouse(player.transform.position);
        }
        if (lockedMovement != null)
            lockedMovement.enabled = false;
        if (lockedBody != null)
        {
            // Без физики снесённый пол не выталкивает игрока через стены.
            lockedBodyWasSimulated = lockedBody.simulated;
            lockedBody.linearVelocity = Vector2.zero;
            lockedBody.simulated = false;
        }
        // Карта в планировке тащится вручную (HouseDragController):
        // CameraFollow на время выключаем, на выходе возвращаем к игроку.
        lockedCameraFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (lockedCameraFollow != null)
        {
            lockedCameraWasEnabled = lockedCameraFollow.enabled;
            lockedCameraFollow.enabled = false;
        }
        IsOpen = true;
        HouseEditUI.Instance.Open();
        Debug.Log("[House] Режим планировки: открыт.");
    }

    /// <summary>После сноса пола: если игрок оказался вне комнаты/в стене — на безопасную клетку.</summary>
    public static void EnsurePlayerSafe()
    {
        if (_instance != null) _instance.KeepPlayerSafe();
    }

    bool KeepPlayerSafe()
    {
        if (!playerInsideHouse) return false; // планировал снаружи — игрока не трогаем
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return false;
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
            return false;
        Vector3 pos = player.transform.position;
        Vector3Int current = floors.WorldToCell(pos);
        if (region.Contains(current) && floors.HasTile(current) && !IsBlocked(pos, player))
            return false;
        if (!TryFindSafeCell(floors, region, pos, player, out Vector3Int safe))
        {
            Debug.LogWarning("[House] Не нашёл свободную клетку — игрок остался на месте.");
            return false;
        }
        Vector3 target = floors.GetCellCenterWorld(safe);
        target.z = pos.z;
        player.transform.position = target;
        Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;
        Debug.Log("[House] Игрок перенесён в безопасную клетку " + safe + ".");
        return true;
    }

    static bool TryFindSafeCell(Tilemap floors, System.Collections.Generic.HashSet<Vector3Int> region,
        Vector3 pos, GameObject player, out Vector3Int safe)
    {
        safe = default;
        bool found = false;
        float best = float.MaxValue;
        // 1) свободная клетка (без стен и мебели, не выход)
        foreach (Vector3Int c in region)
        {
            if (!floors.HasTile(c)) continue;
            Vector3 p = floors.GetCellCenterWorld(c);
            float d = (p - pos).sqrMagnitude;
            if (d >= best) continue;
            if (IsBlocked(p, player)) continue;
            if (IsExitTrigger(p)) continue;
            best = d;
            safe = c;
            found = true;
        }
        if (found) return true;
        // 2) фолбэк: клетка без стен (мебель допустима), но не выход
        float bestWalls = float.MaxValue;
        foreach (Vector3Int c in region)
        {
            if (!floors.HasTile(c)) continue;
            Vector3 p = floors.GetCellCenterWorld(c);
            float d = (p - pos).sqrMagnitude;
            if (d >= bestWalls) continue;
            if (IsBlocked(p, player, wallsOnly: true)) continue;
            if (IsExitTrigger(p)) continue;
            bestWalls = d;
            safe = c;
            found = true;
        }
        if (found) return true;
        // 3) крайний случай: комната заставлена мебелью — любая клетка пола,
        // лишь бы игрок остался ВНУТРИ дома (не за стеной).
        float bestAny = float.MaxValue;
        foreach (Vector3Int c in region)
        {
            if (!floors.HasTile(c)) continue;
            Vector3 p = floors.GetCellCenterWorld(c);
            float d = (p - pos).sqrMagnitude;
            if (d >= bestAny) continue;
            if (IsExitTrigger(p)) continue;
            bestAny = d;
            safe = c;
            found = true;
        }
        return found;
    }

    static bool IsInsideHouse(Vector3 worldPos)
    {
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
            return false;
        Vector3Int cell = floors.WorldToCell(worldPos);
        return region.Contains(cell) && floors.HasTile(cell);
    }

    /// <summary>Перекрыты ли ноги игрока (стены/мебель; триггеры и животные не мешают).</summary>
    static bool IsBlocked(Vector3 worldPos, GameObject player, bool wallsOnly = false)
    {
        // Ноги (Feet у Player) — низкая капсула у пола, а не всё тело:
        // иначе верхняя стена над комнатой ложно считалась бы перекрытием.
        Vector2 center = new Vector2(worldPos.x, worldPos.y + 0.06f);
        Collider2D[] hits = Physics2D.OverlapBoxAll(center, new Vector2(0.5f, 0.18f), 0f);
        foreach (Collider2D h in hits)
        {
            if (h == null || h.isTrigger) continue;
            if (player != null && h.GetComponentInParent<PlayerMovement>() != null) continue;
            if (h.GetComponentInParent<AnimalController>() != null) continue;
            if (wallsOnly && h.GetComponent<TilemapCollider2D>() == null &&
                h.GetComponent<CompositeCollider2D>() == null) continue;
            return true;
        }
        return false;
    }

    /// <summary>Клетка с выходной дверью (triggerOnTouch) — не ставим туда игрока.</summary>
    static bool IsExitTrigger(Vector3 worldPos)
    {
        // Чуть шире точки: триггер двери может стоять со смещением от центра клетки.
        Collider2D[] hits = Physics2D.OverlapBoxAll(worldPos, new Vector2(0.6f, 0.6f), 0f);
        foreach (Collider2D h in hits)
        {
            if (h == null) continue;
            DoorTeleport door = h.GetComponentInParent<DoorTeleport>();
            if (door != null && door.triggerOnTouch) return true;
        }
        return false;
    }

    public void Exit(bool silent = false)
    {
        if (!IsOpen) return;
        if (playerInsideHouse) KeepPlayerSafe();
        IsOpen = false;
        if (HouseEditUI.Instance != null)
            HouseEditUI.Instance.Close();
        if (lockedMovement != null)
        {
            lockedMovement.enabled = true;
            lockedMovement = null;
        }
        if (lockedBody != null)
        {
            lockedBody.simulated = lockedBodyWasSimulated;
            lockedBody = null;
        }
        if (lockedCameraFollow != null)
        {
            bool wasEnabled = lockedCameraWasEnabled;
            lockedCameraFollow.enabled = wasEnabled;
            if (wasEnabled && lockedCameraFollow.target != null)
            {
                Vector3 camPos = lockedCameraFollow.target.position;
                camPos.z = lockedCameraFollow.transform.position.z;
                lockedCameraFollow.transform.position = camPos;
            }
            lockedCameraFollow = null;
        }
        playerInsideHouse = false;
        SaveManager.Instance?.Save();
        if (!silent)
            Debug.Log("[House] Режим планировки: закрыт.");
    }
}
