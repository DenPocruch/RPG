using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI шахты. Показывает 6 слотов руды (persistent data-слоты шахты)
/// и 1 слот-склад досок (overflow, вместимость 50+). Игрок перетаскивает
/// руду из инвентаря прямо в слоты — обработка идёт в фоне сама.
/// Доски появляются в выходном слоте, игрок перетаскивает их себе в инвентарь
/// (переносится максимум обычный стак предмета за раз, остаток остаётся на складе).
/// </summary>
public class MinerUI : MonoBehaviour
{
    public static MinerUI Instance;

    [Header("UI панели")]
    public GameObject minerPanel;
    public RectTransform minerRect;

    [Header("Слоты руды")]
    public GameObject slotPrefab;      // обычный SlotPrefab (с ItemDragHandler)
    public Transform oreSlotsGrid;

    [Header("Фильтр — какую категорию ресурса принимает шахта")]
    public string acceptedResourceCategory = "Ore";

    [Header("Слот-склад слитков")]
    public Transform outputSlotParent; // родитель для единственного UI-слота досок

    [Header("Прогресс переработки")]
    public GameObject progressBarRoot;
    public Image progressBarFill;
    public TMP_Text progressText;

    [Header("Информация")]
    public TMP_Text storageText;   // "Склад слитков: 23/50"
    public TMP_Text warningText;   // "Недостаточно золота!"
    public Button autoFillButton; // "Сложить всю руду"

    [Header("Позиции панели")]
    public float panelY = 47f;
    public float shiftDistance = 325f;
    public float shiftSpeed = 8f;

    private bool isOpen = false;
    private Vector2 targetPos;
    private Vector2 normalPos;

    private InventorySlot[] oreUiSlots;
    private InventorySlot outputUiSlot;

    void Awake()
    {
        Instance = this;
        if (minerPanel != null) minerPanel.SetActive(false);
    }

    void Start()
    {
        normalPos = new Vector2(0, panelY);
        if (minerRect != null)
        {
            minerRect.anchoredPosition = normalPos;
            targetPos = normalPos;
        }

        autoFillButton?.onClick.AddListener(OnAutoFillClick);

        if (MinerStorage.Instance != null)
            MinerStorage.Instance.onStorageChanged += RefreshStorageView;
    }

    void OnDestroy()
    {
        if (MinerStorage.Instance != null)
            MinerStorage.Instance.onStorageChanged -= RefreshStorageView;
    }

    void Update()
    {
        if (minerRect != null)
            minerRect.anchoredPosition = Vector2.Lerp(
                minerRect.anchoredPosition, targetPos, Time.deltaTime * shiftSpeed);

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
        minerPanel.SetActive(true);
        isOpen = true;

        InventoryPanelMover.Instance?.SetOffsetX(-shiftDistance);
        targetPos = new Vector2(shiftDistance, panelY);
        if (minerRect != null)
            minerRect.anchoredPosition = new Vector2(shiftDistance, panelY);

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.OpenInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        BuildSlotsUI();
        RefreshStorageView();
    }

    public void Close()
    {
        minerPanel.SetActive(false);
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
        if (MinerStorage.Instance == null) return;

        // Слоты руды
        foreach (Transform child in oreSlotsGrid) Destroy(child.gameObject);

        InventorySlot[] dataSlots = MinerStorage.Instance.GetOreSlots();
        oreUiSlots = new InventorySlot[dataSlots.Length];

        for (int i = 0; i < dataSlots.Length; i++)
        {
            GameObject obj = Instantiate(slotPrefab, oreSlotsGrid);
            obj.name = "OreUiSlot_" + i;
            ResetSlotTransform(obj); // Instantiate(prefab, parent) сохраняет мировую позицию — сбрасываем
            InventorySlot uiSlot = obj.GetComponent<InventorySlot>();
            uiSlot.slotIndex = i;
            uiSlot.isHotbarSlot = false;
            uiSlot.linkedChestSlot = dataSlots[i];
            uiSlot.allowedResourceCategory = acceptedResourceCategory; // фильтр — только руда

            if (!dataSlots[i].IsEmpty())
                uiSlot.SetItemWithWater(dataSlots[i].currentItem, dataSlots[i].quantity, 0);

            oreUiSlots[i] = uiSlot;
        }

        // Слот-склад слитков (overflow)
        if (outputSlotParent != null)
        {
            foreach (Transform child in outputSlotParent) Destroy(child.gameObject);

            GameObject outObj = Instantiate(slotPrefab, outputSlotParent);
            outObj.name = "IngotOutputUiSlot";
            ResetSlotTransform(outObj); // тот же фикс позиции
            outputUiSlot = outObj.GetComponent<InventorySlot>();
            outputUiSlot.isHotbarSlot = false;
            outputUiSlot.allowOverflow = true;
            outputUiSlot.acceptsManualDeposit = false; // только автоматическая переработка, никакого ручного drag-in
            outputUiSlot.linkedChestSlot = MinerStorage.Instance.GetOutputSlot();
            outputUiSlot.overflowCapacity = MinerStorage.Instance.GetIngotCapacity();

            InventorySlot dataOut = MinerStorage.Instance.GetOutputSlot();
            if (!dataOut.IsEmpty())
                outputUiSlot.SetItemWithWater(dataOut.currentItem, dataOut.quantity, 0);
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ОБНОВЛЕНИЕ ВИЗУАЛА (когда фоновая обработка что-то изменила)
    // ═══════════════════════════════════════════════════════════
    void RefreshStorageView()
    {
        if (MinerStorage.Instance == null || !isOpen) return;

        InventorySlot[] dataSlots = MinerStorage.Instance.GetOreSlots();
        if (oreUiSlots != null)
        {
            for (int i = 0; i < oreUiSlots.Length && i < dataSlots.Length; i++)
            {
                if (dataSlots[i].IsEmpty()) oreUiSlots[i].ClearSlot();
                else oreUiSlots[i].SetItemWithWater(dataSlots[i].currentItem, dataSlots[i].quantity, 0);
            }
        }

        InventorySlot dataOut = MinerStorage.Instance.GetOutputSlot();
        if (outputUiSlot != null)
        {
            if (dataOut.IsEmpty()) outputUiSlot.ClearSlot();
            else outputUiSlot.SetItemWithWater(dataOut.currentItem, dataOut.quantity, 0);
            outputUiSlot.overflowCapacity = MinerStorage.Instance.GetIngotCapacity();
        }

        UpdateStorageText();
    }

    void UpdateStorageText()
    {
        if (storageText == null || MinerStorage.Instance == null) return;
        InventorySlot dataOut = MinerStorage.Instance.GetOutputSlot();
        storageText.text = "Склад слитков: " + dataOut.quantity + "/" + MinerStorage.Instance.GetIngotCapacity();
    }

    void UpdateProgressBar()
    {
        if (MinerStorage.Instance == null) return;

        ItemData current = MinerStorage.Instance.GetCurrentProcessingItem();

        if (warningText != null)
            warningText.text = MinerStorage.Instance.IsWaitingForGold()
                ? "Недостаточно золота — производство приостановлено!"
                : "";

        if (current == null)
        {
            if (progressBarRoot != null) progressBarRoot.SetActive(false);
            return;
        }

        if (progressBarRoot != null) progressBarRoot.SetActive(true);

        float total = MinerStorage.Instance.GetTotalTime();
        float remaining = MinerStorage.Instance.GetTimeRemaining();
        float progress = total > 0 ? 1f - (remaining / total) : 1f;

        if (progressBarFill != null) progressBarFill.fillAmount = progress;
        if (progressText != null)
            progressText.text = "Плавим " + current.itemName + "... " +
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
        if (InventoryUI.Instance == null || oreUiSlots == null) return;

        var processed = new System.Collections.Generic.HashSet<ItemData>();
        foreach (InventorySlot s in oreUiSlots)
            if (!s.IsEmpty()) processed.Add(s.currentItem);

        int slotPtr = 0;
        InventorySlot[] invSlots = InventoryUI.Instance.slots;

        foreach (InventorySlot invSlot in invSlots)
        {
            while (slotPtr < oreUiSlots.Length && !oreUiSlots[slotPtr].IsEmpty()) slotPtr++;
            if (slotPtr >= oreUiSlots.Length) break;

            if (invSlot.IsEmpty()) continue;
            if (invSlot.currentItem.convertsToItem == null) continue;               // не сырьё для переработки
            if (invSlot.currentItem.resourceCategory != acceptedResourceCategory) continue; // не та категория
            if (processed.Contains(invSlot.currentItem)) continue;                  // уже есть такой вид в слотах

            processed.Add(invSlot.currentItem);
            oreUiSlots[slotPtr].SetItem(invSlot.currentItem, invSlot.quantity);
            invSlot.ClearSlot();
            slotPtr++;
        }
    }
}