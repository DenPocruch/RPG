using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class HotbarManager : MonoBehaviour
{
    public static HotbarManager Instance;

    [Header("Слоты хотбара")]
    public InventorySlot[] slots;

    [Header("Цвет активного слота")]
    public Color activeSlotColor = new Color(1f, 0.8f, 0f, 1f);
    public Color normalSlotColor = new Color(1f, 1f, 1f, 1f);

    [Header("Текущий активный слот")]
    public int activeSlotIndex = 0;

    [Header("Тестовые предметы (только для разработки)")]
    public List<TestItem> testItems = new List<TestItem>();

    // Событие — активный предмет изменился
    // Подписчики: EquipmentSlot (слот оружия), PlayerStats
    public System.Action<ItemData> onActiveItemChanged;

    void Awake() { Instance = this; }

    void Start()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].slotIndex = i;
            slots[i].isHotbarSlot = true;
        }

        SetActiveSlot(0);
        GiveTestItems();
    }

    void GiveTestItems()
    {
        for (int i = 0; i < testItems.Count; i++)
        {
            if (testItems[i].item == null) continue;
            if (i >= slots.Length) break;
            slots[i].SetItem(testItems[i].item, testItems[i].amount);
        }
    }

    public void SetActiveSlot(int index)
    {
        if (index < 0 || index >= slots.Length) return;

        // Снимаем выделение со всех
        for (int i = 0; i < slots.Length; i++)
        {
            Image img = slots[i].GetComponent<Image>();
            if (img != null) img.color = normalSlotColor;
        }

        activeSlotIndex = index;

        // Выделяем активный
        Image activeImg = slots[activeSlotIndex].GetComponent<Image>();
        if (activeImg != null) activeImg.color = activeSlotColor;

        // Уведомляем всех подписчиков об изменении активного предмета
        NotifyActiveItemChanged();
    }

    public ItemData GetActiveItem()
    {
        if (slots[activeSlotIndex].IsEmpty()) return null;
        return slots[activeSlotIndex].currentItem;
    }

    public InventorySlot GetActiveSlot()
    {
        if (activeSlotIndex < 0 || activeSlotIndex >= slots.Length) return null;
        return slots[activeSlotIndex];
    }

    public void SetItemInSlot(int index, ItemData item, int amount = 1)
    {
        if (index < 0 || index >= slots.Length) return;
        slots[index].SetItem(item, amount);
        // Если изменился активный слот — уведомляем
        if (index == activeSlotIndex)
            NotifyActiveItemChanged();
    }

    /// <summary>
    /// Вызывается когда содержимое активного слота изменилось.
    /// Например: игрок переложил меч из активного слота.
    /// </summary>
    public void NotifyActiveItemChanged()
    {
        ItemData active = GetActiveItem();
        onActiveItemChanged?.Invoke(active);

        // Пересчитываем бонусы от оружия в PlayerStats
        if (PlayerStats.Instance != null)
            PlayerStats.Instance.OnActiveWeaponChanged(active);
    }
}

[System.Serializable]
public class TestItem
{
    public ItemData item;
    public int amount = 1;
}