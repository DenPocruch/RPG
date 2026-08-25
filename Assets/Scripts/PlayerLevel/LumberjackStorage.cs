using UnityEngine;
using System;

/// <summary>
/// Постоянное хранилище лесопилки. Работает через persistent data-слоты
/// (как ChestInteraction) — брёвна лежат прямо в слотах, обработка идёт
/// в фоне через Update() независимо от того, открыта ли панель.
/// Игрок кладёт/забирает предметы обычным drag&drop.
/// </summary>
public class LumberjackStorage : MonoBehaviour, ISaveable
{
    public static LumberjackStorage Instance;

    [Header("Слоты под брёвна")]
    public int logSlotCount = 6;

    [Header("Базовая вместимость склада досок")]
    public int basePlankCapacity = 50;

    // Постоянные data-слоты (не UI!) — существуют пока жив объект лесопилки
    private InventorySlot[] logSlots;
    private InventorySlot outputSlot; // склад досок, allowOverflow = true

    // Текущая обработка
    private InventorySlot processingSlot = null;
    private float timeRemaining = 0f;
    private float totalTime = 0f;
    private bool waitingForGold = false;

    public System.Action onStorageChanged;

    void Awake()
    {
        // Сценовый синглтон: при повторном входе в сцену новый экземпляр
        // занимает место старого, дубликат в той же сцене уничтожается
        if (Instance != null && Instance != this && Instance.gameObject.scene == gameObject.scene)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        CreateDataSlots();
        SaveManager.Instance?.Register(this);
    }

    void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        SaveManager.Instance?.LoadInto(this);

        // Переподписываем UI на СВОЁ событие (UI вечный, склады пересоздаются)
        LumberjackUI.Instance?.BindToStorage();
    }

    // ─── ISaveable ─────────────────────────────────────────────
    [System.Serializable]
    private class SlotSave { public int index; public string itemName; public int quantity; }
    [System.Serializable]
    private class StorageSave
    {
        public System.Collections.Generic.List<SlotSave> logs = new System.Collections.Generic.List<SlotSave>();
        public float timeRemaining;
        public long savedAtTicks;
        public string outputItem;
        public int outputQty;
    }

    public string SaveKey => "lumberjack";

    public string CaptureState()
    {
        StorageSave save = new StorageSave();
        for (int i = 0; i < logSlots.Length; i++)
        {
            if (logSlots[i] == null || logSlots[i].IsEmpty()) continue;
            save.logs.Add(new SlotSave
            {
                index = i,
                itemName = logSlots[i].currentItem.name,
                quantity = logSlots[i].quantity
            });
        }
        if (outputSlot != null && !outputSlot.IsEmpty())
        {
            save.outputItem = outputSlot.currentItem.name;
            save.timeRemaining = timeRemaining;
            save.savedAtTicks = DateTime.UtcNow.Ticks;
            save.outputQty = outputSlot.quantity;
        }
        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        StorageSave save = JsonUtility.FromJson<StorageSave>(json);
        if (save == null) return;

        foreach (InventorySlot s in logSlots)
            if (s != null) s.ClearSlot();
        if (outputSlot != null) outputSlot.ClearSlot();

        foreach (SlotSave ss in save.logs)
        {
            if (ss.index < 0 || ss.index >= logSlots.Length) continue;
            ItemData item = ItemDatabase.Find(ss.itemName);
            if (item == null) { Debug.LogWarning("[Save] Бревно не найдено: " + ss.itemName); continue; }
            logSlots[ss.index].SetItem(item, ss.quantity);
        }

        if (!string.IsNullOrEmpty(save.outputItem))
        {
            ItemData outItem = ItemDatabase.Find(save.outputItem);
            if (outItem != null)
                outputSlot.SetItem(outItem, save.outputQty);
        }

        // Оффлайн-переработка: вычитаем реальное время с момента сохранения,
        // чтобы текущая единица не начиналась заново с полным временем
        if (save.savedAtTicks > 0 && save.timeRemaining > 0f)
        {
            double elapsed = (DateTime.UtcNow.Ticks - save.savedAtTicks) / (double)TimeSpan.TicksPerSecond;
            if (elapsed > 0)
                timeRemaining = Mathf.Max(0f, timeRemaining - (float)elapsed);
        }

        onStorageChanged?.Invoke();
    }

    void CreateDataSlots()
    {
        logSlots = new InventorySlot[logSlotCount];
        for (int i = 0; i < logSlotCount; i++)
        {
            GameObject go = new GameObject("LogDataSlot_" + i);
            go.transform.SetParent(transform);
            go.SetActive(false);
            InventorySlot slot = go.AddComponent<InventorySlot>();
            slot.slotIndex = i;
            logSlots[i] = slot;
        }

        GameObject outGo = new GameObject("PlankOutputDataSlot");
        outGo.transform.SetParent(transform);
        outGo.SetActive(false);
        outputSlot = outGo.AddComponent<InventorySlot>();
        outputSlot.allowOverflow = true;
    }

    void Update()
    {
        if (outputSlot != null) outputSlot.overflowCapacity = GetPlankCapacity();
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
        foreach (InventorySlot slot in logSlots)
        {
            if (slot.IsEmpty()) continue;

            ItemData log = slot.currentItem;
            if (log.convertsToItem == null) continue;

            int ratio = Mathf.Max(1, log.conversionRatio);
            if (slot.quantity < ratio) continue;

            // Склад досок занят другим типом — пропускаем этот вид брёвен
            if (!outputSlot.IsEmpty() && outputSlot.currentItem != log.convertsToItem) continue;

            // Склад полон
            if (!outputSlot.IsEmpty() && outputSlot.quantity >= outputSlot.overflowCapacity) continue;

            processingSlot = slot;
            totalTime = GetTimePerUnit(log);
            timeRemaining = totalTime;
            return;
        }
    }

    void TryCompleteUnit()
    {
        ItemData log = processingSlot.currentItem;
        if (log == null) { processingSlot = null; return; }

        int cost = GetCostPerPlank(log);
        if (cost > 0 && (CurrencyManager.Instance == null || CurrencyManager.Instance.Gold < cost))
        {
            waitingForGold = true; // ждём пока у игрока не появится золото
            return;
        }
        waitingForGold = false;

        // Защита от повреждения склада: если в выходном слоте уже лежит
        // ЧУЖОЙ предмет (не должно случиться при acceptsManualDeposit=false,
        // но проверяем на всякий случай) — не завершаем, ждём пока слот освободится
        ItemData expectedOutput = log.convertsToItem;
        if (!outputSlot.IsEmpty() && outputSlot.currentItem != expectedOutput)
        {
            return; // склад занят чем-то посторонним — приостанавливаем без потери ресурса
        }

        if (cost > 0) CurrencyManager.Instance.SpendGold(cost);

        // Списываем брёвна прямо из слота
        int ratio = Mathf.Max(1, log.conversionRatio);
        processingSlot.quantity -= ratio;
        if (processingSlot.quantity <= 0) processingSlot.ClearSlot();
        else processingSlot.UpdateUI();

        // Добавляем доску в выходной склад
        ItemData plank = log.convertsToItem;
        if (outputSlot.IsEmpty())
            outputSlot.SetItem(plank, 1);
        else
            outputSlot.quantity += 1;
        outputSlot.UpdateUI();

        if (PlayerLevel.Instance != null)
            PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Crafting, 1);

        processingSlot = null;
        onStorageChanged?.Invoke();

        // Сейв по событию: единица переработки завершена
        SaveManager.Instance?.Save();
    }

    float GetTimePerUnit(ItemData logItem)
    {
        float t = logItem.conversionTimePerUnit;
        if (SkillTreeManager.Instance != null)
            t = Mathf.Max(0f, t - SkillTreeManager.Instance.GetCraftTimeReduction());
        return t;
    }

    int GetCostPerPlank(ItemData logItem)
    {
        if (logItem.conversionGoldCost <= 0) return 0;
        float discount = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetServiceCostReduction()
            : 0f;
        float cost = logItem.conversionGoldCost * (1f - discount / 100f);
        return Mathf.Max(0, Mathf.RoundToInt(cost));
    }

    // ═══════════════════════════════════════════════════════════
    // ГЕТТЕРЫ ДЛЯ UI
    // ═══════════════════════════════════════════════════════════
    public InventorySlot[] GetLogSlots() => logSlots;
    public InventorySlot GetOutputSlot() => outputSlot;

    public int GetPlankCapacity()
    {
        int bonus = SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetStorageCapacityBonus()
            : 0;
        return basePlankCapacity + bonus;
    }

    public ItemData GetCurrentProcessingItem() =>
        processingSlot != null ? processingSlot.currentItem : null;

    public float GetTimeRemaining() => timeRemaining;
    public float GetTotalTime() => totalTime;
    public bool IsWaitingForGold() => waitingForGold;
}