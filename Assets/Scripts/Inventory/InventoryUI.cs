using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class InventoryUI : MonoBehaviour, ISaveable
{
    public static InventoryUI Instance;

    [Header("UI ������")]
    public GameObject inventoryPanel;

    [Header("����� ���������")]
    public InventorySlot[] slots;

    [Header("������ �����")]
    public GameObject slotPrefab;
    public Transform slotsGrid;

    [Header("������ ���������")]
    public int inventorySize = 20;

    private bool isOpen = false;

    void Awake()
    {
        Instance = this;
        SaveManager.Instance?.Register(this);
    }

    void Start()
    {
        if (slots == null || slots.Length == 0)
            CreateSlots();
        inventoryPanel.SetActive(false);

        // Подписываемся на изменение дерева навыков
        if (SkillTreeManager.Instance != null)
            SkillTreeManager.Instance.onSkillTreeChanged += OnSkillTreeChanged;

        SaveManager.Instance?.LoadInto(this);
    }

    // ─── ISaveable ─────────────────────────────────────────────
    [System.Serializable]
    private class SlotSave
    {
        public int index;
        public string itemName;
        public int quantity;
        public int water;
    }
    [System.Serializable]
    private class InventorySave { public List<SlotSave> slots = new List<SlotSave>(); }

    public string SaveKey => "inventory";

    public string CaptureState()
    {
        InventorySave save = new InventorySave();
        for (int i = 0; i < slots.Length; i++)
        {
            InventorySlot s = slots[i];
            if (s == null || s.IsEmpty()) continue;
            save.slots.Add(new SlotSave
            {
                index = i,
                itemName = s.currentItem.name,
                quantity = s.quantity,
                water = s.currentWater
            });
        }
        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        InventorySave save = JsonUtility.FromJson<InventorySave>(json);
        if (save == null) return;

        // Убеждаемся что слотов достаточно (мог быть бонус от навыков)
        int maxIndex = 0;
        foreach (SlotSave ss in save.slots)
            if (ss.index > maxIndex) maxIndex = ss.index;
        if (slots.Length <= maxIndex)
            AddExtraSlots(maxIndex + 1 - slots.Length);

        // Чистим всё
        foreach (InventorySlot s in slots)
            if (s != null) s.ClearSlot();

        // Расставляем сохранённые предметы
        foreach (SlotSave ss in save.slots)
        {
            if (ss.index < 0 || ss.index >= slots.Length) continue;
            ItemData item = ItemDatabase.Find(ss.itemName);
            if (item == null)
            {
                Debug.LogWarning("[Save] Предмет не найден: " + ss.itemName);
                continue;
            }
            slots[ss.index].SetItemWithWater(item, ss.quantity, ss.water);
        }
    }

    void OnDestroy()
    {
        if (SkillTreeManager.Instance != null)
            SkillTreeManager.Instance.onSkillTreeChanged -= OnSkillTreeChanged;
    }

    void OnSkillTreeChanged()
    {
        // Проверяем нужно ли расширить инвентарь
        int extraSlots = SkillTreeManager.Instance.GetExtraInventorySlots();
        int targetSize = inventorySize + extraSlots;

        if (slots.Length < targetSize)
            AddExtraSlots(targetSize - slots.Length);
    }

    void AddExtraSlots(int count)
    {
        // Расширяем массив слотов
        InventorySlot[] newSlots = new InventorySlot[slots.Length + count];
        System.Array.Copy(slots, newSlots, slots.Length);

        for (int i = 0; i < count; i++)
        {
            int idx = slots.Length + i;
            GameObject slotObj = Instantiate(slotPrefab, slotsGrid);
            slotObj.name = "InvSlot_" + idx;
            InventorySlot slot = slotObj.GetComponent<InventorySlot>();
            slot.slotIndex = idx;
            slot.isHotbarSlot = false;
            newSlots[idx] = slot;
            Debug.Log("[Инвентарь] Добавлен слот " + (idx + 1));
        }

        slots = newSlots;
    }

    void CreateSlots()
    {
        slots = new InventorySlot[inventorySize];
        for (int i = 0; i < inventorySize; i++)
        {
            GameObject slotObj = Instantiate(slotPrefab, slotsGrid);
            slotObj.name = "InvSlot_" + i;
            InventorySlot slot = slotObj.GetComponent<InventorySlot>();
            slot.slotIndex = i;
            slot.isHotbarSlot = false;
            slots[i] = slot;
        }
    }

    public void ToggleInventory()
    {
        isOpen = !isOpen;
        inventoryPanel.SetActive(isOpen);
        // �� ������� PlayerMovement ���� ������ ������
        if (ChestUI.Instance == null || !ChestUI.Instance.IsOpen())
        {
            PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
            if (pm != null) pm.enabled = !isOpen;
        }
    }

    // ������� ��� ������������ (���������� �� ChestUI)
    public void OpenInventory()
    {
        isOpen = true;
        inventoryPanel.SetActive(true);
    }

    public void CloseInventory()
    {
        isOpen = false;
        inventoryPanel.SetActive(false);
        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;
    }

    public bool AddItem(ItemData item, int amount = 1)
    {
        if (item == null) return false;

        int remaining = amount;

        if (item.isStackable)
        {
            foreach (InventorySlot slot in slots)
            {
                if (remaining <= 0) break;
                if (!slot.IsEmpty() && slot.currentItem == item)
                {
                    int canAdd = item.maxStack - slot.quantity;
                    if (canAdd > 0)
                    {
                        int addAmount = Mathf.Min(canAdd, remaining);
                        slot.quantity += addAmount;
                        slot.UpdateUI();
                        remaining -= addAmount;
                    }
                }
            }
        }

        while (remaining > 0)
        {
            InventorySlot emptySlot = null;
            foreach (InventorySlot slot in slots)
            {
                if (slot.IsEmpty())
                {
                    emptySlot = slot;
                    break;
                }
            }

            if (emptySlot == null)
            {
                Debug.Log("��������� �����!");
                return false;
            }

            int addAmount = item.isStackable
                ? Mathf.Min(item.maxStack, remaining)
                : 1;

            emptySlot.SetItem(item, addAmount);
            remaining -= addAmount;
        }

        return true;
    }

    public bool RemoveItem(ItemData item, int amount = 1)
    {
        foreach (InventorySlot slot in slots)
        {
            if (!slot.IsEmpty() && slot.currentItem == item)
            {
                if (slot.quantity > amount)
                {
                    slot.quantity -= amount;
                    slot.UpdateUI();
                }
                else
                {
                    slot.ClearSlot();
                }
                return true;
            }
        }
        return false;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.I))
            ToggleInventory();
    }
}