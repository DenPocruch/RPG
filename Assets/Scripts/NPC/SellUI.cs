using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Окно скупщика урожая (референс: деревянная панель с пергаментом).
///  - Шапка: портрет в золотой рамке, имя, подзаголовок
///  - Блок «Спрос дня»: иконка культуры + название + таймер до смены
///  - Блок «Репутация»: уровень + зелёный прогресс-бар до следующего уровня
///  - Список урожая: иконка в тёмном слоте, имя ×кол-во, цена с монеткой,
///    кнопки «×1» / «Всё», клик по строке = продать 1
///
/// Весь UI достраивается кодом (EnsureBuiltUI) — ссылки в инспекторе не нужны.
/// Спрайты портрета/монетки — поля portraitSprite/coinSprite (ассеты).
/// Открытие — диалог BuyerNPC (действие OpenSell) → SellUI.Open().
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
    public Transform itemListContent;

    [Header("Стиль (ассеты, опционально)")]
    public Sprite portraitSprite;   // портрет скупщика в шапку
    public Sprite coinSprite;       // иконка монетки у цены
    public Sprite rowSprite;        // фон строки (иначе берётся из скроллбара)
    public Sprite buttonSprite;     // фон кнопок строк

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

    private Image repFill;              // заполняшка прогресс-бара репутации
    private TMP_Text repBarText;        // «1200 / 2000» на баре
    private TMP_Text repLevelText;      // название уровня

    // Режим Морека: та же панель, но продаём рыбу по ценам FishData ×1.5
    // (без спроса дня и репутации). Включается через OpenFish().
    private bool fishMode = false;
    private Dictionary<ItemData, int> fishPrices = new Dictionary<ItemData, int>();

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

        // Панель ищется и привязывается ОДИН РАЗ при старте (как у повара)
        EnsureBuiltUI();

        // Панель могла сохраниться в сцене АКТИВНОЙ (копия из Play-режима) — гасим
        if (sellPanel != null) sellPanel.SetActive(false);

        if (debugAutoOpen) Invoke(nameof(Open), 2.5f);
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
        if (sellPanel == null) EnsureBuiltUI();
        if (sellPanel == null)
        {
            Debug.LogError("[SellUI] Открытие невозможно — панель 'Sell Panel' не найдена в Canvas!");
            return;
        }
        fishMode = false;
        ApplySellerStyle();
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

    /// <summary>Режим Морека: продажа рыбы (цены FishData ×1.5, без спроса/репутации).</summary>
    public void OpenFish()
    {
        if (sellPanel == null) EnsureBuiltUI();
        if (sellPanel == null)
        {
            Debug.LogError("[SellUI] Открытие невозможно — панель 'Sell Panel' не найдена в Canvas!");
            return;
        }
        fishMode = true;
        fishPrices = LoadFishPrices();
        ApplySellerStyle();
        sellPanel.SetActive(true);
        isOpen = true;

        InventoryPanelMover.Instance?.SetOffsetX(-shiftDistance);
        targetPos = new Vector2(shiftDistance, panelY);

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.OpenInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        RebuildList();
    }

    // Шапка под продавца: имя/подзаголовок + спрос/репутация только у Дрона
    void ApplySellerStyle()
    {
        var t = sellPanel.transform;
        var title = t.Find("TitleText");
        if (title != null)
        {
            var tmp = title.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = fishMode ? "Морек" : "Скупщик Дрон";
        }
        var sub = t.Find("SubtitleText");
        if (sub != null)
        {
            var tmp = sub.GetComponent<TMP_Text>();
            if (tmp != null) tmp.text = fishMode ? "Свежая рыба — беру всё!" : "Я покупаю только лучшее!";
        }
        var demand = t.Find("DemandBox");
        if (demand != null) demand.gameObject.SetActive(!fishMode);
        var rep = t.Find("RepBox");
        if (rep != null) rep.gameObject.SetActive(!fishMode);
    }

    Dictionary<ItemData, int> LoadFishPrices()
    {
        var d = new Dictionary<ItemData, int>();
        foreach (FishData f in FishData.LoadAll())
            if (f != null && f.fishItem != null && !d.ContainsKey(f.fishItem))
                d[f.fishItem] = Mathf.Max(1, Mathf.RoundToInt(f.price * 1.5f));
        return d;
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
    // ПОСТРОЕНИЕ UI
    // ═══════════════════════════════════════════════════════════
    // Перепривязка полей к детям существующей панели (после copy-paste из Play)
    void RewireFromExisting()
    {
        var t = sellPanel.transform;

        var demandBox = t.Find("DemandBox");
        if (demandBox != null)
        {
            var icon = demandBox.Find("DemandIconFrame/DemandIcon");
            if (icon != null && demandIcon == null) demandIcon = icon.GetComponent<Image>();
            var dt = demandBox.Find("DemandText");
            if (dt != null && demandText == null) demandText = dt.GetComponent<TMP_Text>();
        }

        var repBox = t.Find("RepBox");
        if (repBox != null)
        {
            var lvl = repBox.Find("RepLevelText");
            if (lvl != null)
            {
                if (reputationText == null) reputationText = lvl.GetComponent<TMP_Text>();
                repLevelText = lvl.GetComponent<TMP_Text>();
            }
            var bar = repBox.Find("RepBar");
            if (bar != null)
            {
                var fill = bar.Find("Fill");
                if (fill != null) repFill = fill.GetComponent<Image>();
                var barTxt = bar.Find("BarText");
                if (barTxt != null) repBarText = barTxt.GetComponent<TMP_Text>();
            }
        }
    }
    void EnsureBuiltUI()
    {
        if (sellPanel == null)
        {
            // 1) прямой ребёнок родителя
            Transform p = transform.parent != null ? transform.parent.Find("Sell Panel") : null;
            // 2) рекурсивно по всему Canvas (находит и НЕАКТИВные копии).
            // Имя сравниваем обрезанным и без учёта регистра — от лишних пробелов.
            if (p == null)
            {
                var canvas = GetComponentInParent<Canvas>();
                Transform rootT = canvas != null ? canvas.transform : (transform.parent != null ? transform.parent.root : null);
                if (rootT != null)
                {
                    foreach (Transform t in rootT.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name.Trim().Equals("sell panel", System.StringComparison.OrdinalIgnoreCase))
                        { p = t; break; }
                    }
                    if (p == null)
                    {
                        var candidates = new System.Text.StringBuilder();
                        foreach (Transform t in rootT.GetComponentsInChildren<Transform>(true))
                            if (t.name.ToLower().Contains("sell") || t.name.ToLower().Contains("панел"))
                                candidates.Append(t.name).Append(" | ");
                        Debug.Log("[SellUI] Кандидаты с 'sell/панел': " + (candidates.Length > 0 ? candidates.ToString() : "НИ ОДНОГО. Дети Canvas: " + rootT.name));
                    }
                }
            }
            // 3) последний шанс — активная по имени
            if (p == null) { var found = GameObject.Find("Sell Panel"); p = found ? found.transform : null; }
            sellPanel = p != null ? p.gameObject : null;
            Debug.Log("[SellUI] Панель " + (sellPanel != null ? "найдена: " + sellPanel.name : "НЕ НАЙДЕНА"));
        }
        if (sellPanel == null) return;

        // Панель могли заменить копией из Play-режима — перепривязываем поля
        RewireFromExisting();

        // 9-slice спрайт — берём у ползунка скроллбара
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

        BuildHeader();
        BuildDemandBox();
        BuildReputationBox();

        var sv = sellPanel.transform.Find("RecipeScrollView");
        if (sv != null)
        {
            StyleScrollView(sv);
            if (itemListContent == null)
                itemListContent = sv.Find("Viewport/Content");
            if (itemListContent != null) StyleContent(itemListContent);
        }

        WireCloseButton();
    }

    void BuildHeader()
    {
        var t = sellPanel.transform;

        // Портрет в золотой рамке
        if (t.Find("PortraitFrame") == null)
        {
            var frame = NewRect("PortraitFrame", t);
            var frameImg = frame.AddComponent<Image>();
            frameImg.color = new Color(0.83f, 0.62f, 0.28f, 1f);
            ApplySlice(frameImg);

            var ph = NewRect("Portrait", frame.transform);
            var pImg = ph.AddComponent<Image>();
            pImg.preserveAspect = true;
            pImg.raycastTarget = false;
            if (portraitSprite != null) pImg.sprite = portraitSprite;
            Anchor(ph.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 1f),
                new Vector2(5f, 5f), new Vector2(-5f, -5f));

            Anchor(frame.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(18f, -102f), new Vector2(100f, -20f));
        }

        // Имя
        if (t.Find("TitleText") == null)
        {
            var go = NewRect("TitleText", t);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "Скупщик Дрон";
            tmp.fontSize = 34;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = (TextAlignmentOptions)TextAnchor.MiddleLeft;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.color = HexColor("#3E2A16");
            tmp.raycastTarget = false;
            Anchor(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(112f, -58f), new Vector2(-96f, -18f));
        }

        // Подзаголовок
        if (t.Find("SubtitleText") == null)
        {
            var go = NewRect("SubtitleText", t);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = "Я покупаю только лучшее!";
            tmp.fontSize = 17;
            tmp.alignment = (TextAlignmentOptions)TextAnchor.MiddleLeft;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.color = HexColor("#8A6A48");
            tmp.raycastTarget = false;
            Anchor(go.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(113f, -82f), new Vector2(-96f, -60f));
        }

        // Старые элементы прежних версий не трогаем — их нет в новых сценах
        if (demandIcon == null)
            demandIcon = t.Find("DemandBox/DemandIcon") != null
                ? t.Find("DemandBox/DemandIcon").GetComponent<Image>() : null;
        if (demandText == null)
            demandText = t.Find("DemandBox/DemandText") != null
                ? t.Find("DemandBox/DemandText").GetComponent<TMP_Text>() : null;
        if (reputationText == null)
            reputationText = t.Find("RepBox/RepLevelText") != null
                ? t.Find("RepBox/RepLevelText").GetComponent<TMP_Text>() : null;
    }

    void BuildDemandBox()
    {
        var t = sellPanel.transform;
        if (t.Find("DemandBox") != null) return;

        var box = NewRect("DemandBox", t);
        var boxImg = box.AddComponent<Image>();
        boxImg.color = new Color(0.9f, 0.83f, 0.68f, 0.95f);
        ApplySlice(boxImg);
        Anchor(box.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0.56f, 1f),
            new Vector2(16f, -184f), new Vector2(-6f, -112f));

        var label = MakeText(box.transform, "Label", "Спрос дня:",
            (TextAlignmentOptions)TextAnchor.MiddleLeft, 17);
        label.color = HexColor("#6B5138");
        Anchor(label.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f),
            new Vector2(12f, 4f), new Vector2(-96f, -4f));

        // Золотая рамка + иконка культуры
        var frame = NewRect("DemandIconFrame", box.transform);
        var frameImg = frame.AddComponent<Image>();
        frameImg.color = new Color(0.83f, 0.62f, 0.28f, 1f);
        ApplySlice(frameImg);
        var frameRt = frame.GetComponent<RectTransform>();
        frameRt.anchorMin = new Vector2(0f, 0.5f);
        frameRt.anchorMax = new Vector2(0f, 0.5f);
        frameRt.pivot = new Vector2(0f, 0.5f);
        frameRt.anchoredPosition = new Vector2(104f, 0f);
        frameRt.sizeDelta = new Vector2(52f, 52f);

        var icon = NewRect("DemandIcon", frame.transform);
        var iconImg = icon.AddComponent<Image>();
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        if (demandIcon == null) demandIcon = iconImg;
        Anchor(icon.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(4f, 4f), new Vector2(-4f, -4f));

        var txt = NewRect("DemandText", box.transform);
        var tmp = txt.AddComponent<TextMeshProUGUI>();
        tmp.text = "—";
        tmp.fontSize = 20;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = (TextAlignmentOptions)TextAnchor.MiddleLeft;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.color = HexColor("#3E2A16");
        tmp.raycastTarget = false;
        if (demandText == null) demandText = tmp;
        Anchor(txt.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(166f, 6f), new Vector2(-10f, -6f));
    }

    void BuildReputationBox()
    {
        var t = sellPanel.transform;
        if (t.Find("RepBox") != null) return;

        var box = NewRect("RepBox", t);
        var boxImg = box.AddComponent<Image>();
        boxImg.color = new Color(0.9f, 0.83f, 0.68f, 0.95f);
        ApplySlice(boxImg);
        Anchor(box.GetComponent<RectTransform>(), new Vector2(0.56f, 1f), new Vector2(1f, 1f),
            new Vector2(6f, -184f), new Vector2(-16f, -112f));

        var label = MakeText(box.transform, "Label", "Репутация:",
            (TextAlignmentOptions)TextAnchor.MiddleLeft, 17);
        label.color = HexColor("#6B5138");
        Anchor(label.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(12f, 4f), new Vector2(-10f, -30f));

        var level = MakeText(box.transform, "RepLevelText", "Новичок",
            (TextAlignmentOptions)TextAnchor.MiddleRight, 18);
        level.fontStyle = FontStyles.Bold;
        level.color = HexColor("#3E2A16");
        if (reputationText == null) reputationText = level;
        Anchor(level.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(120f, 4f), new Vector2(-10f, -30f));

        // Прогресс-бар
        var bar = NewRect("RepBar", box.transform);
        var barImg = bar.AddComponent<Image>();
        barImg.color = new Color(0.22f, 0.17f, 0.12f, 1f);
        ApplySlice(barImg);
        Anchor(bar.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(12f, 10f), new Vector2(-12f, -34f));

        var fill = NewRect("Fill", bar.transform);
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.42f, 0.72f, 0.28f, 1f);
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillAmount = 0f;
        repFill = fillImg;
        Anchor(fill.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(3f, 3f), new Vector2(-3f, -3f));

        var barTxt = MakeText(bar.transform, "BarText", "",
            (TextAlignmentOptions)TextAnchor.MiddleCenter, 14);
        barTxt.fontStyle = FontStyles.Bold;
        barTxt.color = Color.white;
        repBarText = barTxt;
        Anchor(barTxt.rectTransform, Vector2.zero, Vector2.one,
            new Vector2(4f, 2f), new Vector2(-4f, -2f));
    }

    void StyleScrollView(Transform sv)
    {
        var rt = sv as RectTransform;
        if (rt == null) rt = sv.GetComponent<RectTransform>();
        if (rt == null) return;

        foreach (Transform child in sv)
            if (child.name.Trim() == "Scrollbar Horizontal" ||
                child.name.Trim() == "Scrollbar Vertical")
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
            vlg.padding = new RectOffset(8, 8, 6, 8);
        }
        var csf = content.GetComponent<ContentSizeFitter>();
        if (csf != null) csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
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
            demandIcon.gameObject.SetActive(demandItem != null || demandIcon.transform.parent.name != "DemandIconFrame");
            if (demandItem != null) demandIcon.sprite = demandItem.icon;
        }

        if (demandText != null)
        {
            if (demandItem != null)
            {
                double s = BuyerManager.Instance.GetDemandSecondsLeft();
                int h = (int)(s / 3600.0);
                int m = (int)((s % 3600.0) / 60.0);
                string timer = h > 0 ? h + "ч " + m + "м" : m + "м";
                demandText.text = "<color=#2E6B8A>" + demandItem.itemName + "</color> <color=#D89000>×2</color>\n" +
                    "<size=15><color=#8A6A48>смена через " + timer + "</color></size>";
            }
            else
            {
                demandText.text = "<size=17><color=#8A6A48>сейчас нет</color></size>";
            }
        }
        if (demandTimerText != null) demandTimerText.gameObject.SetActive(false);
    }

    void RefreshReputation()
    {
        if (BuyerManager.Instance == null) return;
        var bm = BuyerManager.Instance;

        int level = bm.GetReputationLevel();
        if (reputationText != null) reputationText.text = bm.GetReputationName();

        float rep = bm.GetReputation();
        var thresholds = bm.reputationThresholds;
        float frac = 1f;
        string barStr = "МАКС";
        if (level + 1 < thresholds.Length)
        {
            float prev = thresholds[level];
            float next = thresholds[level + 1];
            frac = Mathf.Clamp01((rep - prev) / Mathf.Max(1f, next - prev));
            barStr = (int)rep + " / " + next;
        }

        if (repFill != null) repFill.fillAmount = frac;
        if (repBarText != null) repBarText.text = barStr;
    }

    // ═══════════════════════════════════════════════════════════
    // СПИСОК ПРОДАЖИ
    // ═══════════════════════════════════════════════════════════
    // Рыбный режим: строки ПО СЛОТАМ (у каждой рыбы свой вес и цена)
    private Dictionary<ItemData, FishData> fishByItem = new Dictionary<ItemData, FishData>();

    void RebuildFishList()
    {
        fishByItem.Clear();
        foreach (FishData f in FishData.LoadAll())
            if (f != null && f.fishItem != null && !fishByItem.ContainsKey(f.fishItem))
                fishByItem[f.fishItem] = f;

        var rows = new List<System.Tuple<InventorySlot, FishData, int>>();
        foreach (InventorySlot s in AllSlots())
        {
            if (s.IsEmpty()) continue;
            if (!fishByItem.TryGetValue(s.currentItem, out FishData f)) continue;
            rows.Add(System.Tuple.Create(s, f, FishSlotPrice(f, s)));
        }

        if (rows.Count == 0)
        {
            var empty = NewRect("EmptyHint", itemListContent);
            var tmp = empty.AddComponent<TextMeshProUGUI>();
            tmp.text = "Пока нечего продать.\nУдочку в руки — и на пляж!";
            tmp.fontSize = 21;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.35f, 0.24f, 0.14f, 0.9f);
            tmp.raycastTarget = false;
            var le = empty.AddComponent<LayoutElement>();
            le.minHeight = 140f;
            le.preferredHeight = 140f;
            return;
        }

        rows.Sort((a, b) => b.Item3.CompareTo(a.Item3));
        foreach (var r in rows)
            CreateFishRow(r.Item1, r.Item2, r.Item3);
    }

    /// <summary>Цена слота с рыбой: за кг по весу, старым стакам без веса — по штукам.</summary>
    int FishSlotPrice(FishData f, InventorySlot s)
    {
        if (s.fishWeightKg > 0f)
            return Mathf.Max(1, Mathf.RoundToInt(f.price * s.fishWeightKg * 1.5f));
        return Mathf.Max(1, Mathf.RoundToInt(f.price * 1.5f)) * Mathf.Max(1, s.quantity);
    }

    void CreateFishRow(InventorySlot slot, FishData fish, int price)
    {
        ItemData item = slot.currentItem;
        string sub = slot.fishWeightKg > 0f
            ? FishData.FormatWeight(slot.fishWeightKg)
            : "×" + slot.quantity;
        GameObject row = new GameObject("SellRow_" + item.name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(itemListContent, false);

        var rowImg = row.GetComponent<Image>();
        rowImg.color = new Color(0.25f, 0.17f, 0.11f, 0.98f);
        ApplySlice(rowImg);

        var le = row.GetComponent<LayoutElement>();
        le.minHeight = 72f;
        le.preferredHeight = 72f;

        var slotBg = NewRect("IconSlot", row.transform);
        var slotBgImg = slotBg.AddComponent<Image>();
        slotBgImg.color = new Color(0.55f, 0.42f, 0.24f, 1f);
        ApplySlice(slotBgImg);
        var slotRt = slotBg.GetComponent<RectTransform>();
        slotRt.anchorMin = new Vector2(0f, 0.5f);
        slotRt.anchorMax = new Vector2(0f, 0.5f);
        slotRt.pivot = new Vector2(0f, 0.5f);
        slotRt.anchoredPosition = new Vector2(10f, 0f);
        slotRt.sizeDelta = new Vector2(56f, 56f);

        GameObject iconGo = NewRect("Icon", slotBg.transform);
        var iconImg = iconGo.AddComponent<Image>();
        iconImg.sprite = item.icon;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        Anchor(iconGo.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
            new Vector2(5f, 5f), new Vector2(-5f, -5f));

        var nameText = MakeText(row.transform, "Name",
            "<b>" + Colored(item.itemName, QualityHex(item.name)) + "</b> <size=17><alpha=#B0>" + sub + "</size>",
            (TextAlignmentOptions)TextAnchor.MiddleLeft, 23);
        Anchor(nameText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(78f, 6f), new Vector2(-330f, -6f));

        if (coinSprite != null)
        {
            var coin = NewRect("Coin", row.transform);
            var coinImg = coin.AddComponent<Image>();
            coinImg.sprite = coinSprite;
            coinImg.preserveAspect = true;
            coinImg.raycastTarget = false;
            var coinRt = coin.GetComponent<RectTransform>();
            coinRt.anchorMin = new Vector2(1f, 0.5f);
            coinRt.anchorMax = new Vector2(1f, 0.5f);
            coinRt.pivot = new Vector2(1f, 0.5f);
            coinRt.anchoredPosition = new Vector2(-186f, 0f);
            coinRt.sizeDelta = new Vector2(26f, 26f);
        }

        var priceText = MakeText(row.transform, "Price", "<b>" + price + "</b>",
            (TextAlignmentOptions)TextAnchor.MiddleRight, 24);
        priceText.color = HexColor("#F5C542");
        Anchor(priceText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(-320f, 4f), new Vector2(-192f, -4f));

        InventorySlot captured = slot;
        CreateSellButton(row.transform, "Sell1", "×1", -166f, -94f,
            new Color(0.36f, 0.25f, 0.14f, 1f), () => SellFishSlot(captured));
        CreateSellButton(row.transform, "SellAll", "Всё", -88f, -8f,
            new Color(0.83f, 0.6f, 0.18f, 1f), () => SellAllFish());

        var btn = row.AddComponent<Button>();
        btn.targetGraphic = rowImg;
        var cols = btn.colors;
        cols.pressedColor = new Color(1f, 1f, 1f, 0.6f);
        cols.fadeDuration = 0.08f;
        btn.colors = cols;
        btn.onClick.AddListener(() => SellFishSlot(captured));
    }

    void SellFishSlot(InventorySlot s)
    {
        if (s == null || s.IsEmpty()) return;
        if (!fishByItem.TryGetValue(s.currentItem, out FishData f)) return;
        int gold = FishSlotPrice(f, s);
        if (CurrencyManager.Instance == null) return;
        CurrencyManager.Instance.AddGold(gold);
        string desc = s.currentItem.itemName + " "
            + (s.fishWeightKg > 0f ? FishData.FormatWeight(s.fishWeightKg) : "×" + s.quantity);
        s.ClearSlot();
        HotbarManager.Instance?.NotifyActiveItemChanged();
        ActionLogUI.Show("[Морек] Продано: " + desc + " за " + gold + "g");
        RebuildList();
    }

    void SellAllFish()
    {
        int total = 0, n = 0;
        foreach (InventorySlot s in AllSlots())
        {
            if (s.IsEmpty()) continue;
            if (!fishByItem.TryGetValue(s.currentItem, out FishData f)) continue;
            total += FishSlotPrice(f, s);
            n++;
            s.ClearSlot();
        }
        if (n <= 0 || CurrencyManager.Instance == null) return;
        CurrencyManager.Instance.AddGold(total);
        HotbarManager.Instance?.NotifyActiveItemChanged();
        ActionLogUI.Show("[Морек] Продано рыб: " + n + " за " + total + "g");
        RebuildList();
    }

    void RebuildList()
    {
        if (itemListContent == null) return;
        if (!fishMode && BuyerManager.Instance == null) return;

        foreach (Transform child in itemListContent)
            Destroy(child.gameObject);

        if (fishMode) { RebuildFishList(); return; }

        var counts = new Dictionary<ItemData, int>();
        foreach (InventorySlot s in AllSlots())
        {
            if (s.IsEmpty()) continue;
            bool sellable = fishMode
                ? fishPrices.ContainsKey(s.currentItem)
                : BuyerManager.Instance.IsSellable(s.currentItem);
            if (!sellable) continue;
            if (!counts.ContainsKey(s.currentItem)) counts[s.currentItem] = 0;
            counts[s.currentItem] += s.quantity;
        }

        if (counts.Count == 0)
        {
            var empty = NewRect("EmptyHint", itemListContent);
            var tmp = empty.AddComponent<TextMeshProUGUI>();
            tmp.text = fishMode
                ? "Пока нечего продать.\nУдочку в руки — и на пляж!"
                : "Пока нечего продать.\nСкупщик покупает урожай (не семена).\nВырасти урожай и возвращайся!";
            tmp.fontSize = 21;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.35f, 0.24f, 0.14f, 0.9f);
            tmp.raycastTarget = false;
            var le = empty.AddComponent<LayoutElement>();
            le.minHeight = 140f;
            le.preferredHeight = 140f;
            return;
        }

        foreach (var kvp in counts.OrderByDescending(k => fishMode ? fishPrices[k.Key] : BuyerManager.Instance.GetUnitPrice(k.Key)))
        {
            CreateRow(kvp.Key, kvp.Value);
        }
    }

    void CreateRow(ItemData item, int count)
    {
        GameObject row = new GameObject("SellRow_" + item.name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(itemListContent, false);

        var rowImg = row.GetComponent<Image>();
        rowImg.color = new Color(0.25f, 0.17f, 0.11f, 0.98f);
        ApplySlice(rowImg);

        var le = row.GetComponent<LayoutElement>();
        le.minHeight = 72f;
        le.preferredHeight = 72f;

        // Тёмный слот с золотистой рамкой под иконку
        var slot = NewRect("IconSlot", row.transform);
        var slotImg = slot.AddComponent<Image>();
        slotImg.color = new Color(0.55f, 0.42f, 0.24f, 1f);
        ApplySlice(slotImg);
        var slotRt = slot.GetComponent<RectTransform>();
        slotRt.anchorMin = new Vector2(0f, 0.5f);
        slotRt.anchorMax = new Vector2(0f, 0.5f);
        slotRt.pivot = new Vector2(0f, 0.5f);
        slotRt.anchoredPosition = new Vector2(10f, 0f);
        slotRt.sizeDelta = new Vector2(56f, 56f);

        var slotBg = NewRect("SlotBg", slot.transform);
        var slotBgImg = slotBg.AddComponent<Image>();
        slotBgImg.color = new Color(0.13f, 0.09f, 0.06f, 1f);
        ApplySlice(slotBgImg);
        Anchor(slotBg.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
            new Vector2(3f, 3f), new Vector2(-3f, -3f));

        GameObject iconGo = NewRect("Icon", slot.transform);
        var iconImg = iconGo.AddComponent<Image>();
        iconImg.sprite = item.icon;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget = false;
        Anchor(iconGo.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
            new Vector2(5f, 5f), new Vector2(-5f, -5f));

        // Название (жирное, цвет по качеству) + количество мелким
        var nameText = MakeText(row.transform, "Name",
            "<b>" + Colored(item.itemName, QualityHex(item.name)) + "</b> <size=17><alpha=#B0>×" + count + "</size>",
            (TextAlignmentOptions)TextAnchor.MiddleLeft, 23);
        Anchor(nameText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f),
            new Vector2(78f, 6f), new Vector2(-330f, -6f));

        // Монетка + цена
        if (coinSprite != null)
        {
            var coin = NewRect("Coin", row.transform);
            var coinImg = coin.AddComponent<Image>();
            coinImg.sprite = coinSprite;
            coinImg.preserveAspect = true;
            coinImg.raycastTarget = false;
            var coinRt = coin.GetComponent<RectTransform>();
            coinRt.anchorMin = new Vector2(1f, 0.5f);
            coinRt.anchorMax = new Vector2(1f, 0.5f);
            coinRt.pivot = new Vector2(1f, 0.5f);
            coinRt.anchoredPosition = new Vector2(-186f, 0f);
            coinRt.sizeDelta = new Vector2(26f, 26f);
        }

        int unit = fishMode && fishPrices.TryGetValue(item, out int fp)
            ? fp
            : BuyerManager.Instance.GetUnitPrice(item);
        bool inDemand = !fishMode && BuyerManager.Instance.IsInDemand(item);
        var priceText = MakeText(row.transform, "Price",
            "<b>" + unit + "</b>" + (inDemand ? " <color=#FFD54A>×2</color>" : ""),
            (TextAlignmentOptions)TextAnchor.MiddleRight, 24);
        priceText.color = HexColor("#F5C542");
        Anchor(priceText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f),
            new Vector2(-320f, 4f), new Vector2(-192f, -4f));

        // Кнопки прижаты к ПРАВОМУ краю строки
        CreateSellButton(row.transform, "Sell1", "×1", -166f, -94f,
            new Color(0.36f, 0.25f, 0.14f, 1f), () => Sell(item, 1));
        CreateSellButton(row.transform, "SellAll", "Всё", -88f, -8f,
            new Color(0.83f, 0.6f, 0.18f, 1f), () => Sell(item, count));

        // Клик по строке — продаёт 1
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
        int have = 0;
        foreach (InventorySlot s in AllSlots())
            if (!s.IsEmpty() && s.currentItem == item) have += s.quantity;

        int sellCount = Mathf.Min(count, have);
        if (sellCount <= 0) return;

        int gold;
        if (fishMode)
        {
            if (!fishPrices.TryGetValue(item, out int fp)) return;
            gold = sellCount * fp;
            if (CurrencyManager.Instance == null) return;
            CurrencyManager.Instance.AddGold(gold);
        }
        else
        {
            gold = BuyerManager.Instance.Sell(item, sellCount);
            if (gold <= 0) return;
        }

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

        if (fishMode)
            ActionLogUI.Show("[Морек] Продано: " + item.itemName + " ×" + sellCount + " за " + gold + "g");
        else
            ActionLogUI.Show("[Скупщик] Продано: " + item.itemName + " ×" + sellCount + " за " + gold + "g");

        if (!fishMode) RefreshReputation();
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
