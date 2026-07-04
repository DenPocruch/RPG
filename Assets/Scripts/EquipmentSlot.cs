using UnityEngine;
using UnityEngine.UI;

public class EquipmentSlot : MonoBehaviour
{
    [Header("Тип слота")]
    public EquipmentSlotType slotType;

    [Header("UI")]
    public Image iconImage;
    public Sprite emptySlotIcon; // иконка пустого слота (опционально)

    // Эффект редкости (находится автоматически, как в InventorySlot)
    private SlotRarityGlow rarityGlow;

    bool IsWeaponMirror => slotType == EquipmentSlotType.Weapon;

    void Start()
    {
        rarityGlow = GetComponentInChildren<SlotRarityGlow>();

        if (EquipmentManager.Instance != null)
            EquipmentManager.Instance.onEquipmentChanged += Refresh;

        if (IsWeaponMirror && HotbarManager.Instance != null)
            HotbarManager.Instance.onActiveItemChanged += OnActiveItemChanged;

        Refresh();
    }

    void OnDestroy()
    {
        if (EquipmentManager.Instance != null)
            EquipmentManager.Instance.onEquipmentChanged -= Refresh;

        if (IsWeaponMirror && HotbarManager.Instance != null)
            HotbarManager.Instance.onActiveItemChanged -= OnActiveItemChanged;
    }

    void OnActiveItemChanged(ItemData item)
    {
        Refresh();
    }

    // ═══════════════════════════════════════════════════════════
    // ОБНОВЛЕНИЕ ВНЕШНЕГО ВИДА
    // ═══════════════════════════════════════════════════════════
    public void Refresh()
    {
        ItemData item;

        if (IsWeaponMirror)
        {
            item = HotbarManager.Instance?.GetActiveItem();
            if (item != null &&
                item.itemType != ItemType.Weapon &&
                item.itemType != ItemType.RangedWeapon)
                item = null;
        }
        else
        {
            item = EquipmentManager.Instance?.GetEquipped(slotType);
        }

        if (item != null)
        {
            if (iconImage != null)
            {
                iconImage.sprite = item.icon;
                iconImage.enabled = true;
                iconImage.color = Color.white;
            }
        }
        else
        {
            if (iconImage != null)
            {
                if (emptySlotIcon != null)
                {
                    iconImage.sprite = emptySlotIcon;
                    iconImage.enabled = true;
                    iconImage.color = new Color(1f, 1f, 1f, 0.3f);
                }
                else
                {
                    iconImage.enabled = false;
                }
            }
        }

        // Эффект редкости — луч вместо цветной рамки
        if (rarityGlow == null) rarityGlow = GetComponentInChildren<SlotRarityGlow>();
        if (rarityGlow != null)
        {
            if (item == null) rarityGlow.Clear();
            else rarityGlow.SetItem(item);
        }
    }

    public ItemData GetCurrentItem()
    {
        if (IsWeaponMirror)
        {
            ItemData active = HotbarManager.Instance?.GetActiveItem();
            if (active != null &&
               (active.itemType == ItemType.Weapon ||
                active.itemType == ItemType.RangedWeapon))
                return active;
            return null;
        }
        return EquipmentManager.Instance?.GetEquipped(slotType);
    }

    public bool HasItem() => GetCurrentItem() != null;
}