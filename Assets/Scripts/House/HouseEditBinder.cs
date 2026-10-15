using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Биндер панели планировки дома (фаза 7; верстает владелец в SampleScene/Canvas).
/// ВАЖНО: скрипт вешать на ВСЕГДА АКТИВНЫЙ объект (напр. пустой HouseEditBinder
/// рядом с панелью), а НЕ на саму панель — иначе Awake не сработает пока панель
/// скрыта и код её не найдёт. Панель (panel) на старте скрыта, код сам откроет.
/// Всё перетаскиванием в инспекторе (паттерн TutorialTrackerBinder/ShopUI).
/// объект с этим скриптом — ребёнок Canvas, ссылки — на объекты панели.
/// Табы: Склад (своё) / Магазин (купить). Категории: Пол / Обои / Мебель / Декор.
/// Слот-префаб: корень + HousePanelSlot (icon/price/count на нём же).
/// Деталь: иконка + название + описание + счётчик/цена + кнопка Купить (только Магазин).
/// goldLabel опционален (null = не показываем, голда видна в HUD).
/// </summary>
public class HouseEditBinder : MonoBehaviour
{
    [Header("Корень панели + крестик")]
    public GameObject panel;
    public Button closeButton;

    [Header("Табы")]
    public Button tabStockButton;
    public Button tabShopButton;

    [Header("Категории: Пол / Обои / Мебель / Декор")]
    public Button catFloorButton;
    public Button catWallpaperButton;
    public Button catFurnitureButton;
    public Button catDecorButton;

    [Header("Сетка + префаб слота (с HousePanelSlot)")]
    public Transform contentGrid;
    public GameObject slotPrefab;

    [Header("Карточка товара")]
    public Image detailIcon;
    public TMP_Text detailName;
    public TMP_Text detailDesc;
    public TMP_Text detailCount;
    public TMP_Text detailPrice;
    public Button buyButton;

    [Header("Подсказка снизу + голда (опц.)")]
    public TMP_Text hintLabel;
    public TMP_Text goldLabel;

    void Awake()
    {
        ApplyBind();
    }

    public void ApplyBind()
    {
        if (HouseEditUI.Instance != null)
            HouseEditUI.Instance.Bind(this);
    }
}
