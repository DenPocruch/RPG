using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// UI магазина. Слева список товаров, справа детали выбранного:
/// иконка, название, цена, выбор количества (+/-), итоговая стоимость, кнопка "Купить".
/// Открывается как повар — рюкзак сдвигается влево.
/// </summary>
public class ShopUI : MonoBehaviour
{
    public static ShopUI Instance;

    [Header("UI панели")]
    public GameObject shopPanel;
    public RectTransform shopRect;

    [Header("Список товаров")]
    public GameObject itemButtonPrefab;
    public Transform itemListContent;

    [Header("Детали выбранного товара")]
    public GameObject detailPanel;
    public Image detailIcon;
    public TMP_Text detailName;
    public TMP_Text detailDescription;
    public TMP_Text detailPrice;       // "Цена: 10g / шт"
    public TMP_Text quantityText;      // текущее выбранное количество
    public TMP_Text totalCostText;     // "Итого: 50g"
    public Button plusButton;
    public Button minusButton;
    public Button buyButton;
    public TMP_Text buyButtonText;

    [Header("Инфо")]
    public TMP_Text goldText; // "Золото: 480"
    public TMP_Text titleText; // заголовок окна ("Семена", "Инструменты"...)

    [Header("Позиции панели")]
    public float panelY = 47f;
    public float shiftDistance = 325f;
    public float shiftSpeed = 8f;

    private bool isOpen = false;
    private Vector2 targetPos;
    private Vector2 normalPos;

    private ShopManager.ShopItem[] currentStock; // товар ТЕКУЩЕГО торговца
    private ShopManager.ShopItem selectedItem;
    private int selectedQuantity = 1;
    private List<ShopItemButtonUI> itemButtons = new List<ShopItemButtonUI>();

    void Awake()
    {
        // Защита от дубликата: копия PersistentRoot при возврате в сцену
        // создаёт второй экземпляр — копию уничтожаем, оригинал живёт
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (shopPanel != null) shopPanel.SetActive(false);
    }

    void Start()
    {
        normalPos = new Vector2(0, panelY);
        if (shopRect != null)
        {
            shopRect.anchoredPosition = normalPos;
            targetPos = normalPos;
        }

        plusButton?.onClick.AddListener(OnPlusClick);
        minusButton?.onClick.AddListener(OnMinusClick);
        buyButton?.onClick.AddListener(OnBuyClick);

        if (CurrencyManager.Instance != null)
            CurrencyManager.Instance.onGoldChanged += _ => { UpdateGoldText(); RefreshDetail(); };

        if (detailPanel != null) detailPanel.SetActive(false);
    }

    void Update()
    {
        if (shopRect != null)
            shopRect.anchoredPosition = Vector2.Lerp(
                shopRect.anchoredPosition, targetPos, Time.deltaTime * shiftSpeed);
    }

    // ═══════════════════════════════════════════════════════════
    // ОТКРЫТИЕ / ЗАКРЫТИЕ
    // ═══════════════════════════════════════════════════════════

    /// <summary>Открыть магазин конкретного торговца со своим товаром.
    /// stock = null → берётся общий ассортимент ShopManager (старое поведение).</summary>
    public void Open(ShopManager.ShopItem[] stock = null, string title = null)
    {
        currentStock = (stock != null && stock.Length > 0)
            ? stock
            : (ShopManager.Instance != null ? ShopManager.Instance.itemsForSale : null);

        if (!string.IsNullOrEmpty(title) && titleText != null)
            titleText.text = title;

        shopPanel.SetActive(true);
        isOpen = true;

        InventoryPanelMover.Instance?.SetOffsetX(-shiftDistance);
        targetPos = new Vector2(shiftDistance, panelY);
        if (shopRect != null)
            shopRect.anchoredPosition = new Vector2(shiftDistance, panelY);

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.OpenInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        BuildItemList();
        UpdateGoldText();

        if (detailPanel != null) detailPanel.SetActive(false);
        selectedItem = null;
    }

    public void Close()
    {
        shopPanel.SetActive(false);
        isOpen = false;

        InventoryPanelMover.Instance?.ResetPosition();
        targetPos = normalPos;

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.CloseInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;
    }

    public bool IsOpen() => isOpen;

    // ═══════════════════════════════════════════════════════════
    // СПИСОК ТОВАРОВ
    // ═══════════════════════════════════════════════════════════
    void BuildItemList()
    {
        if (itemListContent == null || itemButtonPrefab == null)
            return;

        foreach (Transform child in itemListContent) Destroy(child.gameObject);
        itemButtons.Clear();

        if (currentStock == null) return;

        foreach (ShopManager.ShopItem si in currentStock)
        {
            if (si == null || si.item == null) continue;
            GameObject obj = Instantiate(itemButtonPrefab, itemListContent);
            ResetTransform(obj);
            ShopItemButtonUI btn = obj.GetComponent<ShopItemButtonUI>();
            btn.Setup(si);
            itemButtons.Add(btn);
        }
    }

    public void SelectItem(ShopManager.ShopItem item)
    {
        selectedItem = item;
        selectedQuantity = 1;

        foreach (ShopItemButtonUI btn in itemButtons)
            btn.SetSelected(btn.GetShopItem() == item);

        if (detailPanel != null) detailPanel.SetActive(item != null);
        RefreshDetail();
    }

    void RefreshDetail()
    {
        if (selectedItem == null || selectedItem.item == null || ShopManager.Instance == null)
            return;

        ItemData item = selectedItem.item;

        if (detailIcon != null) detailIcon.sprite = item.icon;
        if (detailName != null) detailName.text = item.itemName;
        if (detailDescription != null) detailDescription.text = item.description;

        int unitPrice = ShopManager.Instance.GetPrice(selectedItem);
        if (detailPrice != null) detailPrice.text = "Цена: " + unitPrice + "g / шт";

        if (quantityText != null) quantityText.text = selectedQuantity.ToString();

        int totalCost = unitPrice * selectedQuantity;
        if (totalCostText != null) totalCostText.text = "Итого: " + totalCost + "g";

        bool canAfford = CurrencyManager.Instance != null && CurrencyManager.Instance.Gold >= totalCost;
        if (buyButton != null)
        {
            buyButton.interactable = canAfford;
            if (buyButtonText != null)
                buyButtonText.text = canAfford ? "Купить" : "Не хватает золота";
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ВЫБОР КОЛИЧЕСТВА
    // ═══════════════════════════════════════════════════════════
    void OnPlusClick()
    {
        if (selectedItem == null) return;
        selectedQuantity = Mathf.Min(selectedQuantity + 1, 999);
        RefreshDetail();
    }

    void OnMinusClick()
    {
        if (selectedItem == null) return;
        selectedQuantity = Mathf.Max(selectedQuantity - 1, 1);
        RefreshDetail();
    }

    // ═══════════════════════════════════════════════════════════
    // ПОКУПКА
    // ═══════════════════════════════════════════════════════════
    void OnBuyClick()
    {
        if (selectedItem == null || ShopManager.Instance == null) return;

        bool ok = ShopManager.Instance.TryBuy(selectedItem, selectedQuantity);
        if (ok)
        {
            UpdateGoldText();
            RefreshDetail();

            // Сейв по событию: покупка (золото и инвентарь изменились)
            SaveManager.Instance?.Save();
        }
    }

    void UpdateGoldText()
    {
        if (goldText != null && CurrencyManager.Instance != null)
            goldText.text = "Золото: " + CurrencyManager.Instance.Gold;
    }

    void ResetTransform(GameObject obj)
    {
        RectTransform rt = obj.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchoredPosition = Vector2.zero;
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }
}