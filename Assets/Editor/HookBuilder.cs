using UnityEngine;
using UnityEditor;

/// <summary>
/// Генератор крючков: ItemData-ассеты в Resources/Items/Hooks/.
/// 18 штук: 9 материалов (колонки шита) × одинарный/двойной поддев.
/// Каждый крючок держит свой весовой диапазон — вне диапазона рыба НЕ КЛЮЁТ
/// (фильтр поклёвки в FishingSpot).
/// Спрайты: Assets/Art/Icons/Fish/Artificial bait.png (сетка 9×4, уже нарезан):
///   кадры 0–8 = ряд 0, двойные ОБЫЧНЫЕ (worldSprite для дропа);
///   кадры 9–17 = ряд 1, одинарные ОБЫЧНЫЕ (worldSprite);
///   кадры 18–26 = ряд 2, двойные с белой обводкой (ИКОНКИ);
///   кадры 27–35 = ряд 3, одинарные с белой обводкой (ИКОНКИ).
/// Запуск: Tools → Fish → 8. Build Hooks. Повторный запуск ОБНОВЛЯЕТ in place
/// (иконки/worldSprite перезапишутся кадрами из таблицы, guid стабильны).
/// Продажа: торговец подхватывает крючки кодом (ShopInteraction, без перков).
/// </summary>
public static class HookBuilder
{
    const string HooksDir = "Assets/Resources/Items/Hooks/";
    const string SheetPath = "Assets/Art/Icons/Fish/Artificial bait.png";

    struct HookDef
    {
        public string assetBase;
        public string nameRu;
        public string descRu;
        public int iconFrame;   // кадр иконки в шите (белоконтурный ряд)
        public int worldFrame;  // кадр обычного спрайта (worldSprite для дропа)
        public float minKg;
        public float maxKg;
        public int casts;
        public int price;
        public ItemRarity rarity;
    }

    // Материалы по колонкам шита (цвета): медь, серебро, золото, железо,
    // рубин, сапфир, аметист, розовый кварц, обсидиан. I = одинарный, II = двойной.
    static readonly HookDef[] Hooks = new HookDef[]
    {
        new HookDef { assetBase = "Hook_Copper_I", nameRu = "Медный крючок I",
            descRu = "Один поддев. Мелочь 0–500 г.",
            iconFrame = 27, worldFrame = 9,
            minKg = 0f, maxKg = 0.5f, casts = 20, price = 50, rarity = ItemRarity.Common },
        new HookDef { assetBase = "Hook_Copper_II", nameRu = "Медный крючок II",
            descRu = "Два поддева. Рыба 100 г – 1 кг.",
            iconFrame = 18, worldFrame = 0,
            minKg = 0.1f, maxKg = 1f, casts = 20, price = 120, rarity = ItemRarity.Common },
        new HookDef { assetBase = "Hook_Silver_I", nameRu = "Серебряный крючок I",
            descRu = "Один поддев. Рыба 200 г – 2 кг.",
            iconFrame = 28, worldFrame = 10,
            minKg = 0.2f, maxKg = 2f, casts = 20, price = 250, rarity = ItemRarity.Common },
        new HookDef { assetBase = "Hook_Silver_II", nameRu = "Серебряный крючок II",
            descRu = "Два поддева. Рыба 500 г – 3 кг.",
            iconFrame = 19, worldFrame = 1,
            minKg = 0.5f, maxKg = 3f, casts = 22, price = 400, rarity = ItemRarity.Common },
        new HookDef { assetBase = "Hook_Gold_I", nameRu = "Золотой крючок I",
            descRu = "Один поддев. Рыба 1–5 кг.",
            iconFrame = 29, worldFrame = 11,
            minKg = 1f, maxKg = 5f, casts = 22, price = 700, rarity = ItemRarity.Uncommon },
        new HookDef { assetBase = "Hook_Gold_II", nameRu = "Золотой крючок II",
            descRu = "Два поддева. Рыба 2–8 кг.",
            iconFrame = 20, worldFrame = 2,
            minKg = 2f, maxKg = 8f, casts = 25, price = 1000, rarity = ItemRarity.Uncommon },
        new HookDef { assetBase = "Hook_Iron_I", nameRu = "Железный крючок I",
            descRu = "Один поддев. Рыба 3–12 кг: карпы, щуки.",
            iconFrame = 30, worldFrame = 12,
            minKg = 3f, maxKg = 12f, casts = 25, price = 1500, rarity = ItemRarity.Uncommon },
        new HookDef { assetBase = "Hook_Iron_II", nameRu = "Железный крючок II",
            descRu = "Два поддева. Рыба 5–20 кг.",
            iconFrame = 21, worldFrame = 3,
            minKg = 5f, maxKg = 20f, casts = 25, price = 2500, rarity = ItemRarity.Uncommon },
        new HookDef { assetBase = "Hook_Ruby_I", nameRu = "Рубиновый крючок I",
            descRu = "Один поддев. Рыба 8–30 кг.",
            iconFrame = 31, worldFrame = 13,
            minKg = 8f, maxKg = 30f, casts = 30, price = 4000, rarity = ItemRarity.Rare },
        new HookDef { assetBase = "Hook_Ruby_II", nameRu = "Рубиновый крючок II",
            descRu = "Два поддева. Рыба 10–50 кг: сомы.",
            iconFrame = 22, worldFrame = 4,
            minKg = 10f, maxKg = 50f, casts = 30, price = 6000, rarity = ItemRarity.Rare },
        new HookDef { assetBase = "Hook_Sapphire_I", nameRu = "Сапфировый крючок I",
            descRu = "Один поддев. Рыба 15–70 кг.",
            iconFrame = 32, worldFrame = 14,
            minKg = 15f, maxKg = 70f, casts = 30, price = 8000, rarity = ItemRarity.Rare },
        new HookDef { assetBase = "Hook_Sapphire_II", nameRu = "Сапфировый крючок II",
            descRu = "Два поддева. Рыба 20–100 кг.",
            iconFrame = 23, worldFrame = 5,
            minKg = 20f, maxKg = 100f, casts = 35, price = 10000, rarity = ItemRarity.Epic },
        new HookDef { assetBase = "Hook_Amethyst_I", nameRu = "Аметистовый крючок I",
            descRu = "Один поддев. Рыба 30–120 кг. Нужна качаная удочка.",
            iconFrame = 33, worldFrame = 15,
            minKg = 30f, maxKg = 120f, casts = 35, price = 12000, rarity = ItemRarity.Epic },
        new HookDef { assetBase = "Hook_Amethyst_II", nameRu = "Аметистовый крючок II",
            descRu = "Два поддева. Рыба 40–150 кг.",
            iconFrame = 24, worldFrame = 6,
            minKg = 40f, maxKg = 150f, casts = 35, price = 15000, rarity = ItemRarity.Epic },
        new HookDef { assetBase = "Hook_Rose_I", nameRu = "Крючок розового кварца I",
            descRu = "Один поддев. Рыба 50–170 кг.",
            iconFrame = 34, worldFrame = 16,
            minKg = 50f, maxKg = 170f, casts = 40, price = 18000, rarity = ItemRarity.Legendary },
        new HookDef { assetBase = "Hook_Rose_II", nameRu = "Крючок розового кварца II",
            descRu = "Два поддева. Рыба 60–180 кг.",
            iconFrame = 25, worldFrame = 7,
            minKg = 60f, maxKg = 180f, casts = 40, price = 20000, rarity = ItemRarity.Legendary },
        new HookDef { assetBase = "Hook_Obsidian_I", nameRu = "Обсидиановый крючок I",
            descRu = "Один поддев. Рыба 80–190 кг. Левиафаны.",
            iconFrame = 35, worldFrame = 17,
            minKg = 80f, maxKg = 190f, casts = 40, price = 25000, rarity = ItemRarity.Legendary },
        new HookDef { assetBase = "Hook_Obsidian_II", nameRu = "Обсидиановый крючок II",
            descRu = "Два поддева. Рыба 100–200 кг. Удочку качай перками до 200 кг.",
            iconFrame = 26, worldFrame = 8,
            minKg = 100f, maxKg = 200f, casts = 45, price = 30000, rarity = ItemRarity.Legendary },
    };

    [MenuItem("Tools/Fish/8. Build Hooks (крючки)")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Items/Hooks"))
        {
            System.IO.Directory.CreateDirectory(HooksDir);
            AssetDatabase.Refresh();
        }

        int made = 0, noSprite = 0;
        foreach (HookDef h in Hooks)
        {
            string path = HooksDir + h.assetBase + ".asset";
            ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemData>();
                AssetDatabase.CreateAsset(item, path);
                made++;
            }

            Sprite icon = FindFrame(h.iconFrame);
            Sprite world = FindFrame(h.worldFrame);

            item.itemName = h.nameRu;
            item.description = h.descRu;
            if (icon != null) item.icon = icon;
            else if (item.icon == null) noSprite++;
            if (world != null) item.worldSprite = world;
            else if (item.worldSprite == null) item.worldSprite = item.icon;
            item.itemType = ItemType.FishingHook;
            item.rarity = h.rarity;
            item.isStackable = false;
            item.maxStack = 1;
            item.hookMinKg = h.minKg;
            item.hookMaxKg = h.maxKg;
            item.hookMaxCasts = h.casts;
            item.shopPrice = h.price;
            EditorUtility.SetDirty(item);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Hooks] Готово: " + Hooks.Length + " крючков (новых: " + made
            + (noSprite > 0 ? ", без спрайта: " + noSprite : "")
            + "). Продаются у торговца автоматически.");
    }

    /// <summary>Кадр шита Artificial bait_N. null если шит не нарезан/нет файла.</summary>
    static Sprite FindFrame(int frame)
    {
        Object[] sub = AssetDatabase.LoadAllAssetsAtPath(SheetPath);
        if (sub == null) return null;
        string want = "Artificial bait_" + frame;
        foreach (Object o in sub)
            if (o is Sprite s && s.name == want) return s;
        return null;
    }
}
