using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Кнопка одного товара в списке магазина.
/// Клик → ShopUI показывает детали и выбор количества.
/// </summary>
public class ShopItemButtonUI : MonoBehaviour
{
    [Header("UI")]
    public Image iconImage;
    public TMP_Text nameText;
    public TMP_Text priceText;
    public Image background;

    [Header("Цвета выделения")]
    public Color colorSelected = new Color(0.9f, 0.8f, 0.3f, 1f);
    public Color colorNormal = new Color(1f, 1f, 1f, 0.15f);

    private ShopManager.ShopItem shopItem;

    public void Setup(ShopManager.ShopItem item)
    {
        shopItem = item;

        if (iconImage != null)
            iconImage.sprite = item.item != null ? item.item.icon : null;

        if (nameText != null)
            nameText.text = item.item != null ? item.item.itemName : "???";

        if (priceText != null)
        {
            int price = ShopManager.Instance != null
                ? ShopManager.Instance.GetPrice(item)
                : item.price;
            priceText.text = price + "g";
        }

        SetSelected(false);
    }

    public void OnClick()
    {
        if (ShopUI.Instance != null)
            ShopUI.Instance.SelectItem(shopItem);
    }

    public void SetSelected(bool selected)
    {
        if (background != null)
            background.color = selected ? colorSelected : colorNormal;
    }

    public ShopManager.ShopItem GetShopItem() => shopItem;
}