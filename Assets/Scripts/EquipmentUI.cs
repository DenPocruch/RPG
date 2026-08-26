using UnityEngine;

public class EquipmentUI : MonoBehaviour
{
    public static EquipmentUI Instance;

    [Header("UI панели")]
    public GameObject equipmentPanel;
    public RectTransform equipmentRect; // только экипировка — рюкзак двигает InventoryPanelMover

    [Header("Слоты экипировки (12 шт)")]
    public EquipmentSlot[] slots;

    [Header("Позиции")]
    public float panelY = 47f;
    public float shiftDistance = 325f;
    public float shiftSpeed = 8f;

    private bool isOpen = false;

    private Vector2 targetEquipmentPos;
    private Vector2 equipmentNormalPos;

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
        equipmentNormalPos = new Vector2(0, panelY);
        if (equipmentRect != null)
        {
            equipmentRect.anchoredPosition = equipmentNormalPos;
            targetEquipmentPos = equipmentNormalPos;
        }
        equipmentPanel.SetActive(false);
    }

    void Update()
    {
        // Двигаем ТОЛЬКО панель экипировки — рюкзак двигает InventoryPanelMover
        if (equipmentRect != null)
            equipmentRect.anchoredPosition = Vector2.Lerp(
                equipmentRect.anchoredPosition,
                targetEquipmentPos,
                Time.deltaTime * shiftSpeed
            );

        if (Input.GetKeyDown(KeyCode.Escape) && isOpen)
            Close();
    }

    public void Toggle()
    {
        if (isOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (ChestUI.Instance != null && ChestUI.Instance.IsOpen())
        {
            Debug.Log("[Equipment] Сначала закрой сундук!");
            return;
        }

        equipmentPanel.SetActive(true);
        isOpen = true;

        // Рюкзак → влево через InventoryPanelMover
        InventoryPanelMover.Instance?.SetOffsetX(-shiftDistance);

        // Экипировка → вправо
        targetEquipmentPos = new Vector2(shiftDistance, panelY);
        if (equipmentRect != null)
            equipmentRect.anchoredPosition = new Vector2(shiftDistance, panelY);

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.OpenInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        RefreshAllSlots();
    }

    public void Close()
    {
        equipmentPanel.SetActive(false);
        isOpen = false;

        // Рюкзак → центр
        InventoryPanelMover.Instance?.ResetPosition();
        targetEquipmentPos = equipmentNormalPos;

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.CloseInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;
    }

    public bool IsOpen() => isOpen;

    void RefreshAllSlots()
    {
        if (slots == null) return;
        foreach (EquipmentSlot slot in slots)
            if (slot != null) slot.Refresh();
    }

    public EquipmentSlot FindSlot(EquipmentSlotType type)
    {
        foreach (EquipmentSlot slot in slots)
            if (slot != null && slot.slotType == type)
                return slot;
        return null;
    }
}