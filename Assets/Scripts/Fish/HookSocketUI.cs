using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Слот крючка на кнопке атаки (строится кодом — сцену не трогаем).
/// Взял удочку в руки → кнопка атаки меняет скин + появляется слот.
/// Все операции ТОЛЬКО с открытым инвентарём (закрыт — ничего положить/снять нельзя).
/// Положить: перетащить крючок из инвентаря в слот.
/// Снять: перетащить из слота в пустой слот инвентаря/хотбара (как обычный драг).
/// Тап по слоту ничего не делает (тултипа нет).
/// Во время вываживания слот заблокирован (не снять, не заменить).
/// Прочность: −1 за каждый заброс, 0 = сломался (сообщение, слот пуст).
/// Состояние (предмет + остаток) — в сейве под ключом "fishing_hook".
/// </summary>
public class HookSocketUI : MonoBehaviour, ISaveable
{
    public static HookSocketUI Instance
    {
        get
        {
            if (_instance == null)
            {
                Canvas canvas = GameObject.Find("Canvas") != null
                    ? GameObject.Find("Canvas").GetComponent<Canvas>()
                    : FindFirstObjectByType<Canvas>();
                if (canvas == null) return null;
                var go = new GameObject("HookSocketUI");
                go.transform.SetParent(canvas.transform, false);
                _instance = go.AddComponent<HookSocketUI>();
            }
            return _instance;
        }
    }
    private static HookSocketUI _instance;
    public static bool HasInstance => _instance != null;

    private ItemData hookItem;
    private int castsLeft = -1; // -1 = пусто; вечный крючок (hookMaxCasts<=0) = int.MaxValue

    private Button attackButton;
    private Image attackImage;
    private Sprite defaultSkin;
    private Color defaultBtnColor = Color.white;
    private GameObject socketGO;
    private Image socketIcon;
    private TMP_Text socketText;
    private bool rodInHands;

    private GameObject dragVisual;
    private bool socketDragging;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning("[Крючок] Лишний HookSocketUI уничтожен (дубликат).");
            Destroy(gameObject);
            return;
        }
        _instance = this;
        SaveManager.Instance?.Register(this);
        BindAttackButton(); // внутри: сценовый Slot либо кодовый бейдж
        Rebind();
        SaveManager.Instance?.LoadInto(this);
        Refresh();
    }

    /// <summary>Переподписка на живой HotbarManager (копия PersistentRoot при
    /// возврате в сцену могла подписать нас на уничтожаемый менеджер).</summary>
    public void Rebind()
    {
        if (HotbarManager.Instance != null)
        {
            HotbarManager.Instance.onActiveItemChanged -= OnActiveItemChanged;
            HotbarManager.Instance.onActiveItemChanged += OnActiveItemChanged;
        }
        Refresh();
    }

    void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
        if (HotbarManager.Instance != null)
            HotbarManager.Instance.onActiveItemChanged -= OnActiveItemChanged;
    }

    void OnActiveItemChanged(ItemData active)
    {
        Refresh();
    }

    // ── Привязка к сценовой кнопке атаки ──
    // Слот берём РУЧНОЙ из сцены (AttackButton/Slot — юзер выставил по центру).
    // Нет слота — строим кодовый бейдж (запасной вариант).
    void BindAttackButton()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;
        Transform btn = FindDeep(canvas.transform, "AttackButton");
        if (btn == null) { Debug.LogWarning("[Крючок] Кнопка AttackButton не найдена — слот отключён."); return; }
        attackButton = btn.GetComponent<Button>();
        attackImage = btn.GetComponent<Image>();
        if (attackImage != null)
        {
            defaultSkin = attackImage.sprite;
            defaultBtnColor = attackImage.color;
        }

        Transform slotT = btn.Find("Slot");
        if (slotT == null) slotT = FindDeep(btn, "Slot");
        if (slotT != null)
        {
            Debug.Log("[Крючок] Слот: сценовый AttackButton/Slot.");
            SetupSceneSlot(slotT.gameObject);
        }
        else
        {
            Debug.Log("[Крючок] Слот: кодовый бейдж (сценовый AttackButton/Slot НЕ НАЙДЕН).");
            BuildSocket();
        }
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        foreach (Transform c in root.GetComponentsInChildren<Transform>(true))
            if (c.name == name) return c;
        return null;
    }

    // ── Ручной слот из сцены: достраиваем иконку/текст/триггер, размеры не трогаем.
    // Тап по слоту НЕ бьёт Attack: EventTrigger на слоте перехватывает pointerPress
    // (первый хендлер вверх по иерархии), клик идёт в слот, кнопка видит только
    // нажатие без клика — onClick не срабатывает.
    void SetupSceneSlot(GameObject slot)
    {
        socketGO = slot;

        // Слот юзера — дубликат хотбар-слота: внутри висят InventorySlot +
        // ItemDragHandler + иконка/текст. Инвентарную логику ОТКЛЮЧАЕМ, иначе
        // драг-система будет таскать через него предметы мимо сокета:
        // хендлеры драга сносим, сам InventorySlot остаётся инертным
        // (в массивы слотов он не входит — сейвы/свапы его не трогают).
        foreach (ItemDragHandler h in socketGO.GetComponentsInChildren<ItemDragHandler>(true))
            Destroy(h);

        Image bg = socketGO.GetComponent<Image>();
        if (bg == null)
        {
            bg = socketGO.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
        }
        bg.raycastTarget = true;

        // Иконка: HookIcon → Icon (дубликат хотбар-слота) → создать.
        // Существующие НЕ перестилизовываем (размер/шрифт юзера), только наполняем.
        Transform iconT = socketGO.transform.Find("HookIcon");
        if (iconT == null) iconT = socketGO.transform.Find("Icon");
        bool iconIsNew = iconT == null;
        GameObject iconGO = iconIsNew ? new GameObject("HookIcon") : iconT.gameObject;
        if (iconIsNew)
        {
            iconGO.transform.SetParent(socketGO.transform, false);
            var iconRt = iconGO.AddComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0f, 0f);
            iconRt.anchorMax = new Vector2(1f, 1f);
            iconRt.pivot = new Vector2(0.5f, 0.5f);
            iconRt.offsetMin = new Vector2(4f, 24f);
            iconRt.offsetMax = new Vector2(-4f, -4f);
        }
        socketIcon = iconGO.GetComponent<Image>();
        if (socketIcon == null) socketIcon = iconGO.AddComponent<Image>();
        socketIcon.raycastTarget = true;
        socketIcon.preserveAspect = true;
        socketIcon.enabled = false;

        // Текст прочности: HookCasts → QuantityText (дубликат) → создать.
        Transform txtT = socketGO.transform.Find("HookCasts");
        if (txtT == null) txtT = socketGO.transform.Find("QuantityText");
        bool txtIsNew = txtT == null;
        GameObject txtGO = txtIsNew ? new GameObject("HookCasts") : txtT.gameObject;
        if (txtIsNew)
        {
            txtGO.transform.SetParent(socketGO.transform, false);
            var txtRt = txtGO.AddComponent<RectTransform>();
            txtRt.anchorMin = new Vector2(0f, 0f);
            txtRt.anchorMax = new Vector2(1f, 0f);
            txtRt.pivot = new Vector2(0.5f, 0f);
            txtRt.offsetMin = new Vector2(0f, 2f);
            txtRt.offsetMax = new Vector2(0f, 24f);
            socketText = txtGO.AddComponent<TextMeshProUGUI>();
            socketText.fontSize = 18;
            socketText.color = Color.white;
            socketText.alignment = TextAlignmentOptions.Center;
            socketText.overflowMode = TextOverflowModes.Ellipsis;
        }
        else
        {
            socketText = txtGO.GetComponent<TextMeshProUGUI>();
            if (socketText == null) socketText = txtGO.AddComponent<TextMeshProUGUI>();
        }
        socketText.raycastTarget = false;
        socketText.text = "";
        // ВАЖНО: дублированный InventorySlot в своём Start→UpdateUI выключает
        // САМ КОМПОНЕНТ текста (enabled=false) — включаем обратно, иначе при
        // Play текст мёртвый и прочность не видно
        socketText.enabled = true;

        EnsureTrigger();
        socketGO.SetActive(false);
    }

    void EnsureTrigger()
    {
        if (socketGO == null) return;
        // ВАЖНО: только drag-интерфейсы, БЕЗ pointerDown/Up/Click!
        // EventTrigger перехватывал бы и ТАПЫ (pointerPress оседает на первом
        // хендлере вверх по иерархии) — и нажатия на зону слота не доходили бы
        // до кнопки атаки (поклёвка 1.5с → мини-игра не стартовала).
        // Так: тап (без движения) идёт в кнопку Attack, драг — в слот.
        if (socketGO.GetComponent<HookSocketDrag>() != null) return;
        socketGO.AddComponent<HookSocketDrag>().socket = this;
        // Старый EventTrigger (был в ранних версиях) — снести, он глотает тапы
        EventTrigger old = socketGO.GetComponent<EventTrigger>();
        if (old != null) Destroy(old);
    }

    // ── Запасной вариант: слота в сцене нет — строим бейдж кодом ──
    void BuildSocket()
    {
        if (attackButton == null) return;
        RectTransform btnRt = attackButton.GetComponent<RectTransform>();
        socketGO = new GameObject("HookSocket");
        socketGO.transform.SetParent(attackButton.transform.parent, false);
        var rt = socketGO.AddComponent<RectTransform>();
        if (btnRt != null)
        {
            rt.anchorMin = btnRt.anchorMin;
            rt.anchorMax = btnRt.anchorMax;
            rt.pivot = btnRt.pivot;
            rt.sizeDelta = new Vector2(76f, 76f);
            rt.anchoredPosition = btnRt.anchoredPosition
                + new Vector2(-btnRt.sizeDelta.x / 2f + 38f, 0f);
        }
        else
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(76f, 76f);
        }
        socketGO.transform.SetAsLastSibling(); // поверх кнопки — тапы идут в слот, а не в Attack

        var bg = socketGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);
        bg.raycastTarget = true;

        GameObject iconGO = new GameObject("HookIcon");
        iconGO.transform.SetParent(socketGO.transform, false);
        var iconRt = iconGO.AddComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 0.5f);
        iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.pivot = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = new Vector2(56f, 56f);
        iconRt.anchoredPosition = new Vector2(0f, 6f);
        socketIcon = iconGO.AddComponent<Image>();
        socketIcon.raycastTarget = true;
        socketIcon.enabled = false;

        GameObject txtGO = new GameObject("HookCasts");
        txtGO.transform.SetParent(socketGO.transform, false);
        var txtRt = txtGO.AddComponent<RectTransform>();
        txtRt.anchorMin = new Vector2(0.5f, 0f);
        txtRt.anchorMax = new Vector2(0.5f, 0f);
        txtRt.pivot = new Vector2(0.5f, 0f);
        txtRt.sizeDelta = new Vector2(76f, 22f);
        txtRt.anchoredPosition = new Vector2(0f, 2f);
        socketText = txtGO.AddComponent<TextMeshProUGUI>();
        socketText.fontSize = 18;
        socketText.color = Color.white;
        socketText.alignment = TextAlignmentOptions.Center;
        socketText.overflowMode = TextOverflowModes.Ellipsis;
        socketText.raycastTarget = false;
        socketText.text = "";
        socketText.enabled = true; // см. выше: чужой UpdateUI мог выключить компонент

        EnsureTrigger();
        socketGO.SetActive(false);
    }

    // ── Состояние ──
    public bool HasHook() => hookItem != null;

    /// <summary>Этот InventorySlot — слот сокета? (исключить из инвентарного драга).</summary>
    public bool IsSocketSlot(InventorySlot s)
    {
        if (s == null || socketGO == null) return false;
        InventorySlot sock = socketGO.GetComponent<InventorySlot>();
        return sock != null && s == sock;
    }

    /// <summary>Диапазон текущего крючка. false — крючка нет (ловля без фильтра).</summary>
    public bool GetHookRange(out float minKg, out float maxKg)
    {
        if (hookItem == null) { minKg = 0f; maxKg = float.MaxValue; return false; }
        minKg = Mathf.Max(0f, hookItem.hookMinKg);
        maxKg = Mathf.Max(minKg, hookItem.hookMaxKg);
        return true;
    }

    public bool IsRodInHands() => rodInHands;

    /// <summary>Вываживание идёт — слот заблокирован (не снять, не заменить).</summary>
    public bool IsBusy()
    {
        // Без Instance-гейтера (он создавал бы контроллер побочным эффектом):
        // ищем только если объект уже есть в сцене
        var fc = FindFirstObjectByType<FishingController>();
        return fc != null && fc.IsBusy();
    }

    /// <summary>Дроп крючка из инвентаря в слот (зовёт ItemDragHandler).</summary>
    public bool TrySocketFromSlot(InventorySlot src)
    {
        if (src == null || src.IsEmpty()) return false;
        ItemData item = src.currentItem;
        if (item == null || item.itemType != ItemType.FishingHook) return false;
        if (!IsInventoryOpen())
        {
            ActionLogUI.Show("[Крючок] Открой инвентарь!");
            return false;
        }
        if (IsBusy())
        {
            ActionLogUI.Show("[Крючок] Во время вываживания крючок не сменить!");
            return false;
        }
        if (!rodInHands)
        {
            ActionLogUI.Show("[Крючок] Возьми удочку в руки!");
            return false;
        }
        if (hookItem != null)
        {
            ActionLogUI.Show("[Крючок] Слот занят — сначала перетащи крючок в инвентарь!");
            return false;
        }
        int casts = src.hookCastsLeft;
        if (casts < 0) casts = item.hookMaxCasts; // свежий крючок — полная прочность
        if (item.hookMaxCasts <= 0) casts = int.MaxValue; // вечный
        src.ClearSlot();
        // Хотбар перерисовывает активный слот сам через ClearSlot→UpdateUI;
        // о смене активного предмета сообщаем, чтобы иконки не зависли
        if (src.isHotbarSlot) HotbarManager.Instance?.NotifyActiveItemChanged();
        hookItem = item;
        castsLeft = casts;
        Refresh();
        SaveManager.Instance?.Save();
        ActionLogUI.Show("[Крючок] Нацеплен: " + item.itemName);
        return true;
    }

    /// <summary>Проверка дропа: точка над слотом? (один объект).</summary>
    public bool IsSocketHit(GameObject go)
    {
        if (go == null || socketGO == null || !socketGO.activeInHierarchy) return false;
        return go.transform.IsChildOf(socketGO.transform);
    }

    /// <summary>Проверка дропа по всей цепочке рейкаста (поверх слота бывает фон панелей).</summary>
    public bool IsSocketHitAny(System.Collections.Generic.List<GameObject> hits)
    {
        if (hits == null) return false;
        foreach (GameObject go in hits)
            if (IsSocketHit(go)) return true;
        return false;
    }

    public static bool IsInventoryOpen()
    {
        return InventoryUI.Instance != null && InventoryUI.Instance.IsOpen();
    }

    // ── Драг ИЗ слота (как инвентарь ↔ хотбар): в пустой слот инвентаря/хотбара.
    // Тапа и тултипа на слоте нет — только драг. Инвентарь закрыт — снять нельзя.
    public void OnSocketBeginDrag(PointerEventData eventData)
    {
        socketDragging = false;
        if (hookItem == null) return;
        if (IsBusy())
        {
            ActionLogUI.Show("[Крючок] Во время вываживания крючок не снять!");
            return;
        }
        if (!IsInventoryOpen())
        {
            ActionLogUI.Show("[Крючок] Открой инвентарь, чтобы снять крючок!");
            return;
        }
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null || hookItem.icon == null) return;
        dragVisual = new GameObject("HookDragIcon");
        dragVisual.transform.SetParent(canvas.transform, false);
        dragVisual.transform.SetAsLastSibling();
        Image img = dragVisual.AddComponent<Image>();
        img.sprite = hookItem.icon;
        img.raycastTarget = false;
        dragVisual.GetComponent<RectTransform>().sizeDelta = new Vector2(60, 60);
        socketDragging = true;
        if (socketIcon != null) socketIcon.color = new Color(1, 1, 1, 0.4f);
    }

    public void OnSocketDrag(PointerEventData eventData)
    {
        if (!socketDragging || dragVisual == null) return;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.GetComponent<RectTransform>(),
            eventData.position, canvas.worldCamera,
            out Vector2 localPos);
        dragVisual.GetComponent<RectTransform>().localPosition = localPos;
    }

    public void OnSocketEndDrag(PointerEventData eventData)
    {
        if (socketIcon != null && hookItem != null)
            socketIcon.color = IsBusy() ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
        if (dragVisual != null) { Destroy(dragVisual); dragVisual = null; }
        if (!socketDragging) return;
        socketDragging = false;
        if (hookItem == null) return;

        InventorySlot target = GetDropSlot(eventData);
        if (target == null) return; // мимо — отмена
        if (!target.IsEmpty())
        {
            ActionLogUI.Show("[Крючок] Слот занят!");
            return;
        }
        target.SetItemWithWater(hookItem, 1, 0, 0f, castsLeft);
        if (target.isHotbarSlot) HotbarManager.Instance?.NotifyActiveItemChanged();
        ActionLogUI.Show("[Крючок] Снят: " + hookItem.itemName);
        hookItem = null;
        castsLeft = -1;
        Refresh();
        SaveManager.Instance?.Save();
    }

    InventorySlot GetDropSlot(PointerEventData eventData)
    {
        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        foreach (var r in results)
        {
            if (r.gameObject == null) continue;
            if (r.gameObject.GetComponentInParent<EquipmentSlot>() != null) continue;
            InventorySlot s = r.gameObject.GetComponentInParent<InventorySlot>();
            if (s == null) continue;
            if (IsSocketSlot(s)) continue; // сам сокет — не цель (отмена)
            return s;
        }
        return null;
    }

    // ── Заброс: −1 прочность. false = крючка нет (ловим без фильтра). ──
    public bool UseCast()
    {
        if (hookItem == null) return false;
        if (hookItem.hookMaxCasts > 0)
        {
            castsLeft--;
            if (castsLeft <= 0)
            {
                ActionLogUI.Show("[Крючок] " + hookItem.itemName + " сломался!");
                hookItem = null;
                castsLeft = -1;
                Refresh();
                SaveManager.Instance?.Save();
                return false;
            }
        }
        Refresh();
        SaveManager.Instance?.Save();
        return true;
    }

    // ── Отрисовка ──
    public void Refresh()
    {
        rodInHands = HotbarManager.Instance != null
            && HotbarManager.Instance.GetActiveItem() != null
            && HotbarManager.Instance.GetActiveItem().itemType == ItemType.FishingRod;

        if (socketGO != null) socketGO.SetActive(rodInHands);

        // Скин кнопки атаки: с удочкой — «рыбацкий», иначе — дефолт сцены.
        // Дефолт (спрайт+цвет) захвачен при бинде — возвращается всегда.
        if (attackImage != null)
        {
            FishingTuning t = FishingTuning.Instance;
            if (rodInHands && t != null && t.attackRodSkin != null)
            {
                attackImage.sprite = t.attackRodSkin;
                attackImage.color = Color.white;
            }
            else
            {
                if (defaultSkin != null) attackImage.sprite = defaultSkin;
                attackImage.color = rodInHands ? new Color(0.75f, 0.9f, 1f) : defaultBtnColor;
            }
        }

        if (socketIcon != null)
        {
            if (hookItem != null && hookItem.icon != null)
            {
                socketIcon.sprite = hookItem.icon;
                socketIcon.enabled = true;
                socketIcon.color = IsBusy() ? new Color(0.6f, 0.6f, 0.6f) : Color.white;
            }
            else
            {
                socketIcon.sprite = null;
                socketIcon.enabled = false;
            }
        }
        if (socketText != null)
        {
            socketText.enabled = true; // чужой InventorySlot.UpdateUI выключает компонент
            if (hookItem == null) socketText.text = "";
            else if (hookItem.hookMaxCasts <= 0) socketText.text = "∞";
            else socketText.text = castsLeft + "/" + hookItem.hookMaxCasts;
        }
    }

    // ── Сейв ──
    public string SaveKey => "fishing_hook";

    [System.Serializable]
    private class HookSave { public string itemName; public int casts; }

    public string CaptureState()
    {
        if (hookItem == null) return JsonUtility.ToJson(new HookSave());
        return JsonUtility.ToJson(new HookSave { itemName = hookItem.name, casts = castsLeft });
    }

    public void RestoreState(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        HookSave s = JsonUtility.FromJson<HookSave>(json);
        if (s == null || string.IsNullOrEmpty(s.itemName)) return;
        ItemData item = ItemDatabase.Find(s.itemName);
        if (item == null || item.itemType != ItemType.FishingHook)
        {
            Debug.LogWarning("[Save] Крючок не найден: " + s.itemName);
            return;
        }
        hookItem = item;
        castsLeft = s.casts < 0 ? item.hookMaxCasts : s.casts;
        if (item.hookMaxCasts <= 0) castsLeft = int.MaxValue;
        Refresh();
    }
}

/// <summary>
/// Форвардер драга со слота крючка. Реализует ТОЛЬКО drag-интерфейсы —
/// специально без pointerDown/Up/Click: тап (без движения) должен уходить
/// в кнопку атаки (поклёвка/заброс), а драг — в слот (снятие крючка).
/// Висит на самом слоте (находит HookSocketUI через GetComponentInParent).
/// </summary>
public class HookSocketDrag : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [HideInInspector] public HookSocketUI socket;

    void Awake()
    {
        if (socket == null) socket = GetComponentInParent<HookSocketUI>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (socket == null) socket = GetComponentInParent<HookSocketUI>();
        socket?.OnSocketBeginDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        socket?.OnSocketDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        socket?.OnSocketEndDrag(eventData);
    }
}
