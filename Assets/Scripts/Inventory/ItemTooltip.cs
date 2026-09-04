using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Всплывающая подсказка предмета.
/// Показывает название (цвет по редкости), тип, бонусы и описание.
/// Один на весь Canvas — переиспользуется.
/// </summary>
public class ItemTooltip : MonoBehaviour
{
    public static ItemTooltip Instance;

    [Header("UI элементы")]
    public GameObject tooltipRoot;   // корневой объект (скрывается)
    public RectTransform tooltipRect;
    public TMP_Text nameText;
    public TMP_Text typeText;
    public TMP_Text statsText;
    public TMP_Text descriptionText;
    public Image rarityBorder;

    [Header("Цвета редкости")]
    public Color colorCommon = new Color(0.7f, 0.7f, 0.7f);
    public Color colorUncommon = new Color(0.2f, 0.8f, 0.2f);
    public Color colorRare = new Color(0.2f, 0.4f, 1f);
    public Color colorEpic = new Color(0.7f, 0.2f, 1f);
    public Color colorLegendary = new Color(1f, 0.5f, 0f);

    [Header("Смещение от пальца")]
    public Vector2 offset = new Vector2(80f, 80f);

    private Canvas canvas;

    private bool justShown = false; // защита от закрытия в тот же кадр когда показали

    void Awake()
    {
        // Защита от дубликата: при возврате в сцену её копия PersistentRoot
        // создаёт второй экземпляр — уничтожаем копию, оригинал продолжает жить.
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        canvas = GetComponentInParent<Canvas>();
        if (tooltipRoot != null) tooltipRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Показать tooltip для предмета у позиции на экране.</summary>
    public void Show(ItemData item, Vector2 screenPos)
    {
        if (item == null || tooltipRoot == null) return;

        tooltipRoot.SetActive(true);
        justShown = true; // защита от закрытия в этот же кадр

        // Название с цветом редкости
        if (nameText != null)
        {
            nameText.text = item.itemName;
            nameText.color = GetRarityColor(item.rarity);
        }

        // Рамка
        if (rarityBorder != null)
            rarityBorder.color = GetRarityColor(item.rarity);

        // Тип предмета
        if (typeText != null)
            typeText.text = GetTypeDisplayName(item);

        // Бонусы
        if (statsText != null)
            statsText.text = BuildStatsText(item);

        // Описание
        if (descriptionText != null)
        {
            descriptionText.text = item.description;
            descriptionText.gameObject.SetActive(!string.IsNullOrEmpty(item.description));
        }

        // Позиционирование с учётом краёв экрана
        PositionTooltip(screenPos);
    }

    public void Hide()
    {
        if (tooltipRoot != null) tooltipRoot.SetActive(false);
    }

    public bool IsVisible() => tooltipRoot != null && tooltipRoot.activeSelf;

    void Update()
    {
        if (!IsVisible()) return;

        // Пропускаем кадр в который только что показали
        if (justShown) { justShown = false; return; }

        // Закрываем tooltip при любом клике/тапе
        if (Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began))
        {
            // Не закрываем если клик пришёлся на сам tooltip
            if (IsPointerOverTooltip()) return;
            Hide();
        }
    }

    bool IsPointerOverTooltip()
    {
        if (tooltipRect == null) return false;
        Vector2 pos = Input.touchCount > 0 ? Input.GetTouch(0).position : (Vector2)Input.mousePosition;
        return RectTransformUtility.RectangleContainsScreenPoint(
            tooltipRect, pos, canvas != null ? canvas.worldCamera : null);
    }

    // ─────────────────────────────────────────────────────────────────
    void PositionTooltip(Vector2 screenPos)
    {
        if (tooltipRect == null || canvas == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvas.GetComponent<RectTransform>(),
            screenPos + offset,
            canvas.worldCamera,
            out Vector2 localPos
        );

        tooltipRect.anchoredPosition = localPos;
    }

    string GetTypeDisplayName(ItemData item)
    {
        // Если экипировка — показываем слот
        if (item.IsEquipment)
            return TranslateSlot(item.equipSlot) + " · " + TranslateRarity(item.rarity);
        if (item.itemType == ItemType.RangedWeapon && item.isStaff)
            return "Посох · " + TranslateRarity(item.rarity);
        return TranslateType(item.itemType) + " · " + TranslateRarity(item.rarity);
    }

    string BuildStatsText(ItemData item)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        // Урон оружия
        if ((item.itemType == ItemType.Weapon || item.itemType == ItemType.RangedWeapon)
            && item.damage > 0)
            sb.AppendLine("Урон: " + item.damage);

        // Бонусы
        if (item.bonusHealth != 0) sb.AppendLine(FormatBonus("HP", item.bonusHealth));
        if (item.bonusAttack != 0) sb.AppendLine(FormatBonus("Атака", item.bonusAttack));
        if (item.bonusDefense != 0) sb.AppendLine(FormatBonus("Защита", item.bonusDefense));
        if (item.bonusAttackSpeed != 0) sb.AppendLine(FormatBonus("Скорость атаки", item.bonusAttackSpeed, true));
        if (item.bonusMoveSpeed != 0) sb.AppendLine(FormatBonus("Скорость", item.bonusMoveSpeed));
        if (item.bonusCritChance != 0) sb.AppendLine(FormatBonus("Шанс крита", item.bonusCritChance, false, "%"));
        if (item.bonusCritDamage != 0) sb.AppendLine(FormatBonus("Урон крита", item.bonusCritDamage, false, "%"));
        if (item.bonusDodgeChance != 0) sb.AppendLine(FormatBonus("Уворот", item.bonusDodgeChance, false, "%"));
        if (item.bonusBlockChance != 0) sb.AppendLine(FormatBonus("Блок", item.bonusBlockChance, false, "%"));
        if (item.bonusAccuracy != 0) sb.AppendLine(FormatBonus("Точность", item.bonusAccuracy, false, "%"));
        if (item.bonusPenetration != 0) sb.AppendLine(FormatBonus("Пробитие", item.bonusPenetration));
        if (item.bonusYield != 0) sb.AppendLine(FormatBonus("Добыча", item.bonusYield, false, "%"));

        // Стак
        if (item.isStackable && item.maxStack > 1)
            sb.AppendLine("Макс. стак: " + item.maxStack);

        return sb.ToString().TrimEnd();
    }

    string FormatBonus(string name, float value, bool isMultiplier = false, string suffix = "")
    {
        string sign = value > 0 ? "+" : "";
        string color = value > 0 ? "#7CFF7C" : "#FF7C7C";
        string val = isMultiplier ? value.ToString("0.##") : (value >= 1 || value <= -1 ? value.ToString("0") : value.ToString("0.##"));
        return "<color=" + color + ">" + sign + val + suffix + " " + name + "</color>";
    }

    string FormatBonus(string name, int value)
    {
        string sign = value > 0 ? "+" : "";
        string color = value > 0 ? "#7CFF7C" : "#FF7C7C";
        return "<color=" + color + ">" + sign + value + " " + name + "</color>";
    }

    Color GetRarityColor(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Common: return colorCommon;
            case ItemRarity.Uncommon: return colorUncommon;
            case ItemRarity.Rare: return colorRare;
            case ItemRarity.Epic: return colorEpic;
            case ItemRarity.Legendary: return colorLegendary;
        }
        return Color.white;
    }

    string TranslateRarity(ItemRarity r)
    {
        switch (r)
        {
            case ItemRarity.Common: return "Обычный";
            case ItemRarity.Uncommon: return "Необычный";
            case ItemRarity.Rare: return "Редкий";
            case ItemRarity.Epic: return "Эпический";
            case ItemRarity.Legendary: return "Легендарный";
        }
        return "";
    }

    string TranslateType(ItemType t)
    {
        switch (t)
        {
            case ItemType.Weapon: return "Оружие";
            case ItemType.RangedWeapon: return "Лук";
            case ItemType.Hoe: return "Мотыга";
            case ItemType.Pickaxe: return "Кирка";
            case ItemType.BugNet: return "Сачок";
            case ItemType.Axe: return "Топор";
            case ItemType.Sickle: return "Серп";
            case ItemType.Sapling: return "Саженец";
            case ItemType.WateringCan: return "Лейка";
            case ItemType.Consumable: return "Расходник";
            case ItemType.Material: return "Материал";
            case ItemType.Seed: return "Семена";
            case ItemType.Crop: return "Урожай";
            case ItemType.Tool: return "Инструмент";
        }
        return t.ToString();
    }

    string TranslateSlot(EquipmentSlotType s)
    {
        switch (s)
        {
            case EquipmentSlotType.Helmet: return "Шлем";
            case EquipmentSlotType.Armor: return "Броня";
            case EquipmentSlotType.Pants: return "Штаны";
            case EquipmentSlotType.Boots: return "Сапоги";
            case EquipmentSlotType.Gloves: return "Перчатки";
            case EquipmentSlotType.Weapon: return "Оружие";
            case EquipmentSlotType.Shield: return "Щит";
            case EquipmentSlotType.Ring:
            case EquipmentSlotType.Ring1:
            case EquipmentSlotType.Ring2: return "Кольцо";
            case EquipmentSlotType.Earrings: return "Серьги";
            case EquipmentSlotType.Bracelet: return "Браслет";
            case EquipmentSlotType.Amulet: return "Амулет";
        }
        return "";
    }
}