using UnityEngine;

/// <summary>
/// Путеводитель: выдача квестовых предметов СРАЗУ В ХОТБААР (а не в рюкзак —
/// новичок не полезет искать). Порядок: добрать стак в хотбаре → пустой слот
/// хотбара → рюкзак. select=true — сразу взять в руки (меч, мотыга, хлеб).
/// </summary>
public static class QuestGive
{
    // Только в рюкзак (урок хотбара: игрок сам перетаскивает). Мимо хотбара.
    public static void GiveToPack(ItemData item, int count = 1)
    {
        if (item == null || count <= 0 || InventoryUI.Instance == null) return;
        InventoryUI.Instance.AddItem(item, count);
        ActionLogUI.Show("[Рюкзак] Получено: " + item.itemName + (count > 1 ? " ×" + count : ""));
    }

    // Лежит ли предмет в хотбаре (урок ui_hotbar)
    public static bool HasHotbarItem(string assetName)
    {
        if (string.IsNullOrEmpty(assetName)) return false;
        var hotbar = HotbarManager.Instance;
        if (hotbar == null || hotbar.slots == null) return false;
        foreach (var s in hotbar.slots)
            if (s != null && !s.IsEmpty() && s.currentItem != null
                && s.currentItem.name == assetName) return true;
        return false;
    }

    // Есть ли предмет хоть где-то (хотбар + рюкзак). Проверка дублей выдачи.
    public static bool HasItem(string assetName)
    {
        if (string.IsNullOrEmpty(assetName)) return false;
        var hotbar = HotbarManager.Instance;
        if (hotbar != null && hotbar.slots != null)
            foreach (var s in hotbar.slots)
                if (s != null && !s.IsEmpty() && s.currentItem != null
                    && s.currentItem.name == assetName) return true;
        var inv = InventoryUI.Instance;
        if (inv != null && inv.slots != null)
            foreach (var s in inv.slots)
                if (s != null && !s.IsEmpty() && s.currentItem != null
                    && s.currentItem.name == assetName) return true;
        return false;
    }

    public static void Give(ItemData item, int count = 1, bool select = false)
    {
        if (item == null || count <= 0) return;
        int left = count;
        int selectSlot = -1;
        var hotbar = HotbarManager.Instance;

        if (hotbar != null && hotbar.slots != null)
        {
            // 1. Добить стак тем же предметом
            if (item.isStackable)
            {
                foreach (var s in hotbar.slots)
                {
                    if (left <= 0) break;
                    if (s == null || s.IsEmpty() || s.currentItem != item) continue;
                    int room = Mathf.Max(0, item.maxStack - s.quantity);
                    int put = Mathf.Min(room, left);
                    if (put > 0)
                    {
                        s.quantity += put;
                        s.UpdateUI();
                        left -= put;
                        selectSlot = s.slotIndex;
                    }
                }
            }
            // 2. Пустые слоты хотбара
            foreach (var s in hotbar.slots)
            {
                if (left <= 0) break;
                if (s == null || !s.IsEmpty()) continue;
                int put = item.isStackable ? Mathf.Min(item.maxStack, left) : 1;
                hotbar.SetItemInSlot(s.slotIndex, item, put);
                left -= put;
                selectSlot = s.slotIndex;
            }
        }
        // 3. Остаток — в рюкзак
        if (left > 0 && InventoryUI.Instance != null)
        {
            InventoryUI.Instance.AddItem(item, left);
            selectSlot = -1; // в рюкзаке — не выбираем
        }
        // 4. Взять в руки
        if (select && selectSlot >= 0 && hotbar != null)
            hotbar.SetActiveSlot(selectSlot);

        ActionLogUI.Show("[Рюкзак] Получено: " + item.itemName + (count > 1 ? " ×" + count : ""));
    }
}
