using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Окно кормушки: перенос корма из рюкзака/хотбара в FeederStorage.
/// Весь UI строится кодом (EnsureInstance при первом открытии) — ссылки в
/// инспекторе не нужны (грабли MCP). Открывается ударом по кормушке.
/// </summary>
public class FeedUI : MonoBehaviour
{
    public static FeedUI Instance { get; private set; }

    private GameObject panel;
    private TMP_Text capacityText;
    private TMP_Text stockText;
    private Transform content;
    private FeederStorage currentFeeder;
    private bool isOpen;

    const float PANEL_W = 580f;
    const float PANEL_H = 540f;

    // ═══════════════════════════════════════════════════════════
    // ОТКРЫТИЕ (внешняя точка входа — FeederStorage.Interact)
    // ═══════════════════════════════════════════════════════════
    public static void Open(FeederStorage feeder)
    {
        EnsureInstance();
        if (Instance != null) Instance.OpenInternal(feeder);
    }

    static void EnsureInstance()
    {
        if (Instance != null) return;

        Canvas canvas = GameObject.Find("Canvas") != null
            ? GameObject.Find("Canvas").GetComponent<Canvas>()
            : FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[FeedUI] Не найден Canvas!");
            return;
        }

        GameObject go = new GameObject("FeedUI", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        Instance = go.AddComponent<FeedUI>();
        Instance.BuildUI();
    }

    void OpenInternal(FeederStorage feeder)
    {
        currentFeeder = feeder;
        panel.SetActive(true);
        isOpen = true;

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        Refresh();
    }

    public void Close()
    {
        if (panel == null) return;
        panel.SetActive(false);
        isOpen = false;
        currentFeeder = null;

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;
    }

    public bool IsOpen() => isOpen;

    // ═══════════════════════════════════════════════════════════
    // ПОСТРОЕНИЕ UI (один раз)
    // ═══════════════════════════════════════════════════════════
    void BuildUI()
    {
        panel = new GameObject("Feed Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        var rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(PANEL_W, PANEL_H);
        rt.anchoredPosition = Vector2.zero;

        var img = panel.GetComponent<Image>();
        img.color = new Color(0.14f, 0.11f, 0.09f, 0.97f);
        img.raycastTarget = true; // ловим клики, не пропускаем сквозь панель

        var title = MakeText(panel.transform, "Title", "Кормушка", TextAlignmentOptions.Center, 30);
        Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -52), new Vector2(-12, -6));

        capacityText = MakeText(panel.transform, "Capacity", "", TextAlignmentOptions.Center, 20);
        capacityText.color = new Color(1f, 0.85f, 0.5f);
        Anchor(capacityText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(12, -80), new Vector2(-12, -54));

        stockText = MakeText(panel.transform, "StockText", "", TextAlignmentOptions.Left, 17);
        stockText.color = new Color(0.8f, 0.8f, 0.8f);
        Anchor(stockText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(16, -106), new Vector2(-16, -80));

        // ══ Список кормов (скролл) ══
        var scrollGo = new GameObject("FeedScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
        scrollGo.transform.SetParent(panel.transform, false);
        var scrollRt = scrollGo.GetComponent<RectTransform>();
        Anchor(scrollRt, new Vector2(0, 0), new Vector2(1, 1), new Vector2(12, 78), new Vector2(-12, -112));
        scrollGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.25f);

        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(scrollGo.transform, false);
        Anchor(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var contentGo = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewport.transform, false);
        var vlg = contentGo.GetComponent<VerticalLayoutGroup>();
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.spacing = 4f;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        var csf = contentGo.GetComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var srt = scrollGo.GetComponent<ScrollRect>();
        srt.content = contentGo.GetComponent<RectTransform>();
        srt.viewport = viewport.GetComponent<RectTransform>();
        srt.horizontal = false;
        srt.movementType = ScrollRect.MovementType.Clamped;
        srt.scrollSensitivity = 20f;

        content = contentGo.transform;

        // ══ Нижние кнопки ══
        CreateButton(panel.transform, "TakeAllBtn", "Забрать всё", new Color(0.45f, 0.3f, 0.15f),
            () => TakeAllBack());
        Anchor(GetBtnRect("TakeAllBtn"), new Vector2(0, 0), new Vector2(0.45f, 0), new Vector2(14, 12), new Vector2(-8, 54));

        CreateButton(panel.transform, "CloseBtn", "Закрыть", new Color(0.5f, 0.2f, 0.15f),
            () => Close());
        Anchor(GetBtnRect("CloseBtn"), new Vector2(0.55f, 0), new Vector2(1, 0), new Vector2(8, 12), new Vector2(-14, 54));

        panel.SetActive(false);
    }

    RectTransform GetBtnRect(string name) => panel.transform.Find(name).GetComponent<RectTransform>();

    // ═══════════════════════════════════════════════════════════
    // ОБНОВЛЕНИЕ СПИСКА
    // ═══════════════════════════════════════════════════════════
    void Refresh()
    {
        if (currentFeeder == null) return;

        capacityText.text = currentFeeder.CapacityInfo() +
            (currentFeeder.FreeSpace > 0 ? "" : "  (ПОЛНО)");
        stockText.text = StockSummary();

        foreach (Transform child in content)
            Destroy(child.gameObject);

        // Уникальные кормовые предметы, лежащие в рюкзаке/хотбаре
        var seen = new HashSet<string>();
        foreach (InventorySlot slot in AllSlots())
        {
            if (slot == null || slot.IsEmpty() || slot.currentItem == null) continue;
            ItemData item = slot.currentItem;
            if (slot.quantity <= 0) continue;
            if (!IsAnimalFeed(item)) continue;
            if (!seen.Add(item.name)) continue;

            CreateRow(item);
        }

        if (seen.Count == 0)
        {
            var emptyGo = new GameObject("EmptyHint", typeof(RectTransform));
            emptyGo.transform.SetParent(content, false);
            var tmp = emptyGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "Нет корма для животных.\nЖивотные едят: пшеница и другие культуры.";
            tmp.fontSize = 19;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.7f, 0.7f, 0.7f);
            var le = emptyGo.AddComponent<LayoutElement>();
            le.minHeight = 90;
        }
    }

    string StockSummary()
    {
        if (currentFeeder.stock.Count == 0) return "Кормушка пуста";
        var parts = new List<string>();
        foreach (var e in currentFeeder.stock)
        {
            ItemData item = ItemDatabase.Find(e.item);
            parts.Add((item != null ? item.itemName : e.item) + " ×" + e.count);
        }
        return "Внутри: " + string.Join(", ", parts);
    }

    void CreateRow(ItemData item)
    {
        var row = new GameObject("FeedRow_" + item.name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(content, false);
        row.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
        row.GetComponent<LayoutElement>().minHeight = 62;
        row.GetComponent<LayoutElement>().preferredHeight = 62;

        // Иконка
        var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(row.transform, false);
        var iconImg = iconGo.GetComponent<Image>();
        iconImg.sprite = item.icon;
        iconImg.raycastTarget = false;
        iconImg.preserveAspect = true;
        Anchor(iconGo.GetComponent<RectTransform>(), new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(8, -24), new Vector2(56, 24));

        int inInventory = CountInSlots(item);
        int inFeeder = currentFeeder.CountFeed(item);

        var nameTmp = MakeText(row.transform, "Name", item.itemName +
            "  <color=#9f9>(" + inInventory + ")</color>" +
            (inFeeder > 0 ? "  <color=#fc6>в кормушке " + inFeeder + "</color>" : ""),
            TextAlignmentOptions.Left, 19);
        Anchor(nameTmp.rectTransform, new Vector2(0, 0), new Vector2(0.62f, 1), new Vector2(64, 4), new Vector2(-4, -4));

        CreateRowButton(row.transform, "Plus1", "+1", 0.62f, 0.76f, () => Transfer(item, 1));
        CreateRowButton(row.transform, "Plus5", "+5", 0.76f, 0.89f, () => Transfer(item, 5));
        CreateRowButton(row.transform, "All", "Всё", 0.89f, 1f, () => Transfer(item, int.MaxValue));
    }

    void CreateRowButton(Transform parent, string name, string label, float xMin, float xMax, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.35f, 0.25f, 0.12f, 0.95f);
        var btn = go.GetComponent<Button>();
        var cols = btn.colors;
        cols.fadeDuration = 0.08f;
        btn.colors = cols;
        btn.onClick.AddListener(onClick);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 19;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 0.97f, 0.9f);
        tmp.raycastTarget = false;
        Anchor(labelGo.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(1, 1), new Vector2(-1, -1));

        Anchor(go.GetComponent<RectTransform>(), new Vector2(xMin, 0.5f), new Vector2(xMax, 0.5f),
            new Vector2(0, -20), new Vector2(0, 20));
    }

    // ═══════════════════════════════════════════════════════════
    // ПЕРЕНОС КОРМА
    // ═══════════════════════════════════════════════════════════
    void Transfer(ItemData item, int amount)
    {
        if (currentFeeder == null || item == null) return;

        int moved = 0;
        while (moved < amount)
        {
            if (currentFeeder.FreeSpace <= 0) break;
            InventorySlot src = FindSlotWith(item);
            if (src == null) break;

            src.quantity--;
            if (src.quantity <= 0) src.ClearSlot();
            else src.UpdateUI();

            currentFeeder.AddFeed(item, 1);
            moved++;
        }

        if (moved > 0)
        {
            HotbarManager.Instance?.NotifyActiveItemChanged();
            SaveManager.Instance?.Save();
            ActionLogUI.Show("Загружено в кормушку: " + item.itemName + " ×" + moved);
        }
        else if (currentFeeder.FreeSpace <= 0)
            ActionLogUI.Show("Кормушка заполнена (" + currentFeeder.CapacityInfo() + ")");

        Refresh();
    }

    void TakeAllBack()
    {
        if (currentFeeder == null) return;
        int taken = currentFeeder.TakeAllBack();
        if (taken > 0)
        {
            HotbarManager.Instance?.NotifyActiveItemChanged();
            SaveManager.Instance?.Save();
            ActionLogUI.Show("Забрано из кормушки: ×" + taken);
        }
        Refresh();
    }

    // ═══════════════════════════════════════════════════════════
    // ХЕЛПЕРЫ
    // ═══════════════════════════════════════════════════════════
    private static HashSet<string> feedNameCache;

    /// <summary>Является ли предмет кормом для хоть какого-то животного
    /// (кэшируется — используется и FeederStorage для быстрой загрузки).</summary>
    public static bool IsAnimalFeed(ItemData item)
    {
        if (item == null) return false;
        if (feedNameCache == null)
        {
            feedNameCache = new HashSet<string>();
            foreach (AnimalData ad in Resources.FindObjectsOfTypeAll<AnimalData>())
                if (ad != null && ad.feedItem != null)
                    feedNameCache.Add(ad.feedItem.name);
        }
        return feedNameCache.Contains(item.name);
    }
    IEnumerable<InventorySlot> AllSlots()
    {
        if (InventoryUI.Instance != null && InventoryUI.Instance.slots != null)
            foreach (var s in InventoryUI.Instance.slots)
                if (s != null) yield return s;
        if (HotbarManager.Instance != null && HotbarManager.Instance.slots != null)
            foreach (var s in HotbarManager.Instance.slots)
                if (s != null) yield return s;
    }

    InventorySlot FindSlotWith(ItemData item)
    {
        foreach (InventorySlot s in AllSlots())
            if (s != null && !s.IsEmpty() && s.currentItem == item && s.quantity > 0)
                return s;
        return null;
    }

    int CountInSlots(ItemData item)
    {
        int total = 0;
        foreach (InventorySlot s in AllSlots())
            if (s != null && !s.IsEmpty() && s.currentItem == item)
                total += s.quantity;
        return total;
    }

    TMP_Text MakeText(Transform parent, string name, string text, TextAlignmentOptions alignment, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    void CreateButton(Transform parent, string name, string label, Color bg, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = bg;
        var btn = go.GetComponent<Button>();
        var cols = btn.colors;
        cols.fadeDuration = 0.08f;
        btn.colors = cols;
        btn.onClick.AddListener(onClick);

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 21;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 0.97f, 0.9f);
        tmp.raycastTarget = false;
        Anchor(labelGo.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(2, 2), new Vector2(-2, -2));
    }

    void Anchor(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }
}
