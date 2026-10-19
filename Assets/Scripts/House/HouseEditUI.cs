using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Логика панели планировки дома (фаза 7). Панель верстает владелец
/// (HouseEditBinder); без биндера не работаем (кодового UI не строим).
/// Табы: Склад (таскать в дом) / Магазин (купить в склад).
/// Категории: Пол (стили пола, базовый первый) / Обои / Мебель (напольная) /
/// Декор (настенная + ковры). Всё применяется сразу с сейвом, выход — крестик.
/// </summary>
public class HouseEditUI : MonoBehaviour
{
    public static HouseEditUI Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("HouseEditUI");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<HouseEditUI>();
            }
            return _instance;
        }
    }
    private static HouseEditUI _instance;

    enum Cat { Floor, Wallpaper, Furniture, Decor }
    enum RowKind { Furniture, FloorStyle, WallStyle, Expansion }

    class Row
    {
        public RowKind kind;
        public string id; // prefabName или styleId
        public string title;
        public string desc;
        public Sprite icon;
        public int price; // Магазин
        public int count; // Склад
        public GameObject prefab; // мебель
        public FurnitureMount mount;
    }

    static readonly string[] StyleOrder = { "Brick", "Barn" }; // базовый пол первый в списке

    HouseEditBinder binder;
    HouseEditBinder boundBinder;
    bool tabStock = true;
    Cat cat = Cat.Floor;
    readonly List<Row> rows = new List<Row>();
    int selectedIndex = -1;
    int ghostRowIndex = -1;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool IsReady => binder != null && binder.panel != null;

    public void Bind(HouseEditBinder b)
    {
        if (b == null) return;
        binder = b;
        if (boundBinder == b) return;
        boundBinder = b;
        Wire(b.closeButton, () => HouseEditMode.Instance.Exit());
        Wire(b.tabStockButton, () => { tabStock = true; selectedIndex = -1; Refresh(); });
        Wire(b.tabShopButton, () => { tabStock = false; selectedIndex = -1; Refresh(); });
        Wire(b.catFloorButton, () => { cat = Cat.Floor; selectedIndex = -1; Refresh(); });
        Wire(b.catWallpaperButton, () => { cat = Cat.Wallpaper; selectedIndex = -1; Refresh(); });
        Wire(b.catFurnitureButton, () => { cat = Cat.Furniture; selectedIndex = -1; Refresh(); });
        Wire(b.catDecorButton, () => { cat = Cat.Decor; selectedIndex = -1; Refresh(); });
        Wire(b.buyButton, BuySelected);
    }

    static void Wire(Button btn, UnityEngine.Events.UnityAction act)
    {
        if (btn == null || act == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(act);
    }

    public bool Open()
    {
        if (!IsReady)
        {
            Debug.LogWarning("[House] Панель не связана (HouseEditBinder).");
            return false;
        }
        binder.panel.SetActive(true);
        tabStock = true;
        cat = Cat.Floor;
        selectedIndex = -1;
        Refresh();
        return true;
    }

    public void Close()
    {
        CancelPanelDrag();
        if (binder != null && binder.panel != null)
            binder.panel.SetActive(false);
    }

    /// <summary>Точка над панелью? (бросок со сцены на склад).</summary>
    public bool IsPointerOverPanel(Vector2 screenPos)
    {
        if (binder == null || binder.panel == null || !binder.panel.activeInHierarchy) return false;
        RectTransform rt = binder.panel.transform as RectTransform;
        if (rt == null) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos);
    }

    // ─── Сетка ───
    public void Refresh()
    {
        if (!IsReady) return;
        BuildRows();
        foreach (Transform child in binder.contentGrid)
            Destroy(child.gameObject);
        for (int i = 0; i < rows.Count; i++)
        {
            Row r = rows[i];
            GameObject slot = Instantiate(binder.slotPrefab, binder.contentGrid);
            HousePanelSlot ps = slot.GetComponent<HousePanelSlot>();
            if (ps == null)
            {
                Debug.LogError("[House] На slotPrefab нет HousePanelSlot.");
                Destroy(slot);
                continue;
            }
            ps.rowIndex = i;
            if (ps.icon != null) ps.icon.sprite = r.icon;
            if (ps.priceText != null) ps.priceText.text = tabStock ? "" : r.price + "g";
            if (ps.countText != null) ps.countText.text = tabStock ? "×" + r.count : "";
        }
        RefreshDetail();
        RefreshGold();
        SetHint(tabStock ? "Drag an item into the house to place it" : "Buy it first — it lands in stock, then drag it home");
    }

    void BuildRows()
    {
        rows.Clear();
        if (tabStock) BuildStockRows();
        else BuildShopRows();
    }

    void BuildStockRows()
    {
        if (HouseStock.Instance == null) return;
        if (cat == Cat.Floor || cat == Cat.Wallpaper)
        {
            bool wall = cat == Cat.Wallpaper;
            foreach (string id in StyleOrder)
            {
                string key = wall ? HouseReskin.WallKey(id) : HouseReskin.FloorKey(id);
                ItemData item = StyleItem(wall, id);
                rows.Add(new Row
                {
                    kind = wall ? RowKind.WallStyle : RowKind.FloorStyle,
                    id = id,
                    title = item != null ? item.itemName : key,
                    desc = item != null ? item.description : "",
                    icon = item != null ? item.icon : null,
                    count = HouseStock.Instance.Count(key)
                });
            }
            // Билет расширения живёт во вкладке Пол (склад): +1 тайл, тащи в дом.
            if (!wall)
                rows.Add(ExpansionRow(ExpansionIcon(), HouseExpansion.TicketCount(), 0));
            return;
        }
        bool wantDecor = cat == Cat.Decor;
        foreach (string id in HouseStock.Instance.AllIds())
        {
            GameObject prefab = HouseFurniture.ResolvePrefab(id);
            if (prefab == null) continue; // стили/служебное — выше
            FurnitureMount m = HouseFurniture.MountOfPrefab(id);
            bool isDecor = m == FurnitureMount.Wall || m == FurnitureMount.Both;
            if (isDecor != wantDecor) continue;
            Sprite icon = null;
            SpriteRenderer sr = prefab.GetComponent<SpriteRenderer>();
            if (sr != null) icon = sr.sprite;
            rows.Add(new Row
            {
                kind = RowKind.Furniture,
                id = id,
                title = id,
                desc = m.ToString(),
                icon = icon,
                count = HouseStock.Instance.Count(id),
                prefab = prefab,
                mount = m
            });
        }
    }

    void BuildShopRows()
    {
        if (cat == Cat.Floor || cat == Cat.Wallpaper)
        {
            bool wall = cat == Cat.Wallpaper;
            foreach (string id in StyleOrder)
            {
                ItemData item = StyleItem(wall, id);
                if (item == null) continue;
                rows.Add(new Row
                {
                    kind = wall ? RowKind.WallStyle : RowKind.FloorStyle,
                    id = id,
                    title = item.itemName,
                    desc = item.description,
                    icon = item.icon,
                    price = item.shopPrice
                });
            }
            // Билет расширения живёт во вкладке Пол (магазин): цена-лесенка, только голда.
            if (!wall)
                rows.Add(ExpansionRow(ExpansionIcon(), 0, HouseExpansion.NextTicketGold()));
            return;
        }
        HouseCatalog catalog = HouseCatalog.Load();
        if (catalog == null || catalog.entries == null) return;
        bool wantDecor = cat == Cat.Decor;
        foreach (HouseCatalog.Entry e in catalog.entries)
        {
            if (e == null || e.prefab == null) continue;
            FurnitureMount m = HouseFurniture.MountOfPrefab(e.prefab.name);
            bool isDecor = m == FurnitureMount.Wall || m == FurnitureMount.Both;
            if (isDecor != wantDecor) continue;
            Sprite icon = null;
            SpriteRenderer sr = e.prefab.GetComponent<SpriteRenderer>();
            if (sr != null) icon = sr.sprite;
            rows.Add(new Row
            {
                kind = RowKind.Furniture,
                id = e.prefab.name,
                title = e.prefab.name,
                desc = m.ToString(),
                icon = icon,
                price = e.price,
                prefab = e.prefab,
                mount = m
            });
        }
    }

    static ItemData StyleItem(bool wall, string styleId)
    {
        return Resources.Load<ItemData>("Items/House/" + (wall ? "Wallpaper_" : "FloorTile_") + styleId);
    }

    // ─── Карточка + покупка ───
    public void SlotClicked(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= rows.Count) return;
        selectedIndex = rowIndex;
        RefreshDetail();
    }

    void RefreshDetail()
    {
        if (!IsReady) return;
        Row r = selectedIndex >= 0 && selectedIndex < rows.Count ? rows[selectedIndex] : null;
        if (binder.detailIcon != null) binder.detailIcon.sprite = r != null ? r.icon : null;
        if (binder.detailName != null) binder.detailName.text = r != null ? r.title : "";
        if (binder.detailDesc != null) binder.detailDesc.text = r != null ? r.desc : "";
        if (binder.detailCount != null) binder.detailCount.text = r != null && tabStock ? "In stock: " + r.count : "";
        if (binder.detailPrice != null) binder.detailPrice.text = r != null && !tabStock ? r.price + "g" : "";
        if (binder.buyButton != null) binder.buyButton.gameObject.SetActive(!tabStock && r != null);
    }

    void BuySelected()
    {
        if (tabStock) return;
        if (selectedIndex < 0 || selectedIndex >= rows.Count) return;
        Row r = rows[selectedIndex];
        // Билет расширения: лесенка цены + счётчик покупок внутри BuyTicket.
        if (r.kind == RowKind.Expansion)
        {
            HouseExpansion.BuyTicket(out _);
            Refresh();
            return;
        }
        if (CurrencyManager.Instance == null) return;
        if (CurrencyManager.Instance.Gold < r.price)
        {
            Debug.Log("[House] Не хватает голды: надо " + r.price + "g.");
            return;
        }
        string stockId = r.kind == RowKind.Furniture ? r.id
            : r.kind == RowKind.WallStyle ? HouseReskin.WallKey(r.id)
            : HouseReskin.FloorKey(r.id);
        if (!CurrencyManager.Instance.SpendGold(r.price)) return;
        HouseStock.Instance.Add(stockId, 1);
        Debug.Log("[House] Куплено: " + r.title + " (на складе " + HouseStock.Instance.Count(stockId) + ").");
        Refresh();
    }

    void RefreshGold()
    {
        if (binder != null && binder.goldLabel != null && CurrencyManager.Instance != null)
            binder.goldLabel.text = CurrencyManager.Instance.Gold + "g";
    }

    void SetHint(string text)
    {
        if (binder != null && binder.hintLabel != null)
            binder.hintLabel.text = text;
    }

    // ─── Драг из панели (только Склад): один видимый предмет за раз.
    // Над панелью — UI-иконка в РЕАЛЬНОМ размере поверх оверлея (мировой
    // спрайт всегда под Screen Space Overlay, иначе стол прячется за панель).
    // Над миром — настоящий инстанс в сцене (что видишь, туда и встанет).
    // Переключение — по IsPointerOverPanel, двойника нет.
    GameObject dragInstance;
    Color dragInstanceColor;
    GameObject cellHighlight;
    GameObject dragIcon; // UI-копия для отображения поверх панели
    Vector2 dragIconOffsetPx; // палец = низ стола как в доме, а не центр картинки
    static Sprite whiteSquare;

    /// <summary>Сейчас тащат предмет из панели (мировой свайп карты не должен вмешиваться).</summary>
    public static bool PanelDragActive
    {
        get { return _instance != null && _instance.ghostRowIndex >= 0; }
    }

    /// <summary>Канвас панели без создания синглтона (для драга со сцены).</summary>
    public static Canvas PanelCanvas()
    {
        if (_instance == null) return null;
        if (_instance.binder == null || _instance.binder.panel == null) return null;
        return _instance.binder.panel.GetComponentInParent<Canvas>();
    }

    /// <summary>Точка над панелью без создания синглтона (для драга со сцены).</summary>
    public static bool OverPanelNoCreate(Vector2 screenPos)
    {
        if (_instance == null) return false;
        return _instance.IsPointerOverPanel(screenPos);
    }

    public void BeginPanelDrag(int rowIndex, BaseEventData _)
    {
        CancelPanelDrag();
        ghostRowIndex = -1;
        if (!tabStock) return; // из магазина — сначала купи
        if (rowIndex < 0 || rowIndex >= rows.Count) return;
        Row r = rows[rowIndex];
        if (HouseStock.Instance == null || HouseStock.Instance.Count(StockIdOf(r)) <= 0) return;
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
            return;
        Vector3 wp = ScreenToWorld(PointerPos(_));
        wp.z = 0f;
        ghostRowIndex = rowIndex;
        Vector2 screenPos = PointerPos(_);
        Vector2 iconPx = new Vector2(72, 72);
        Vector2 iconOffset = Vector2.zero;
        if (r.kind == RowKind.Furniture && r.prefab != null)
        {
            dragInstance = Instantiate(r.prefab, wp, Quaternion.identity);
            dragInstance.name = r.prefab.name;
            SpriteRenderer sr = dragInstance.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                dragInstanceColor = sr.color;
                sr.color = new Color(dragInstanceColor.r, dragInstanceColor.g, dragInstanceColor.b, 0.6f);
                if (sr.sprite != null)
                {
                    iconPx = WorldToScreenSize(sr.bounds.size);
                    // Якорь как в доме: палец держит низ (трансформ), а не центр спрайта.
                    iconOffset = PxPerUnit() * ((Vector2)sr.bounds.center - (Vector2)dragInstance.transform.position);
                }
            }
        }
        else
        {
            cellHighlight = new GameObject("HousePaintHighlight");
            SpriteRenderer hr = cellHighlight.AddComponent<SpriteRenderer>();
            hr.sprite = WhiteSquare();
            hr.sortingOrder = 59;
            cellHighlight.transform.localScale = new Vector3(0.9f, 0.9f, 1f);
            Vector2 tileWorld = floors != null
                ? new Vector2(floors.cellSize.x, floors.cellSize.y) : Vector2.one;
            if (tileWorld.x <= 0f) tileWorld.x = 1f;
            if (tileWorld.y <= 0f) tileWorld.y = 1f;
            iconPx = WorldToScreenSize(tileWorld);
        }
        ShowDragIcon(RowSprite(r), screenPos, iconPx);
        dragIconOffsetPx = iconOffset; // после Show (внутри Hide сбрасывает в ноль)
        MoveDragIcon(screenPos);
        UpdatePanelDragVisibility(screenPos);
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

    static string StockIdOf(Row r)
    {
        if (r.kind == RowKind.Expansion) return HouseExpansion.TicketKey;
        return r.kind == RowKind.Furniture ? r.id
            : r.kind == RowKind.WallStyle ? HouseReskin.WallKey(r.id)
            : HouseReskin.FloorKey(r.id);
    }

    static Row ExpansionRow(Sprite icon, int count, int price)
    {
        return new Row
        {
            kind = RowKind.Expansion,
            id = HouseExpansion.TicketKey,
            title = "Расширение дома",
            desc = "Билет +1 тайл пола. Тащи в дом. Цена растёт каждые 5.",
            icon = icon,
            count = count,
            price = price
        };
    }

    Sprite ExpansionIcon()
    {
        if (binder != null && binder.expansionIcon != null) return binder.expansionIcon;
        return null;
    }

    public void PanelDrag(BaseEventData data)
    {
        if (ghostRowIndex < 0 || ghostRowIndex >= rows.Count) return;
        Row r = rows[ghostRowIndex];
        Vector2 screenPos = PointerPos(data);
        Vector3 wp = ScreenToWorld(screenPos);
        wp.z = 0f;
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
            return;
        if (dragInstance != null)
        {
            dragInstance.transform.position = wp;
            bool ok = HouseFurniture.CanPlaceAt(walls, floors, region, r.prefab, wp, r.mount, out _, dragInstance);
            SpriteRenderer sr = dragInstance.GetComponent<SpriteRenderer>();
            if (sr != null)
                sr.color = ok ? new Color(0.55f, 1f, 0.55f, 0.6f)
                              : new Color(1f, 0.35f, 0.35f, 0.6f);
            TintDragIcon(ok);
            MoveDragIcon(screenPos);
        }
        else if (cellHighlight != null)
        {
            if (r.kind == RowKind.Expansion)
            {
                // Драг билета: подсветка ghost-клетки встык (плюсы на сцену не рисуем).
                Vector3Int cell = floors.WorldToCell(wp);
                cellHighlight.transform.position = floors.GetCellCenterWorld(cell);
                WallStyle brick = WallStyle.Load(HouseReskin.BaseStyleId);
                bool ok = brick != null
                    && HouseExpansion.CanExpandAt(walls, floors, region, brick, lockY, cell)
                    && region.Count < HousePricing.MaxFloorTiles;
                SpriteRenderer hr0 = cellHighlight.GetComponent<SpriteRenderer>();
                if (hr0 != null)
                    hr0.color = ok ? new Color(0.55f, 1f, 0.55f, 0.5f)
                                   : new Color(1f, 0.35f, 0.35f, 0.5f);
                TintDragIcon(ok);
                MoveDragIcon(screenPos);
            }
            else
            {
            Vector3Int cell = r.kind == RowKind.WallStyle
                ? walls.WorldToCell(wp) : floors.WorldToCell(wp);
            Vector3 center = r.kind == RowKind.WallStyle
                ? walls.GetCellCenterWorld(cell) : floors.GetCellCenterWorld(cell);
            cellHighlight.transform.position = center;
            bool ok = r.kind == RowKind.WallStyle
                ? HouseReskin.CanPaintWallCell(cell, r.id)
                : region.Contains(cell) && floors.HasTile(cell);
            SpriteRenderer hr = cellHighlight.GetComponent<SpriteRenderer>();
            if (hr != null)
                hr.color = ok ? new Color(0.55f, 1f, 0.55f, 0.5f)
                              : new Color(1f, 0.35f, 0.35f, 0.5f);
            TintDragIcon(ok);
            MoveDragIcon(screenPos);
            }
        }
        UpdatePanelDragVisibility(screenPos);
    }

    public void EndPanelDrag(BaseEventData data)
    {
        if (ghostRowIndex < 0 || ghostRowIndex >= rows.Count)
        {
            CancelPanelDrag();
            return;
        }
        Row r = rows[ghostRowIndex];
        Vector2 screenPos = PointerPos(data);
        // Отпустил над панелью = отмена без списания (предмет остаётся на складе).
        if (IsPointerOverPanel(screenPos))
        {
            CancelPanelDrag();
            Refresh();
            Debug.Log("[House] Возврат: отпустил над панелью.");
            return;
        }
        Vector3 wp = ScreenToWorld(screenPos);
        wp.z = 0f;
        GameObject instance = dragInstance;
        dragInstance = null;
        if (cellHighlight != null) Destroy(cellHighlight);
        cellHighlight = null;
        HideDragIcon();
        ghostRowIndex = -1;
        SetDragWorldVisible(instance, true);
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
        {
            if (instance != null) Destroy(instance);
            return;
        }
        if (instance != null)
        {
            // Валидно = забираем со склада и оставляем (маркер + сейв), мимо = сносим.
            instance.transform.position = wp;
            if (HouseFurniture.CanPlaceAt(walls, floors, region, r.prefab, wp, r.mount, out string reason, instance)
                && HouseStock.Instance != null && HouseStock.Instance.Remove(StockIdOf(r)))
            {
                SpriteRenderer sr = instance.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = dragInstanceColor;
                HousePlacedFurniture marker = instance.AddComponent<HousePlacedFurniture>();
                marker.prefabName = r.prefab.name;
                SaveManager.Instance?.Save();
                Debug.Log("[House] Поставлено из панели: " + r.id + ".");
            }
            else
            {
                Destroy(instance);
                Debug.Log("[House] Сюда нельзя" +
                          (string.IsNullOrEmpty(reason) ? "." : " (" + reason + ")."));
            }
        }
        else if (r.kind == RowKind.Expansion)
        {
            Vector3Int cell = floors.WorldToCell(wp);
            bool built = HouseExpansion.TryExpandTicket(cell);
            Debug.Log(built ? "[House] Дом расширен: " + cell + "."
                            : "[House] Сюда нельзя (только клетка встык / нет билета).");
            if (built) SaveManager.Instance?.Save();
        }
        else if (r.kind == RowKind.FloorStyle)
        {
            Vector3Int cell = floors.WorldToCell(wp);
            bool painted = HouseReskin.TryPaintFloorCell(cell, r.id, true);
            Debug.Log(painted ? "[House] Пол перекрашен: " + cell + "."
                              : "[House] Сюда нельзя (только пол комнаты / нет на складе).");
            if (painted) SaveManager.Instance?.Save();
        }
        else if (r.kind == RowKind.WallStyle)
        {
            Vector3Int cell = walls.WorldToCell(wp);
            bool painted = HouseReskin.TryPaintWallCell(cell, r.id, true);
            Debug.Log(painted ? "[House] Стена перекрашена: " + cell + "."
                              : "[House] Сюда нельзя (только стена комнаты / нет на складе).");
            if (painted) SaveManager.Instance?.Save();
        }
        Refresh();
    }

    void CancelPanelDrag()
    {
        ghostRowIndex = -1;
        if (dragInstance != null) Destroy(dragInstance);
        dragInstance = null;
        if (cellHighlight != null) Destroy(cellHighlight);
        cellHighlight = null;
        HideDragIcon();
    }

    // ─── UI-иконка поверх панели (паттерн ItemDragHandler/HookSocketUI) ───
    // Размер — реальный мировой (стол над панелью тот же что в доме).
    void ShowDragIcon(Sprite sprite, Vector2 screenPos, Vector2 pxSize)
    {
        HideDragIcon();
        if (sprite == null) return;
        if (binder == null || binder.panel == null) return;
        Canvas canvas = binder.panel.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        dragIcon = new GameObject("HouseDragIcon");
        dragIcon.transform.SetParent(canvas.transform, false);
        dragIcon.transform.SetAsLastSibling();
        Image img = dragIcon.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        img.preserveAspect = true;
        if (pxSize.x < 8f) pxSize.x = 8f;
        if (pxSize.y < 8f) pxSize.y = 8f;
        dragIcon.GetComponent<RectTransform>().sizeDelta = pxSize;
        MoveDragIcon(screenPos);
        TintDragIcon(true);
    }

    /// <summary>Один видимый: над панелью — иконка, над миром — настоящий.</summary>
    void UpdatePanelDragVisibility(Vector2 screenPos)
    {
        if (dragIcon == null) return; // иконки нет — мир не прячем
        bool overPanel = IsPointerOverPanel(screenPos);
        dragIcon.SetActive(overPanel);
        SetDragWorldVisible(dragInstance, !overPanel);
        if (cellHighlight != null)
        {
            SpriteRenderer hr = cellHighlight.GetComponent<SpriteRenderer>();
            if (hr != null) hr.enabled = !overPanel;
        }
    }

    static void SetDragWorldVisible(GameObject go, bool visible)
    {
        if (go == null) return;
        foreach (SpriteRenderer sr in go.GetComponentsInChildren<SpriteRenderer>(true))
            sr.enabled = visible;
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

    void MoveDragIcon(Vector2 screenPos)
    {
        if (dragIcon == null) return;
        if (binder == null || binder.panel == null) return;
        Canvas canvas = binder.panel.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        dragIcon.transform.SetAsLastSibling(); // сетка пересобирается — держим иконку сверху
        // Центр картинки смещён так, чтобы палец был внизу стола как в доме.
        Vector2 iconScreen = screenPos + dragIconOffsetPx;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvas.GetComponent<RectTransform>(),
                iconScreen, canvas.worldCamera, out Vector2 local))
            dragIcon.GetComponent<RectTransform>().localPosition = local;
    }

    void TintDragIcon(bool ok)
    {
        if (dragIcon == null) return;
        Image img = dragIcon.GetComponent<Image>();
        if (img == null) return;
        img.color = ok ? new Color(0.7f, 1f, 0.7f, 0.95f)
                       : new Color(1f, 0.45f, 0.45f, 0.95f);
    }

    void HideDragIcon()
    {
        if (dragIcon != null) Destroy(dragIcon);
        dragIcon = null;
        dragIconOffsetPx = Vector2.zero;
    }

    static Sprite RowSprite(Row r)
    {
        if (r == null) return null;
        if (r.icon != null) return r.icon;
        if (r.prefab != null)
        {
            SpriteRenderer sr = r.prefab.GetComponent<SpriteRenderer>();
            if (sr != null) return sr.sprite;
        }
        return null;
    }

    static Vector2 PointerPos(BaseEventData data)
    {
        if (data is PointerEventData ped) return ped.position;
        if (Input.touchCount > 0) return Input.GetTouch(0).position;
        return (Vector2)Input.mousePosition;
    }

    static Vector3 ScreenToWorld(Vector2 screenPos)
    {
        Camera cam = Camera.main;
        if (cam == null) return Vector3.zero;
        Vector3 wp = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -cam.transform.position.z));
        wp.z = 0f;
        return wp;
    }
}
