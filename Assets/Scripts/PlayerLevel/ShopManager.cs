using UnityEngine;

/// <summary>
/// ������ ��������: ������ ������� � ��������������� ������.
/// �������� �� NPC ��������. ����������� ����� � �������� ����� ������� ������.
/// </summary>
public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance;

    [System.Serializable]
    public class ShopItem
    {
        public ItemData item;
        public int price = 10; // ���� � 1 �����
        [Tooltip("������ ��������������� (unlocksFeature ������ ������). ������ ��� ������ �� ��������� � ������������.")]
        public string unlockTag = "";
    }

    [Header("����������� ��������")]
    public ShopItem[] itemsForSale;

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

    // ���� ������ � ������ ������ �� ������� (�������������� ReduceServiceCost)
    public int GetPrice(ShopItem shopItem)
    {
        if (shopItem == null || shopItem.price <= 0) return 0;

        float discount = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetServiceCostReduction()
            : 0f;

        return Mathf.Max(1, Mathf.RoundToInt(shopItem.price * (1f - discount / 100f)));
    }

    /// <summary>������ amount ���� ������. ���������� true ��� ������.</summary>
    public bool TryBuy(ShopItem shopItem, int amount)
    {
        if (shopItem == null || shopItem.item == null || amount <= 0) return false;
        if (InventoryUI.Instance == null) return false;

        int totalCost = GetPrice(shopItem) * amount;

        // ��������� ������
        if (CurrencyManager.Instance == null || CurrencyManager.Instance.Gold < totalCost)
        {
            Debug.Log("[�������] ������������ ������! ����� " + totalCost);
            return false;
        }

        // ��������� ����� � ��������� (������� ��������)
        bool added = InventoryUI.Instance.AddItem(shopItem.item, amount);
        if (!added)
        {
            Debug.Log("[�������] ��������� �����!");
            return false;
        }

        // ��������� ������ ������ ����� ��������� ����������
        CurrencyManager.Instance.SpendGold(totalCost);
        Debug.Log("[�������] ������� " + shopItem.item.itemName + " x" + amount + " �� " + totalCost + "g");
        return true;
    }
}