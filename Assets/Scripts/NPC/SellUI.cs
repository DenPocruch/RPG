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
/// Строки создаются программно. Если ссылки на шапку не привязаны в инспекторе —
/// шапка (заголовок, спрос, репутация) достраивается кодом сама.
/// Вешается на объект рядом с панелью в Canvas (вечный). Открытие — BuyerNPC
/// (диалог OpenSell) или напрямую BuyerInteraction.
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

    [Header("Стиль (опционально, иначе берётся спрайт из скроллбара)")]
    public Sprite rowSprite;
    public Sprite buttonSprite;

    [Header("Позиции панели")]
    public float panelY = 47f;
    public float shiftDistance = 325f;
    public float shiftSpeed = 8f;

    [Header("Debug")]
    public bool debugAutoOpen = false;

    private bool isOpen = false;
    private Vector2 targetPos;
    private Vector2 normalPos;
    private float timerRefresh = 0f;
    private Sprite sliceSprite;         // 9-slice для строк и кнопок

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        EnsureBuiltUI();
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

        if (debugAutoOpen) Invoke(nameof(Open), 2.5f); // задержка — дать сейву загрузиться
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
        EnsureBuiltUI();
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
    // ПОСТРОЕНИЕ UI, ЕСЛИ ЧЕГО-ТО НЕТ В СЦЕНЕ
    // ═══════════════════════════════════════════════════════════
    void EnsureBuiltUI()
    {
        if (sellPanel == null)
        {
            Transform p = transform.parent != null ? transform.parent.Find("Sell Panel") : null;
            if (p == null) { var found = GameObject.Find("Sell Panel"); p = found ? found.transform : null; }
            sellPanel = p != null ? p.gameObject : null;
        }
        if (sellPanel == null) return;

        var sv = sellPanel.transform.Find("RecipeScrollView");
        if (sv != null)
        {
            StyleScrollView(sv);

            if (itemListContent == null)
                itemListContent = sv.Find("Viewport/Content");
            if (itemListContent != null) StyleContent(itemListContent);
        }

        if (sellPanel.transform.Find("TitleText") == null) BuildTitle();
        if (sellPanel.transform.Find("DemandRow") == null) BuildDemandRow();
        if (sellPanel.transform.Find("ReputationText") == null) BuildReputation();
        if (demandIcon == null)
            demandIcon = sellPanel.transform.Find("DemandRow/DemandIcon") != null
                ? sellPanel.transform.Find("DemandRow/DemandIcon").GetComponent<Image>() : null;
        if (demandText == null)
            demandText = sellPanel.transform.Find("DemandRow/DemandText") != null
                ? sellPanel.transform.Find("DemandRow/DemandText").GetComponent<TMP_Text>() : null;
        if (reputationText == null)
            reputationText = sellPanel.transform.Find("ReputationText") != null
                ? sellPanel.transform.Find("ReputationText").GetComponent<TMP_Text>() : null;

        WireCloseButton();

        // 9-slice спрайт для строк/кнопок — берём у ползунка скроллбара
        if (sliceSprite == null && rowSprite != null) sliceSprite = rowSprite;
        if (sliceSprite == null)
        {
            var sb = sellPanel.GetComponentInChildren<Scrollbar>(true);
            if (sb != null && sb.handleRect != null)
            {
                var hImg = sb.handleRect.GetComponent<Image>();
                if (hImg != null && hImg.sprite != null) sliceSprite = hImg.sprite;
            }
        }
    }

    void StyleScrollView(Transform sv)
    {
        var rt = sv as RectTransform;
        if (rt == null) rt = sv.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(20f, -14f);
        rt.offsetMax = new Vector2(-20f, -216f);

        foreach (Transform child in sv)
            if (child.name.Trim() == "Scrollbar Horizontal")
                child.gameObject.SetActive(false);

        var srect = sv.GetComponent<ScrollRect>();
        if (srect != null) srect.horizontal = false;
    }

    void StyleContent(Transform content)
    {
        var vlg = content.GetComponent<VerticalLayoutGroup>();
        if (vlg != null)
        {
            vlg.spacing = 8f;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.padding = new RectOffset(10, 10, 8, 8);
        }
        var csf = content.GetComponent<ContentSizeFitter>();
        if (csf != null) csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    void BuildTitle()
    {
        var go = NewRect("TitleText", sellPanel.transform);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "Скупщик Дрон";
        tmp.fontSize = 32;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = (TextAlignmentOptions)TextAnchor.UpperCenter;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.color = HexColor("#5A3A1E");
        tmp.raycastTarget = false;
        var sh = go.AddComponent<Shadow>();
        sh.effectColor = new Color(1f, 0.95f, 0.85f, 0.7f);
        sh.effectDistance = new Vector2(1.5f, -1.5f);
        Anchor(go.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(90f, -58f), new Vector2(-90f, -12f));
    }

    void BuildDemandRow()
    {
        var row = NewRect("DemandRow", sellPanel.transform);
        Anchor(row.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(0f, -142f), new Vector2(0f, -68f));

        // Тёмная подложка под иконку
        var bg = NewRect("DemandIconBg", row.transform);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.09f, 0.06f, 0.04f, 0.85f);
        ApplySlice(bgImg);
        PlaceSquare(bg.GetComponent<RectTransform>(), 26f, 66f);

        // Иконка культуры спроса (спрайт ставит RefreshDemandHeader)
        var icon = NewRect("DemandIcon", row.transform);
        var iconImg = icon.AddComponent<Image>();
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        PlaceSquare(icon.GetComponent<RectTransform>(), 29f, 60f);

        var txt = NewRect("DemandText", row.transform);
        var tmp = txt.AddComponent<TextMeshProUGUI>();
        tmp.text = "Спрос дня: …";
        tmp.fontSize = 21;
        tmp.alignment = (TextAlignmentOptions)TextAnchor.MiddleLeft;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.color = HexColor("#4A3020");
        tmp.raycastTarget = false;
        Anchor(txt.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
            new Vector2(104f, 4f), new Vector2(-14f, -4f));
    }

    void BuildReputation()
    {
        var go = NewRect("ReputationText", sellPanel.transform);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "Репутация: …";
        tmp.fontSize = 20;
        tmp.alignment = (TextAlignmentOptions)TextAnchor.UpperCenter;
        tmp.color = HexColor("#4A3020");
        tmp.raycastTarget = false;
        Anchor(go.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(1, 1),
            new Vector2(24f, -208f), new Vector2(-24f, -146f));
    }

    void PlaceSquare(RectTransform rt, float x, float size)
    {
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(size, size);
    }

    void WireCloseButton()
    {
        Transform t = null;
        foreach (Transform ch in sellPanel.transform)
            if (ch.name.Trim() == "CloseButton") { t = ch; break; }
        if (t == null) return;

        var btn = t.GetComponent<Button>();
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(Close);
    }

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
        // Подложка видна всегда, даже без спроса
        var bg = demandIcon != null && demandIcon.transform.parent != null
            ? demandIcon.transform.parent.Find("DemandIconBg") : null;
        if (bg != null) bg.gameObject.SetActive(true);

        if (demandText != null)
        {
            if (demandItem != null)
            {
                double hours = BuyerManager.Instance.GetDemandSecondsLeft() / 3600.0;
                demandText.text = "Спрос дня: <color=#D89000>" + demandItem.itemName + "</color> ×2  (" +
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

        string rep = "Репутация: <color=#D89000>" + bm.GetReputationName() + "</color> (+" +
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

        if (counts.Count == 0)
        {
            var empty = NewRect("EmptyHint", itemListContent);
            var tmp = empty.AddComponent<TextMeshProUGUI>();
            tmp.text = "Пока нечего продать.\nСкупщик покупает урожай (не семена).\nВырасти урожай и возвращайся!";
            tmp.fontSize = 21;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.35f, 0.24f, 0.14f, 0.9f);
            tmp.raycastTarget = false;
            var le = empty.AddComponent<LayoutElement>();
            le.minHeight = 140f;
            le.preferredHeight = 140f;
            return;
        }

        // Сортировка: по цене убывание (дорогой урожай сверху)
        foreach (var kvp in counts.OrderByDescending(k => BuyerManager.Instance.GetUnitPrice(k.Key)))
        {
            CreateRow(kvp.Key, kvp.Value);
        }
    }

    void CreateRow(ItemData item, int count)
    {
        GameObject row = new GameObject("SellRow_" + item.name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(itemListContent, false);

        var rowImg = row.GetComponent<Image>();
        rowImg.color = new Color(0.23f, 0.15f, 0.09f, 0.97f);
        ApplySlice(rowImg);

        var le = row.GetComponent<LayoutElement>();
        le.minHeight = 76f;
        le.preferredHeight = 76f;

        // Тёмный слот-рамка под иконку
        var slot = NewRect("IconSlot", row.transform);
        var slotImg = slot.AddComponent<Image>();
        slotImg.color = new Color(0.1f, 0.065f, 0.04f, 0.95f);
        ApplySlice(slotImg);
        var slotRt = slot.GetComponent<RectTransform>();
        slotRt.anchorMin = new Vector2(0f, 0.5f);
        slotRt.anchorMax = new Vector2(0f, 0.5f);
        slotRt.pivot = new Vector2(0f, 0.5f);
        slotRt.anchoredPosition = new Vector2(9f, 0f);
        slotRt.sizeDelta = new Vector2(58f, 58f);

        // Иконка
        GameObject iconGo = NewRect("Icon", row.transform);
        var iconImg = iconGo.AddComponent<Image>();
        iconImg.sprite = item.icon;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        var iconRt = iconGo.GetComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0f, 0.5f);
        iconRt.anchorMax = new Vector2(0f, 0.5f);
        iconRt.pivot = new Vector2(0f, 0.5f);
        iconRt.anchoredPosition = new Vector2(11f, 0f);
        iconRt.sizeDelta = new Vector2(54f, 54f);

        // Название (жирное, цвет по качеству) + количество мелким
        var nameText = MakeText(row.transform, "Name",
            "<b>" + Colored(item.itemName, QualityHex(item.name)) + "</b> <size=17><alpha=#B0>×" + count + "</size>",
            (TextAlignmentOptions)TextAnchor.MiddleLeft, 22);
        Anchor(nameText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(78f, 6f), new Vector2(-330f, -6f));

        // Цена: крупное число + мелкое «/шт», бейдж спроса
        int unit = BuyerManager.Instance.GetUnitPrice(item);
        bool inDemand = BuyerManager.Instance.IsInDemand(item);
        var priceText = MakeText(row.transform, "Price",
            "<b><color=#FFD54A>" + unit + "g</color></b><size=15><alpha=#CC>/шт</size>" +
            (inDemand ? " <b><color=#FFD54A>×2</color></b>" : ""),
            (TextAlignmentOptions)TextAnchor.MiddleRight, 22);
        Anchor(priceText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(-322f, 4f), new Vector2(-180f, -4f));

        // Кнопки прижаты к ПРАВОМУ краю строки: xMin/xMax — отступы от правого края
        CreateSellButton(row.transform, "Sell1", "×1", -166f, -94f,
            new Color(0.42f, 0.27f, 0.12f, 1f), () => Sell(item, 1));
        CreateSellButton(row.transform, "SellAll", "Всё", -88f, -8f,
            new Color(0.76f, 0.52f, 0.16f, 1f), () => Sell(item, count));

        // Клик по строке — тоже продаёт 1 (удобно на телефоне)
        var btn = row.AddComponent<Button>();
        btn.targetGraphic = rowImg;
        var cols = btn.colors;
        cols.pressedColor = new Color(1f, 1f, 1f, 0.6f);
        cols.fadeDuration = 0.08f;
        btn.colors = cols;
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

    GameObject NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    TMP_Text MakeText(Transform parent, string name, string text, TextAlignmentOptions alignment, float fontSize)
    {
        var go = NewRect(name, parent);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = Color.white;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        return tmp;
    }

    void CreateSellButton(Transform parent, string name, string label,
        float xMin, float xMax, Color bgColor, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = bgColor;
        ApplySlice(img);

        var btn = go.GetComponent<Button>();
        var cols = btn.colors;
        cols.fadeDuration = 0.08f;
        btn.colors = cols;
        btn.onClick.AddListener(onClick);

        // Текст — дочерним объектом: на GO с Image нельзя второй Graphic
        var labelGo = NewRect("Label", go.transform);
        var tmp = labelGo.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 21;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(1f, 0.97f, 0.9f, 1f);
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        Anchor(labelGo.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
            new Vector2(2f, 2f), new Vector2(-2f, -2f));

        Anchor(go.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(xMin, 8f), new Vector2(xMax, -8f));
    }

    void ApplySlice(Image img)
    {
        var s = sliceSprite;
        var parentName = img.transform.parent != null ? img.transform.parent.name : "";
        if (parentName.StartsWith("SellRow") && buttonSprite != null) s = buttonSprite;
        if (s == null) return;
        img.sprite = s;
        img.type = Image.Type.Sliced;
    }

    void Anchor(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    string QualityHex(string assetName)
    {
        if (assetName.EndsWith(" Silver")) return "#CFDBE4";
        if (assetName.EndsWith(" Gold")) return "#FFD34D";
        if (assetName.EndsWith(" Purple")) return "#DCA9FF";
        return "#FFFFFF";
    }

    string Colored(string s, string hex) => "<color=" + hex + ">" + s + "</color>";

    Color HexColor(string hex)
    {
        Color c = Color.white;
        ColorUtility.TryParseHtmlString(hex, out c);
        return c;
    }
}
