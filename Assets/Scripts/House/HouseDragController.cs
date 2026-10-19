using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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

    enum DragState { Idle, Pressing, Carrying, Panning }
    DragState state = DragState.Idle;

    GameObject candidate; // под пальцем в Pressing
    Vector2 downScreenPos;
    float downTime;
    bool downOnUI;
    Vector2 lastPanScreenPos; // в Panning

    GameObject carried; // в Carrying
    GameObject carriedPrefab; // для валидации (префаб из каталога или сам инстанс)
    FurnitureMount carriedMount;
    Vector3 originPos;
    Color originColor;
    GameObject sourceHighlight;
    GameObject carryIcon; // UI-копия поверх панели (обратный драг дом → панель)
    Vector2 carryIconOffsetPx; // тот же якорь: палец внизу стола
    Vector3Int floorCell; // кандидат на снос (лонг-пресс по пристроенному полу)
    bool hasFloorCell;

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
            if (!held || released) { state = DragState.Idle; candidate = null; hasFloorCell = false; return; } // был тап
            if ((screenPos - downScreenPos).magnitude > DRAG_THRESHOLD_PX)
            {
                // Драг начат со слота панели: не перехватываем его картой,
                // даже если пресс успел стать Pressing до первого кадра UI.
                if (HouseEditUI.PanelDragActive)
                {
                    state = DragState.Idle;
                    candidate = null;
                    hasFloorCell = false;
                    return;
                }
                // В планировке сдвиг пустого места/мебели = тащим карту
                // (лонг-пресс без сдвига по-прежнему берёт мебель/сносит пол).
                if (HouseEditMode.IsOpen && !downOnUI)
                {
                    candidate = null;
                    hasFloorCell = false;
                    StartPan(screenPos);
                    return;
                }
                state = DragState.Idle; // сдвиг — не лонг-пресс
                candidate = null;
                hasFloorCell = false;
                return;
            }
            if (!downOnUI && Time.unscaledTime - downTime >= HOLD_TIME)
            {
                if (candidate != null) Pickup();
                else if (hasFloorCell) DemolishFloor();
            }
        }
        else if (state == DragState.Carrying)
        {
            if (!held || released) { Drop(screenPos); return; }
            MoveCarried(screenPos);
        }
        else if (state == DragState.Panning)
        {
            if (!held || released) { state = DragState.Idle; return; }
            if (HouseEditUI.PanelDragActive) { state = DragState.Idle; return; }
            PanCamera(screenPos - lastPanScreenPos);
            lastPanScreenPos = screenPos;
        }
    }

    void StartPan(Vector2 screenPos)
    {
        state = DragState.Panning;
        lastPanScreenPos = downScreenPos;
        PanCamera(screenPos - lastPanScreenPos);
        lastPanScreenPos = screenPos;
    }

    /// <summary>Сдвиг мира за пальцем (CameraFollow в планировке выключен).</summary>
    static void PanCamera(Vector2 deltaPx)
    {
        if (deltaPx.sqrMagnitude <= 0f) return;
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic) return;
        float unitsPerPixel = cam.orthographicSize * 2f / Mathf.Max(1, Screen.height);
        cam.transform.position += new Vector3(-deltaPx.x * unitsPerPixel,
                                              -deltaPx.y * unitsPerPixel, 0f);
    }

    void BeginPress(Vector2 screenPos, int pointerId)
    {
        // Панель открыта — не помеха: старт с UI игнорируем ниже (downOnUI),
        // а бросок на панель = убрать на склад (см. Drop).
        downOnUI = PointerOverUI(screenPos, pointerId);
        // Страховка по прямоугольнику панели: даже если GraphicRaycaster/EventSystem
        // в этом кадре ещё не ответил, старт со слота не должен стать свайпом карты.
        if (!downOnUI && HouseEditMode.IsOpen)
            downOnUI = HouseEditUI.OverPanelNoCreate(screenPos);
        if (downOnUI) return;
        GameObject hit = FindFurniture(ScreenToWorld(screenPos));
        if (hit != null)
        {
            candidate = hit;
            downScreenPos = screenPos;
            downTime = Time.unscaledTime;
            state = DragState.Pressing;
            return;
        }
        // Мебели нет: в режиме планировки лонг-пресс по пристроенному полу =
        // снос с возвратом билета (рантайм-аналог п.21). Только планировка —
        // в обычной игре пол свят.
        hasFloorCell = false;
        if (HouseEditMode.IsOpen &&
            HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
        {
            Vector3 wp = ScreenToWorld(screenPos);
            Vector3Int cell = floors.WorldToCell(wp);
            if (region.Contains(cell) && floors.HasTile(cell))
            {
                floorCell = cell;
                hasFloorCell = true;
                downScreenPos = screenPos;
                downTime = Time.unscaledTime;
                state = DragState.Pressing;
            }
        }
        // Пустая земля/фон в планировке: тап мимо, а сдвиг пальца тащит карту
        // (см. Update → StartPan). Нужен и когда игрок планирует снаружи дома.
        if (HouseEditMode.IsOpen && state == DragState.Idle)
        {
            downScreenPos = screenPos;
            downTime = Time.unscaledTime;
            state = DragState.Pressing;
        }
    }

    /// <summary>
    /// Нажатие над UI. EventSystem в том же кадре может ещё не обработать
    /// pointer (порядок Update), поэтому дополнительно бьём RaycastAll прямо
    /// по точке — иначе драг со слота панели уходил в свайп карты.
    /// </summary>
    static bool PointerOverUI(Vector2 screenPos, int pointerId)
    {
        if (EventSystem.current == null) return false;
        bool over = pointerId < 0
            ? EventSystem.current.IsPointerOverGameObject()
            : EventSystem.current.IsPointerOverGameObject(pointerId);
        if (over) return true;
        var data = new PointerEventData(EventSystem.current) { position = screenPos };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, hits);
        foreach (RaycastResult hit in hits)
            if (hit.module is GraphicRaycaster) return true; // только UI, не физика
        return false;
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
        // Несомый остаётся видимым под пальцем (рабочая версия).
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
        // UI-копия для панели: реальный размер + якорь внизу (палец как в доме).
        // Над миром скрыта, над панелью — она вместо мирового (он под оверлеем).
        Vector2 iconOffset = Vector2.zero;
        Sprite iconSprite = sr != null ? sr.sprite : null;
        Vector2 iconPx = new Vector2(72, 72);
        if (sr != null && sr.sprite != null)
        {
            iconPx = WorldToScreenSize(sr.bounds.size);
            iconOffset = PxPerUnit() * ((Vector2)sr.bounds.center - (Vector2)carried.transform.position);
        }
        ShowCarryIcon(iconSprite, iconPx);
        carryIconOffsetPx = iconOffset; // после Show (внутри Hide сбрасывает в ноль)
        MoveCarryIcon(downScreenPos);
        try { Handheld.Vibrate(); } catch { }
        Debug.Log("[House] Несу: " + carried.name + " (отпусти — встанет, мимо — вернётся).");
        state = DragState.Carrying;
    }

    /// <summary>Лонг-пресс по пристроенному полу: снос с возвратом билета.</summary>
    void DemolishFloor()
    {
        state = DragState.Idle;
        Vector3Int cell = floorCell;
        hasFloorCell = false;
        candidate = null;
        if (HouseExpansion.TryDemolishTicket(cell))
        {
            try { Handheld.Vibrate(); } catch { }
            HouseEditMode.EnsurePlayerSafe(); // не оставлять игрока за стенами
        }
        Object.FindFirstObjectByType<HouseEditUI>()?.Refresh(); // счётчик билетов сразу
    }

    void MoveCarried(Vector2 screenPos)
    {
        if (carried == null) { state = DragState.Idle; return; }
        // Строго под пальцем, без сдвигов.
        Vector3 wp = ScreenToWorld(screenPos);
        carried.transform.position = new Vector3(wp.x, wp.y, 0f);
        // Над панелью = убрать на склад: всегда зелёный.
        // Над панелью видна только UI-копия (мир под оверлеем), над миром — настоящий.
        bool overPanel = HouseEditUI.OverPanelNoCreate(screenPos);
        bool ok = overPanel;
        if (!overPanel && HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
            ok = HouseFurniture.CanPlaceAt(walls, floors, region, carriedPrefab,
                carried.transform.position, carriedMount, out _, carried);
        SpriteRenderer sr = carried.GetComponent<SpriteRenderer>();
        if (sr != null)
            sr.color = ok ? new Color(0.55f, 1f, 0.55f, 0.6f)
                          : new Color(1f, 0.35f, 0.35f, 0.6f);
        TintCarryIcon(ok);
        MoveCarryIcon(screenPos);
        UpdateCarryVisibility(screenPos);
    }

    void Drop(Vector2 screenPos)
    {
        if (carried == null) { state = DragState.Idle; return; }
        GameObject go = carried;
        carried = null;
        state = DragState.Idle;
        SetCarriedVisible(go, true);
        HideCarryIcon();
        CleanupCarryVisual(go);
        // Бросок на панель = убрать на склад (режим планировки).
        if (HouseEditUI.OverPanelNoCreate(screenPos))
        {
            StoreToPanel(go);
            return;
        }
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
        {
            go.transform.position = originPos;
            return;
        }
        // Встаёт точно под палец, без сдвигов и снапов.
        Vector3 wp = ScreenToWorld(screenPos);
        wp.z = 0f;
        Vector3 snapped = wp;
        if (HouseFurniture.CanPlaceAt(walls, floors, region, carriedPrefab, snapped, carriedMount, out string reason, go))
        {
            go.transform.position = snapped;
            // Ручной декор входит в сейв-систему.
            bool sceneDefault = go.transform.parent != null && go.transform.parent.name == "Interior";
            HousePlacedFurniture marker = go.GetComponent<HousePlacedFurniture>();
            if (marker == null)
            {
                marker = go.AddComponent<HousePlacedFurniture>();
                marker.prefabName = go.name;
                if (sceneDefault) marker.fromScene = true;
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
            SetCarriedVisible(carried, true);
            CleanupCarryVisual(carried);
            Debug.Log("[House] Перенос отменён.");
        }
        HideCarryIcon();
        carried = null;
        candidate = null;
        hasFloorCell = false;
        state = DragState.Idle;
    }

    /// <summary>Бросок несомого на панель: снять со сцены, +1 на склад.</summary>
    void StoreToPanel(GameObject go)
    {
        if (go == null) return;
        HousePlacedFurniture marker = go.GetComponent<HousePlacedFurniture>();
        if (marker == null)
        {
            marker = go.AddComponent<HousePlacedFurniture>();
            marker.prefabName = go.name;
            if (go.transform.parent != null && go.transform.parent.name == "Interior")
                marker.fromScene = true;
        }
        if (HouseFurniture.TryStore(marker, true, out string reason))
        {
            Debug.Log("[House] Убрано на склад: " + marker.prefabName + ".");
            Object.FindFirstObjectByType<HouseEditUI>()?.Refresh(); // счётчики сразу
        }
        else
            Debug.Log("[House] Не убралось (" + reason + ") — вернул на место.");
        if (marker != null && marker.gameObject != null)
            marker.transform.position = originPos; // откат если не убралось
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

    // ─── UI-копия несомого поверх панели (обратный драг дом → панель) ───
    void ShowCarryIcon(Sprite sprite, Vector2 pxSize)
    {
        HideCarryIcon();
        if (sprite == null) return;
        Canvas canvas = ResolveCanvas();
        if (canvas == null) return;
        carryIcon = new GameObject("HouseCarryIcon");
        carryIcon.transform.SetParent(canvas.transform, false);
        carryIcon.transform.SetAsLastSibling();
        Image img = carryIcon.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        img.preserveAspect = true;
        if (pxSize.x < 8f) pxSize.x = 8f;
        if (pxSize.y < 8f) pxSize.y = 8f;
        carryIcon.GetComponent<RectTransform>().sizeDelta = pxSize;
        carryIcon.SetActive(false); // подхват из мира: покажем только над панелью
        TintCarryIcon(true);
    }

    void MoveCarryIcon(Vector2 screenPos)
    {
        if (carryIcon == null) return;
        Canvas canvas = ResolveCanvas();
        if (canvas == null) return;
        carryIcon.transform.SetAsLastSibling();
        Vector2 iconScreen = screenPos + carryIconOffsetPx;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.GetComponent<RectTransform>(),
                iconScreen, canvas.worldCamera, out Vector2 local))
            carryIcon.GetComponent<RectTransform>().localPosition = local;
    }

    void TintCarryIcon(bool ok)
    {
        if (carryIcon == null) return;
        Image img = carryIcon.GetComponent<Image>();
        if (img == null) return;
        img.color = ok ? new Color(0.7f, 1f, 0.7f, 0.95f)
                       : new Color(1f, 0.45f, 0.45f, 0.95f);
    }

    void UpdateCarryVisibility(Vector2 screenPos)
    {
        if (carryIcon == null) return;
        bool overPanel = HouseEditUI.OverPanelNoCreate(screenPos);
        carryIcon.SetActive(overPanel);
        SetCarriedVisible(carried, !overPanel);
    }

    void HideCarryIcon()
    {
        if (carryIcon != null) Destroy(carryIcon);
        carryIcon = null;
        carryIconOffsetPx = Vector2.zero;
    }

    static void SetCarriedVisible(GameObject go, bool visible)
    {
        if (go == null) return;
        foreach (SpriteRenderer sr in go.GetComponentsInChildren<SpriteRenderer>(true))
            sr.enabled = visible;
    }

    static Canvas ResolveCanvas()
    {
        Canvas c = HouseEditUI.PanelCanvas();
        if (c != null) return c;
        return Object.FindFirstObjectByType<Canvas>();
    }

    static Vector2 WorldToScreenSize(Vector2 worldSize)
    {
        return worldSize * PxPerUnit();
    }

    static Vector2 PxPerUnit()
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic) return Vector2.one * 72f;
        if (cam.orthographicSize <= 0f) return Vector2.one * 72f;
        float p = Screen.height / (2f * cam.orthographicSize);
        return new Vector2(p, p);
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
