using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// Отображает золото в UI. Иконка монеты + число.
/// </summary>
public class CurrencyUI : MonoBehaviour
{
    public static CurrencyUI Instance;

    [Header("UI элементы")]
    public TMP_Text goldText;
    public Image goldIcon; // иконка монеты (опционально)

    [Header("Формат отображения")]
    public string prefix = ""; // например "💰 " или оставь пустым

    void Awake()
    {
        // Защита от дубликата: копия PersistentRoot при возврате в сцену
        // создаёт второй экземпляр — копию уничтожаем, оригинал живёт
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        if (CurrencyManager.Instance != null)
            CurrencyManager.Instance.onGoldChanged += UpdateUI;

        UpdateUI(CurrencyManager.Instance != null ? CurrencyManager.Instance.Gold : 0);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (CurrencyManager.Instance != null)
            CurrencyManager.Instance.onGoldChanged -= UpdateUI;
    }

    void UpdateUI(int amount)
    {
        if (goldText != null)
            goldText.text = prefix + FormatGold(amount);
    }

    // Форматирование: 1500 → "1.5K", 1000000 → "1M"
    string FormatGold(int amount)
    {
        if (amount >= 1000000) return (amount / 1000000f).ToString("0.#") + "M";
        if (amount >= 1000) return (amount / 1000f).ToString("0.#") + "K";
        return amount.ToString();
    }
}