using UnityEngine;
using System.Collections.Generic;

public class ChestInteraction : MonoBehaviour, IInteractable, ISaveable
{
    [Header("Размер сундука")]
    public int chestSize = 20;

    [Header("Уникальный ID (для сохранения — если сундуков несколько, задай разные ID)")]
    [Tooltip("Пусто = ID возьмётся из позиции сундука на карте. Если ты будешь двигать сундук в редакторе — лучше задать ID вручную, чтобы сохранение не потерялось.")]
    public string chestId = "";

    [Header("Спрайты")]
    public Sprite spriteOpen;
    public Sprite spriteClosed;

    private InventorySlot[] chestSlots;
    private SpriteRenderer sr;
    private bool isOpen = false;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();

        chestSlots = new InventorySlot[chestSize];
        for (int i = 0; i < chestSize; i++)
        {
            GameObject slotObj = new GameObject("ChestDataSlot_" + i);
            slotObj.transform.SetParent(transform);
            slotObj.SetActive(false);
            InventorySlot slot = slotObj.AddComponent<InventorySlot>();
            slot.slotIndex = i;
            chestSlots[i] = slot;
        }

        SetSprite(false);

        SaveManager.Instance?.Register(this);
        SaveManager.Instance?.LoadInto(this);
    }

    // ─── ISaveable ─────────────────────────────────────────────
    // Ключ уникален для каждого сундука — по chestId, или по позиции если ID не задан
    public string SaveKey => "chest_" + (string.IsNullOrEmpty(chestId) ? transform.position.ToString() : chestId);

    [System.Serializable]
    private class ChestSlotSave
    {
        public int index;
        public string itemName;
        public int quantity;
        public int water;
    }
    [System.Serializable]
    private class ChestSave { public List<ChestSlotSave> slots = new List<ChestSlotSave>(); }

    public string CaptureState()
    {
        ChestSave save = new ChestSave();
        for (int i = 0; i < chestSlots.Length; i++)
        {
            InventorySlot s = chestSlots[i];
            if (s == null || s.IsEmpty()) continue;
            save.slots.Add(new ChestSlotSave
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
        ChestSave save = JsonUtility.FromJson<ChestSave>(json);
        if (save == null) return;

        foreach (InventorySlot s in chestSlots)
            if (s != null) s.ClearSlot();

        foreach (ChestSlotSave ss in save.slots)
        {
            if (ss.index < 0 || ss.index >= chestSlots.Length) continue;
            ItemData item = ItemDatabase.Find(ss.itemName);
            if (item == null)
            {
                Debug.LogWarning("[Save] Предмет в сундуке не найден: " + ss.itemName);
                continue;
            }
            chestSlots[ss.index].SetItemWithWater(item, ss.quantity, ss.water);
        }
    }

    // ── IInteractable ──────────────────────────────────────────
    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        if (isOpen) Close();
        else Open();
    }
    // ───────────────────────────────────────────────────────────

    void Open()
    {
        isOpen = true;
        SetSprite(true);

        if (ChestUI.Instance != null)
            ChestUI.Instance.OpenChest(chestSlots, this);
    }

    public void Close()
    {
        isOpen = false;
        SetSprite(false);

        if (ChestUI.Instance != null && ChestUI.Instance.IsOpen())
            ChestUI.Instance.CloseChest();
    }

    public void ForceClose()
    {
        isOpen = false;
        SetSprite(false);
    }

    void SetSprite(bool open)
    {
        if (sr == null) return;
        if (open && spriteOpen != null) sr.sprite = spriteOpen;
        if (!open && spriteClosed != null) sr.sprite = spriteClosed;
    }
}