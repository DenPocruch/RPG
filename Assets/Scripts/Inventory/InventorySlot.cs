using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventorySlot : MonoBehaviour
{
    [Header("Данные слота")]
    public ItemData currentItem;
    public int quantity = 0;
    public int currentWater = 0;

    [Header("UI слота")]
    public Image iconImage;
    public TMP_Text quantityText;

    [Header("Тип слота")]
    public bool isHotbarSlot = false;
    public int slotIndex = 0;

    [HideInInspector] public InventorySlot linkedChestSlot = null;

    [Header("Переполнение (склад со своим лимитом, напр. выход лесопилки)")]
    [Tooltip("Если true — слот может хранить БОЛЬШЕ чем item.maxStack. При перетаскивании ИЗ такого слота игроку переносится не больше обычного maxStack предмета, остаток остаётся здесь.")]
    public bool allowOverflow = false;
    [Tooltip("0 = без ограничения сверху")]
    public int overflowCapacity = 0;

    [Tooltip("Если false — игрок НЕ может вручную положить предмет в этот слот drag&drop'ом (только система). Используется для выходных складов мастерских (доски/слитки) — туда кладёт только сама переработка.")]
    public bool acceptsManualDeposit = true;

    [Header("Фильтр по категории ресурса (для складов лесопилки/шахты и т.д.)")]
    [Tooltip("Пусто = без ограничения. Иначе принимает только предметы с таким же ItemData.resourceCategory (например \"Wood\" или \"Ore\")")]
    public string allowedResourceCategory = "";

    /// <summary>Можно ли положить этот предмет в слот (с учётом фильтра категории).</summary>
    public bool IsItemAllowed(ItemData item)
    {
        if (string.IsNullOrEmpty(allowedResourceCategory)) return true;
        if (item == null) return true;
        return item.resourceCategory == allowedResourceCategory;
    }

    // Эффект редкости (найдётся автоматически если есть на объекте)
    private SlotRarityGlow rarityGlow;

    void Start()
    {
        rarityGlow = GetComponentInChildren<SlotRarityGlow>();
        UpdateUI();
    }

    public void SetItem(ItemData item, int amount = 1)
    {
        currentItem = item;
        quantity = amount;
        currentWater = 0;
        UpdateUI();
        SyncToChest();
    }

    public void SetItemWithWater(ItemData item, int amount, int water)
    {
        currentItem = item;
        quantity = amount;
        currentWater = water;
        UpdateUI();
        SyncToChest();
    }

    public void ClearSlot()
    {
        currentItem = null;
        quantity = 0;
        currentWater = 0;
        UpdateUI();
        SyncToChest();
    }

    void SyncToChest()
    {
        if (linkedChestSlot == null) return;
        linkedChestSlot.currentItem = currentItem;
        linkedChestSlot.quantity = quantity;
        linkedChestSlot.currentWater = currentWater;
    }

    public bool IsEmpty() => currentItem == null;
    public bool IsWateringCan() => currentItem != null && currentItem.itemType == ItemType.WateringCan;

    public int GetMaxWater()
    {
        if (!IsWateringCan()) return 0;
        int bonus = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetBonusMaxWater()
            : 0;
        return currentItem.maxWater + bonus;
    }

    public bool HasWater() => IsWateringCan() && currentWater > 0;

    public bool UseWater()
    {
        if (!HasWater()) return false;
        currentWater--;
        UpdateUI();
        return true;
    }

    public void FillWater()
    {
        if (!IsWateringCan()) return;
        currentWater = GetMaxWater();
        UpdateUI();
    }

    public void UpdateUI()
    {
        // Эффект редкости
        if (rarityGlow == null) rarityGlow = GetComponentInChildren<SlotRarityGlow>();
        if (rarityGlow != null)
        {
            if (IsEmpty()) rarityGlow.Clear();
            else rarityGlow.SetItem(currentItem);
        }

        if (iconImage == null) return;

        if (currentItem != null && currentItem.icon != null)
        {
            iconImage.sprite = currentItem.icon;
            iconImage.enabled = true;
        }
        else
        {
            iconImage.sprite = null;
            iconImage.enabled = false;
        }

        if (quantityText != null)
        {
            if (currentItem != null && quantity > 1 && !IsWateringCan())
            {
                quantityText.text = quantity.ToString();
                quantityText.enabled = true;
            }
            else
            {
                quantityText.text = "";
                quantityText.enabled = false;
            }
        }
    }
}