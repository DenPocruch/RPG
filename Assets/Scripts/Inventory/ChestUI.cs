using UnityEngine;
using UnityEngine.UI;

public class ChestUI : MonoBehaviour
{
    public static ChestUI Instance;

    [Header("UI панели")]
    public GameObject chestPanel;
    public RectTransform chestRect; // только сундук — рюкзак двигает InventoryPanelMover

    [Header("Слоты")]
    public InventorySlot[] slots;
    public GameObject slotPrefab;
    public Transform slotsGrid;
    public int chestSize = 20;

    [Header("Позиции")]
    public float panelY = 47f;
    public float shiftDistance = 300f;
    public float shiftSpeed = 8f;

    private Vector2 targetChestPos;
    private Vector2 chestNormalPos;

    private bool isOpen = false;
    private ChestInteraction currentChest = null;
    private SiloInteraction currentSilo = null;

    [HideInInspector] public bool isSiloMode = false;
    [HideInInspector] public int siloMaxStack = 50;

    void Awake()
    {
        // Защита от дубликата: копия PersistentRoot при возврате в сцену
        // создаёт второй экземпляр — копию уничтожаем, оригинал живёт
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        chestNormalPos = new Vector2(0, panelY);
        if (chestRect != null)
        {
            chestRect.anchoredPosition = chestNormalPos;
            targetChestPos = chestNormalPos;
        }
        chestPanel.SetActive(false);
    }

    void Update()
    {
        // Двигаем ТОЛЬКО панель сундука — рюкзак двигает InventoryPanelMover
        if (chestRect != null)
            chestRect.anchoredPosition = Vector2.Lerp(
                chestRect.anchoredPosition, targetChestPos,
                Time.deltaTime * shiftSpeed);

        if (Input.GetKeyDown(KeyCode.Escape) && isOpen)
            CloseChest();
    }

    public void OpenChest(InventorySlot[] chestSlots, ChestInteraction chest = null)
    {
        isSiloMode = false;
        siloMaxStack = 0;
        currentChest = chest;
        currentSilo = null;
        OpenInternal(chestSlots);
    }

    public void OpenSilo(InventorySlot[] siloSlots, SiloInteraction silo = null)
    {
        isSiloMode = true;
        siloMaxStack = silo != null ? silo.GetSiloMaxStack() : 50;
        currentSilo = silo;
        currentChest = null;
        OpenInternal(siloSlots);
    }

    void OpenInternal(InventorySlot[] dataSlots)
    {
        slots = dataSlots;
        RefreshChestUI();
        chestPanel.SetActive(true);
        isOpen = true;

        // Рюкзак → влево через InventoryPanelMover
        InventoryPanelMover.Instance?.SetOffsetX(-shiftDistance);

        // Сундук → вправо
        targetChestPos = new Vector2(shiftDistance, panelY);
        if (chestRect != null)
            chestRect.anchoredPosition = new Vector2(shiftDistance, panelY);

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.OpenInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;
    }

    public void CloseChest()
    {
        chestPanel.SetActive(false);
        isOpen = false;

        if (currentChest != null) { currentChest.ForceClose(); currentChest = null; }
        if (currentSilo != null) { currentSilo.ForceClose(); currentSilo = null; }

        // Сейв по событию: содержимое сундука/силоса могло измениться
        SaveManager.Instance?.Save();

        // Рюкзак → центр
        InventoryPanelMover.Instance?.ResetPosition();
        targetChestPos = chestNormalPos;
        isSiloMode = false;

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.CloseInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;
    }

    public bool IsOpen() => isOpen;

    void RefreshChestUI()
    {
        foreach (Transform child in slotsGrid)
            Destroy(child.gameObject);

        for (int i = 0; i < slots.Length; i++)
        {
            GameObject slotObj = Instantiate(slotPrefab, slotsGrid);
            slotObj.name = "ChestSlot_" + i;
            InventorySlot uiSlot = slotObj.GetComponent<InventorySlot>();
            uiSlot.slotIndex = i;
            uiSlot.isHotbarSlot = false;
            uiSlot.linkedChestSlot = slots[i];

            if (!slots[i].IsEmpty())
                uiSlot.SetItemWithWater(
                    slots[i].currentItem,
                    slots[i].quantity,
                    slots[i].currentWater,
                    slots[i].fishWeightKg,
                    slots[i].hookCastsLeft);
        }
    }
}