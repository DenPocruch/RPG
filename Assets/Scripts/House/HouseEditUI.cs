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
    enum RowKind { Furniture, FloorStyle, WallStyle }

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

    GameObject panelGhost;
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
        CancelGhost();
        if (binder != null && binder.panel != null)
            binder.panel.SetActive(false);
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
        SetHint(tabStock ? "Перетащи предмет в дом, чтобы установить" : "Купи — упадёт на склад, потом тащи в дом");
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
        if (binder.detailCount != null) binder.detailCount.text = r != null && tabStock ? "На складе: " + r.count : "";
        if (binder.detailPrice != null) binder.detailPrice.text = r != null && !tabStock ? r.price + "g" : "";
        if (binder.buyButton != null) binder.buyButton.gameObject.SetActive(!tabStock && r != null);
    }

    void BuySelected()
    {
        if (tabStock) return;
        if (selectedIndex < 0 || selectedIndex >= rows.Count) return;
        Row r = rows[selectedIndex];
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

    // ─── Драг из панели (только Склад) ───
    public void BeginPanelDrag(int rowIndex, BaseEventData _)
    {
        CancelGhost();
        ghostRowIndex = -1;
        if (!tabStock) return; // из магазина — сначала купи
        if (rowIndex < 0 || rowIndex >= rows.Count) return;
        Row r = rows[rowIndex];
        if (r.icon == null) return;
        if (HouseStock.Instance == null || HouseStock.Instance.Count(StockIdOf(r)) <= 0) return;
        ghostRowIndex = rowIndex;
        panelGhost = new GameObject("HousePanelGhost");
        SpriteRenderer sr = panelGhost.AddComponent<SpriteRenderer>();
        sr.sprite = r.icon;
        sr.sortingOrder = 60;
        Color c = sr.color;
        c.a = 0.6f;
        sr.color = c;
    }

    static string StockIdOf(Row r)
    {
        return r.kind == RowKind.Furniture ? r.id
            : r.kind == RowKind.WallStyle ? HouseReskin.WallKey(r.id)
            : HouseReskin.FloorKey(r.id);
    }

    public void PanelDrag(BaseEventData data)
    {
        if (panelGhost == null || ghostRowIndex < 0 || ghostRowIndex >= rows.Count) return;
        Vector2 screenPos = PointerPos(data);
        Vector3 wp = ScreenToWorld(screenPos);
        panelGhost.transform.position = new Vector3(wp.x, wp.y, 0f);
        bool ok = ValidateGhost(rows[ghostRowIndex], wp);
        SpriteRenderer sr = panelGhost.GetComponent<SpriteRenderer>();
        if (sr != null)
            sr.color = ok ? new Color(0.55f, 1f, 0.55f, 0.6f)
                          : new Color(1f, 0.35f, 0.35f, 0.6f);
    }

    public void EndPanelDrag(BaseEventData data)
    {
        if (panelGhost == null || ghostRowIndex < 0 || ghostRowIndex >= rows.Count)
        {
            CancelGhost();
            return;
        }
        Row r = rows[ghostRowIndex];
        Vector3 wp = ScreenToWorld(PointerPos(data));
        CancelGhost();
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
            return;
        bool placed = false;
        if (r.kind == RowKind.Furniture && r.prefab != null)
        {
            GameObject go = HouseFurniture.TryPlace(walls, floors, r.prefab, wp, true, out string reason);
            if (go == null)
                Debug.Log("[House] Сюда нельзя (" + reason + ").");
            else
            {
                placed = true;
                Debug.Log("[House] Поставлено из панели: " + r.id + ".");
            }
        }
        else if (r.kind == RowKind.FloorStyle)
        {
            Vector3Int cell = floors.WorldToCell(wp);
            placed = HouseReskin.TryPaintFloorCell(cell, r.id, true);
            Debug.Log(placed ? "[House] Пол перекрашен: " + cell + "."
                             : "[House] Сюда нельзя (только пол комнаты / нет на складе).");
        }
        else if (r.kind == RowKind.WallStyle)
        {
            Vector3Int cell = walls.WorldToCell(wp);
            placed = HouseReskin.TryPaintWallCell(cell, r.id, true);
            Debug.Log(placed ? "[House] Стена перекрашена: " + cell + "."
                             : "[House] Сюда нельзя (только лента лица / нет на складе).");
        }
        Refresh();
    }

    bool ValidateGhost(Row r, Vector3 wp)
    {
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
            return false;
        if (r.kind == RowKind.Furniture && r.prefab != null)
            return HouseFurniture.CanPlaceAt(walls, floors, region, r.prefab, wp, r.mount, out _);
        if (r.kind == RowKind.FloorStyle)
        {
            Vector3Int cell = floors.WorldToCell(wp);
            return region.Contains(cell) && floors.HasTile(cell);
        }
        Vector3Int wcell = walls.WorldToCell(wp);
        return HouseReskin.CanPaintWallCell(wcell, r.id);
    }

    void CancelGhost()
    {
        ghostRowIndex = -1;
        if (panelGhost != null) Destroy(panelGhost);
        panelGhost = null;
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
