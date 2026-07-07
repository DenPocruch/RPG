using UnityEngine;

/// <summary>
/// Постоянное хранилище шахты. Работает через persistent data-слоты
/// (как ChestInteraction) — руда лежат прямо в слотах, обработка идёт
/// в фоне через Update() независимо от того, открыта ли панель.
/// Игрок кладёт/забирает предметы обычным drag&drop.
/// </summary>
public class MinerStorage : MonoBehaviour, ISaveable
{
    public static MinerStorage Instance;

    [Header("Слоты под руда")]
    public int oreSlotCount = 6;

    [Header("Базовая вместимость склада слитков")]
    public int baseIngotCapacity = 50;

    // Постоянные data-слоты (не UI!) — существуют пока жив объект шахты
    private InventorySlot[] oreSlots;
    private InventorySlot outputSlot; // склад слитков, allowOverflow = true

    // Текущая обработка
    private InventorySlot processingSlot = null;
    private float timeRemaining = 0f;
    private float totalTime = 0f;
    private bool waitingForGold = false;

    public System.Action onStorageChanged;

    void Awake()
    {
        Instance = this;
        CreateDataSlots();
        SaveManager.Instance?.Register(this);
    }

    void Start()
    {
        SaveManager.Instance?.LoadInto(this);
    }

    // ─── ISaveable ─────────────────────────────────────────────
    [System.Serializable]
    private class SlotSave { public int index; public string itemName; public int quantity; }
    [System.Serializable]
    private class StorageSave
    {
        public System.Collections.Generic.List<SlotSave> ores = new System.Collections.Generic.List<SlotSave>();
        public string outputItem;
        public int outputQty;
    }

    public string SaveKey => "miner";

    public string CaptureState()
    {
        StorageSave save = new StorageSave();
        for (int i = 0; i < oreSlots.Length; i++)
        {
            if (oreSlots[i] == null || oreSlots[i].IsEmpty()) continue;
            save.ores.Add(new SlotSave
            {
                index = i,
                itemName = oreSlots[i].currentItem.name,
                quantity = oreSlots[i].quantity
            });
        }
        if (outputSlot != null && !outputSlot.IsEmpty())
        {
            save.outputItem = outputSlot.currentItem.name;
            save.outputQty = outputSlot.quantity;
        }
        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        StorageSave save = JsonUtility.FromJson<StorageSave>(json);
        if (save == null) return;

        foreach (InventorySlot s in oreSlots)
            if (s != null) s.ClearSlot();
        if (outputSlot != null) outputSlot.ClearSlot();

        foreach (SlotSave ss in save.ores)
        {
            if (ss.index < 0 || ss.index >= oreSlots.Length) continue;
            ItemData item = ItemDatabase.Find(ss.itemName);
            if (item == null) { Debug.LogWarning("[Save] Руда не найдена: " + ss.itemName); continue; }
            oreSlots[ss.index].SetItem(item, ss.quantity);
        }

        if (!string.IsNullOrEmpty(save.outputItem))
        {
            ItemData outItem = ItemDatabase.Find(save.outputItem);
            if (outItem != null)
                outputSlot.SetItem(outItem, save.outputQty);
        }

        onStorageChanged?.Invoke();
    }

    void CreateDataSlots()
    {
        oreSlots = new InventorySlot[oreSlotCount];
        for (int i = 0; i < oreSlotCount; i++)
        {
            GameObject go = new GameObject("OreDataSlot_" + i);
            go.transform.SetParent(transform);
            go.SetActive(false);
            InventorySlot slot = go.AddComponent<InventorySlot>();
            slot.slotIndex = i;
            oreSlots[i] = slot;
        }

        GameObject outGo = new GameObject("IngotOutputDataSlot");
        outGo.transform.SetParent(transform);
        outGo.SetActive(false);
        outputSlot = outGo.AddComponent<InventorySlot>();
        outputSlot.allowOverflow = true;
    }

    void Update()
    {
        if (outputSlot != null) outputSlot.overflowCapacity = GetIngotCapacity();
        ProcessTick();
    }

    // ═══════════════════════════════════════════════════════════
    // ФОНОВАЯ ОБРАБОТКА
    // ═══════════════════════════════════════════════════════════
    void ProcessTick()
    {
        if (processingSlot == null)
        {
            PickNextSlot();
            if (processingSlot == null) return;
        }

        if (timeRemaining > 0f)
        {
            timeRemaining -= Time.deltaTime;
            return;
        }

        TryCompleteUnit();
    }

    void PickNextSlot()
    {
        foreach (InventorySlot slot in oreSlots)
        {
            if (slot.IsEmpty()) continue;

            ItemData ore = slot.currentItem;
            if (ore.convertsToItem == null) continue;

            int ratio = Mathf.Max(1, ore.conversionRatio);
            if (slot.quantity < ratio) continue;

            // Склад слитков занят другим типом — пропускаем этот вид руды
            if (!outputSlot.IsEmpty() && outputSlot.currentItem != ore.convertsToItem) continue;

            // Склад полон
            if (!outputSlot.IsEmpty() && outputSlot.quantity >= outputSlot.overflowCapacity) continue;

            processingSlot = slot;
            totalTime = GetTimePerUnit(ore);
            timeRemaining = totalTime;
            return;
        }
    }

    void TryCompleteUnit()
    {
        ItemData ore = processingSlot.currentItem;
        if (ore == null) { processingSlot = null; return; }

        int cost = GetCostPerIngot(ore);
        if (cost > 0 && (CurrencyManager.Instance == null || CurrencyManager.Instance.Gold < cost))
        {
            waitingForGold = true; // ждём пока у игрока не появится золото
            return;
        }
        waitingForGold = false;

        // Защита от повреждения склада: если в выходном слоте уже лежит
        // ЧУЖОЙ предмет (не должно случиться при acceptsManualDeposit=false,
        // но проверяем на всякий случай) — не завершаем, ждём пока слот освободится
        ItemData expectedOutput = ore.convertsToItem;
        if (!outputSlot.IsEmpty() && outputSlot.currentItem != expectedOutput)
        {
            return; // склад занят чем-то посторонним — приостанавливаем без потери ресурса
        }

        if (cost > 0) CurrencyManager.Instance.SpendGold(cost);

        // Списываем руду прямо из слота
        int ratio = Mathf.Max(1, ore.conversionRatio);
        processingSlot.quantity -= ratio;
        if (processingSlot.quantity <= 0) processingSlot.ClearSlot();
        else processingSlot.UpdateUI();

        // Добавляем слиток в выходной склад
        ItemData ingot = ore.convertsToItem;
        if (outputSlot.IsEmpty())
            outputSlot.SetItem(ingot, 1);
        else
            outputSlot.quantity += 1;
        outputSlot.UpdateUI();

        if (PlayerLevel.Instance != null)
            PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Crafting, 1);

        processingSlot = null;
        onStorageChanged?.Invoke();
    }

    float GetTimePerUnit(ItemData oreItem)
    {
        float t = oreItem.conversionTimePerUnit;
        if (SkillTreeManager.Instance != null)
            t = Mathf.Max(0f, t - SkillTreeManager.Instance.GetCraftTimeReduction());
        return t;
    }

    int GetCostPerIngot(ItemData oreItem)
    {
        if (oreItem.conversionGoldCost <= 0) return 0;
        float discount = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetServiceCostReduction()
            : 0f;
        float cost = oreItem.conversionGoldCost * (1f - discount / 100f);
        return Mathf.Max(0, Mathf.RoundToInt(cost));
    }

    // ═══════════════════════════════════════════════════════════
    // ГЕТТЕРЫ ДЛЯ UI
    // ═══════════════════════════════════════════════════════════
    public InventorySlot[] GetOreSlots() => oreSlots;
    public InventorySlot GetOutputSlot() => outputSlot;

    public int GetIngotCapacity()
    {
        int bonus = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetStorageCapacityBonus()
            : 0;
        return baseIngotCapacity + bonus;
    }

    public ItemData GetCurrentProcessingItem() =>
        processingSlot != null ? processingSlot.currentItem : null;

    public float GetTimeRemaining() => timeRemaining;
    public float GetTotalTime() => totalTime;
    public bool IsWaitingForGold() => waitingForGold;
}