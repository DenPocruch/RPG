using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// Перенос мебели дома (конструктор дома, фаза 5, без панели):
/// лонг-пресс 0.5с по мебели → вибрация + полупрозрачность → драг →
/// отпуск: валидно = встало точно под палец + сейв, невалид = возврат
/// на старое место (не в склад, не на пол). Подсветка клетки-источника пока несёшь.
/// Таскается и кодовая (с маркером), и ручная (дети Interior — при переносе
/// получают маркер и входят в сейв). Бросок на панель — с фазой 7.
/// Мышь (тест в редакторе) + один палец (Android); два пальца = отмена.
/// Пока в хотбаре placeable (кормушка/пугало/станок) — не мешаем ghost-постановке.
/// </summary>
public class HouseDragController : MonoBehaviour
{
    public static HouseDragController Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("HouseDragController");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<HouseDragController>();
            }
            return _instance;
        }
    }
    private static HouseDragController _instance;

    const float HOLD_TIME = 0.5f; // лонг-пресс (как в концепте)
    const float DRAG_THRESHOLD_PX = 10f; // порог тап/драг (как в ItemDragHandler)

    enum DragState { Idle, Pressing, Carrying }
    DragState state = DragState.Idle;

    GameObject candidate; // под пальцем в Pressing
    Vector2 downScreenPos;
    float downTime;
    bool downOnUI;

    GameObject carried; // в Carrying
    GameObject carriedPrefab; // для валидации (префаб из каталога или сам инстанс)
    FurnitureMount carriedMount;
    Vector3 originPos;
    Color originColor;
    GameObject sourceHighlight;

    static Sprite whiteSquare;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        // Прогрев синглтона: дальше Update сам следит за вводом.
        var _ = Instance;
    }

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        // Ввод: один палец (тач) или мышь. Мультитач во время носки = отмена.
        bool hasPointer;
        Vector2 screenPos;
        bool pressed, held, released;
        int pointerId = -1;
        if (Input.touchCount > 0)
        {
            Touch t = Input.GetTouch(0);
            hasPointer = true;
            screenPos = t.position;
            pointerId = t.fingerId;
            pressed = t.phase == TouchPhase.Began;
            held = t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary;
            released = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
            if (Input.touchCount > 1 && state == DragState.Carrying)
            {
                CancelCarry();
                return;
            }
        }
        else
        {
            hasPointer = true;
            screenPos = Input.mousePosition;
            pressed = Input.GetMouseButtonDown(0);
            held = Input.GetMouseButton(0);
            released = Input.GetMouseButtonUp(0);
        }
        if (!hasPointer) return;

        if (pressed && state == DragState.Idle)
            BeginPress(screenPos, pointerId);
        else if (state == DragState.Pressing)
        {
            if (!held || released) { state = DragState.Idle; candidate = null; return; } // был тап
            if ((screenPos - downScreenPos).magnitude > DRAG_THRESHOLD_PX)
            {
                state = DragState.Idle; // сдвиг — не лонг-пресс
                candidate = null;
                return;
            }
            if (!downOnUI && Time.unscaledTime - downTime >= HOLD_TIME)
                Pickup();
        }
        else if (state == DragState.Carrying)
        {
            if (!held || released) { Drop(screenPos); return; }
            MoveCarried(screenPos);
        }
    }

    void BeginPress(Vector2 screenPos, int pointerId)
    {
        if (HouseEditMode.IsOpen) return; // панель открыта — таскаем из неё, не со сцены
        downOnUI = EventSystem.current != null &&
            (pointerId < 0 ? EventSystem.current.IsPointerOverGameObject()
                           : EventSystem.current.IsPointerOverGameObject(pointerId));
        if (downOnUI) return;
        GameObject hit = FindFurniture(ScreenToWorld(screenPos));
        if (hit == null) return;
        candidate = hit;
        downScreenPos = screenPos;
        downTime = Time.unscaledTime;
        state = DragState.Pressing;
    }

    /// <summary>Мебель под точкой: с маркером или ручная (дети Interior). Игрок/животные — мимо.</summary>
    static GameObject FindFurniture(Vector3 worldPos)
    {
        Collider2D[] hits = Physics2D.OverlapPointAll(worldPos);
        foreach (Collider2D h in hits)
        {
            if (h == null || h.isTrigger) continue;
            GameObject go = h.gameObject;
            if (go.CompareTag("Player")) continue;
            if (go.GetComponentInParent<AnimalController>() != null) continue;
            if (go.GetComponent<SpriteRenderer>() == null) continue;
            if (go.GetComponent<HousePlacedFurniture>() != null) return go;
            Transform p = go.transform.parent;
            if (p != null && p.name == "Interior") return go;
        }
        // Без коллайдера (свечи): ищем по спрайтам вблизи точки (кодовые + ручные).
        SpriteRenderer[] all = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        GameObject best = null;
        float bestD = 0.5f;
        foreach (SpriteRenderer sr in all)
        {
            if (sr == null) continue;
            GameObject go = sr.gameObject;
            bool marked = go.GetComponent<HousePlacedFurniture>() != null;
            bool hand = !marked && go.transform.parent != null && go.transform.parent.name == "Interior";
            if (!marked && !hand) continue;
            float d = Vector2.Distance(sr.bounds.center, (Vector2)worldPos);
            if (d < bestD) { bestD = d; best = go; }
        }
        return best;
    }

    void Pickup()
    {
        state = DragState.Idle;
        if (candidate == null) return;
        // Ghost-режим хотбара (кормушки/пугало/станки) — не мешаем.
        try
        {
            ItemData active = HotbarManager.Instance != null ? HotbarManager.Instance.GetActiveItem() : null;
            if (PlayerMovement.IsPlaceable(active)) { candidate = null; return; }
        }
        catch { }
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
        {
            candidate = null;
            return;
        }
        carried = candidate;
        candidate = null;
        originPos = carried.transform.position;
        HousePlacedFurniture marker = carried.GetComponent<HousePlacedFurniture>();
        string prefabName = marker != null && !string.IsNullOrEmpty(marker.prefabName)
            ? marker.prefabName : carried.name;
        carriedPrefab = HouseFurniture.ResolvePrefab(prefabName);
        if (carriedPrefab == null) carriedPrefab = carried; // ручной инстанс: меряем по себе
        carriedMount = HouseFurniture.MountOfPrefab(prefabName);
        SpriteRenderer sr = carried.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            originColor = sr.color;
            sr.color = new Color(originColor.r, originColor.g, originColor.b, 0.55f);
        }
        // Призрак-источник: тот же предмет, зелёный, на старом месте.
        sourceHighlight = new GameObject("HouseDragSource");
        SpriteRenderer hr = sourceHighlight.AddComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            hr.sprite = sr.sprite;
            hr.flipX = sr.flipX;
            hr.flipY = sr.flipY;
            hr.sortingOrder = sr.sortingOrder;
            sourceHighlight.transform.position = originPos;
            sourceHighlight.transform.rotation = carried.transform.rotation;
            sourceHighlight.transform.localScale = carried.transform.localScale;
        }
        else
        {
            Vector3Int cell = floors.WorldToCell(originPos);
            hr.sprite = WhiteSquare();
            hr.sortingOrder = 59;
            sourceHighlight.transform.position = floors.GetCellCenterWorld(cell);
            sourceHighlight.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
        }
        hr.color = new Color(0.55f, 1f, 0.55f, 0.5f);
        try { Handheld.Vibrate(); } catch { }
        Debug.Log("[House] Несу: " + carried.name + " (отпусти — встанет, мимо — вернётся).");
        state = DragState.Carrying;
    }

    void MoveCarried(Vector2 screenPos)
    {
        if (carried == null) { state = DragState.Idle; return; }
        Vector3 wp = ScreenToWorld(screenPos);
        carried.transform.position = new Vector3(wp.x, wp.y, 0f);
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
            return;
        bool ok = HouseFurniture.CanPlaceAt(walls, floors, region, carriedPrefab,
            carried.transform.position, carriedMount, out _, carried);
        SpriteRenderer sr = carried.GetComponent<SpriteRenderer>();
        if (sr != null)
            sr.color = ok ? new Color(0.55f, 1f, 0.55f, 0.6f)
                          : new Color(1f, 0.35f, 0.35f, 0.6f);
    }

    void Drop(Vector2 screenPos)
    {
        if (carried == null) { state = DragState.Idle; return; }
        GameObject go = carried;
        carried = null;
        state = DragState.Idle;
        CleanupCarryVisual(go);
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
        {
            go.transform.position = originPos;
            return;
        }
        // Без снапа: встаёт точно под палец/курсор (снап к клетке тянул мимо).
        Vector3 wp = ScreenToWorld(screenPos);
        wp.z = 0f;
        Vector3 snapped = wp;
        if (HouseFurniture.CanPlaceAt(walls, floors, region, carriedPrefab, snapped, carriedMount, out string reason, go))
        {
            go.transform.position = snapped;
            // Ручной декор входит в сейв-систему.
            HousePlacedFurniture marker = go.GetComponent<HousePlacedFurniture>();
            if (marker == null)
            {
                marker = go.AddComponent<HousePlacedFurniture>();
                marker.prefabName = go.name;
            }
            SaveManager.Instance?.Save();
            Debug.Log("[House] Поставлено: " + go.name + " в " + snapped + ".");
        }
        else
        {
            go.transform.position = originPos; // невалид = возврат, не в склад
            Debug.Log("[House] Сюда нельзя (" + reason + ") — вернул на место.");
        }
    }

    void CancelCarry()
    {
        if (carried != null)
        {
            carried.transform.position = originPos;
            CleanupCarryVisual(carried);
            Debug.Log("[House] Перенос отменён.");
        }
        carried = null;
        candidate = null;
        state = DragState.Idle;
    }

    void CleanupCarryVisual(GameObject go)
    {
        if (go != null)
        {
            SpriteRenderer sr = go.GetComponent<SpriteRenderer>();
            if (sr != null) sr.color = originColor;
        }
        if (sourceHighlight != null) Destroy(sourceHighlight);
        sourceHighlight = null;
    }

    static Vector3 ScreenToWorld(Vector2 screenPos)
    {
        Camera cam = Camera.main;
        if (cam == null) return Vector3.zero;
        Vector3 wp = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -cam.transform.position.z));
        wp.z = 0f;
        return wp;
    }

    static Sprite WhiteSquare()
    {
        if (whiteSquare != null) return whiteSquare;
        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        whiteSquare = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return whiteSquare;
    }
}
