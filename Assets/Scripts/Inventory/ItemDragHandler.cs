using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class ItemDragHandler : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    private InventorySlot parentSlot;
    private Canvas canvas;
    private CanvasGroup canvasGroup;

    private Image GetItemIcon()
    {
        if (parentSlot != null && parentSlot.iconImage != null)
            return parentSlot.iconImage;
        return GetComponent<Image>();
    }

    private static GameObject dragObject;
    private static InventorySlot sourceSlot;
    private static EquipmentSlot sourceEquipSlot;

    private Vector2 pointerDownPos;
    private bool didDrag = false;
    private const float DRAG_THRESHOLD = 10f;

    void Awake()
    {
        parentSlot = GetComponentInParent<InventorySlot>();
        canvas = GetComponentInParent<Canvas>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (didDrag) { didDrag = false; return; }

        if (parentSlot != null && parentSlot.isHotbarSlot)
        {
            HotbarManager.Instance?.SetActiveSlot(parentSlot.slotIndex);
            return;
        }

        ItemData item = GetItemForTooltip();
        if (item == null) return;

        if (ItemTooltip.Instance != null)
            ItemTooltip.Instance.Show(item, eventData.position);
    }

    ItemData GetItemForTooltip()
    {
        EquipmentSlot equipSlot = GetComponentInParent<EquipmentSlot>();
        if (equipSlot != null)
            return equipSlot.GetCurrentItem();

        if (parentSlot != null && !parentSlot.IsEmpty())
            return parentSlot.currentItem;

        return null;
    }

    // ─── НАЧАЛО DRAG ───────────────────────────────────────────────────
    public void OnBeginDrag(PointerEventData eventData)
    {
        didDrag = true;
        if (ItemTooltip.Instance != null) ItemTooltip.Instance.Hide();

        sourceEquipSlot = null;
        sourceSlot = null;

        EquipmentSlot equipSlot = GetComponentInParent<EquipmentSlot>();
        if (equipSlot != null)
        {
            if (equipSlot.slotType == EquipmentSlotType.Weapon) return;

            ItemData equipped = equipSlot.GetCurrentItem();
            if (equipped == null) return;

            sourceEquipSlot = equipSlot;
            StartDragVisual(equipped.icon);
            return;
        }

        if (parentSlot == null || parentSlot.IsEmpty()) return;
        sourceSlot = parentSlot;
        Image icon = GetItemIcon();
        if (icon == null || icon.sprite == null) return;
        StartDragVisual(icon.sprite);
        icon.color = new Color(1, 1, 1, 0.4f);
        canvasGroup.blocksRaycasts = false;
    }

    void StartDragVisual(Sprite sprite)
    {
        dragObject = new GameObject("DragIcon");
        dragObject.transform.SetParent(canvas.transform, false);
        dragObject.transform.SetAsLastSibling();
        Image img = dragObject.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        dragObject.GetComponent<RectTransform>().sizeDelta = new Vector2(60, 60);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragObject == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.GetComponent<RectTransform>(),
            eventData.position, canvas.worldCamera,
            out Vector2 localPos);
        dragObject.GetComponent<RectTransform>().localPosition = localPos;
    }

    // ─── КОНЕЦ DRAG ────────────────────────────────────────────────────
    public void OnEndDrag(PointerEventData eventData)
    {
        Image icon = GetItemIcon();
        if (icon != null) icon.color = Color.white;
        canvasGroup.blocksRaycasts = true;

        if (dragObject != null) { Destroy(dragObject); dragObject = null; }

        if (sourceEquipSlot != null)
        {
            HandleDragFromEquipment(eventData);
            sourceEquipSlot = null;
            return;
        }

        if (sourceSlot != null)
        {
            HandleDragFromInventory(eventData);
            sourceSlot = null;
        }
    }

    // Если drag&drop затронул активный слот хотбара — пересчитываем урон/статы
    void NotifyHotbarIfNeeded(InventorySlot a, InventorySlot b)
    {
        if (HotbarManager.Instance == null) return;

        bool affectsActive =
            (a != null && a.isHotbarSlot && a.slotIndex == HotbarManager.Instance.activeSlotIndex) ||
            (b != null && b.isHotbarSlot && b.slotIndex == HotbarManager.Instance.activeSlotIndex);

        if (affectsActive)
            HotbarManager.Instance.NotifyActiveItemChanged();
    }

    // ─────────────────────────────────────────────────────────────────
    // ИЗ СЛОТА ЭКИПИРОВКИ
    // ─────────────────────────────────────────────────────────────────
    void HandleDragFromEquipment(PointerEventData eventData)
    {
        ItemData equipped = sourceEquipSlot.GetCurrentItem();
        if (equipped == null) return;

        EquipmentSlot equipTarget = GetEquipmentSlotUnderPointer(eventData);
        if (equipTarget != null && equipTarget != sourceEquipSlot)
        {
            if (equipTarget.slotType == EquipmentSlotType.Weapon) return;

            if (EquipmentManager.Instance != null &&
                EquipmentManager.Instance.IsSlotCompatible(equipped, equipTarget.slotType))
            {
                ItemData itemInTarget = equipTarget.GetCurrentItem();
                EquipmentManager.Instance.Unequip(sourceEquipSlot.slotType);

                if (itemInTarget != null)
                    EquipmentManager.Instance.Unequip(equipTarget.slotType);

                EquipmentManager.Instance.Equip(equipped, equipTarget.slotType);
                if (itemInTarget != null)
                    EquipmentManager.Instance.Equip(itemInTarget, sourceEquipSlot.slotType);
            }
            return;
        }

        InventorySlot invTarget = GetInventorySlotUnderPointer(eventData);
        if (invTarget != null)
        {
            if (invTarget.IsEmpty())
            {
                EquipmentManager.Instance?.Unequip(sourceEquipSlot.slotType);
                invTarget.SetItem(equipped);
            }
            else
            {
                ItemData itemInInv = invTarget.currentItem;
                if (itemInInv.IsEquipment &&
                    EquipmentManager.Instance != null &&
                    EquipmentManager.Instance.IsSlotCompatible(itemInInv, sourceEquipSlot.slotType))
                {
                    EquipmentManager.Instance.Unequip(sourceEquipSlot.slotType);
                    invTarget.ClearSlot();
                    invTarget.SetItem(equipped);
                    EquipmentManager.Instance.Equip(itemInInv, sourceEquipSlot.slotType);
                }
                else
                {
                    EquipmentManager.Instance?.Unequip(sourceEquipSlot.slotType);
                    InventoryUI.Instance?.AddItem(equipped);
                }
            }

            NotifyHotbarIfNeeded(invTarget, null);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // ИЗ ИНВЕНТАРЯ/ХОТБАРА
    // ─────────────────────────────────────────────────────────────────
    void HandleDragFromInventory(PointerEventData eventData)
    {
        if (sourceSlot == null || sourceSlot.IsEmpty()) return;

        EquipmentSlot equipTarget = GetEquipmentSlotUnderPointer(eventData);
        if (equipTarget != null)
        {
            if (equipTarget.slotType == EquipmentSlotType.Weapon)
            {
                Debug.Log("[Equipment] Слот оружия управляется через хотбар!");
                return;
            }
            TryEquipFromInventory(sourceSlot, equipTarget);
            NotifyHotbarIfNeeded(sourceSlot, null);
            return;
        }

        InventorySlot invTarget = GetInventorySlotUnderPointer(eventData);
        if (invTarget != null && invTarget != sourceSlot)
        {
            SwapInventorySlots(sourceSlot, invTarget);
            NotifyHotbarIfNeeded(sourceSlot, invTarget);
        }
    }

    void TryEquipFromInventory(InventorySlot from, EquipmentSlot equipSlot)
    {
        ItemData item = from.currentItem;
        if (item == null || !item.IsEquipment) return;

        if (EquipmentManager.Instance == null ||
            !EquipmentManager.Instance.IsSlotCompatible(item, equipSlot.slotType))
        {
            Debug.Log("[Equipment] " + item.itemName + " не подходит для " + equipSlot.slotType);
            return;
        }

        ItemData previous = EquipmentManager.Instance.GetEquipped(equipSlot.slotType);

        from.ClearSlot();

        if (previous != null)
            EquipmentManager.Instance.Unequip(equipSlot.slotType);
        EquipmentManager.Instance.Equip(item, equipSlot.slotType);

        if (previous != null)
            from.SetItem(previous);
    }

    // ─────────────────────────────────────────────────────────────────
    // SWAP ИНВЕНТАРЬ ↔ ИНВЕНТАРЬ / СУНДУК / СИЛОС
    // ─────────────────────────────────────────────────────────────────
    void SwapInventorySlots(InventorySlot from, InventorySlot to)
    {
        if (from == null || to == null) return;

        // ─── ФИЛЬТР ПО ТИПУ ПРЕДМЕТА (лесопилка принимает только дерево, шахта — только руду) ───
        // Проверяем обе стороны: нельзя положить неподходящий предмет ни в to, ни (при обратном свапе) в from
        if (from.currentItem != null && !to.IsItemAllowed(from.currentItem))
        {
            Debug.Log("[Слот] Сюда нельзя класть: " + from.currentItem.itemName);
            return;
        }
        if (to.currentItem != null && !from.IsItemAllowed(to.currentItem))
        {
            Debug.Log("[Слот] Сюда нельзя класть: " + to.currentItem.itemName);
            return;
        }

        // ─── ПЕРЕТАСКИВАНИЕ ИЗ OVERFLOW-СЛОТА (например склад досок лесопилки) ───
        // Переносим не больше обычного maxStack предмета, остаток остаётся на складе
        if (from.allowOverflow && !to.allowOverflow && from.currentItem != null)
        {
            int maxStack = from.currentItem.maxStack;

            if (!to.IsEmpty() && to.currentItem == from.currentItem)
            {
                int canAdd = maxStack - to.quantity;
                if (canAdd > 0)
                {
                    int add = Mathf.Min(canAdd, from.quantity);
                    to.quantity += add;
                    from.quantity -= add;
                    to.UpdateUI();
                    if (from.quantity <= 0) from.ClearSlot(); else from.UpdateUI();
                }
                return;
            }
            if (to.IsEmpty())
            {
                int take = Mathf.Min(from.quantity, maxStack);
                to.SetItemWithWater(from.currentItem, take, from.currentWater);
                from.quantity -= take;
                if (from.quantity <= 0) from.ClearSlot(); else from.UpdateUI();
                return;
            }
            // Целевой слот занят другим предметом — перенос невозможен
            return;
        }

        // ─── ПЕРЕТАСКИВАНИЕ В OVERFLOW-СЛОТ (на будущее — ручная докладка) ───
        if (to.allowOverflow && from.currentItem != null)
        {
            // Выходные склады мастерских (доски/слитки) наполняются ТОЛЬКО автоматически —
            // ручной drag&drop в них запрещён, чтобы не подменить содержимое во время обработки
            if (!to.acceptsManualDeposit)
            {
                Debug.Log("[Слот] Сюда нельзя класть предметы вручную — это выход мастерской.");
                return;
            }

            int cap = to.overflowCapacity > 0 ? to.overflowCapacity : int.MaxValue;
            if (to.IsEmpty() || to.currentItem == from.currentItem)
            {
                int already = to.IsEmpty() ? 0 : to.quantity;
                int add = Mathf.Min(cap - already, from.quantity);
                if (add > 0)
                {
                    if (to.IsEmpty()) to.SetItemWithWater(from.currentItem, add, from.currentWater);
                    else { to.quantity += add; to.UpdateUI(); }
                    from.quantity -= add;
                    if (from.quantity <= 0) from.ClearSlot(); else from.UpdateUI();
                }
                return;
            }
            return; // разные предметы — склад занят другим типом
        }

        bool isSiloMode = ChestUI.Instance != null && ChestUI.Instance.isSiloMode;
        int siloStack = ChestUI.Instance != null ? ChestUI.Instance.siloMaxStack : 50;
        bool toIsSilo = isSiloMode && to.linkedChestSlot != null;
        bool fromIsSilo = isSiloMode && from.linkedChestSlot != null;

        if (toIsSilo && from.currentItem != null && !SiloInteraction.IsAllowed(from.currentItem))
        { Debug.Log("[Силос] Нельзя: " + from.currentItem.itemName); return; }
        if (fromIsSilo && to.currentItem != null && !SiloInteraction.IsAllowed(to.currentItem))
        { Debug.Log("[Силос] Нельзя: " + to.currentItem.itemName); return; }

        if (toIsSilo && from.currentItem != null && !to.IsEmpty()
            && to.currentItem == from.currentItem)
        {
            int add = Mathf.Min(siloStack - to.quantity, from.quantity);
            if (add > 0)
            {
                to.quantity += add; from.quantity -= add;
                to.UpdateUI();
                if (from.quantity <= 0) from.ClearSlot(); else from.UpdateUI();
            }
            return;
        }
        if (toIsSilo && to.IsEmpty() && from.currentItem != null)
        {
            int take = Mathf.Min(from.quantity, siloStack);
            to.SetItemWithWater(from.currentItem, take, from.currentWater);
            from.quantity -= take;
            if (from.quantity <= 0) from.ClearSlot(); else from.UpdateUI();
            return;
        }

        if (fromIsSilo && !toIsSilo && from.currentItem != null)
        {
            int max = from.currentItem.maxStack;
            int take = Mathf.Min(from.quantity, max);
            if (!to.IsEmpty() && to.currentItem == from.currentItem)
            {
                int add = Mathf.Min(max - to.quantity, from.quantity);
                if (add > 0)
                {
                    to.quantity += add; from.quantity -= add;
                    to.UpdateUI();
                    if (from.quantity <= 0) from.ClearSlot(); else from.UpdateUI();
                }
                return;
            }
            if (to.IsEmpty())
            {
                to.SetItemWithWater(from.currentItem, take, from.currentWater);
                from.quantity -= take;
                if (from.quantity <= 0) from.ClearSlot(); else from.UpdateUI();
            }
            return;
        }

        // ─── СЛИЯНИЕ СТАКОВ (один и тот же предмет — доливаем, а не меняем местами) ───
        if (!to.IsEmpty() && !from.IsEmpty()
            && to.currentItem == from.currentItem && from.currentItem.isStackable)
        {
            int canAdd = from.currentItem.maxStack - to.quantity;
            if (canAdd > 0)
            {
                int add = Mathf.Min(canAdd, from.quantity);
                to.quantity += add;
                from.quantity -= add;
                to.UpdateUI();
                if (from.quantity <= 0) from.ClearSlot(); else from.UpdateUI();
                return;
            }
        }

        ItemData tempItem = to.currentItem;
        int tempQty = to.quantity;
        int tempWater = to.currentWater;

        if (from.currentItem != null)
            to.SetItemWithWater(from.currentItem, from.quantity, from.currentWater);
        else
            to.ClearSlot();

        if (tempItem != null)
            from.SetItemWithWater(tempItem, tempQty, tempWater);
        else
            from.ClearSlot();
    }

    // ─────────────────────────────────────────────────────────────────
    // ПОИСК СЛОТОВ
    // ─────────────────────────────────────────────────────────────────
    InventorySlot GetInventorySlotUnderPointer(PointerEventData eventData)
    {
        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        foreach (var r in results)
        {
            if (r.gameObject == null) continue;
            if (r.gameObject.GetComponentInParent<EquipmentSlot>() != null) continue;
            InventorySlot slot = r.gameObject.GetComponentInParent<InventorySlot>();
            if (slot != null && slot != sourceSlot) return slot;
        }
        return null;
    }

    EquipmentSlot GetEquipmentSlotUnderPointer(PointerEventData eventData)
    {
        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        foreach (var r in results)
        {
            if (r.gameObject == null) continue;
            EquipmentSlot slot = r.gameObject.GetComponentInParent<EquipmentSlot>();
            if (slot != null) return slot;
        }
        return null;
    }
}