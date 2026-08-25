using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Окно скупщика урожая. Показывает:
///  - Спрос дня: иконка + название культуры (×2 цена) + таймер до смены
///  - Репутацию: уровень, бонус, прогресс до следующего
///  - Список урожая игрока (инвентарь + хотбар) с итоговой ценой за 1 шт
///    и кнопками «Продать 1» / «Продать всё»
///
/// Строки создаются программно — из префабов нужен только слот-шаблон не требуется.
/// Вешается на панель в Canvas (вечный). Открытие — BuyerNPC (диалог OpenSell)
/// или напрямую BuyerInteraction.
/// </summary>
public class SellUI : MonoBehaviour
{
    public static SellUI Instance;

    [Header("Панель")]
    public GameObject sellPanel;

    [Header("Спрос дня")]
    public Image demandIcon;
    public TMP_Text demandText;
    public TMP_Text demandTimerText;

    [Header("Репутация")]
    public TMP_Text reputationText;

    [Header("Список товаров")]
    public Transform itemListContent;   // с VerticalLayoutGroup

    [Header("Позиции панели")]
    public float panelY = 47f;
    public float shiftDistance = 325f;
    public float shiftSpeed = 8f;

    private bool isOpen = false;
    private Vector2 targetPos;
    private Vector2 normalPos;
    private float timerRefresh = 0f;

    // Продаваемая строка
    private class SellRow
    {
        public ItemData item;
        public int count;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (sellPanel != null) sellPanel.SetActive(false);
    }

    void Start()
    {
        normalPos = new Vector2(0, panelY);
        if (sellPanel != null)
        {
            var rt = sellPanel.GetComponent<RectTransform>();
            if (rt != null) rt.anchoredPosition = normalPos;
        }
        targetPos = normalPos;
    }

    void Update()
    {
        if (!isOpen) return;

        if (sellPanel != null)
        {
            var rt = sellPanel.GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, targetPos, Time.deltaTime * shiftSpeed);
        }

        // Таймер спроса обновляем раз в секунду
        timerRefresh -= Time.deltaTime;
        if (timerRefresh <= 0f)
        {
            timerRefresh = 1f;
            RefreshDemandHeader();
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ОТКРЫТИЕ / ЗАКРЫТИЕ
    // ═══════════════════════════════════════════════════════════
    public void Open()
    {
        sellPanel.SetActive(true);
        isOpen = true;

        InventoryPanelMover.Instance?.SetOffsetX(-shiftDistance);
        targetPos = new Vector2(shiftDistance, panelY);

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.OpenInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        RefreshDemandHeader();
        RefreshReputation();
        RebuildList();
    }

    public void Close()
    {
        sellPanel.SetActive(false);
        isOpen = false;

        InventoryPanelMover.Instance?.ResetPosition();
        targetPos = normalPos;

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.CloseInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;
    }

    public bool IsOpen() => isOpen;

    // ═══════════════════════════════════════════════════════════
    // ШАПКА: СПРОС + РЕПУТАЦИЯ
    // ═══════════════════════════════════════════════════════════
    void RefreshDemandHeader()
    {
        if (BuyerManager.Instance == null) return;

        string demand = BuyerManager.Instance.GetDemandCrop();
        ItemData demandItem = string.IsNullOrEmpty(demand) ? null : ItemDatabase.Find(demand);

        if (demandIcon != null)
        {
            demandIcon.gameObject.SetActive(demandItem != null);
            if (demandItem != null) demandIcon.sprite = demandItem.icon;
        }

        if (demandText != null)
        {
            if (demandItem != null)
            {
                double hours = BuyerManager.Instance.GetDemandSecondsLeft() / 3600.0;
                demandText.text = "Спрос дня: <color=#FFD700>" + demandItem.itemName + "</color> ×2  (" +
                    (hours >= 1 ? Mathf.CeilToInt((float)hours) + " ч" : Mathf.CeilToInt((float)(hours * 60)) + " мин") + ")";
            }
            else
            {
                demandText.text = "Спрос дня: нет";
            }
        }
        if (demandTimerText != null) demandTimerText.gameObject.SetActive(false);
    }

    void RefreshReputation()
    {
        if (reputationText == null || BuyerManager.Instance == null) return;

        var bm = BuyerManager.Instance;
        int level = bm.GetReputationLevel();
        int toNext = bm.GetReputationToNext();

        string rep = "Репутация: <color=#FFD700>" + bm.GetReputationName() + "</color> (+" +
            bm.GetReputationBonus() + "% к ценам)";
        if (toNext > 0) rep += "\nДо следующего уровня: продать на " + toNext + "g";
        else rep += "\nМаксимальный уровень!";

        reputationText.text = rep;
    }

    // ═══════════════════════════════════════════════════════════
    // СПИСОК ПРОДАЖИ
    // ═══════════════════════════════════════════════════════════
    void RebuildList()
    {
        if (itemListContent == null || BuyerManager.Instance == null) return;

        // Чистим старые строки
        foreach (Transform child in itemListContent)
            Destroy(child.gameObject);

        // Собираем продаваемое из инвентаря + хотбара
        var counts = new Dictionary<ItemData, int>();
        foreach (InventorySlot s in AllSlots())
        {
            if (s.IsEmpty() || !BuyerManager.Instance.IsSellable(s.currentItem)) continue;
            if (!counts.ContainsKey(s.currentItem)) counts[s.currentItem] = 0;
            counts[s.currentItem] += s.quantity;
        }

        // Сортировка: по цене убывание (дорогой урожай сверху)
        foreach (var kvp in counts.OrderByDescending(k => BuyerManager.Instance.GetUnitPrice(k.Key)))
        {
            CreateRow(kvp.Key, kvp.Value);
        }
    }

    void CreateRow(ItemData item, int count)
    {
        GameObject row = new GameObject("SellRow_" + item.name, typeof(RectTransform), typeof(Image));
        row.transform.SetParent(itemListContent, false);
        row.GetComponent<Image>().color = new Color(0.16f, 0.1f, 0.06f, 0.9f);

        var le = row.AddComponent<LayoutElement>();
        le.minHeight = 56f;
        le.preferredHeight = 56f;

        // Иконка
        GameObject iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGo.transform.SetParent(row.transform, false);
        var iconImg = iconGo.GetComponent<Image>();
        iconImg.sprite = item.icon;
        Stretch(iconGo.GetComponent<RectTransform>(), 4, 4, 48, 48);

        // Название + количество
        var nameText = CreateText(row.transform, "Name", item.itemName + " ×" + count,
            TextAnchor.MiddleLeft, new Vector2(60, 8), new Vector2(-170, -8));

        // Цена за штуку (с множителями)
        int unit = BuyerManager.Instance.GetUnitPrice(item);
        bool inDemand = BuyerManager.Instance.IsInDemand(item);
        string priceColor = inDemand ? "#FFD700" : "#7CFC7C";
        var priceText = CreateText(row.transform, "Price", unit + "g/шт" + (inDemand ? " 🔥" : ""),
            TextAnchor.MiddleRight, new Vector2(-260, 8), new Vector2(-170, -8));
        priceText.color = HexColor(priceColor);

        // Кнопка «Продать 1»
        CreateSellButton(row.transform, "Sell1", "×1", new Vector2(-160, 8), new Vector2(-90, -8),
            () => Sell(item, 1));

        // Кнопка «Продать всё»
        CreateSellButton(row.transform, "SellAll", "Всё", new Vector2(-84, 8), new Vector2(-8, -8),
            () => Sell(item, count));

        // Клик по строке — тоже продаёт 1 (удобно на телефоне)
        var btn = row.AddComponent<Button>();
        var captured = item;
        btn.onClick.AddListener(() => Sell(captured, 1));
    }

    void Sell(ItemData item, int count)
    {
        // Сколько реально есть сейчас (окно могло устареть)
        int have = 0;
        foreach (InventorySlot s in AllSlots())
            if (!s.IsEmpty() && s.currentItem == item) have += s.quantity;

        int sellCount = Mathf.Min(count, have);
        if (sellCount <= 0) return;

        int gold = BuyerManager.Instance.Sell(item, sellCount);
        if (gold <= 0) return;

        // Списываем из инвентаря и хотбара
        int left = sellCount;
        foreach (InventorySlot s in AllSlots())
        {
            if (left <= 0) break;
            if (s.IsEmpty() || s.currentItem != item) continue;
            int takeNow = Mathf.Min(s.quantity, left);
            s.quantity -= takeNow;
            left -= takeNow;
            if (s.quantity <= 0) s.ClearSlot();
            else s.UpdateUI();
        }
        HotbarManager.Instance?.NotifyActiveItemChanged();

        Debug.Log("[Скупщик] Продано: " + item.itemName + " ×" + sellCount + " за " + gold + "g");

        RefreshReputation();
        RebuildList();
    }

    // ═══════════════════════════════════════════════════════════
    // ХЕЛПЕРЫ UI
    // ═══════════════════════════════════════════════════════════
    IEnumerable<InventorySlot> AllSlots()
    {
        if (InventoryUI.Instance != null)
            foreach (InventorySlot s in InventoryUI.Instance.slots)
                if (s != null) yield return s;
        if (HotbarManager.Instance != null)
            foreach (InventorySlot s in HotbarManager.Instance.slots)
                if (s != null) yield return s;
    }

    TMP_Text CreateText(Transform parent, string name, string text, TextAnchor anchor,
        Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 18;
        tmp.alignment = (TextAlignmentOptions)anchor;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        Stretch(go.GetComponent<RectTransform>(), offsetMin.x, offsetMin.y, offsetMax.x, offsetMax.y);
        return tmp;
    }

    void CreateSellButton(Transform parent, string name, string label,
        Vector2 offsetMin, Vector2 offsetMax, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.55f, 0.35f, 0.15f, 1f);

        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        Stretch(go.GetComponent<RectTransform>(), offsetMin.x, offsetMin.y, offsetMax.x, offsetMax.y);
    }

    void Stretch(RectTransform rt, float left, float bottom, float right, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(right, top);
    }

    Color HexColor(string hex)
    {
        Color c = Color.white;
        ColorUtility.TryParseHtmlString(hex, out c);
        return c;
    }
}
