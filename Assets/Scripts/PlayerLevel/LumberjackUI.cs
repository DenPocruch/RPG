using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI лесопилки. Показывает 6 слотов брёвен (persistent data-слоты лесопилки)
/// и 1 слот-склад досок (overflow, вместимость 50+). Игрок перетаскивает
/// брёвна из инвентаря прямо в слоты — обработка идёт в фоне сама.
/// Доски появляются в выходном слоте, игрок перетаскивает их себе в инвентарь
/// (переносится максимум обычный стак предмета за раз, остаток остаётся на складе).
/// </summary>
public class LumberjackUI : MonoBehaviour
{
    public static LumberjackUI Instance;

    [Header("UI панели")]
    public GameObject lumberjackPanel;
    public RectTransform lumberjackRect;

    [Header("Слоты брёвен")]
    public GameObject slotPrefab;      // обычный SlotPrefab (с ItemDragHandler)
    public Transform logSlotsGrid;

    [Header("Фильтр — какую категорию ресурса принимает лесопилка")]
    public string acceptedResourceCategory = "Wood";

    [Header("Слот-склад досок")]
    public Transform outputSlotParent; // родитель для единственного UI-слота досок

    [Header("Прогресс переработки")]
    public GameObject progressBarRoot;
    public Image progressBarFill;
    public TMP_Text progressText;

    [Header("Информация")]
    public TMP_Text storageText;   // "Склад досок: 23/50"
    public TMP_Text warningText;   // "Недостаточно золота!"
    public Button autoFillButton; // "Сложить всю древесину"

    [Header("Позиции панели")]
    public float panelY = 47f;
    public float shiftDistance = 325f;
    public float shiftSpeed = 8f;

    private bool isOpen = false;
    private Vector2 targetPos;
    private Vector2 normalPos;

    private InventorySlot[] logUiSlots;
    private InventorySlot outputUiSlot;

    void Awake()
    {
        // Защита от дубликата: копия PersistentRoot при возврате в сцену
        // создаёт второй экземпляр — копию уничтожаем, оригинал живёт
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (lumberjackPanel != null) lumberjackPanel.SetActive(false);
    }

    void Start()
    {
        normalPos = new Vector2(0, panelY);
        if (lumberjackRect != null)
        {
            lumberjackRect.anchoredPosition = normalPos;
            targetPos = normalPos;
        }

        autoFillButton?.onClick.AddListener(OnAutoFillClick);

        if (LumberjackStorage.Instance != null)
            LumberjackStorage.Instance.onStorageChanged += RefreshStorageView;
    }

    /// <summary>Переподписка на событие склада (склады пересоздаются при смене сцен).</summary>
    public void BindToStorage()
    {
        if (LumberjackStorage.Instance != null)
        {
            LumberjackStorage.Instance.onStorageChanged -= RefreshStorageView;
            LumberjackStorage.Instance.onStorageChanged += RefreshStorageView;
        }
        RefreshStorageView();
    }

    void OnDestroy()
    {
        if (LumberjackStorage.Instance != null)
            LumberjackStorage.Instance.onStorageChanged -= RefreshStorageView;
    }

    void Update()
    {
        if (lumberjackRect != null)
            lumberjackRect.anchoredPosition = Vector2.Lerp(
                lumberjackRect.anchoredPosition, targetPos, Time.deltaTime * shiftSpeed);

        if (isOpen)
        {
            UpdateProgressBar();
            UpdateStorageText(); // забор досок игроком меняет data-слот напрямую через drag&drop,
                                 // без события onStorageChanged — поэтому обновляем текст каждый кадр
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ОТКРЫТИЕ / ЗАКРЫТИЕ
    // ═══════════════════════════════════════════════════════════
    public void Open()
    {
        lumberjackPanel.SetActive(true);
        isOpen = true;

        InventoryPanelMover.Instance?.SetOffsetX(-shiftDistance);
        targetPos = new Vector2(shiftDistance, panelY);
        if (lumberjackRect != null)
            lumberjackRect.anchoredPosition = new Vector2(shiftDistance, panelY);

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.OpenInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        BuildSlotsUI();
        RefreshStorageView();
    }

    public void Close()
    {
        lumberjackPanel.SetActive(false);
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
    // СОЗДАНИЕ UI-СЛОТОВ ПРИВЯЗАННЫХ К DATA-СЛОТАМ ЛЕСОПИЛКИ
    // ═══════════════════════════════════════════════════════════
    void BuildSlotsUI()
    {
        if (LumberjackStorage.Instance == null) return;

        // Слоты брёвен
        foreach (Transform child in logSlotsGrid) Destroy(child.gameObject);

        InventorySlot[] dataSlots = LumberjackStorage.Instance.GetLogSlots();
        logUiSlots = new InventorySlot[dataSlots.Length];

        for (int i = 0; i < dataSlots.Length; i++)
        {
            GameObject obj = Instantiate(slotPrefab, logSlotsGrid);
            obj.name = "LogUiSlot_" + i;
            ResetSlotTransform(obj); // Instantiate(prefab, parent) сохраняет мировую позицию — сбрасываем
            InventorySlot uiSlot = obj.GetComponent<InventorySlot>();
            uiSlot.slotIndex = i;
            uiSlot.isHotbarSlot = false;
            uiSlot.linkedChestSlot = dataSlots[i];
            uiSlot.allowedResourceCategory = acceptedResourceCategory; // фильтр — только дерево

            if (!dataSlots[i].IsEmpty())
                uiSlot.SetItemWithWater(dataSlots[i].currentItem, dataSlots[i].quantity, 0);

            logUiSlots[i] = uiSlot;
        }

        // Слот-склад досок (overflow)
        if (outputSlotParent != null)
        {
            foreach (Transform child in outputSlotParent) Destroy(child.gameObject);

            GameObject outObj = Instantiate(slotPrefab, outputSlotParent);
            outObj.name = "PlankOutputUiSlot";
            ResetSlotTransform(outObj); // тот же фикс позиции
            outputUiSlot = outObj.GetComponent<InventorySlot>();
            outputUiSlot.isHotbarSlot = false;
            outputUiSlot.allowOverflow = true;
            outputUiSlot.acceptsManualDeposit = false; // только автоматическая переработка, никакого ручного drag-in
            outputUiSlot.linkedChestSlot = LumberjackStorage.Instance.GetOutputSlot();
            outputUiSlot.overflowCapacity = LumberjackStorage.Instance.GetPlankCapacity();

            InventorySlot dataOut = LumberjackStorage.Instance.GetOutputSlot();
            if (!dataOut.IsEmpty())
                outputUiSlot.SetItemWithWater(dataOut.currentItem, dataOut.quantity, 0);
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ОБНОВЛЕНИЕ ВИЗУАЛА (когда фоновая обработка что-то изменила)
    // ═══════════════════════════════════════════════════════════
    void RefreshStorageView()
    {
        if (LumberjackStorage.Instance == null || !isOpen) return;

        InventorySlot[] dataSlots = LumberjackStorage.Instance.GetLogSlots();
        if (logUiSlots != null)
        {
            for (int i = 0; i < logUiSlots.Length && i < dataSlots.Length; i++)
            {
                if (dataSlots[i].IsEmpty()) logUiSlots[i].ClearSlot();
                else logUiSlots[i].SetItemWithWater(dataSlots[i].currentItem, dataSlots[i].quantity, 0);
            }
        }

        InventorySlot dataOut = LumberjackStorage.Instance.GetOutputSlot();
        if (outputUiSlot != null)
        {
            if (dataOut.IsEmpty()) outputUiSlot.ClearSlot();
            else outputUiSlot.SetItemWithWater(dataOut.currentItem, dataOut.quantity, 0);
            outputUiSlot.overflowCapacity = LumberjackStorage.Instance.GetPlankCapacity();
        }

        UpdateStorageText();
    }

    void UpdateStorageText()
    {
        if (storageText == null || LumberjackStorage.Instance == null) return;
        InventorySlot dataOut = LumberjackStorage.Instance.GetOutputSlot();
        storageText.text = "Склад досок: " + dataOut.quantity + "/" + LumberjackStorage.Instance.GetPlankCapacity();
    }

    void UpdateProgressBar()
    {
        if (LumberjackStorage.Instance == null) return;

        ItemData current = LumberjackStorage.Instance.GetCurrentProcessingItem();

        if (warningText != null)
            warningText.text = LumberjackStorage.Instance.IsWaitingForGold()
                ? "Недостаточно золота — производство приостановлено!"
                : "";

        if (current == null)
        {
            if (progressBarRoot != null) progressBarRoot.SetActive(false);
            return;
        }

        if (progressBarRoot != null) progressBarRoot.SetActive(true);

        float total = LumberjackStorage.Instance.GetTotalTime();
        float remaining = LumberjackStorage.Instance.GetTimeRemaining();
        float progress = total > 0 ? 1f - (remaining / total) : 1f;

        if (progressBarFill != null) progressBarFill.fillAmount = progress;
        if (progressText != null)
            progressText.text = "Пилим " + current.itemName + "... " +
                Mathf.CeilToInt(remaining) + "с";
    }

    // ═══════════════════════════════════════════════════════════
    // АВТО-ЗАПОЛНЕНИЕ ПУСТЫХ СЛОТОВ БРЁВЕН ИЗ ИНВЕНТАРЯ
    // ═══════════════════════════════════════════════════════════
    // Instantiate(prefab, parent) в Unity сохраняет МИРОВУЮ позицию префаба,
    // а не обнуляет её относительно нового родителя — из-за этого слот
    // может "улететь" в случайное место если родитель без GridLayoutGroup.
    void ResetSlotTransform(GameObject obj)
    {
        RectTransform rt = obj.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }

    void OnAutoFillClick()
    {
        if (InventoryUI.Instance == null || logUiSlots == null) return;

        var processed = new System.Collections.Generic.HashSet<ItemData>();
        foreach (InventorySlot s in logUiSlots)
            if (!s.IsEmpty()) processed.Add(s.currentItem);

        int slotPtr = 0;
        InventorySlot[] invSlots = InventoryUI.Instance.slots;

        foreach (InventorySlot invSlot in invSlots)
        {
            while (slotPtr < logUiSlots.Length && !logUiSlots[slotPtr].IsEmpty()) slotPtr++;
            if (slotPtr >= logUiSlots.Length) break;

            if (invSlot.IsEmpty()) continue;
            if (invSlot.currentItem.convertsToItem == null) continue;               // не сырьё для переработки
            if (invSlot.currentItem.resourceCategory != acceptedResourceCategory) continue; // не та категория
            if (processed.Contains(invSlot.currentItem)) continue;                  // уже есть такой вид в слотах

            processed.Add(invSlot.currentItem);
            logUiSlots[slotPtr].SetItem(invSlot.currentItem, invSlot.quantity);
            invSlot.ClearSlot();
            slotPtr++;
        }
    }
}