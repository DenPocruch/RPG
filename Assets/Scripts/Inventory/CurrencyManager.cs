using UnityEngine;

/// <summary>
/// Хранит золото игрока. Доступ через CurrencyManager.Instance.
/// </summary>
public class CurrencyManager : MonoBehaviour, ISaveable
{
    public static CurrencyManager Instance;

    [Header("Начальное золото")]
    public int startGold = 0;

    private int gold = 0;

    // Событие — золото изменилось (UI подписывается)
    public System.Action<int> onGoldChanged;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SaveManager.Instance?.Register(this);
    }

    void Start()
    {
        gold = startGold;
        onGoldChanged?.Invoke(gold);
        SaveManager.Instance?.LoadInto(this); // перезапишет сохранённым если есть
    }

    // ─── ISaveable ─────────────────────────────────────────────
    [System.Serializable] private class GoldSave { public int gold; }

    public string SaveKey => "gold";

    public string CaptureState()
    {
        return JsonUtility.ToJson(new GoldSave { gold = gold });
    }

    public void RestoreState(string json)
    {
        GoldSave s = JsonUtility.FromJson<GoldSave>(json);
        if (s == null) return;
        gold = s.gold;
        onGoldChanged?.Invoke(gold);
    }

    public int Gold => gold;

    public void AddGold(int amount)
    {
        if (amount <= 0) return;
        gold += amount;
        onGoldChanged?.Invoke(gold);
        Debug.Log("[Gold] +" + amount + " | Итого: " + gold);
    }

    public bool SpendGold(int amount)
    {
        if (gold < amount)
        {
            Debug.Log("[Gold] Недостаточно золота! Нужно: " + amount + " Есть: " + gold);
            return false;
        }
        gold -= amount;
        onGoldChanged?.Invoke(gold);
        return true;
    }
}