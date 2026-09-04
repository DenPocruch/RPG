using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Центральный менеджер экипировки.
/// Хранит 12 слотов (Helmet, Armor, Pants, Boots, Gloves, Weapon, Shield,
/// Ring1, Ring2, Earrings, Bracelet, Amulet).
/// Доступ через EquipmentManager.Instance.
/// </summary>
public class EquipmentManager : MonoBehaviour, ISaveable
{
    public static EquipmentManager Instance { get; private set; }

    // Словарь: тип слота → надетый предмет
    private Dictionary<EquipmentSlotType, ItemData> equippedItems
        = new Dictionary<EquipmentSlotType, ItemData>();

    // Событие — экипировка изменилась (UI подписывается)
    public System.Action onEquipmentChanged;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Инициализируем все слоты пустыми
        foreach (EquipmentSlotType slot in System.Enum.GetValues(typeof(EquipmentSlotType)))
        {
            if (slot == EquipmentSlotType.None) continue;
            equippedItems[slot] = null;
        }

        SaveManager.Instance?.Register(this);
    }

    void Start()
    {
        // Грузим ПОСЛЕ инициализации PlayerStats (он в Awake),
        // чтобы бонусы экипировки корректно легли на базовые статы
        SaveManager.Instance?.LoadInto(this);
    }

    // ─── ISaveable ─────────────────────────────────────────────
    [System.Serializable] private class EquipSave { public string slotType; public string itemName; }
    [System.Serializable] private class EquipmentSave { public List<EquipSave> items = new List<EquipSave>(); }

    public string SaveKey => "equipment";

    public string CaptureState()
    {
        EquipmentSave save = new EquipmentSave();
        foreach (var kvp in equippedItems)
        {
            if (kvp.Value == null) continue;
            save.items.Add(new EquipSave
            {
                slotType = kvp.Key.ToString(),
                itemName = kvp.Value.name
            });
        }
        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        EquipmentSave save = JsonUtility.FromJson<EquipmentSave>(json);
        if (save == null) return;

        // Снимаем всё
        foreach (EquipmentSlotType slot in System.Enum.GetValues(typeof(EquipmentSlotType)))
        {
            if (slot == EquipmentSlotType.None) continue;
            equippedItems[slot] = null;
        }

        // Надеваем сохранённое
        foreach (EquipSave es in save.items)
        {
            ItemData item = ItemDatabase.Find(es.itemName);
            if (item == null) { Debug.LogWarning("[Save] Экипировка не найдена: " + es.itemName); continue; }

            if (System.Enum.TryParse(es.slotType, out EquipmentSlotType slotType))
                equippedItems[slotType] = item;
        }

        // Пересчитываем характеристики с восстановленной экипировкой
        if (PlayerStats.Instance != null)
            PlayerStats.Instance.RecalculateBonuses(GetAllEquipped());

        onEquipmentChanged?.Invoke();
    }

    // ═══════════════════════════════════════════════════════════
    // ОСНОВНЫЕ ОПЕРАЦИИ
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Надеть предмет. Если в слоте уже что-то — возвращает старый предмет
    /// чтобы вернуть в инвентарь.
    /// </summary>
    public ItemData Equip(ItemData item, EquipmentSlotType targetSlot)
    {
        if (item == null || !item.IsEquipment) return null;

        // Замок древа: медь+ надевается только после перка
        if (!EquipmentLocks.IsUnlocked(item))
        {
            ActionLogUI.Show(EquipmentLocks.LockMessage(item));
            return null;
        }

        // Проверка совместимости
        if (!IsSlotCompatible(item, targetSlot))
        {
            Debug.Log("[Equipment] " + item.itemName + " не подходит для слота " + targetSlot);
            return null;
        }

        // Запоминаем что было в слоте — вернём в инвентарь
        ItemData previous = equippedItems[targetSlot];

        // Надеваем новый
        equippedItems[targetSlot] = item;

        Debug.Log("[Equipment] Надето: " + item.itemName + " в слот " + targetSlot);

        Refresh();
        return previous;
    }

    /// <summary>
    /// Снять предмет из слота. Возвращает снятый предмет.
    /// </summary>
    public ItemData Unequip(EquipmentSlotType slot)
    {
        if (!equippedItems.ContainsKey(slot)) return null;

        ItemData removed = equippedItems[slot];
        equippedItems[slot] = null;

        if (removed != null)
            Debug.Log("[Equipment] Снято: " + removed.itemName);

        Refresh();
        return removed;
    }

    /// <summary>Получить предмет из слота.</summary>
    public ItemData GetEquipped(EquipmentSlotType slot)
    {
        return equippedItems.ContainsKey(slot) ? equippedItems[slot] : null;
    }

    /// <summary>Все надетые предметы (для PlayerStats).</summary>
    public List<ItemData> GetAllEquipped()
    {
        List<ItemData> list = new List<ItemData>();
        foreach (var kvp in equippedItems)
            if (kvp.Value != null) list.Add(kvp.Value);
        return list;
    }

    // ═══════════════════════════════════════════════════════════
    // СОВМЕСТИМОСТЬ ПРЕДМЕТА И СЛОТА
    // ═══════════════════════════════════════════════════════════
    public bool IsSlotCompatible(ItemData item, EquipmentSlotType slot)
    {
        if (item == null) return false;

        // Слот оружия — только зеркало хотбара, нельзя надеть вручную
        if (slot == EquipmentSlotType.Weapon) return false;

        // Ring (в ItemData) лезет в Ring1 или Ring2 (в UI)
        if (item.equipSlot == EquipmentSlotType.Ring ||
            item.equipSlot == EquipmentSlotType.Ring1 ||
            item.equipSlot == EquipmentSlotType.Ring2)
        {
            return slot == EquipmentSlotType.Ring1 || slot == EquipmentSlotType.Ring2;
        }

        // Остальное — точное совпадение
        return item.equipSlot == slot;
    }

    // ═══════════════════════════════════════════════════════════
    // ПЕРЕСЧЁТ + УВЕДОМЛЕНИЕ
    // ═══════════════════════════════════════════════════════════
    void Refresh()
    {
        // Пересчитываем характеристики игрока
        if (PlayerStats.Instance != null)
            PlayerStats.Instance.RecalculateBonuses(GetAllEquipped());

        // Уведомляем UI
        onEquipmentChanged?.Invoke();
    }
}