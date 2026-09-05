using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class HotbarManager : MonoBehaviour, ISaveable
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

    void Awake()
    {
        // Защита от дубликата: при возврате в сцену она создаёт копию
        // PersistentRoot вместе со своим HotbarManager. Копию уничтожает
        // GamePersistence, но без этой проверки копия успевает затереть
        // Instance ссылкой на себя — и после удаления копии Instance
        // указывает на уничтоженный объект (MissingReferenceException
        // при смене активного слота).
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SaveManager.Instance?.Register(this);
    }

    void Start()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].slotIndex = i;
            slots[i].isHotbarSlot = true;
        }

        SetActiveSlot(0);
        GiveTestItems(); // тестовые предметы — перезапишутся сохранением, если оно есть

        // Слот крючка на кнопке атаки (ленивый синглтон под Canvas) — создаём
        // сразу и переподписываем на живой менеджер (копия PersistentRoot при
        // возврате в сцену могла подписать его на уничтожаемый экземпляр)
        HookSocketUI.Instance?.Rebind();

        SaveManager.Instance?.LoadInto(this);
    }

    // ─── ISaveable ─────────────────────────────────────────────
    [System.Serializable]
    private class HotbarSlotSave
    {
        public int index;
        public string itemName;
        public int quantity;
        public int water;
        public float weight;
        public int hook;
    }
    [System.Serializable]
    private class HotbarSave
    {
        public int activeIndex;
        public List<HotbarSlotSave> slots = new List<HotbarSlotSave>();
    }

    public string SaveKey => "hotbar";

    public string CaptureState()
    {
        HotbarSave save = new HotbarSave();
        save.activeIndex = activeSlotIndex;

        for (int i = 0; i < slots.Length; i++)
        {
            InventorySlot s = slots[i];
            if (s == null || s.IsEmpty()) continue;
            save.slots.Add(new HotbarSlotSave
            {
                index = i,
                itemName = s.currentItem.name,
                quantity = s.quantity,
                water = s.currentWater,
                weight = s.fishWeightKg,
                hook = s.hookCastsLeft
            });
        }
        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        HotbarSave save = JsonUtility.FromJson<HotbarSave>(json);
        if (save == null) return;

        foreach (InventorySlot s in slots)
            if (s != null) s.ClearSlot();

        foreach (HotbarSlotSave ss in save.slots)
        {
            if (ss.index < 0 || ss.index >= slots.Length) continue;
            ItemData item = ItemDatabase.Find(ss.itemName);
            if (item == null)
            {
                Debug.LogWarning("[Save] Предмет хотбара не найден: " + ss.itemName);
                continue;
            }
            slots[ss.index].SetItemWithWater(item, ss.quantity, ss.water, ss.weight, ss.hook);
        }

        // Восстанавливаем активный слот — это же обновит зеркало оружия и статы
        int idx = Mathf.Clamp(save.activeIndex, 0, slots.Length - 1);
        SetActiveSlot(idx);
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

        // Повторный тап по УЖЕ активному слоту — сигнал для систем
        // (например, поворот кормушки/поилки перед постановкой)
        bool retap = index == activeSlotIndex;

        // Снимаем выделение со всех
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null) continue; // слот уничтожен — пропускаем
            Image img = slots[i].GetComponent<Image>();
            if (img != null) img.color = normalSlotColor;
        }

        activeSlotIndex = index;

        // Выделяем активный
        if (slots[activeSlotIndex] == null) return;
        Image activeImg = slots[activeSlotIndex].GetComponent<Image>();
        if (activeImg != null) activeImg.color = activeSlotColor;

        // Уведомляем всех подписчиков об изменении активного предмета
        NotifyActiveItemChanged();

        if (retap)
            PlayerMovement.NotifySlotRetapped();
    }

    public ItemData GetActiveItem()
    {
        if (activeSlotIndex < 0 || activeSlotIndex >= slots.Length) return null;
        if (slots[activeSlotIndex] == null) return null;
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

        // Слот крючка показываем/прячем и перекрашиваем кнопку напрямую,
        // а не только событием (подписка могла уйти на копию менеджера)
        if (HookSocketUI.HasInstance) HookSocketUI.Instance.Refresh();

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