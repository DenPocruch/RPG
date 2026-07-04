using UnityEngine;

public class ChestInteraction : MonoBehaviour, IInteractable
{
    [Header("Размер сундука")]
    public int chestSize = 20;

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