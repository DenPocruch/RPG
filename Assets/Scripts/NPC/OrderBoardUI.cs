using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Доска объявлений («ордера») в городе: 6 слотов-заказов на урожай.
/// Слоты открываются с уровнем игрока (slotUnlockLevels), задания — ТОЛЬКО из
/// культур, разблокированных в дереве навыков (теги seed_*). Как только игрок
/// берёт перк качества (Silver/Gold/Purple), в заказах начинают попадаться
/// звёздные позиции — сдаются только плоды соответствующего качества.
/// Награда = базовая цена скупщика × количество × 1.15 (бонус ордера)
///          × множитель качества (серебро 1.15 / золото 1.3 / пурпур 1.5).
/// Кнопка-«мусорка» у слота запускает таймер rerollMinutes (реальное время,
/// тикает и оффлайн) — после него заказ меняется. Таймер показывается в
/// «Text will open», этот же текст на закрытых слотах пишет уровень открытия.
/// Панель 'Bulletine board Panel' и слоты 'Order' скрипт находит/клонирует сам,
/// ссылки в инспекторе не нужны (как у SellUI).
/// </summary>
public class OrderBoardUI : MonoBehaviour, ISaveable
{
    public static OrderBoardUI Instance { get; private set; }

    [Header("Настройки")]
    [Tooltip("Через сколько минут реального времени мусорка меняет заказ")]
    public float rerollMinutes = 5f;
    [Tooltip("Бонус награды ордера (1.15 = +15%)")]
    public float orderBonus = 1.15f;
    [Tooltip("Минимум и максимум штук в одной позиции заказа")]
    public int minQuantity = 10;
    public int maxQuantity = 50;
    [Tooltip("Уровни игрока, на которых открываются 6 слотов")]
    public int[] slotUnlockLevels = { 1, 1, 5, 10, 15, 20 };

    [Header("Шанс звёздной позиции (если перк качества куплен)")]
    [Range(0f, 1f)] public float chanceSilver = 0.25f;
    [Range(0f, 1f)] public float chanceGold = 0.15f;
    [Range(0f, 1f)] public float chancePurple = 0.10f;

    [Header("Ручная привязка слотов (по желанию)")]
    [Tooltip("6 пустых RectTransform-опор под панели: создай их детьми панели, поставь над каждым листком, задай размер и перетащи сюда. Если все 6 заданы — карточки встают в них (растягиваются по размеру опоры) и автосетка 3×2 не используется.")]
    public RectTransform[] manualSlots = new RectTransform[6];

    const int SlotCount = 6;
    const string PanelName = "Bulletine board Panel";

    // ── Состояние заказов ──
    [Serializable]
    public class OrderEntry
    {
        public List<string> crops = new List<string>();   // asset names (могут быть со звёздами)
        public List<int> amounts = new List<int>();
        public long rerollAt;                             // DateTime.UtcNow.Ticks, 0 = таймера нет
    }

    [Serializable]
    class BoardSave { public List<OrderEntry> entries = new List<OrderEntry>(); }

    readonly List<OrderEntry> entries = new List<OrderEntry>();

    // ── UI ──
    class CardView
    {
        public RectTransform root;
        public Button giveButton;
        public Button rerollButton;
        public GameObject willOpenGO;
        public TMP_Text willOpenText;
        public string willOpenName;
        public string rerollName;
        public GameObject priceGO;
        public TMP_Text priceText;
        public string priceTextName;
        public GameObject priceIconGO;
        public string priceIconName;
        public List<Image> productIcons = new List<Image>();
        public List<string> productNames = new List<string>();
        public List<TMP_Text> quantityTexts = new List<TMP_Text>();
        public List<string> quantityNames = new List<string>();

        public bool NameIs(GameObject go, string name)
        {
            return go != null && go.name == name;
        }
    }

    GameObject panel;
    readonly List<CardView> cards = new List<CardView>();
    bool uiBuilt;
    bool isOpen;
    float tickTimer;

    // ═══════════════════════════════════════════════════════════
    // ЖИЗНЕННЫЙ ЦИКЛ
    // ═══════════════════════════════════════════════════════════

    public static OrderBoardUI EnsureInstance()
    {
        if (Instance != null) return Instance;

        Canvas canvas = null;
        var canvasGO = GameObject.Find("Canvas");
        if (canvasGO != null) canvas = canvasGO.GetComponent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();

        GameObject host;
        if (canvas != null)
        {
            var existing = canvas.transform.Find("OrderBoardManager");
            if (existing != null)
                return existing.GetComponent<OrderBoardUI>() ?? existing.gameObject.AddComponent<OrderBoardUI>();
            host = new GameObject("OrderBoardManager");
            host.transform.SetParent(canvas.transform, false);
        }
        else
        {
            host = new GameObject("OrderBoardManager");
        }
        return host.AddComponent<OrderBoardUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SaveManager.Instance?.Register(this);
    }

    void Start()
    {
        SaveManager.Instance?.LoadInto(this);
        EnsureEntries();
        EnsureBuiltUI();

        if (PlayerLevel.Instance != null)
            PlayerLevel.Instance.onLevelUp += _ => { EnsureEntries(); RefreshAll(); };
        if (SkillTreeManager.Instance != null)
            SkillTreeManager.Instance.onSkillTreeChanged += () => { EnsureEntries(); RefreshAll(); };
    }

    void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        tickTimer -= Time.deltaTime;
        if (tickTimer > 0f) return;
        tickTimer = 0.5f;

        try
        {
            TickRerolls();
            if (isOpen) RefreshAll();
        }
        catch (Exception ex)
        {
            Debug.LogError("[OrderBoard] Ошибка обновления: " + ex.Message);
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ГЕНЕРАЦИЯ ЗАКАЗОВ
    // ═══════════════════════════════════════════════════════════

    void EnsureEntries()
    {
        while (entries.Count < SlotCount)
        {
            var e = new OrderEntry();
            GenerateEntry(e);
            entries.Add(e);
        }
    }

    void GenerateEntry(OrderEntry e)
    {
        e.crops.Clear();
        e.amounts.Clear();
        e.rerollAt = 0;

        var pool = BuyerManager.GetUnlockedCrops();
        if (pool.Count == 0) return;

        // Перемешиваем и берём 1-3 разных культуры
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        int positions = Mathf.Min(pool.Count, UnityEngine.Random.Range(1, 4));

        for (int i = 0; i < positions; i++)
        {
            e.crops.Add(RollQuality(pool[i]));
            e.amounts.Add(UnityEngine.Random.Range(minQuantity, maxQuantity + 1));
        }
    }

    /// <summary>Звёзды появляются только после покупки перка качества в дереве.</summary>
    string RollQuality(string crop)
    {
        bool silver = SkillTreeManager.Instance != null && SkillTreeManager.Instance.GetSilverQualityBonus() > 0f
                      && ItemDatabase.Find(crop + " Silver") != null;
        bool gold = SkillTreeManager.Instance != null && SkillTreeManager.Instance.GetGoldQualityBonus() > 0f
                    && ItemDatabase.Find(crop + " Gold") != null;
        bool purple = SkillTreeManager.Instance != null && SkillTreeManager.Instance.GetPurpleQualityBonus() > 0f
                      && ItemDatabase.Find(crop + " Purple") != null;

        float r = UnityEngine.Random.value;
        if (purple && r < chancePurple) return crop + " Purple";
        if (gold && r < chancePurple + chanceGold) return crop + " Gold";
        if (silver && r < chancePurple + chanceGold + chanceSilver) return crop + " Silver";
        return crop;
    }

    /// <summary>Награда заказа: база × количество × 1.15 × качество, по каждой позиции.</summary>
    public int GetReward(OrderEntry e)
    {
        if (e == null) return 0;
        int total = 0;
        for (int i = 0; i < e.crops.Count; i++)
        {
            float v = BuyerManager.GetBaseUnitPrice(e.crops[i]) * e.amounts[i]
                      * orderBonus * BuyerManager.QualityMultiplier(e.crops[i]);
            total += Mathf.RoundToInt(v);
        }
        return total;
    }

    // ═══════════════════════════════════════════════════════════
    // ВЫПОЛНЕНИЕ / МУСОРКА
    // ═══════════════════════════════════════════════════════════

    public bool CanComplete(OrderEntry e)
    {
        if (e == null || e.crops.Count == 0) return false;
        for (int i = 0; i < e.crops.Count; i++)
        {
            var item = ItemDatabase.Find(e.crops[i]);
            if (item == null || CountItem(item) < e.amounts[i]) return false;
        }
        return true;
    }

    public int CountItem(ItemData item)
    {
        if (item == null) return 0;
        int total = 0;
        foreach (var s in AllSlots())
        {
            if (s == null || s.IsEmpty() || s.currentItem != item) continue;
            total += s.quantity;
        }
        return total;
    }

    void OnGiveClicked(int index)
    {
        var e = entries[index];
        if (IsSlotLocked(index)) return;
        if (!CanComplete(e))
        {
            ActionLogUI.Show("Не хватает продукции для заказа");
            return;
        }

        for (int i = 0; i < e.crops.Count; i++)
            ConsumeItem(ItemDatabase.Find(e.crops[i]), e.amounts[i]);

        int gold = GetReward(e);
        CurrencyManager.Instance?.AddGold(gold);
        ActionLogUI.Show($"Заказ выполнен: +{gold} золота");

        GenerateEntry(e);
        SaveManager.Instance?.Save();
        RefreshAll();
    }

    void ConsumeItem(ItemData item, int amount)
    {
        if (item == null) return;
        foreach (var s in AllSlots())
        {
            if (s == null || s.IsEmpty() || s.currentItem != item) continue;
            int take = Mathf.Min(s.quantity, amount);
            s.quantity -= take;
            amount -= take;
            if (s.quantity <= 0) s.ClearSlot();
            else s.UpdateUI();
            if (amount <= 0) break;
        }
        HotbarManager.Instance?.NotifyActiveItemChanged();
    }

    public void RequestReroll(int index)
    {
        if (IsSlotLocked(index)) return;
        var e = entries[index];
        if (e.rerollAt != 0) return;

        e.rerollAt = DateTime.UtcNow.Ticks + (long)(rerollMinutes * 60f * TimeSpan.TicksPerSecond);

        // Сразу перерисовываем слот (иконки/цена/кнопки скрываются, появляется таймер),
        // сейв — после, чтобы ошибка сохранения не блокировала UI
        RefreshSlot(index);
        SaveManager.Instance?.Save();
        RefreshSlot(index);
    }

    void TickRerolls()
    {
        long now = DateTime.UtcNow.Ticks;
        bool changed = false;
        foreach (var e in entries)
        {
            if (e.rerollAt != 0 && now >= e.rerollAt)
            {
                GenerateEntry(e);
                changed = true;
            }
        }
        if (changed) SaveManager.Instance?.ScheduleDelayedSave(1f);
    }

    bool IsSlotLocked(int index)
    {
        int level = PlayerLevel.Instance != null ? PlayerLevel.Instance.TotalLevel : 1;
        return level < GetSlotLevel(index);
    }

    int GetSlotLevel(int index)
    {
        if (slotUnlockLevels == null || slotUnlockLevels.Length == 0) return 1;
        return slotUnlockLevels[Mathf.Clamp(index, 0, slotUnlockLevels.Length - 1)];
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

    // ═══════════════════════════════════════════════════════════
    // ОТКРЫТИЕ / ЗАКРЫТИЕ
    // ═══════════════════════════════════════════════════════════

    public void Open()
    {
        EnsureBuiltUI();
        if (panel == null)
        {
            Debug.LogError("[OrderBoard] Панель 'Bulletine board Panel' не найдена в Canvas!");
            return;
        }
        EnsureEntries();
        panel.SetActive(true);
        isOpen = true;

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        RefreshAll();
    }

    public void Close()
    {
        if (panel != null) panel.SetActive(false);
        isOpen = false;

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;

        SaveManager.Instance?.Save();
    }

    public bool IsOpen() => isOpen;

    int CountSlots()
    {
        int n = 0;
        if (manualSlots != null)
            foreach (var s in manualSlots)
                if (s != null) n++;
        return n;
    }

    bool IsManualSlot(Transform t)
    {
        if (manualSlots == null) return false;
        foreach (var s in manualSlots)
            if (s != null && s.transform == t) return true;
        return false;
    }

    // ═══════════════════════════════════════════════════════════
    // ПОСТРОЕНИЕ UI
    // ═══════════════════════════════════════════════════════════

    void EnsureBuiltUI()
    {
        if (uiBuilt && panel != null) return;

        panel = FindPanel();
        if (panel == null)
        {
            Debug.LogWarning("[OrderBoard] Панель 'Bulletine board Panel' не найдена (пока). " +
                             "Проверка повторится при открытии доски.");
            return;
        }

        // ── Собираем существующие карточки (Order, Order (2)... Order (6)) ──
        // Ищем в панели, в OrdersGrid и ВНУТРИ ОПОР manualSlots (карточки могли
        // уже быть привязаны в прошлый раз). Раскладку НЕ трогаем.
        var cardTs = new List<Transform>();
        Transform grid = panel.transform.Find("OrdersGrid");
        foreach (Transform t in panel.transform)
        {
            if (t == grid) continue;
            if (t.name.Trim().ToLower().StartsWith("order")) cardTs.Add(t);
        }
        if (grid != null)
            foreach (Transform t in grid)
                if (t.name.Trim().ToLower().StartsWith("order")) cardTs.Add(t);

        // ── РУЧНАЯ ПРИВЯЗКА: все 6 опор заданы → карточка i встаёт в опору manualSlots[i] ──
        // Позиции/размеры листков на спрайте неровные: создай 6 пустых детей панели,
        // поставь каждый над своим листком (размером = листку) и перетащи в manualSlots.
        bool manual = manualSlots != null && manualSlots.Length >= SlotCount;
        if (manual)
            foreach (var s in manualSlots)
                if (s == null) { manual = false; break; }

        if (manual)
        {
            // Поддержка обоих вариантов: в manualSlots могут быть ПУСТЫЕ опоры
            // (карточка кладётся внутрь) или сами карточки Order (используются напрямую).
            for (int i = 0; i < SlotCount; i++)
            {
                var slotT = manualSlots[i];
                bool slotIsCard = slotT.name.Trim().ToLower().StartsWith("order");

                if (!slotIsCard && slotT.childCount > 0)
                {
                    foreach (Transform c in slotT)
                    {
                        if (c.name.Trim().ToLower().StartsWith("order") && !cardTs.Contains(c))
                            cardTs.Add(c);
                    }
                }
            }

            Transform proto = cardTs.Count > 0 ? cardTs[0] : BuildTemplateFromCode(panel.transform);
            while (cardTs.Count < SlotCount)
            {
                var copy = Instantiate(proto.gameObject, panel.transform);
                copy.name = "Order (" + (cardTs.Count + 1) + ")";
                cardTs.Add(copy.transform);
            }

            cards.Clear();
            for (int i = 0; i < SlotCount; i++)
            {
                var slotT = manualSlots[i];
                bool slotIsCard = slotT.name.Trim().ToLower().StartsWith("order");
                var t = cardTs[i];

                if (slotIsCard)
                {
                    // В опору передана сама карточка — просто используем её как есть
                    EnsurePairs(t);
                    AddCard(t);
                    continue;
                }

                t.SetParent(slotT, false);
                Anchor((RectTransform)t, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                EnsurePairs(t);
                AddCard(t);
            }

            // Лишние карточки вне опор (дубли старых запусков) — убираем
            foreach (Transform t in panel.transform)
                if (t != grid && t.name.Trim().ToLower().StartsWith("order") && !IsManualSlot(t) && !cardTs.Contains(t))
                    Destroy(t.gameObject);
            if (grid != null)
                foreach (Transform t in grid)
                    if (t.name.Trim().ToLower().StartsWith("order") && !cardTs.Contains(t))
                        Destroy(t.gameObject);

            Debug.Log("[OrderBoard] Слоты привязаны вручную через manualSlots (" + SlotCount + " шт).");
        }
        else
        {
            if (manualSlots != null && manualSlots.Length > 0)
                Debug.LogWarning("[OrderBoard] manualSlots заполнены не полностью (" + CountSlots() + "/" + SlotCount + ") — автосетка.");

            // Контейнер (без GridLayoutGroup — ручная раскладка)
            if (grid == null)
            {
                var gridGO = new GameObject("OrdersGrid", typeof(RectTransform));
                gridGO.transform.SetParent(panel.transform, false);
                Anchor(gridGO.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                grid = gridGO.transform;
            }
            else
            {
                // GridLayout от старой версии мешает ручной раскладке — снимаем
                var gl = grid.GetComponent<GridLayoutGroup>();
                if (gl != null) Destroy(gl);
            }

            if (cardTs.Count == 0)
            {
                var template = BuildTemplateFromCode(grid);
                var trt = (RectTransform)template;
                trt.anchoredPosition = new Vector2(-230.8f, 94.3f);
                cardTs.Add(template);
            }

            cardTs.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            // ── Клон недостающих карточек от первой (позиции — сеткой от неё) ──
            RectTransform first = cardTs[0] as RectTransform;
            Vector2 pos0 = first != null ? first.anchoredPosition : Vector2.zero;
            Vector2 cell = new Vector2(174.9f, 188.6f);
            if (first != null && first.rect.width > 10f) cell = first.rect.size;

            while (cardTs.Count < SlotCount)
            {
                int idx = cardTs.Count;
                var copy = Instantiate(cardTs[0].gameObject, grid);
                copy.name = "Order (" + (idx + 1) + ")";
                int col = idx % 3;
                int row = idx / 3;
                var crt = copy.GetComponent<RectTransform>();
                crt.anchoredPosition = pos0 + new Vector2(col * (cell.x + 16f), -row * (cell.y + 12f));
                cardTs.Add(copy.transform);
            }

            // ── Ревайр всех карточек ──
            cards.Clear();
            foreach (var t in cardTs)
            {
                EnsurePairs(t);
                AddCard(t);
            }
        }

        WireCloseButton();
        uiBuilt = true;
        RefreshAll();
    }

    // ═══════════════════════════════════════════════════════════
    // РЕДАКТОРСКИЕ ХЕЛПЕРЫ (ПКМ по компоненту в инспекторе)
    // ═══════════════════════════════════════════════════════════

#if UNITY_EDITOR
    [ContextMenu("Создать 6 опор слотов (только НЕ в Play!)")]
    void CreateManualSlotAnchors()
    {
        GameObject panelGO = panel;
        if (panelGO == null)
        {
            var p = FindPanel();
            if (p != null) panelGO = p;
        }
        if (panelGO == null)
        {
            Debug.LogError("[OrderBoard] Панель не найдена — сначала открой сцену с доской.");
            return;
        }
        if (Application.isPlaying)
        {
            Debug.LogError("[OrderBoard] Опоры создавать ТОЛЬКО вне Play-режима, иначе они исчезнут и ссылки станут Missing!");
            return;
        }

        // Убираем старые опоры (в т.ч. Missing в поле manualSlots не трогаем — перезапишем)
        for (int i = manualSlots != null ? 0 : 6; i < (manualSlots?.Length ?? 0); i++)
            if (manualSlots[i] != null) DestroyImmediate(manualSlots[i].gameObject);

        var names = new[] { "Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5", "Slot 6" };
        var anchors = new Transform[6];
        var prt = panelGO.GetComponent<RectTransform>();
        float w = prt != null ? prt.rect.width : 900f;
        float h = prt != null ? prt.rect.height : 700f;

        for (int i = 0; i < 6; i++)
        {
            var existing = panelGO.transform.Find(names[i]);
            if (existing != null) { anchors[i] = existing; continue; }

            var go = new GameObject(names[i], typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(prt, false);
            // Сетка 3×2 примерно по центру панели — подвинешь каждый вручную
            int col = i % 3, row = i / 3;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2((col - 1) * (w / 3.6f), (1 - row) * (prt.rect.height / 5.2f));
            rt.sizeDelta = new Vector2(w / 3.8f, prt.rect.height / 2.6f);
            anchors[i] = go.transform;
        }

        manualSlots = new RectTransform[6];
        for (int i = 0; i < 6; i++)
            manualSlots[i] = anchors[i] != null ? (RectTransform)anchors[i] : panelGO.transform.Find(names[i]) as RectTransform;

        UnityEditor.EditorUtility.SetDirty(this);
        Debug.Log("[OrderBoard] Создано 6 опор 'Slot 1..6' под панелью. Расставь их по листкам и СОХРАНИ СЦЕНУ.");
    }
#endif

    void AddCard(Transform t)
    {
        var v = new CardView { root = t as RectTransform };

        // ── Гибкий поиск детей по именам (регистр/нумерация не важны) ──
        foreach (Transform c in t)
        {
            string n = c.name.Trim().ToLower();

            if (n.Contains("will open"))
            {
                v.willOpenGO = c.gameObject;
                v.willOpenText = c.GetComponent<TMP_Text>();
                if (v.willOpenText == null) v.willOpenText = c.GetComponentInChildren<TMP_Text>(true);
                v.willOpenName = c.name;
                if (v.willOpenText != null) v.willOpenText.raycastTarget = false;
            }
            else if (n.Contains("give"))
            {
                v.giveButton = c.GetComponent<Button>();
                if (v.giveButton == null) v.giveButton = c.gameObject.AddComponent<Button>();
                v.giveButton.onClick.RemoveAllListeners();
            }
            else if (n.Contains("update") || n.Contains("trash") || n.Contains("мусор"))
            {
                v.rerollButton = c.GetComponent<Button>();
                if (v.rerollButton == null) v.rerollButton = c.gameObject.AddComponent<Button>();
                v.rerollButton.onClick.RemoveAllListeners();
                // Graphic на кнопке обязан принимать клики (иначе она «мёртвая»)
                var g = c.GetComponent<Graphic>();
                if (g != null) g.raycastTarget = true;
                v.rerollName = c.name;
            }
            else if (n.StartsWith("image product") || n.Contains("product"))
            {
                var img = c.GetComponent<Image>();
                if (img != null) v.productIcons.Add(img);
                v.productNames.Add(c.name);
            }
            else if (n.StartsWith("text quantity") || n.Contains("quantity"))
            {
                var tmp = c.GetComponent<TMP_Text>();
                if (tmp == null) tmp = c.GetComponentInChildren<TMP_Text>();
                if (tmp != null)
                {
                    tmp.raycastTarget = false;
                    v.quantityTexts.Add(tmp);
                    v.quantityNames.Add(c.name);
                }
            }
            else if (n.Contains("price") || n.Contains("цен"))
            {
                var tmp = c.GetComponent<TMP_Text>();
                if (tmp != null && v.priceText == null)
                {
                    v.priceText = tmp;
                    v.priceGO = c.gameObject;
                    v.priceTextName = c.name;
                    tmp.raycastTarget = false;
                }
                else if (c.GetComponent<Image>() != null && v.priceIconGO == null)
                {
                    v.priceIconGO = c.gameObject;
                    v.priceIconName = c.name;
                }
            }
        }

        // Пары по порядку имён: "Image product" < "Image product (1)" < "(2)"
        SortByNames(v.productIcons, v.productNames);
        SortByTmp(v.quantityTexts, v.quantityNames);

        int idx = cards.Count;
        if (v.giveButton != null) v.giveButton.onClick.AddListener(() => OnGiveClicked(idx));
        if (v.rerollButton != null) v.rerollButton.onClick.AddListener(() => RequestReroll(idx));

        cards.Add(v);
    }

    static void SortByNames(List<Image> icons, List<string> names)
    {
        if (icons.Count < 2) return;
        var arr = icons.ToArray();
        var keys = new string[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            int idx = names.IndexOf(arr[i].gameObject.name);
            keys[i] = idx >= 0 ? names[idx] : arr[i].gameObject.name;
        }
        System.Array.Sort(keys, arr);
        for (int i = 0; i < arr.Length; i++) icons[i] = arr[i];
    }

    static void SortByTmp(List<TMP_Text> texts, List<string> names)
    {
        if (texts.Count < 2) return;
        var arr = texts.ToArray();
        var keys = new string[arr.Length];
        for (int i = 0; i < arr.Length; i++)
        {
            keys[i] = arr[i] != null ? arr[i].gameObject.name : "";
        }
        System.Array.Sort(keys, arr);
        for (int i = 0; i < arr.Length; i++) texts[i] = arr[i];
    }

    Button WireButton(Transform t, string name)
    {
        var tr = t.Find(name);
        if (tr == null) return null;
        var b = tr.GetComponent<Button>();
        if (b == null) b = tr.gameObject.AddComponent<Button>();
        b.onClick.RemoveAllListeners();
        return b;
    }

    GameObject FindPanel()
    {
        // Имя ищем гибко: 'Bulletine'/'Bulletin' board Panel (и просто содержит 'board panel')
        bool IsBoardName(string n)
        {
            n = n.Trim().ToLower();
            return n.Contains("board panel");
        }

        Transform p = transform.parent != null ? transform.parent.Find(PanelName) : null;
        if (p == null && transform.parent != null)
        {
            foreach (Transform t in transform.parent)
                if (IsBoardName(t.name)) { p = t; break; }
        }
        if (p == null)
        {
            var canvas = GetComponentInParent<Canvas>();
            Transform rootT = canvas != null ? canvas.transform : null;
            if (rootT == null && transform.parent != null) rootT = transform.parent.root;
            if (rootT != null)
            {
                foreach (Transform t in rootT.GetComponentsInChildren<Transform>(true))
                {
                    if (IsBoardName(t.name))
                    { p = t; break; }
                }
            }
        }
        if (p == null)
        {
            var found = GameObject.Find(PanelName);
            p = found ? found.transform : null;
        }
        return p != null ? p.gameObject : null;
    }

    void WireCloseButton()
    {
        var cb = panel.transform.Find("CloseButton");
        if (cb == null) return;
        var b = cb.GetComponent<Button>();
        if (b == null) return;
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(Close);
    }

    /// <summary>Гарантирует 3 пары «иконка + количество» в карточке (3-ю пару клонирует).</summary>
    void EnsurePairs(Transform card)
    {
        RectTransform r1 = card.Find("Image product (1)") as RectTransform;
        RectTransform r2 = card.Find("Image product (2)") as RectTransform;
        Vector2 iconDelta = (r1 != null && r2 != null) ? r2.anchoredPosition - r1.anchoredPosition : Vector2.zero;

        RectTransform q1 = card.Find("Text quantity 1") as RectTransform;
        RectTransform q2 = card.Find("Text quantity 2") as RectTransform;
        Vector2 qtyDelta = (q1 != null && q2 != null) ? q2.anchoredPosition - q1.anchoredPosition : Vector2.zero;

        for (int i = 1; i <= 3; i++)
        {
            if (card.Find("Image product (" + i + ")") == null)
            {
                if (i > 1)
                {
                    var prev = card.Find("Image product (" + (i - 1) + ")");
                    if (prev != null)
                    {
                        var clone = Instantiate(prev.gameObject, card);
                        clone.name = "Image product (" + i + ")";
                        if (i == 3 && iconDelta != Vector2.zero)
                        {
                            var baseRt = card.Find("Image product (2)") as RectTransform;
                            if (baseRt != null)
                                clone.GetComponent<RectTransform>().anchoredPosition =
                                    baseRt.anchoredPosition + iconDelta;
                        }
                    }
                }
                if (card.Find("Image product (" + i + ")") == null)
                    CreateProductIcon(card.transform, "Image product (" + i + ")", i, Vector2.zero);
            }

            if (card.Find("Text quantity " + i) == null)
            {
                if (i > 1)
                {
                    var prev = card.Find("Text quantity " + (i - 1));
                    if (prev != null)
                    {
                        var clone = Instantiate(prev.gameObject, card);
                        clone.name = "Text quantity " + i;
                        if (i == 3 && qtyDelta != Vector2.zero)
                        {
                            var baseRt = card.Find("Text quantity 2") as RectTransform;
                            if (baseRt != null)
                                clone.GetComponent<RectTransform>().anchoredPosition =
                                    baseRt.anchoredPosition + qtyDelta;
                        }
                    }
                }
                if (card.Find("Text quantity " + i) == null)
                    CreateQuantityText(card.transform, "Text quantity " + i, i, Vector2.zero);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ХЕЛПЕРЫ СОЗДАНИЯ UI
    // ═══════════════════════════════════════════════════════════

    void CreateProductIcon(Transform card, string name, int row, Vector2 extraDelta)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(card, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(12f, -12f - (row - 1) * 38f) + extraDelta;
        rt.sizeDelta = new Vector2(30f, 30f);
        var img = go.GetComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;
    }

    void CreateQuantityText(Transform card, string name, int row, Vector2 extraDelta)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(card, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(50f, -12f - (row - 1) * 38f) + extraDelta;
        rt.sizeDelta = new Vector2(112f, 30f);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 18;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        tmp.text = "0/0";
    }

    /// <summary>Заготовка слота, если пользовательской карточки 'Order' нет в панели.</summary>
    Transform BuildTemplateFromCode(Transform panelT)
    {
        var go = new GameObject("Order", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(panelT, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(174.9f, 188.6f);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.96f, 0.86f, 0.64f, 1f);

        // Текст «Откроется на N уровне» / таймер мусорки
        var wo = new GameObject("Text will open", typeof(RectTransform));
        wo.transform.SetParent(go.transform, false);
        var woRt = wo.GetComponent<RectTransform>();
        Anchor(woRt, new Vector2(0f, 0.25f), new Vector2(1f, 0.95f), Vector2.zero, Vector2.zero);
        var woTmp = wo.AddComponent<TextMeshProUGUI>();
        woTmp.fontSize = 18;
        woTmp.alignment = TextAlignmentOptions.Center;
        woTmp.overflowMode = TextOverflowModes.Ellipsis;
        woTmp.raycastTarget = false;
        woTmp.text = "";

        // Пары иконка+количество
        for (int i = 1; i <= 3; i++)
        {
            CreateProductIcon(go.transform, "Image product (" + i + ")", i, Vector2.zero);
            CreateQuantityText(go.transform, "Text quantity " + i, i, Vector2.zero);
        }

        // Цена
        var ip = new GameObject("Image price", typeof(RectTransform), typeof(Image));
        ip.transform.SetParent(go.transform, false);
        var ipRt = ip.GetComponent<RectTransform>();
        ipRt.anchorMin = ipRt.anchorMax = new Vector2(0f, 0f);
        ipRt.pivot = new Vector2(0f, 0f);
        ipRt.anchoredPosition = new Vector2(14f, 12f);
        ipRt.sizeDelta = new Vector2(22f, 22f);
        ip.GetComponent<Image>().color = new Color(1f, 0.85f, 0.2f, 1f);

        var tp = new GameObject("Text price", typeof(RectTransform));
        tp.transform.SetParent(go.transform, false);
        var tpRt = tp.GetComponent<RectTransform>();
        tpRt.anchorMin = tpRt.anchorMax = new Vector2(0f, 0f);
        tpRt.pivot = new Vector2(0f, 0f);
        tpRt.anchoredPosition = new Vector2(44f, 12f);
        tpRt.sizeDelta = new Vector2(110f, 24f);
        var tpTmp = tp.AddComponent<TextMeshProUGUI>();
        tpTmp.fontSize = 17;
        tpTmp.alignment = TextAlignmentOptions.Left;
        tpTmp.overflowMode = TextOverflowModes.Ellipsis;
        tpTmp.raycastTarget = false;

        // Кнопка «Выполнить» (текст — дочерним объектом: на GO с Image второй Graphic нельзя)
        var gb = new GameObject("Give Button", typeof(RectTransform), typeof(Image), typeof(Button));
        gb.transform.SetParent(go.transform, false);
        var gbRt = gb.GetComponent<RectTransform>();
        gbRt.anchorMin = gbRt.anchorMax = new Vector2(1f, 0f);
        gbRt.pivot = new Vector2(1f, 0f);
        gbRt.anchoredPosition = new Vector2(-12f, 10f);
        gbRt.sizeDelta = new Vector2(82f, 26f);
        gb.GetComponent<Image>().color = new Color(0.36f, 0.62f, 0.3f, 1f);
        var lbl = new GameObject("Label", typeof(RectTransform));
        lbl.transform.SetParent(gb.transform, false);
        var lblRt = lbl.GetComponent<RectTransform>();
        Anchor(lblRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var lblTmp = lbl.AddComponent<TextMeshProUGUI>();
        lblTmp.text = "Выполнить";
        lblTmp.fontSize = 15;
        lblTmp.alignment = TextAlignmentOptions.Center;
        lblTmp.raycastTarget = false;

        // Кнопка-мусорка (обновить заказ)
        var tu = new GameObject("Text update", typeof(RectTransform));
        tu.transform.SetParent(go.transform, false);
        var tuRt = tu.GetComponent<RectTransform>();
        tuRt.anchorMin = tuRt.anchorMax = new Vector2(1f, 1f);
        tuRt.pivot = new Vector2(1f, 1f);
        tuRt.anchoredPosition = new Vector2(-10f, -8f);
        tuRt.sizeDelta = new Vector2(26f, 26f);
        var tuTmp = tu.AddComponent<TextMeshProUGUI>();
        tuTmp.text = "↻";
        tuTmp.fontSize = 22;
        tuTmp.alignment = TextAlignmentOptions.Center;
        tu.AddComponent<Button>();

        return go.transform;
    }

    // ═══════════════════════════════════════════════════════════
    // ОБНОВЛЕНИЕ ОТОБРАЖЕНИЯ
    // ═══════════════════════════════════════════════════════════

    void RefreshAll()
    {
        for (int i = 0; i < cards.Count && i < SlotCount; i++)
            RefreshSlot(i);
    }

    void RefreshSlot(int index)
    {
        if (index >= cards.Count || index >= entries.Count) return;
        var v = cards[index];
        var e = entries[index];
        bool locked = IsSlotLocked(index);
        bool rerolling = e.rerollAt != 0;

        // ── Закрытый слот: виден ТОЛЬКО текст «Откроется на N уровне» ──
        if (locked)
        {
            foreach (Transform c in v.root)
            {
                bool isWillOpen = v.willOpenName != null && c.name == v.willOpenName;
                c.gameObject.SetActive(isWillOpen);
            }
            if (v.willOpenGO != null) v.willOpenGO.SetActive(true);
            if (v.willOpenText != null)
                v.willOpenText.text = $"Откроется\nна {GetSlotLevel(index)} уровне";
            return;
        }

        // ── Открытый слот ──
        // Пока идёт таймер мусорки: скрыто ВСЁ кроме текста отсчёта
        if (rerolling)
        {
            foreach (Transform c in v.root)
            {
                bool isWillOpen = v.willOpenName != null && c.name == v.willOpenName;
                c.gameObject.SetActive(isWillOpen);
            }
            if (v.willOpenGO != null) v.willOpenGO.SetActive(true);
            if (v.willOpenText != null)
                v.willOpenText.text = $"Обновление\nчерез {FormatTime(SecondsLeft(e.rerollAt))}";
            return;
        }

        // Обычное задание: все дети видимы КРОМЕ текста таймера/уровня,
        // пары без позиции — скрыты
        foreach (Transform c in v.root)
        {
            bool isWillOpen = v.willOpenName != null && c.name == v.willOpenName;
            c.gameObject.SetActive(!isWillOpen);
        }

        if (v.priceText != null) v.priceText.text = GetReward(e).ToString();

        // Пары «иконка + количество»: показываем только используемые
        int pairs = Mathf.Max(v.productIcons.Count, v.quantityTexts.Count);
        for (int i = 0; i < pairs; i++)
        {
            bool used = i < e.crops.Count;
            var icon = i < v.productIcons.Count ? v.productIcons[i] : null;
            var qty = i < v.quantityTexts.Count ? v.quantityTexts[i] : null;
            if (icon != null)
            {
                icon.gameObject.SetActive(used);
                if (used)
                {
                    var item = ItemDatabase.Find(e.crops[i]);
                    if (icon.sprite != (item != null ? item.icon : null))
                        icon.sprite = item != null ? item.icon : null;
                    icon.preserveAspect = true;
                }
            }
            if (qty != null)
            {
                qty.gameObject.SetActive(used);
                if (used)
                {
                    var item = ItemDatabase.Find(e.crops[i]);
                    int have = CountItem(item);
                    qty.text = $"{have}/{e.amounts[i]}";
                }
            }
        }
    }

    static void Anchor(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    double SecondsLeft(long rerollAt)
    {
        return (rerollAt - DateTime.UtcNow.Ticks) / (double)TimeSpan.TicksPerSecond;
    }

    static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        int total = Mathf.CeilToInt((float)seconds);
        return $"{total / 60}:{total % 60:D2}";
    }

    // ═══════════════════════════════════════════════════════════
    // ISaveable
    // ═══════════════════════════════════════════════════════════

    public string SaveKey => "orderboard";

    public string CaptureState()
    {
        TickRerolls();
        return JsonUtility.ToJson(new BoardSave { entries = entries });
    }

    public void RestoreState(string json)
    {
        var save = JsonUtility.FromJson<BoardSave>(json);
        if (save == null || save.entries == null) return;

        entries.Clear();
        foreach (var e in save.entries)
        {
            if (entries.Count >= SlotCount) break;
            // Культуры, которых больше нет в Resources — выбрасываем
            e.crops.RemoveAll(c => ItemDatabase.Find(c) == null);
            e.amounts.RemoveRange(e.crops.Count, Mathf.Max(0, e.amounts.Count - e.crops.Count));
            while (e.amounts.Count < e.crops.Count)
                e.amounts.Add(UnityEngine.Random.Range(minQuantity, maxQuantity + 1));
            if (e.crops.Count == 0) GenerateEntry(e); // пустые заказы перегенерируем
            entries.Add(e);
        }
        EnsureEntries();
        TickRerolls();
        if (uiBuilt) RefreshAll();
    }
}
