using UnityEngine;
using System.Collections.Generic;

public class SiloInteraction : MonoBehaviour, IInteractable, ISaveable
{
    [Header("Размер силоса")]
    public int siloSize = 20;

    [Header("Уникальный ID (для сохранения — если силосов несколько, задай разные)")]
    [Tooltip("Пусто = ID из позиции силоса на карте.")]
    public string siloId = "";

    [Header("Стак в силосе")]
    public int siloMaxStack = 50;

    [Header("Спрайты")]
    public Sprite spriteOpen;
    public Sprite spriteClosed;

    private static readonly ItemType[] allowedTypes = new ItemType[]
    {
        ItemType.Crop,
        ItemType.Seed
    };

    private InventorySlot[] siloSlots;
    private SpriteRenderer sr;
    private bool isOpen = false;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();

        siloSlots = new InventorySlot[siloSize];
        for (int i = 0; i < siloSize; i++)
        {
            GameObject slotObj = new GameObject("SiloDataSlot_" + i);
            slotObj.transform.SetParent(transform);
            slotObj.SetActive(false);
            InventorySlot slot = slotObj.AddComponent<InventorySlot>();
            slot.slotIndex = i;
            siloSlots[i] = slot;
        }

        SetSprite(false);

        SaveManager.Instance?.Register(this);
        SaveManager.Instance?.LoadInto(this);
    }

    void OnDestroy()
    {
        // Отписка при выгрузке сцены — не держим ссылку на уничтоженный объект
        SaveManager.Instance?.Unregister(this);
    }

    // ─── ISaveable ─────────────────────────────────────────────
    public string SaveKey => "silo_" + (string.IsNullOrEmpty(siloId) ? transform.position.ToString() : siloId);

    [System.Serializable]
    private class SiloSlotSave { public int index; public string itemName; public int quantity; public int water; }
    [System.Serializable]
    private class SiloSave { public List<SiloSlotSave> slots = new List<SiloSlotSave>(); }

    public string CaptureState()
    {
        SiloSave save = new SiloSave();
        for (int i = 0; i < siloSlots.Length; i++)
        {
            InventorySlot s = siloSlots[i];
            if (s == null || s.IsEmpty()) continue;
            save.slots.Add(new SiloSlotSave
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
        SiloSave save = JsonUtility.FromJson<SiloSave>(json);
        if (save == null) return;

        foreach (InventorySlot s in siloSlots)
            if (s != null) s.ClearSlot();

        foreach (SiloSlotSave ss in save.slots)
        {
            if (ss.index < 0 || ss.index >= siloSlots.Length) continue;
            ItemData item = ItemDatabase.Find(ss.itemName);
            if (item == null) { Debug.LogWarning("[Save] Предмет в силосе не найден: " + ss.itemName); continue; }
            siloSlots[ss.index].SetItemWithWater(item, ss.quantity, ss.water);
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

    public static bool IsAllowed(ItemData item)
    {
        if (item == null) return false;
        foreach (ItemType t in allowedTypes)
            if (item.itemType == t) return true;
        return false;
    }

    public int GetSiloMaxStack() => siloMaxStack;

    void Open()
    {
        isOpen = true;
        SetSprite(true);

        if (ChestUI.Instance != null)
            ChestUI.Instance.OpenSilo(siloSlots, this);
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