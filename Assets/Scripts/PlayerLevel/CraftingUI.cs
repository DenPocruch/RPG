using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// UI панель кузни. 9 слотов входа + 1 слот выхода.
/// Открывается как сундук — рюкзак сдвигается влево.
/// </summary>
public class CraftingUI : MonoBehaviour
{
    public static CraftingUI Instance;

    [Header("UI панели")]
    public GameObject craftingPanel;
    public RectTransform craftingRect;

    [Header("Слоты входа (9 штук)")]
    public InventorySlot[] inputSlots;
    public GameObject inputSlotPrefab;
    public Transform inputSlotsGrid;

    [Header("Слот выхода")]
    public Image outputIcon;
    public Image outputRarityBorder;
    public TMP_Text outputNameText;
    public TMP_Text outputRarityText;

    [Header("Информация о крафте")]
    public TMP_Text failChanceText;  // "Шанс провала: 10%"
    public TMP_Text resultText;      // "Успех!" / "Провал!"
    public TMP_Text hintText;        // подсказка что нужно положить

    [Header("Кнопки")]
    public Button craftButton;
    public TMP_Text craftButtonText;
    public Button autoFillButton;

    [Header("Позиции панелей")]
    public float panelY = 47f;
    public float shiftDistance = 325f;
    public float shiftSpeed = 8f;

    [Header("Цвета редкостей")]
    public Color colorCommon = new Color(0.7f, 0.7f, 0.7f);
    public Color colorUncommon = new Color(0.2f, 0.8f, 0.2f);
    public Color colorRare = new Color(0.2f, 0.4f, 1f);
    public Color colorEpic = new Color(0.7f, 0.2f, 1f);
    public Color colorLegendary = new Color(1f, 0.5f, 0f);

    private bool isOpen = false;
    private Vector2 targetCraftingPos;
    private Vector2 craftingNormalPos;

    void Awake()
    {
        // Защита от дубликата: копия PersistentRoot при возврате в сцену
        // создаёт второй экземпляр — копию уничтожаем, оригинал живёт
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (craftingPanel != null) craftingPanel.SetActive(false);
    }

    void Start()
    {
        craftingNormalPos = new Vector2(0, panelY);
        if (craftingRect != null)
        {
            craftingRect.anchoredPosition = craftingNormalPos;
            targetCraftingPos = craftingNormalPos;
        }

        craftButton?.onClick.AddListener(OnCraftClick);
        autoFillButton?.onClick.AddListener(AutoFill);

        CreateInputSlots();
        HideOutput();
    }

    void Update()
    {
        if (craftingRect != null)
            craftingRect.anchoredPosition = Vector2.Lerp(
                craftingRect.anchoredPosition, targetCraftingPos,
                Time.deltaTime * shiftSpeed);

        if (isOpen) UpdatePreview();
    }

    void CreateInputSlots()
    {
        if (inputSlotsGrid == null || inputSlotPrefab == null) return;

        inputSlots = new InventorySlot[CraftingManager.REQUIRED_ITEMS];
        for (int i = 0; i < CraftingManager.REQUIRED_ITEMS; i++)
        {
            GameObject slotObj = Instantiate(inputSlotPrefab, inputSlotsGrid);
            slotObj.name = "CraftSlot_" + i;
            InventorySlot slot = slotObj.GetComponent<InventorySlot>();
            slot.slotIndex = i;
            slot.isHotbarSlot = false;
            inputSlots[i] = slot;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ОТКРЫТИЕ / ЗАКРЫТИЕ
    // ═══════════════════════════════════════════════════════════
    public void Open()
    {
        craftingPanel.SetActive(true);
        isOpen = true;

        // Рюкзак → влево, крафт → вправо
        InventoryPanelMover.Instance?.SetOffsetX(-shiftDistance);
        targetCraftingPos = new Vector2(shiftDistance, panelY);
        if (craftingRect != null)
            craftingRect.anchoredPosition = new Vector2(shiftDistance, panelY);

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.OpenInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        HideOutput();
        UpdatePreview();
    }

    public void Close()
    {
        // Возвращаем предметы из слотов крафта в инвентарь
        ReturnItemsToInventory();

        craftingPanel.SetActive(false);
        isOpen = false;

        InventoryPanelMover.Instance?.ResetPosition();
        targetCraftingPos = craftingNormalPos;

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.CloseInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;
    }

    public bool IsOpen() => isOpen;

    // ═══════════════════════════════════════════════════════════
    // ПРЕВЬЮ РЕЗУЛЬТАТА (обновляется каждый кадр)
    // ═══════════════════════════════════════════════════════════
    void UpdatePreview()
    {
        if (CraftingManager.Instance == null) return;

        int filledCount = CountFilledSlots();

        // Редкость определяем по первому непустому слоту (слоты можно
        // заполнять частично: шаг требует 9/6/3/3 вещей)
        ItemRarity? rarity = null;
        bool mixed = false;
        foreach (InventorySlot slot in inputSlots)
        {
            if (slot == null || slot.IsEmpty() || slot.currentItem == null) continue;
            if (rarity == null) rarity = slot.currentItem.rarity;
            else if (slot.currentItem.rarity != rarity) { mixed = true; break; }
        }

        if (rarity == null)
        {
            if (hintText != null) hintText.text = "Положи предметы для ковки";
            HideOutput();
            if (craftButton != null) craftButton.interactable = false;
            if (failChanceText != null) failChanceText.text = "";
            return;
        }

        if (mixed)
        {
            HideOutput();
            if (hintText != null) hintText.text = "Все предметы должны быть одной редкости!";
            if (craftButton != null) craftButton.interactable = false;
            if (failChanceText != null) failChanceText.text = "";
            return;
        }

        int required = CraftingManager.RequiredForRarity(rarity.Value);
        string rarityName = CraftingManager.Instance.TranslateRarity(rarity.Value);

        if (filledCount != required)
        {
            HideOutput();
            if (hintText != null)
                hintText.text = "Нужно " + required + " шт. (" + rarityName + "), сейчас " + filledCount;
            if (craftButton != null) craftButton.interactable = false;
            if (failChanceText != null) failChanceText.text = "";
            return;
        }

        // Всё заполнено — превью результата + руда
        ItemRarity nextRarity = GetNextRarity(rarity.Value);
        ShowOutputPreview(rarity.Value, nextRarity);

        CraftingManager.Instance.GetOreRequirement(inputSlots, out ItemData ore, out int oreNeed, out int oreHave);
        bool oreOk = ore == null || oreNeed <= 0 || oreHave >= oreNeed;
        if (hintText != null)
        {
            if (ore != null && oreNeed > 0)
            {
                hintText.text = "Руда: " + oreNeed + " × " + ore.itemName + " (есть " + oreHave + ")";
                hintText.color = oreOk ? Color.green : new Color(1f, 0.4f, 0.4f);
            }
            else hintText.text = "";
        }

        // Шанс провала
        float failChance = CraftingManager.Instance.GetCurrentFailChance(rarity.Value);
        if (failChanceText != null)
        {
            failChanceText.text = failChance > 0
                ? "Шанс провала: " + failChance.ToString("0.#") + "%"
                : "Шанс провала: 0%";
            failChanceText.color = failChance > 0 ? new Color(1f, 0.5f, 0.3f) : Color.green;
        }

        if (craftButton != null)
        {
            bool canCraft = nextRarity != rarity.Value && oreOk;
            craftButton.interactable = canCraft;
            if (craftButtonText != null)
                craftButtonText.text = nextRarity == rarity.Value
                    ? "Макс. редкость"
                    : (!oreOk ? "Нет руды" : "Выковать");
        }
    }

    void ShowOutputPreview(ItemRarity currentRarity, ItemRarity nextRarity)
    {
        // Проверяем — все одинаковые? (первый НЕПУСТОЙ слот — сетка может быть заполнена частично)
        ItemData firstItem = null;
        foreach (InventorySlot slot in inputSlots)
            if (slot != null && !slot.IsEmpty() && slot.currentItem != null) { firstItem = slot.currentItem; break; }
        bool allSame = firstItem != null;
        if (allSame)
            foreach (InventorySlot slot in inputSlots)
                if (slot != null && !slot.IsEmpty() && slot.currentItem != firstItem) { allSame = false; break; }

        if (outputIcon != null)
        {
            if (allSame && firstItem.nextRarityVersion != null)
            {
                outputIcon.sprite = firstItem.nextRarityVersion.icon;
                outputIcon.enabled = true;
                if (outputNameText != null)
                    outputNameText.text = firstItem.nextRarityVersion.itemName;
            }
            else
            {
                outputIcon.sprite = null;
                outputIcon.enabled = false;
                if (outputNameText != null)
                    outputNameText.text = "Случайный предмет";
            }
        }

        Color rarityColor = GetRarityColor(nextRarity);
        if (outputRarityBorder != null) outputRarityBorder.color = rarityColor;
        if (outputRarityText != null)
        {
            outputRarityText.text = CraftingManager.Instance.TranslateRarity(nextRarity);
            outputRarityText.color = rarityColor;
        }

        if (resultText != null) resultText.text = "";
    }

    void HideOutput()
    {
        if (outputIcon != null) { outputIcon.enabled = false; }
        if (outputNameText != null) outputNameText.text = "";
        if (outputRarityText != null) outputRarityText.text = "";
        if (outputRarityBorder != null) outputRarityBorder.color = Color.clear;
        if (resultText != null) resultText.text = "";
    }


    // АВТО-ЗАПОЛНЕНИЕ
    public void AutoFill()
    {
        if (InventoryUI.Instance == null) return;

        ReturnItemsToInventory();

        InventorySlot[] invSlots = InventoryUI.Instance.slots;

        // Считаем количество каждого предмета
        var counts = new System.Collections.Generic.Dictionary<ItemData, int>();
        foreach (InventorySlot slot in invSlots)
        {
            if (slot.IsEmpty()) continue;
            if (!counts.ContainsKey(slot.currentItem))
                counts[slot.currentItem] = 0;
            counts[slot.currentItem] += slot.quantity;
        }

        if (counts.Count == 0)
        {
            ShowResult("Инвентарь пуст!", false);
            return;
        }

        // Ищем предмет с наибольшим количеством
        ItemData bestItem = null;
        int bestCount = 0;
        foreach (var kvp in counts)
        {
            if (kvp.Value > bestCount)
            {
                bestCount = kvp.Value;
                bestItem = kvp.Key;
            }
        }

        ItemRarity targetRarity = bestItem != null ? bestItem.rarity : ItemRarity.Common;
        int need = CraftingManager.RequiredForRarity(targetRarity);
        int filled = 0;

        // Шаг 1: берём одинаковые предметы
        if (bestItem != null)
            filled = TakeFromInventory(bestItem, invSlots, filled, need);

        // Шаг 2: добираем разные той же редкости если не хватило
        if (filled < need)
        {
            foreach (InventorySlot invSlot in invSlots)
            {
                if (filled >= need) break;
                if (invSlot.IsEmpty()) continue;
                if (invSlot.currentItem == bestItem) continue;
                if (invSlot.currentItem.rarity != targetRarity) continue;

                inputSlots[filled].SetItem(invSlot.currentItem, 1);
                invSlot.quantity--;
                if (invSlot.quantity <= 0) invSlot.ClearSlot();
                else invSlot.UpdateUI();
                filled++;
            }
        }

        if (filled == 0)
            ShowResult("Нет подходящих предметов!", false);
    }

    int TakeFromInventory(ItemData item, InventorySlot[] invSlots, int startIndex, int need)
    {
        int filled = startIndex;
        foreach (InventorySlot invSlot in invSlots)
        {
            if (filled >= need) break;
            if (invSlot.IsEmpty() || invSlot.currentItem != item) continue;

            while (invSlot.quantity > 0 && filled < need)
            {
                inputSlots[filled].SetItem(item, 1);
                invSlot.quantity--;
                filled++;
            }
            if (invSlot.quantity <= 0) invSlot.ClearSlot();
            else invSlot.UpdateUI();
        }
        return filled;
    }

    void ShowResult(string msg, bool success)
    {
        if (resultText != null)
        {
            resultText.text = msg;
            resultText.color = success ? Color.green : new Color(1f, 0.4f, 0.4f);
        }
        StartCoroutine(ClearResultText());
    }

    // КРАФТ
    void OnCraftClick()
    {
        if (CraftingManager.Instance == null) return;

        CraftingManager.CraftResult result = CraftingManager.Instance.TryCraft(inputSlots);

        if (result.success)
        {
            // Списываем руду (проверка уже была в TryCraft — тут не может не хватить)
            CraftingManager.Instance.GetOreRequirement(inputSlots, out ItemData ore, out int oreNeed, out int _);
            if (ore != null && oreNeed > 0)
                CraftingManager.Instance.ConsumeItem(ore, oreNeed);

            // Убираем предметы из слотов
            foreach (InventorySlot slot in inputSlots)
                slot.ClearSlot();

            // Добавляем результат в инвентарь
            if (InventoryUI.Instance != null)
                InventoryUI.Instance.AddItem(result.outputItem, 1);

            // Попап успеха над игроком
            if (DamagePopupManager.Instance != null)
            {
                PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
                Vector3 pos = pm != null
                    ? pm.transform.position + Vector3.up
                    : Vector3.zero;
                DamagePopupManager.Instance.Spawn(pos, 0, DamagePopup.PopupType.Heal);
            }

            ShowResult("✓ " + result.message, true);
        }
        else
        {
            ShowResult("✗ " + result.message, false);
        }
    }

    IEnumerator ClearResultText()
    {
        yield return new WaitForSeconds(2.5f);
        if (resultText != null) resultText.text = "";
    }

    // ═══════════════════════════════════════════════════════════
    // ВСПОМОГАТЕЛЬНЫЕ
    // ═══════════════════════════════════════════════════════════
    int CountFilledSlots()
    {
        int count = 0;
        foreach (InventorySlot slot in inputSlots)
            if (!slot.IsEmpty()) count++;
        return count;
    }

    ItemRarity GetNextRarity(ItemRarity r)
    {
        switch (r)
        {
            case ItemRarity.Common: return ItemRarity.Uncommon;
            case ItemRarity.Uncommon: return ItemRarity.Rare;
            case ItemRarity.Rare: return ItemRarity.Epic;
            case ItemRarity.Epic: return ItemRarity.Legendary;
            default: return r;
        }
    }

    void ReturnItemsToInventory()
    {
        if (InventoryUI.Instance == null) return;
        foreach (InventorySlot slot in inputSlots)
        {
            if (!slot.IsEmpty())
            {
                InventoryUI.Instance.AddItem(slot.currentItem, slot.quantity);
                slot.ClearSlot();
            }
        }
    }

    Color GetRarityColor(ItemRarity r)
    {
        switch (r)
        {
            case ItemRarity.Common: return colorCommon;
            case ItemRarity.Uncommon: return colorUncommon;
            case ItemRarity.Rare: return colorRare;
            case ItemRarity.Epic: return colorEpic;
            case ItemRarity.Legendary: return colorLegendary;
        }
        return Color.white;
    }
}