using UnityEngine;

public class SiloInteraction : MonoBehaviour, IInteractable
{
    [Header("Размер силоса")]
    public int siloSize = 20;

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