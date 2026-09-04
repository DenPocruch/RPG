using UnityEngine;

/// <summary>
/// Замки экипировки за перками древа (ветка Equipment).
/// Дерево и кожа свободны; медь+ открываются узлами equip_&lt;тир&gt;_&lt;вид&gt;
/// (цепочка: железо требует медь и т.д. — через requiredNodes узлов).
/// Один узел открывает ВСЕ редкости вида (редкость — апгрейд, не разблокировка).
/// Старые/базовые предметы без тира в имени (Weapon, Pickaxe...) — всегда свободны.
/// </summary>
public static class EquipmentLocks
{
    public const string TAG_PREFIX = "equip_";

    static readonly string[] TIERS = { "Copper", "Iron", "Gold", "Platinum", "Obsidian", "Wood" };

    /// <summary>Тег узла для тира+вида. Единый источник для билдера перков,
    /// магазина и проверок (расхождение = магазин показывает, а надеть нельзя!).</summary>
    public static string TagFor(string tierId, string kindId)
    {
        return TAG_PREFIX + tierId.ToLower() + "_" + kindId.ToLower();
    }

    /// <summary>Вид вещи по предмету (null = не экипировка тиров, замка нет).</summary>
    public static string KindOf(ItemData item)
    {
        if (item == null) return null;
        switch (item.itemType)
        {
            case ItemType.Weapon: return "Sword";
            case ItemType.RangedWeapon: return item.isStaff ? "Staff" : "Bow";
            case ItemType.Helmet: return "Helmet";
            case ItemType.Armor: return "Chestplate";
            case ItemType.Pants: return "Leggings";
            case ItemType.Boots: return "Boots";
            case ItemType.Pickaxe: return "Pickaxe";
            case ItemType.Axe: return "Axe";
            default: return null;
        }
    }

    /// <summary>Тир по имени ассета (CopperSword_Common → Copper).
    /// Wood и неизвестные (старые Weapon/Pickaxe...) — свободны.</summary>
    public static string TierOf(ItemData item)
    {
        if (item == null || string.IsNullOrEmpty(item.name)) return null;
        foreach (string t in TIERS)
            if (item.name.StartsWith(t)) return t;
        return null;
    }

    /// <summary>Можно ли носить/использовать предмет.</summary>
    public static bool IsUnlocked(ItemData item)
    {
        if (item == null) return false;
        string kind = KindOf(item);
        if (kind == null) return true; // не тировая экипировка — свободно
        string tier = TierOf(item);
        if (tier == null || tier == "Wood") return true; // дерево и легаси — свободно
        if (SkillTreeManager.Instance == null) return true; // сейф-режим без менеджера
        return SkillTreeManager.Instance.IsNodeUnlockedByFeature(TagFor(tier, kind));
    }

    /// <summary>Сообщение для ActionLog при попытке использовать закрытое.</summary>
    public static string LockMessage(ItemData item)
    {
        string name = item != null ? item.itemName : "предмет";
        return "[Экипировка] Закрыто в древе навыков: " + name;
    }
}
