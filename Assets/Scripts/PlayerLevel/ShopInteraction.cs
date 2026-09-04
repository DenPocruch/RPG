using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Прилавок/NPC-торговец с БЕЗ диалога: удар рядом → сразу открывается
/// магазин со СВОИМ ассортиментом (у каждого прилавка свой товар).
/// Для торговца с диалогом используй NPCInteractable + TraderNPC.
/// </summary>
public class ShopInteraction : MonoBehaviour, IInteractable
{
    [Header("Товар этого прилавка (у каждого свой!)")]
    public ShopManager.ShopItem[] itemsForSale;
    [Header("Вторая вкладка (опц. — напр. животные)")]
    public ShopManager.ShopItem[] itemsForSaleAnimals;

    [Header("Заголовок окна магазина (опц.)")]
    public string shopTitle = "";

    [Header("Анимация приветствия (опц.)")]
    public Animator shopkeeperAnimator;
    public string greetAnimationTrigger = "Greet";

    void Awake()
    {
        EnsureFarmStock();
        EnsureWeaponStock();
    }

    // Оружие/броня Common низких тиров + инструменты Common ВСЕХ тиров —
    // кодом, сцену править не нужно. Редкие тиры оружия/брони — только лут и ковка.
    void EnsureWeaponStock()
    {
        var list = new List<ShopManager.ShopItem>(itemsForSaleAnimals ?? new ShopManager.ShopItem[0]);
        bool added = false;
        foreach (string assetName in new[] {
            "WoodSword_Common", "WoodHelmet_Common", "WoodChestplate_Common", "WoodLeggings_Common", "WoodBoots_Common",
            "CopperSword_Common", "CopperHelmet_Common", "CopperChestplate_Common", "CopperLeggings_Common", "CopperBoots_Common",
            "WoodBow_Common", "WoodStaff_Common", "CopperBow_Common", "CopperStaff_Common",
            // Инструменты — Common ВСЕХ тиров (гейт прогрессии шахты/рубки, цена вместо лута)
            "WoodPickaxe_Common", "CopperPickaxe_Common", "IronPickaxe_Common",
            "GoldPickaxe_Common", "PlatinumPickaxe_Common", "ObsidianPickaxe_Common",
            "WoodAxe_Common", "CopperAxe_Common", "IronAxe_Common",
            "GoldAxe_Common", "PlatinumAxe_Common", "ObsidianAxe_Common" })
        {
            if (list.Exists(si => si != null && si.item != null && si.item.name == assetName)) continue;
            ItemData item = ItemDatabase.Find(assetName);
            if (item == null) continue; // ассеты ещё не сгенерированы (Tools → Equipment → 1) — молча пропускаем
            // Замок древа: медь+ видна только после перка (ShopUI фильтрует по тегу сам)
            string kind = EquipmentLocks.KindOf(item);
            string tier = EquipmentLocks.TierOf(item);
            string tag = (kind != null && tier != null && tier != "Wood")
                ? EquipmentLocks.TagFor(tier, kind) : "";
            list.Add(new ShopManager.ShopItem
            {
                item = item,
                price = item.shopPrice > 0 ? item.shopPrice : 100,
                unlockTag = tag
            });
            added = true;
        }
        if (added) itemsForSaleAnimals = list.ToArray();
    }

    // Кормушка/поилка добавляются кодом — сцену править не нужно.
    // Дубликаты не создаются: если товар уже в инспекторе — пропускаем.
    void EnsureFarmStock()
    {
        var list = new List<ShopManager.ShopItem>(itemsForSaleAnimals ?? new ShopManager.ShopItem[0]);
        bool added = false;
        added |= TryAddFarmStock(list, "Feeder", "feeder");
        added |= TryAddFarmStock(list, "WaterTrough", "trough");
        added |= TryAddFarmStock(list, "Hammer", ""); // молоток — без перка, виден сразу
        added |= TryAddFarmStock(list, "Pickaxe", ""); // кирка — добыча руды в шахте
        added |= TryAddFarmStock(list, "Scarecrow", ""); // пугало — без перка, виден сразу
        added |= TryAddFarmStock(list, "Beehive", "");   // улей — без перка, виден сразу
        added |= TryAddFarmStock(list, "WineBarrel", "");     // бочка брожения (виноград → вино)
        added |= TryAddFarmStock(list, "CheesePress", "");    // сырный пресс (молоко → сыр)
        added |= TryAddFarmStock(list, "ButterChurn", "");    // маслобойка (молоко → масло)
        added |= TryAddFarmStock(list, "JamMaker", "");       // джем-мейкер (ягоды → джем)
        if (added) itemsForSaleAnimals = list.ToArray();
    }

    bool TryAddFarmStock(List<ShopManager.ShopItem> list, string itemName, string unlockTag)
    {
        if (list.Exists(si => si != null && si.item != null && si.item.name == itemName)) return false;

        ItemData item = ItemDatabase.Find(itemName);
        if (item == null)
        {
            Debug.LogWarning("[Shop] Фарм-товар не найден в ItemDatabase: " + itemName);
            return false;
        }

        list.Add(new ShopManager.ShopItem
        {
            item = item,
            price = item.shopPrice > 0 ? item.shopPrice : 500,
            unlockTag = unlockTag
        });
        return true;
    }

    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        if (ShopUI.Instance != null)
            ShopUI.Instance.Open(itemsForSale, itemsForSaleAnimals, shopTitle);

        if (shopkeeperAnimator != null)
            shopkeeperAnimator.SetTrigger(greetAnimationTrigger);
    }
}
