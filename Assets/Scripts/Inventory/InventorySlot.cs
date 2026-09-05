using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InventorySlot : MonoBehaviour
{
    [Header("������ �����")]
    public ItemData currentItem;
    public int quantity = 0;
    public int currentWater = 0;
    [Tooltip("Вес рыбы в кг (0 = нет). Рыба не стакается: 1 слот = 1 рыба.")]
    public float fishWeightKg = 0f;
    [Tooltip("Остаток забросов крючка (-1 = полный/не тронут). Крючки не стакаются: 1 слот = 1 крючок.")]
    public int hookCastsLeft = -1;

    [Header("UI �����")]
    public Image iconImage;
    public TMP_Text quantityText;

    [Header("��� �����")]
    public bool isHotbarSlot = false;
    public int slotIndex = 0;

    [HideInInspector] public InventorySlot linkedChestSlot = null;

    [Header("������������ (����� �� ����� �������, ����. ����� ���������)")]
    [Tooltip("���� true � ���� ����� ������� ������ ��� item.maxStack. ��� �������������� �� ������ ����� ������ ����������� �� ������ �������� maxStack ��������, ������� ������� �����.")]
    public bool allowOverflow = false;
    [Tooltip("0 = ��� ����������� ������")]
    public int overflowCapacity = 0;

    [Tooltip("���� false � ����� �� ����� ������� �������� ������� � ���� ���� drag&drop'�� (������ �������). ������������ ��� �������� ������� ���������� (�����/������) � ���� ����� ������ ���� �����������.")]
    public bool acceptsManualDeposit = true;

    [Header("������ �� ��������� ������� (��� ������� ���������/����� � �.�.)")]
    [Tooltip("����� = ��� �����������. ����� ��������� ������ �������� � ����� �� ItemData.resourceCategory (�������� \"Wood\" ��� \"Ore\")")]
    public string allowedResourceCategory = "";

    /// <summary>����� �� �������� ���� ������� � ���� (� ������ ������� ���������).</summary>
    public bool IsItemAllowed(ItemData item)
    {
        if (string.IsNullOrEmpty(allowedResourceCategory)) return true;
        if (item == null) return true;
        return item.resourceCategory == allowedResourceCategory;
    }

    // ������ �������� (������� ������������� ���� ���� �� �������)
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
        fishWeightKg = 0f;
        hookCastsLeft = -1;
        UpdateUI();
        SyncToChest();
    }

    public void SetItemWithWater(ItemData item, int amount, int water, float weightKg = 0f, int hookCasts = -1)
    {
        currentItem = item;
        quantity = amount;
        currentWater = water;
        fishWeightKg = weightKg;
        hookCastsLeft = hookCasts;
        UpdateUI();
        SyncToChest();
    }

    public void ClearSlot()
    {
        currentItem = null;
        quantity = 0;
        currentWater = 0;
        fishWeightKg = 0f;
        hookCastsLeft = -1;
        UpdateUI();
        SyncToChest();
    }

    void SyncToChest()
    {
        if (linkedChestSlot == null) return;
        linkedChestSlot.currentItem = currentItem;
        linkedChestSlot.quantity = quantity;
        linkedChestSlot.currentWater = currentWater;
        linkedChestSlot.fishWeightKg = fishWeightKg;
        linkedChestSlot.hookCastsLeft = hookCastsLeft;
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
        // ������ ��������
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