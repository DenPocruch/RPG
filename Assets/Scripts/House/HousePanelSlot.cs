using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// Слот сетки панели планировки (фаза 7). Висит на slotPrefab, поля — теми же
/// перетаскиваниями в инспекторе префаба: icon + цена (Магазин) + счётчик (Склад).
/// Тап = выбрать в карточку, драг = потащить в дом (только вкладка Склад).
/// </summary>
public class HousePanelSlot : MonoBehaviour,
    IPointerClickHandler, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Иконка товара")]
    public Image icon;
    [Header("Цена (Магазин)")]
    public TMP_Text priceText;
    [Header("Счётчик (Склад)")]
    public TMP_Text countText;

    [HideInInspector]
    public int rowIndex = -1;

    bool wasDragging;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (wasDragging) { wasDragging = false; return; }
        if (HouseEditUI.Instance != null)
            HouseEditUI.Instance.SlotClicked(rowIndex);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        wasDragging = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        wasDragging = true;
        if (HouseEditUI.Instance != null)
            HouseEditUI.Instance.BeginPanelDrag(rowIndex, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (HouseEditUI.Instance != null)
            HouseEditUI.Instance.PanelDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (HouseEditUI.Instance != null)
            HouseEditUI.Instance.EndPanelDrag(eventData);
    }
}
