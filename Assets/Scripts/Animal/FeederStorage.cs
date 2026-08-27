using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Кормушка на ферме. Ставится игроком из хотбара (ghost-режим в PlayerMovement).
/// Удар (атака) по кормушке открывает FeedUI — перенос корма из рюкзака.
/// Животные (AnimalController) сами подходят и берут корм через TryConsume.
/// Вместимость растёт от перков animal_feeder и feeder_big.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class FeederStorage : MonoBehaviour, IInteractable
{
    public const string FeederPerkTag = "feeder";
    public const string BigFeederPerkTag = "feeder_big";
    public const int BaseCapacity = 5;

    [System.Serializable]
    public class FeedEntry { public string item; public int count; }

    [Tooltip("Содержимое кормушки: предмет → количество")]
    public List<FeedEntry> stock = new List<FeedEntry>();

    // Реестр всех кормушек в мире (для поиска животными)
    private static readonly List<FeederStorage> all = new List<FeederStorage>();
    void OnEnable() { all.Add(this); }
    void OnDisable() { all.Remove(this); }

    /// <summary>Есть ли в мире хоть одна кормушка (иначе голод не включаем).</summary>
    public static bool AnyInWorld => all.Count > 0;

    /// <summary>Ближайшая кормушка ГДЕ ЕСТЬ нужный корм (null если нет в радиусе).</summary>
    public static FeederStorage FindNearest(Vector3 pos, float radius, ItemData feedItem)
    {
        FeederStorage best = null;
        float bd = radius * radius;
        foreach (var f in all)
        {
            if (f == null || !f.HasFeedFor(feedItem)) continue;
            float d = (f.transform.position - pos).sqrMagnitude;
            if (d < bd) { bd = d; best = f; }
        }
        return best;
    }

    public int Capacity
    {
        get
        {
            int cap = BaseCapacity;
            if (SkillTreeManager.Instance != null)
            {
                cap += SkillTreeManager.Instance.GetNodeRankByFeature(FeederPerkTag);
                cap += SkillTreeManager.Instance.GetNodeRankByFeature(BigFeederPerkTag);
            }
            return cap;
        }
    }

    public int TotalStock
    {
        get { int t = 0; foreach (var e in stock) t += e.count; return t; }
    }

    public int FreeSpace => Mathf.Max(0, Capacity - TotalStock);

    // Надпись над кормушкой: [иконка корма] 2/5 (компонент WorldLabel)
    private WorldLabel label;
    private float labelTimer;

    void Update()
    {
        // Дешёвое периодическое обновление (ловит и смену перков вместимости)
        labelTimer -= Time.deltaTime;
        if (labelTimer <= 0f)
        {
            labelTimer = 0.5f;
            UpdateLabel();
        }
    }

    void Awake()
    {
        // Страховка: у префаба должен быть физический коллайдер (не триггер),
        // чтобы нельзя было ставить объекты друг на друга
        var col = GetComponent<BoxCollider2D>();
        if (col != null) col.isTrigger = false;
    }

    public Transform GetTransform() => transform;

    // ═══════════════════════════════════════════════════════════
    // ВЗАИМОДЕЙСТВИЕ (удар кормом в руках = быстрая загрузка,
    // удар с пустыми/другими руками = окно FeedUI)
    // ═══════════════════════════════════════════════════════════
    public void Interact(GameObject player)
    {
        ItemData active = HotbarManager.Instance != null ? HotbarManager.Instance.GetActiveItem() : null;

        if (active != null && FeedUI.IsAnimalFeed(active))
        {
            QuickLoad(active);
            return;
        }

        if (FeedUI.Instance != null)
            FeedUI.Open(this);
        else
            ActionLogUI.Show(CapacityInfo());
    }

    /// <summary>Весь стак корма из активной ячейки хотбара → в кормушку.
    /// Если внутри другой корм — он возвращается в рюкзак (замена).</summary>
    void QuickLoad(ItemData feed)
    {
        InventorySlot slot = HotbarManager.Instance.GetActiveSlot();
        if (slot == null || slot.IsEmpty() || slot.quantity <= 0)
        {
            if (FeedUI.Instance != null) FeedUI.Open(this);
            return;
        }

        // Замена корма: старый → в рюкзак
        int returned = 0;
        bool hasOther = false;
        foreach (var e in stock)
            if (e.item != feed.name) { hasOther = true; break; }
        if (hasOther)
            returned = TakeAllBack();

        int space = FreeSpace;
        int available = slot.quantity;
        int put = Mathf.Min(space, available);
        if (put > 0)
        {
            slot.quantity -= put;
            if (slot.quantity <= 0) slot.ClearSlot();
            else slot.UpdateUI();
            AddFeed(feed, put);
            HotbarManager.Instance.NotifyActiveItemChanged();
            SaveManager.Instance?.Save();
        }

        string msg = CapacityInfo();
        if (returned > 0) msg += ". Старый корм (" + returned + ") в рюкзак";
        if (put == 0 && space == 0) msg += " — ЗАПОЛНЕНА!";
        else if (put < available) msg += " (влезло " + put + " из " + available + ")";
        ActionLogUI.Show(msg);
        UpdateLabel();
    }

    public string CapacityInfo() => "Кормушка: " + TotalStock + "/" + Capacity;

    // ═══════════════════════════════════════════════════════════
    // КОРМ (для FeedUI и животных)
    // ═══════════════════════════════════════════════════════════
    public int CountFeed(ItemData item)
    {
        var e = FindEntry(item);
        return e != null ? e.count : 0;
    }

    public bool HasFeedFor(ItemData feedItem) => CountFeed(feedItem) > 0;

    /// <summary>Положить корм. Возвращает сколько реально влезло (лимит общий на все виды).</summary>
    public int AddFeed(ItemData item, int amount)
    {
        if (item == null || amount <= 0) return 0;
        int added = Mathf.Min(amount, FreeSpace);
        if (added <= 0) return 0;

        var e = FindEntry(item);
        if (e == null) { e = new FeedEntry { item = item.name, count = 0 }; stock.Add(e); }
        e.count += added;
        UpdateLabel();
        return added;
    }

    /// <summary>Животное берёт 1 единицу своего корма.</summary>
    public bool TryConsume(ItemData feedItem)
    {
        var e = FindEntry(feedItem);
        if (e == null || e.count <= 0) return false;
        e.count--;
        if (e.count <= 0) stock.Remove(e);
        UpdateLabel();
        return true;
    }

    /// <summary>Забрать всё обратно в рюкзак (FeedUI). Возвращает сколько удалось.</summary>
    public int TakeAllBack()
    {
        int taken = 0;
        for (int i = stock.Count - 1; i >= 0; i--)
        {
            var e = stock[i];
            ItemData item = ItemDatabase.Find(e.item);
            if (item == null) continue;

            int left = e.count;
            while (left > 0 && InventoryUI.Instance != null)
            {
                if (!InventoryUI.Instance.AddItem(item, 1)) break;
                left--;
                taken++;
            }
            e.count = left;
            if (left <= 0) stock.RemoveAt(i);
        }
        UpdateLabel();
        return taken;
    }

    FeedEntry FindEntry(ItemData item)
    {
        if (item == null) return null;
        return stock.Find(s => s.item == item.name);
    }

    // ═══════════════════════════════════════════════════════════
    // СОХРАНЕНИЕ (данные тянет PlaceablesSaveManager)
    // ═══════════════════════════════════════════════════════════
    public void ApplySave(string[] items, int[] counts)
    {
        stock.Clear();
        if (items == null || counts == null) return;
        int n = Mathf.Min(items.Length, counts.Length);
        for (int i = 0; i < n; i++)
            if (counts[i] > 0)
                stock.Add(new FeedEntry { item = items[i], count = counts[i] });
        UpdateLabel();
    }

    // ═══════════════════════════════════════════════════════════
    // НАДПИСЬ НАД КОРМУШКОЙ: [иконка корма] 2/5
    // ═══════════════════════════════════════════════════════════
    void UpdateLabel()
    {
        EnsureLabel();
        if (label == null) return;

        bool empty = TotalStock == 0;
        label.Set(TotalStock + "/" + Capacity,
            empty ? new Color(1f, 0.75f, 0.6f) : Color.white);

        Sprite icon = null;
        if (stock.Count > 0)
        {
            ItemData item = ItemDatabase.Find(stock[0].item);
            if (item != null) icon = item.icon;
        }
        label.SetIcon(icon);
    }

    void EnsureLabel()
    {
        if (label != null) return;

        // 1) Лейбл вручную добавлен в сцену под кормушкой?
        label = GetComponentInChildren<WorldLabel>(true);

        // 2) Префаб из Resources (Assets/Resources/WorldLabel.prefab)
        if (label == null)
        {
            GameObject prefab = Resources.Load<GameObject>("WorldLabel");
            if (prefab != null)
            {
                var go = Instantiate(prefab, transform, false);
                label = go.GetComponent<WorldLabel>();
                if (label == null) label = go.AddComponent<WorldLabel>();
            }
        }

        // 3) Фолбэк: создаём на месте с настройками по умолчанию из скрипта WorldLabel
        if (label == null)
        {
            var go = new GameObject("WorldLabel");
            go.transform.SetParent(transform, false);
            label = go.AddComponent<WorldLabel>();
        }

        label.EnsureBuilt();
    }

    public string[] SaveItems()
    {
        var arr = new string[stock.Count];
        for (int i = 0; i < stock.Count; i++) arr[i] = stock[i].item;
        return arr;
    }

    public int[] SaveCounts()
    {
        var arr = new int[stock.Count];
        for (int i = 0; i < stock.Count; i++) arr[i] = stock[i].count;
        return arr;
    }
}
