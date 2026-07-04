using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Окно характеристик персонажа.
/// Открывается поверх всего по кнопке — не сдвигает другие панели.
/// </summary>
public class StatsUI : MonoBehaviour
{
    public static StatsUI Instance;

    [Header("UI панель")]
    public GameObject statsPanel;

    [Header("Аватар персонажа (опционально)")]
    public Image characterAvatar;

    [Header("Текстовые поля")]
    public TMP_Text hpText;
    public TMP_Text attackText;      // базовая атака + бонусы от экипировки
    public TMP_Text weaponDamageText; // итоговый урон с оружием
    public TMP_Text defenseText;
    public TMP_Text attackSpeedText;
    public TMP_Text moveSpeedText;
    public TMP_Text critChanceText;
    public TMP_Text critDamageText;
    public TMP_Text dodgeChanceText;
    public TMP_Text blockChanceText;

    [Header("Цвета значений")]
    public Color baseColor = new Color(1f, 1f, 1f);
    public Color bonusColor = new Color(0.5f, 1f, 0.5f);

    private bool isOpen = false;
    private bool justShown = false; // защита от закрытия в тот же кадр
    private RectTransform panelRect;

    void Awake()
    {
        Instance = this;
        if (statsPanel != null)
        {
            statsPanel.SetActive(false);
            panelRect = statsPanel.GetComponent<RectTransform>();
        }
    }

    void Start()
    {
        if (PlayerStats.Instance != null)
            PlayerStats.Instance.onStatsChanged += Refresh;

        if (HotbarManager.Instance != null)
            HotbarManager.Instance.onActiveItemChanged += OnActiveItemChanged;
    }

    void OnActiveItemChanged(ItemData item)
    {
        if (isOpen) UpdateWeaponDamage();
    }

    void Update()
    {
        if (!isOpen) return;

        if (justShown) { justShown = false; return; }

        bool clicked = Input.GetMouseButtonDown(0) ||
                       (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);

        if (!clicked) return;

        Vector2 pos = Input.touchCount > 0
            ? Input.GetTouch(0).position
            : (Vector2)Input.mousePosition;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (panelRect != null &&
            !RectTransformUtility.RectangleContainsScreenPoint(
                panelRect, pos, canvas != null ? canvas.worldCamera : null))
        {
            Close();
        }
    }

    void UpdateWeaponDamage()
    {
        if (weaponDamageText == null || PlayerStats.Instance == null) return;

        ItemData weapon = HotbarManager.Instance?.GetActiveItem();
        bool isWeapon = weapon != null &&
            (weapon.itemType == ItemType.Weapon || weapon.itemType == ItemType.RangedWeapon);

        float weaponDmg = isWeapon ? weapon.damage : 0f;
        float totalDmg = PlayerStats.Instance.TotalAttack + weaponDmg;

        if (isWeapon)
            weaponDamageText.text = "Урон атаки: " + totalDmg.ToString("0") +
                " <color=#FFCC44>(" + weapon.itemName + " +" + weaponDmg + ")</color>";
        else
            weaponDamageText.text = "Урон атаки: " + totalDmg.ToString("0");
    }

    void OnDestroy()
    {
        if (PlayerStats.Instance != null)
            PlayerStats.Instance.onStatsChanged -= Refresh;

        if (HotbarManager.Instance != null)
            HotbarManager.Instance.onActiveItemChanged -= OnActiveItemChanged;
    }

    public void Toggle()
    {
        if (isOpen) Close();
        else Open();
    }

    public void Open()
    {
        statsPanel.SetActive(true);
        isOpen = true;
        justShown = true;
        Refresh();
    }

    public void Close()
    {
        statsPanel.SetActive(false);
        isOpen = false;
    }

    public bool IsOpen() => isOpen;

    // ─────────────────────────────────────────────────────────────────
    // ОБНОВЛЕНИЕ ЗНАЧЕНИЙ
    // ─────────────────────────────────────────────────────────────────
    public void Refresh()
    {
        PlayerStats ps = PlayerStats.Instance;
        if (ps == null || !isOpen) return;

        SetStat(hpText,
            ps.baseHealth,
            ps.TotalHealth - ps.baseHealth,
            "HP");

        SetStat(attackText,
            ps.baseAttack,
            ps.TotalAttack - ps.baseAttack,
            "Атака");

        // Обновляем урон оружия
        UpdateWeaponDamage();

        SetStat(defenseText,
            ps.baseDefense,
            ps.TotalDefense - ps.baseDefense,
            "Защита");

        SetStatFloat(attackSpeedText,
            ps.baseAttackSpeed,
            ps.TotalAttackSpeed - ps.baseAttackSpeed,
            "Скорость атаки", "0.##");

        SetStatFloat(moveSpeedText,
            ps.baseMoveSpeed,
            ps.TotalMoveSpeed - ps.baseMoveSpeed,
            "Скорость", "0.##");

        SetStatFloat(critChanceText,
            ps.baseCritChance,
            ps.TotalCritChance - ps.baseCritChance,
            "Шанс крита", "0.#", "%");

        SetStatFloat(critDamageText,
            ps.baseCritDamage,
            ps.TotalCritDamage - ps.baseCritDamage,
            "Урон крита", "0.#", "%");

        SetStatFloat(dodgeChanceText,
            ps.baseDodgeChance,
            ps.TotalDodgeChance - ps.baseDodgeChance,
            "Уворот", "0.#", "%");

        SetStatFloat(blockChanceText,
            ps.baseBlockChance,
            ps.TotalBlockChance - ps.baseBlockChance,
            "Блок", "0.#", "%");
    }

    // Целочисленный стат
    void SetStat(TMP_Text label, int baseVal, int bonus, string name)
    {
        if (label == null) return;

        if (bonus != 0)
        {
            string sign = bonus > 0 ? "+" : "";
            string bonusStr = " <color=#7CFF7C>(" + sign + bonus + ")</color>";
            label.text = name + ": " + (baseVal + bonus) + bonusStr;
        }
        else
        {
            label.text = name + ": " + baseVal;
        }
    }

    // Дробный стат
    void SetStatFloat(TMP_Text label, float baseVal, float bonus, string name,
                      string fmt = "0.#", string suffix = "")
    {
        if (label == null) return;

        float total = baseVal + bonus;
        if (Mathf.Abs(bonus) > 0.001f)
        {
            string sign = bonus > 0 ? "+" : "";
            string bonusStr = " <color=#7CFF7C>(" + sign + bonus.ToString(fmt) + suffix + ")</color>";
            label.text = name + ": " + total.ToString(fmt) + suffix + bonusStr;
        }
        else
        {
            label.text = name + ": " + total.ToString(fmt) + suffix;
        }
    }
}