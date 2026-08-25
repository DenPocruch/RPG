using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Скупщик урожая: базовые цены, «спрос дня» и репутация.
/// Вешается на объект в сцене City (рядом со скупщиком).
///
/// СПРОС ДНЯ: раз в несколько часов РЕАЛЬНОГО времени скупщик выбирает
/// культуру, за которую платит ×2. В пуле спроса только культуры,
/// разблокированные перками (семена). Иконку и таймер показывает SellUI.
///
/// РЕПУТАЦИЯ: копится с каждого проданного золота. Пороги большие —
/// репутация поднимается медленно и только активной торговлей.
///
/// ЦЕНА: база × качество (серебро +15% / золото +30% / пурпур +50%)
///       × спрос дня (×2) × репутация (+0..20%)
/// </summary>
public class BuyerManager : MonoBehaviour, ISaveable
{
    public static BuyerManager Instance { get; private set; }

    [Header("Спрос дня")]
    [Tooltip("Через сколько часов реального времени меняется востребованная культура")]
    public float demandIntervalHours = 4f;
    [Tooltip("Множитель цены за востребованную культуру")]
    public float demandMultiplier = 2f;

    [Header("Репутация (пороги уровня)")]
    [Tooltip("Золота продаж для каждого уровня репутации")]
    public int[] reputationThresholds = { 0, 2000, 6000, 15000, 40000 };
    [Tooltip("Бонус цены (%) за каждый уровень репутации")]
    public float[] reputationBonus = { 0f, 5f, 10f, 15f, 20f };
    [Tooltip("Названия уровней репутации")]
    public string[] reputationNames = { "Новичок", "Знакомый", "Поставщик", "Партнёр", "Правая рука" };

    // ── Базовые цены культур (за 1 шт) ──
    static readonly Dictionary<string, int> basePrices = new Dictionary<string, int>
    {
        { "Wheat", 8 }, { "Carrot", 12 }, { "Potato", 15 }, { "Tomat", 20 },
        { "Corn", 25 }, { "Cabbage", 30 }, { "Beetroot", 35 }, { "Cucumber", 35 },
        { "Eggplant", 45 }, { "Hot Pepper", 50 }, { "Strawberry", 55 }, { "Grapes", 60 },
        { "Sunflower", 60 }, { "Pumpkin", 70 }, { "Melon", 75 }, { "Watermelon", 85 },
        { "Pineapple", 120 }, { "Blueberry", 50 }
    };

    // ── Теги разблокировки семян (пусто = культура доступна всегда) ──
    static readonly Dictionary<string, string> seedTags = new Dictionary<string, string>
    {
        { "Wheat", "" }, { "Carrot", "seed_carrot" }, { "Potato", "seed_potato" },
        { "Tomat", "seed_tomato" }, { "Corn", "seed_corn" }, { "Cabbage", "seed_cabbage" },
        { "Beetroot", "seed_beetroot" }, { "Eggplant", "seed_eggplant" },
        { "Hot Pepper", "seed_pepper" }, { "Grapes", "seed_grapes" },
        { "Pumpkin", "seed_pumpkin" }, { "Watermelon", "seed_watermelon" },
        { "Sunflower", "seed_sunflower" }, { "Pineapple", "seed_pineapple" },
        { "Strawberry", "seed_strawberry" }, { "Onion", "seed_onion" }
    };

    // ── Множители качества ──
    public static float QualityMultiplier(string assetName)
    {
        if (assetName.EndsWith(" Silver")) return 1.15f;
        if (assetName.EndsWith(" Gold")) return 1.30f;
        if (assetName.EndsWith(" Purple")) return 1.50f;
        return 1f;
    }

    public string state = "ok";

    // ── Состояние ──
    float reputation = 0f;              // всего золота заработано скупщику
    string demandCrop = "";             // востребованная культура (asset name)
    long demandEndTicks = 0;            // когда спрос сменится (реальное время)

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SaveManager.Instance?.Register(this);
    }

    void Start()
    {
        SaveManager.Instance?.LoadInto(this);
        EnsureDemandValid();
    }

    void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    // ═══════════════════════════════════════════════════════════
    // ЦЕНЫ
    // ═══════════════════════════════════════════════════════════

    /// <summary>Базовое имя культуры (убирает суффиксы качества).</summary>
    public static string BaseCropName(string assetName)
    {
        if (assetName.EndsWith(" Silver")) return assetName.Substring(0, assetName.Length - 7);
        if (assetName.EndsWith(" Gold")) return assetName.Substring(0, assetName.Length - 6);
        if (assetName.EndsWith(" Purple")) return assetName.Substring(0, assetName.Length - 7);
        return assetName;
    }

    /// <summary>Является ли предмет урожаем, который скупщик покупает.</summary>
    public bool IsSellable(ItemData item)
    {
        if (item == null) return false;
        return basePrices.ContainsKey(BaseCropName(item.name));
    }

    /// <summary>Итоговая цена за 1 шт с учётом качества, спроса и репутации.</summary>
    public int GetUnitPrice(ItemData item)
    {
        if (item == null || !IsSellable(item)) return 0;

        string baseCrop = BaseCropName(item.name);
        float price = basePrices[baseCrop];
        price *= QualityMultiplier(item.name);

        EnsureDemandValid();
        if (demandCrop == baseCrop) price *= demandMultiplier;

        price *= (1f + GetReputationBonus() / 100f);

        return Mathf.Max(1, Mathf.RoundToInt(price));
    }

    // ═══════════════════════════════════════════════════════════
    // СПРОС ДНЯ (реальное время)
    // ═══════════════════════════════════════════════════════════

    void EnsureDemandValid()
    {
        long now = DateTime.UtcNow.Ticks;
        if (demandEndTicks <= now)
        {
            RerollDemand(now);
        }
    }

    void RerollDemand(long nowTicks)
    {
        // Пул: только культуры, разблокированные перками (или базовая пшеница)
        var pool = new List<string>();
        foreach (var kvp in basePrices)
        {
            string tag = seedTags.ContainsKey(kvp.Key) ? seedTags[kvp.Key] : "";
            if (string.IsNullOrEmpty(tag) ||
                (SkillTreeManager.Instance != null && SkillTreeManager.Instance.IsNodeUnlockedByFeature(tag)))
                pool.Add(kvp.Key);
        }

        if (pool.Count == 0) { demandCrop = ""; }
        else
        {
            // Не повторяем прошлый спрос, если есть выбор
            var candidates = pool.Where(c => c != demandCrop).ToList();
            if (candidates.Count == 0) candidates = pool;
            demandCrop = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        demandEndTicks = nowTicks + (long)(demandIntervalHours * 3600.0 * TimeSpan.TicksPerSecond);
    }

    /// <summary>Востребованная культура сейчас (asset name, "" = спроса нет).</summary>
    public string GetDemandCrop()
    {
        EnsureDemandValid();
        return demandCrop;
    }

    /// <summary>Сколько реальных секунд осталось до смены спроса.</summary>
    public double GetDemandSecondsLeft()
    {
        EnsureDemandValid();
        return (demandEndTicks - DateTime.UtcNow.Ticks) / (double)TimeSpan.TicksPerSecond;
    }

    /// <summary>Спрос активен на эту культуру?</summary>
    public bool IsInDemand(ItemData item)
    {
        if (item == null || string.IsNullOrEmpty(demandCrop)) return false;
        return BaseCropName(item.name) == demandCrop;
    }

    // ═══════════════════════════════════════════════════════════
    // ПРОДАЖА И РЕПУТАЦИЯ
    // ═══════════════════════════════════════════════════════════

    /// <summary>Продать N штук. Возвращает заработанное золото.</summary>
    public int Sell(ItemData item, int count)
    {
        if (item == null || count <= 0 || !IsSellable(item)) return 0;

        int unit = GetUnitPrice(item);
        int gold = unit * count;

        if (CurrencyManager.Instance == null) return 0;
        CurrencyManager.Instance.AddGold(gold);

        // Репутация растёт с заработанного золота
        reputation += gold;

        // Сейв по событию: продажа
        SaveManager.Instance?.Save();
        return gold;
    }

    public float GetReputation() => reputation;

    /// <summary>Уровень репутации (индекс в массивах порогов).</summary>
    public int GetReputationLevel()
    {
        int level = 0;
        for (int i = 0; i < reputationThresholds.Length; i++)
            if (reputation >= reputationThresholds[i]) level = i;
        return level;
    }

    public float GetReputationBonus() => reputationBonus[GetReputationLevel()];

    public string GetReputationName() => reputationNames[GetReputationLevel()];

    /// <summary>Сколько золота продаж до следующего уровня репутации.</summary>
    public int GetReputationToNext()
    {
        int level = GetReputationLevel();
        if (level + 1 >= reputationThresholds.Length) return 0; // максимум
        return reputationThresholds[level + 1] - (int)reputation;
    }

    // ═══════════════════════════════════════════════════════════
    // ISaveable
    // ═══════════════════════════════════════════════════════════
    [System.Serializable]
    private class BuyerSave
    {
        public float reputation;
        public string demandCrop;
        public long demandEndTicks;
    }

    public string SaveKey => "buyer";

    public string CaptureState()
    {
        EnsureDemandValid();
        BuyerSave save = new BuyerSave
        {
            reputation = reputation,
            demandCrop = demandCrop,
            demandEndTicks = demandEndTicks
        };
        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        BuyerSave save = JsonUtility.FromJson<BuyerSave>(json);
        if (save == null) return;
        reputation = save.reputation;
        demandCrop = save.demandCrop;
        demandEndTicks = save.demandEndTicks;
    }
}
