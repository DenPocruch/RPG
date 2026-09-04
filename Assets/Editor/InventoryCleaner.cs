using UnityEngine;
using UnityEditor;

public static class InventoryCleaner
{
    [MenuItem("Tools/Debug/Clear Player Inventory", validate = true)]
    public static bool ClearValidate()
    {
        return Application.isPlaying
            && HotbarManager.Instance != null
            && InventoryUI.Instance != null;
    }

    [MenuItem("Tools/Debug/Clear Player Inventory")]
    public static void Clear()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Debug] Очистка инвентаря работает только в Play-режиме");
            return;
        }

        int removed = 0;

        if (HotbarManager.Instance != null)
        {
            foreach (InventorySlot s in HotbarManager.Instance.slots)
            {
                if (s != null && !s.IsEmpty())
                {
                    s.ClearSlot();
                    removed++;
                }
            }
            // обновляем зеркало оружия и статы (без retap-побочек SetActiveSlot)
            HotbarManager.Instance.NotifyActiveItemChanged();
        }

        if (InventoryUI.Instance != null)
        {
            foreach (InventorySlot s in InventoryUI.Instance.slots)
            {
                if (s != null && !s.IsEmpty())
                {
                    s.ClearSlot();
                    removed++;
                }
            }
        }

        // сразу сохраняем, чтобы автосейв не вернул предметы
        if (SaveManager.Instance != null)
            SaveManager.Instance.Save();

        Debug.Log("[Debug] Инвентарь очищен, стаков удалено: " + removed);
    }
}
