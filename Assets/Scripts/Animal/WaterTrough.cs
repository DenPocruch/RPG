using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Поилка на ферме. Ставится игроком из хотбара (ghost-режим в PlayerMovement).
/// Удар с наполненной лейкой = перелить воду в поилку. Животные пьют сами
/// через TryDrink. Вместимость растёт от перка animal_trough.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class WaterTrough : MonoBehaviour, IInteractable
{
    public const string TroughPerkTag = "trough";
    public const int BaseCapacity = 30;
    public const int CapacityPerRank = 10;

    [Tooltip("Запас воды")]
    public int water = 0;

    // Надпись над поилкой: [иконка лейки] 12/30 (компонент WorldLabel)
    private WorldLabel label;
    private float labelTimer;

    void Update()
    {
        labelTimer -= Time.deltaTime;
        if (labelTimer <= 0f)
        {
            labelTimer = 0.5f;
            UpdateLabel();
        }
    }

    // Реестр всех поилок в мире (для поиска животными)
    private static readonly List<WaterTrough> all = new List<WaterTrough>();
    void OnEnable() { all.Add(this); }
    void OnDisable() { all.Remove(this); }

    /// <summary>Есть ли в мире хоть одна поилка (иначе жажда не включаем).</summary>
    public static bool AnyInWorld => all.Count > 0;

    /// <summary>Ближайшая поилка С ВОДОЙ (null если нет в радиусе).</summary>
    public static WaterTrough FindNearest(Vector3 pos, float radius)
    {
        WaterTrough best = null;
        float bd = radius * radius;
        foreach (var t in all)
        {
            if (t == null || !t.HasWater) continue;
            float d = (t.transform.position - pos).sqrMagnitude;
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }

    public int Capacity
    {
        get
        {
            int cap = BaseCapacity;
            if (SkillTreeManager.Instance != null)
                cap += SkillTreeManager.Instance.GetNodeRankByFeature(TroughPerkTag) * CapacityPerRank;
            return cap;
        }
    }

    void Awake()
    {
        var col = GetComponent<BoxCollider2D>();
        if (col != null) col.isTrigger = false;
    }

    public Transform GetTransform() => transform;

    // ═══════════════════════════════════════════════════════════
    // ВЗАИМОДЕЙСТВИЕ (удар лейкой = налить воду)
    // ═══════════════════════════════════════════════════════════
    public void Interact(GameObject player)
    {
        InventorySlot slot = HotbarManager.Instance != null ? HotbarManager.Instance.GetActiveSlot() : null;

        if (slot == null || !slot.IsWateringCan())
        {
            ActionLogUI.Show("Поилка: " + water + "/" + Capacity + ". Наливай водой из лейки.");
            return;
        }

        if (!slot.HasWater())
        {
            ActionLogUI.Show("Лейка пуста! Наполни её у колодца.");
            return;
        }

        int free = Capacity - water;
        if (free <= 0)
        {
            ActionLogUI.Show("Поилка уже полна (" + water + "/" + Capacity + ").");
            return;
        }

        int pour = Mathf.Min(free, slot.currentWater);
        water += pour;
        slot.currentWater -= pour;
        slot.UpdateUI();
        ActionLogUI.Show("Налил воду в поилку: " + water + "/" + Capacity);

        // Сейв сразу: иначе уровень воды теряется при выходе до автосейва
        SaveManager.Instance?.Save();
        UpdateLabel();
    }

    /// <summary>Животное пьёт 1 единицу воды.</summary>
    public bool TryDrink()
    {
        if (water <= 0) return false;
        water--;
        UpdateLabel();
        return true;
    }

    public bool HasWater => water > 0;

    // ═══════════════════════════════════════════════════════════
    // НАДПИСЬ НАД ПОИЛКОЙ: [иконка лейки] 12/30
    // ═══════════════════════════════════════════════════════════
    void UpdateLabel()
    {
        EnsureLabel();
        if (label == null) return;

        label.Set(water + "/" + Capacity,
            water > 0 ? new Color(0.65f, 0.85f, 1f) : new Color(1f, 0.75f, 0.6f));
    }

    void EnsureLabel()
    {
        if (label != null) return;

        // 1) Лейбл вручную добавлен в сцену под поилкой?
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

        // Иконка лейки — из предмета WateringCan
        ItemData can = ItemDatabase.Find("WateringCan");
        label.SetIcon(can != null ? can.icon : null);
    }
}
