using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Генерация экипировки из спрайтов "Weapons and Armor":
/// Tools → Equipment → 1. Build Equipment Set, затем 2. Patch Loot.
/// 6 тиров: Wood (только Common для магазина), Copper/Iron/Gold/Platinum/Obsidian
/// (меч/шлем/нагрудник/поножи/сапоги × 5 редкостей). Повторный запуск ОБНОВЛЯЕТ
/// ассеты in place (guid сохраняются — лут/магазин не ломаются), цепочки
/// nextRarityVersion перелинковываются.
/// Баланс (якоря: игрок 100 HP/10 атк, слайм ~100 HP/~10 урона):
///   урон меча = база тира × множитель редкости (1 / 1.15 / 1.3 / 1.5 / 1.75);
///   легендарная медь (25) ≈ обычному золоту (28, будет позже), но с 4 доп. статами.
/// Апгрейд у кузнеца: 9/6/3/3 вещей + руда тира 5/10/20/35 за крафт.
/// </summary>
public static class EquipmentBuilder
{
    const string ART = "Assets/Art/Icons/RPG icons/Weapons and Armor/";
    const string OUT = "Assets/Resources/Items/Equipment/";
    const string SLIME_TABLE = "Assets/Resources/Items/SlimeLootTable.asset";
    const string GOBLIN_TABLE = "Assets/Resources/Items/GoblinLootTable.asset";
    const string LOOT_PREFAB = "Assets/Resources/LootItemPrefab.prefab";

    static readonly string[] RARITY_FILES = { "Common", "Uncommon", "Rare", "Epic", "Legendary" };
    static readonly float[] RARITY_MULT = { 1f, 1.15f, 1.3f, 1.5f, 1.75f };
    // Сколько вещей нужно на апгрейд С текущей редкости (9/6/3/3 — синхронно с CraftingManager.RequiredForRarity!)
    static readonly int[] UPGRADE_COUNT = { 9, 6, 3, 3 };
    static readonly int[] ORE_COST = { 5, 10, 20, 35 };

    private class TierDef
    {
        public string id;        // Copper (папка OUT + имя файла)
        public string artFolder; // "2. Cooper" (внимание: опечатка Cooper — как в проекте!)
        public string adjM;      // "Медный" (меч/шлем/нагрудник/топор)
        public string adjPl;     // "Медные" (поножи/сапоги)
        public string adjF;      // "Медная" (кирка)
        public int swordBase;    // базовый урон меча (Common)
        public int helmDef, helmHp, chestDef, chestHp, legsDef, legsHp, bootsDef, bootsHp;
        public string orePath;   // null = нет апгрейда (Wood)
        public string oreRu;     // "медной руды" (для описания)
        public int swordPrice, helmPrice, chestPrice, legsPrice, bootsPrice; // shopPrice Common (0 = не продаётся)
        public bool fullChain;   // false = только Common (Wood)
        public float statMult = 1f; // множитель ДОП. статов тира (bonusAttack/HP/critDmg)
        public float critBump = 0f; // плоская добавка к шансу крита тира (%, чтобы не взрывалось)
        public int toolTier = 1;    // уровень кирки/топора тира (гейт жил: камень 1, медь 2, ...)
        public int pickPrice = 0;   // shopPrice кирки Common (инструменты продаются всех тиров)
        public int axePrice = 0;    // shopPrice топора Common
    }

    static readonly TierDef[] TIERS = {
        new TierDef { id = "Wood", artFolder = "1. Wood", adjM = "Деревянный", adjPl = "Деревянные", adjF = "Деревянная",
            swordBase = 8, helmDef = 1, helmHp = 10, chestDef = 1, chestHp = 15, legsDef = 1, legsHp = 10, bootsDef = 0, bootsHp = 10,
            orePath = null, oreRu = "", swordPrice = 100, helmPrice = 60, chestPrice = 80, legsPrice = 60, bootsPrice = 50, fullChain = false,
            toolTier = 1, pickPrice = 150, axePrice = 150 },
        new TierDef { id = "Copper", artFolder = "2. Cooper", adjM = "Медный", adjPl = "Медные", adjF = "Медная",
            swordBase = 14, helmDef = 2, helmHp = 15, chestDef = 3, chestHp = 30, legsDef = 2, legsHp = 20, bootsDef = 1, bootsHp = 15,
            orePath = "Assets/Resources/Items/Ore/CopperOre.asset", oreRu = "медной руды",
            swordPrice = 250, helmPrice = 150, chestPrice = 200, legsPrice = 150, bootsPrice = 120, fullChain = true,
            statMult = 1f, critBump = 0f, toolTier = 2, pickPrice = 300, axePrice = 300 },
        new TierDef { id = "Iron", artFolder = "3. Iron", adjM = "Железный", adjPl = "Железные", adjF = "Железная",
            swordBase = 20, helmDef = 3, helmHp = 25, chestDef = 4, chestHp = 45, legsDef = 3, legsHp = 30, bootsDef = 2, bootsHp = 20,
            orePath = "Assets/Resources/Items/Ore/IronOre.asset", oreRu = "железной руды",
            swordPrice = 0, helmPrice = 0, chestPrice = 0, legsPrice = 0, bootsPrice = 0, fullChain = true,
            statMult = 1.5f, critBump = 1f, toolTier = 3, pickPrice = 800, axePrice = 800 },
        new TierDef { id = "Gold", artFolder = "4. Gold", adjM = "Золотой", adjPl = "Золотые", adjF = "Золотая",
            swordBase = 28, helmDef = 4, helmHp = 35, chestDef = 6, chestHp = 65, legsDef = 4, legsHp = 45, bootsDef = 3, bootsHp = 30,
            orePath = "Assets/Resources/Items/Ore/GoldOre.asset", oreRu = "золотой руды",
            swordPrice = 0, helmPrice = 0, chestPrice = 0, legsPrice = 0, bootsPrice = 0, fullChain = true,
            statMult = 2f, critBump = 2f, toolTier = 4, pickPrice = 2000, axePrice = 2000 },
        new TierDef { id = "Platinum", artFolder = "5. Platinum", adjM = "Платиновый", adjPl = "Платиновые", adjF = "Платиновая",
            swordBase = 38, helmDef = 5, helmHp = 50, chestDef = 8, chestHp = 90, legsDef = 6, legsHp = 60, bootsDef = 4, bootsHp = 40,
            orePath = "Assets/Resources/Items/Ore/SilverOre.asset", oreRu = "серебряной руды",
            swordPrice = 0, helmPrice = 0, chestPrice = 0, legsPrice = 0, bootsPrice = 0, fullChain = true,
            statMult = 2.75f, critBump = 3f, toolTier = 5, pickPrice = 5000, axePrice = 5000 },
        new TierDef { id = "Obsidian", artFolder = "9. Obsidian", adjM = "Обсидиановый", adjPl = "Обсидиановые", adjF = "Обсидиановая",
            swordBase = 50, helmDef = 7, helmHp = 70, chestDef = 11, chestHp = 120, legsDef = 8, legsHp = 85, bootsDef = 5, bootsHp = 55,
            orePath = "Assets/Resources/Items/Ore/Obsidian.asset", oreRu = "обсидиана",
            swordPrice = 0, helmPrice = 0, chestPrice = 0, legsPrice = 0, bootsPrice = 0, fullChain = true,
            statMult = 3.5f, critBump = 4f, toolTier = 6, pickPrice = 12000, axePrice = 12000 },
    };

    private class SlotDef
    {
        public string id; // Sword (имя PNG + спрайтов, имя файла)
        public string nounM;  // "меч" — null если мн.ч.
        public string nounPl; // "поножи"
        public ItemType itemType;
        public EquipmentSlotType equipSlot;
        public bool isWeapon;
    }

    static readonly SlotDef[] SLOTS = {
        new SlotDef { id = "Sword", nounM = "меч", nounPl = null, itemType = ItemType.Weapon, equipSlot = EquipmentSlotType.Weapon, isWeapon = true },
        new SlotDef { id = "Helmet", nounM = "шлем", nounPl = null, itemType = ItemType.Helmet, equipSlot = EquipmentSlotType.Helmet },
        new SlotDef { id = "Chestplate", nounM = "нагрудник", nounPl = null, itemType = ItemType.Armor, equipSlot = EquipmentSlotType.Armor },
        new SlotDef { id = "Leggings", nounM = null, nounPl = "поножи", itemType = ItemType.Pants, equipSlot = EquipmentSlotType.Pants },
        new SlotDef { id = "Boots", nounM = null, nounPl = "сапоги", itemType = ItemType.Boots, equipSlot = EquipmentSlotType.Boots },
    };

    // Доп. статы меча по редкости [C,U,R,E,L] — бонусы есть С Common, растут с тиром
    static readonly int[] SWORD_BONUS_ATK = { 2, 4, 6, 9, 12 };
    static readonly float[] SWORD_CRIT = { 1, 2, 4, 6, 8 };
    static readonly float[] SWORD_CRIT_DMG = { 0, 10, 20, 35, 55 };
    static readonly float[] SWORD_ATK_SPD = { 0, 0, 0, 0.05f, 0.1f };
    static readonly int[] SWORD_HP = { 0, 0, 0, 10, 25 };
    static readonly float[] SWORD_ACC = { 1, 2, 4, 6, 9 }; // точность плоская (%, без множителя тира)
    static readonly int[] SWORD_PEN = { 0, 1, 2, 3, 5 };   // пробитие × statMult тира

    // Доп. статы брони по редкости
    static readonly float[] ARMOR_DODGE = { 0, 0, 2, 3, 4 };
    static readonly float[] ARMOR_BLOCK_TOP = { 0, 0, 0, 3, 5 };   // шлем+нагрудник
    static readonly float[] ARMOR_BLOCK_LEGS = { 0, 0, 0, 0, 5 };  // поножи
    static readonly float[] BOOTS_MOVE = { 0, 0.1f, 0.1f, 0.15f, 0.2f };
    const int CHEST_LEG_HP = 30; // +HP нагруднику Legendary

    // Инструменты: кирка (ж.р.) и топор (м.р.) — урон топора = 3/4 базы меча тира
    private class ToolDef
    {
        public string id; // Pickaxe / Axe (имя PNG + спрайтов, имя файла)
        public string noun; // "кирка" / "топор"
        public bool feminine; // кирка — ж.р. (adjF), топор — м.р. (adjM)
        public ItemType itemType;
        public float dmgFactor; // доля базы меча
    }

    static readonly ToolDef[] TOOLS = {
        new ToolDef { id = "Pickaxe", noun = "кирка", feminine = true, itemType = ItemType.Pickaxe, dmgFactor = 0f },
        new ToolDef { id = "Axe", noun = "топор", feminine = false, itemType = ItemType.Axe, dmgFactor = 0.75f },
    };

    // Бонус добычи инструментов по редкости [C,U,R,E,L]: 0/25/50/100/150
    static readonly int[] TOOL_YIELD = { 0, 25, 50, 100, 150 };

    // Удочки: зона мини-игры + скорость прогресса. База тира + прибавка редкости.
    // Файлы <Tier>Rod_<Rarity> (CopperRod_Common…), PNG "Fishing Rod".
    static readonly float[] ROD_ZONE_TIER = { 0f, 0.02f, 0.04f, 0.06f, 0.08f, 0.10f }; // Wood..Obsidian
    static readonly float[] ROD_SPEED_TIER = { 0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
    static readonly float[] ROD_ZONE_RAR = { 0f, 0.02f, 0.04f, 0.06f, 0.08f };
    static readonly float[] ROD_SPEED_RAR = { 0f, 0.1f, 0.2f, 0.3f, 0.4f };
    static readonly int[] ROD_PRICE = { 200, 500, 1200, 3000, 7000, 15000 };

    // Дальнобой: лук (точный, быстрый) и посох (критовый, медленный).
    // Урон = доля базы меча тира. Работают через BowController БЕЗ кода:
    // RangedWeapon + arrowPrefab (лук — общая стрела, посох — MagicBolt).
    private class RangedDef
    {
        public string id; // Bow / Staff (имя PNG + спрайтов, имя файла)
        public string noun; // "лук" / "посох" (оба м.р. — adjM)
        public bool isStaff;
        public float dmgFactor; // 0.7 / 0.9 от базы меча
        public float cooldown;  // attackSpeed-секунды: 1.0 / 1.3
        public float projSpeed; // 12 / 9
        public float projRange; // 9 / 7
        public int priceAdd;    // +к цене меча тира (0 = не продаётся)
    }

    static readonly RangedDef[] RANGED = {
        new RangedDef { id = "Bow", noun = "лук", isStaff = false, dmgFactor = 0.7f, cooldown = 1f, projSpeed = 12f, projRange = 9f, priceAdd = 50 },
        new RangedDef { id = "Staff", noun = "посох", isStaff = true, dmgFactor = 0.6f, cooldown = 1.3f, projSpeed = 9f, projRange = 7f, priceAdd = 100 },
    };

    // Доп. статы лука [C,U,R,E,L]: точность плоская, остальное × statMult
    static readonly int[] BOW_ATK = { 0, 1, 2, 4, 6 };
    static readonly float[] BOW_ACC = { 2, 4, 6, 8, 12 };
    static readonly float[] BOW_CRIT = { 0, 1, 2, 3, 5 };
    static readonly float[] BOW_CRITDMG = { 0, 5, 10, 20, 30 };
    // Доп. статы посоха: критовый бёрст
    static readonly int[] STAFF_ATK = { 0, 1, 3, 5, 8 };
    static readonly float[] STAFF_CRIT = { 0, 2, 4, 6, 9 };
    static readonly float[] STAFF_CRITDMG = { 0, 15, 30, 50, 80 };
    static readonly int[] STAFF_HP = { 0, 0, 0, 15, 40 };

    const string ARROW_PREFAB = "Assets/Prefab/Arrow.prefab";
    const string BOLT_PREFAB = "Assets/Prefab/MagicBolt.prefab";
    const string BOLT_SPRITE_TEX = "Assets/Art/Icons/RPG icons/Extras/Gemstones.png";

    // ═══════════════════════════════════════════════════════════
    [MenuItem("Tools/Equipment/1. Build Equipment Set (все 6 тиров)")]
    public static void BuildPilotSet()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Items/Equipment"))
            AssetDatabase.CreateFolder("Assets/Resources/Items", "Equipment");

        GameObject arrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ARROW_PREFAB);
        if (arrowPrefab == null) { Debug.LogError("[Equip] Нет префаба стрелы: " + ARROW_PREFAB); return; }
        GameObject boltPrefab = EnsureMagicBolt();
        if (boltPrefab == null) return;

        int made = 0, updated = 0;
        var chains = new Dictionary<string, List<ItemData>>();

        foreach (TierDef t in TIERS)
        {
            ItemData ore = null;
            if (t.orePath != null)
            {
                ore = AssetDatabase.LoadAssetAtPath<ItemData>(t.orePath);
                if (ore == null) { Debug.LogError("[Equip] Руда не найдена: " + t.orePath); return; }
            }

            int rarities = t.fullChain ? 5 : 1;
            for (int r = 0; r < rarities; r++)
            {
                foreach (SlotDef s in SLOTS)
                {
                    string file = t.id + s.id + "_" + RARITY_FILES[r];
                    string path = OUT + t.id + "/" + file + ".asset";
                    bool exists = AssetDatabase.LoadAssetAtPath<ItemData>(path) != null;

                    ItemData a = GetOrCreate(path);
                    FillAsset(a, t, s, r, ore);
                    if (exists) updated++; else made++;

                    if (t.fullChain)
                    {
                        string key = t.id + s.id;
                        if (!chains.ContainsKey(key)) chains[key] = new List<ItemData>();
                        chains[key].Add(a);
                    }
                }

                // Инструменты тира (кирка/топор) — та же цепочка редкостей
                foreach (ToolDef tool in TOOLS)
                {
                    string file = t.id + tool.id + "_" + RARITY_FILES[r];
                    string path = OUT + t.id + "/" + file + ".asset";
                    bool exists = AssetDatabase.LoadAssetAtPath<ItemData>(path) != null;

                    ItemData a = GetOrCreate(path);
                    FillToolAsset(a, t, tool, r, ore);
                    if (exists) updated++; else made++;

                    if (t.fullChain)
                    {
                        string key = t.id + tool.id;
                        if (!chains.ContainsKey(key)) chains[key] = new List<ItemData>();
                        chains[key].Add(a);
                    }
                }

                // Дальнобой тира (лук/посох) — та же цепочка редкостей
                foreach (RangedDef rd in RANGED)
                {
                    string file = t.id + rd.id + "_" + RARITY_FILES[r];
                    string path = OUT + t.id + "/" + file + ".asset";
                    bool exists = AssetDatabase.LoadAssetAtPath<ItemData>(path) != null;

                    ItemData a = GetOrCreate(path);
                    FillRangedAsset(a, t, rd, r, ore, rd.isStaff ? boltPrefab : arrowPrefab);
                    if (exists) updated++; else made++;

                    if (t.fullChain)
                    {
                        string key = t.id + rd.id;
                        if (!chains.ContainsKey(key)) chains[key] = new List<ItemData>();
                        chains[key].Add(a);
                    }
                }

                // Удочка тира — та же цепочка редкостей
                {
                    string file = t.id + "Rod_" + RARITY_FILES[r];
                    string path = OUT + t.id + "/" + file + ".asset";
                    bool exists = AssetDatabase.LoadAssetAtPath<ItemData>(path) != null;

                    ItemData a = GetOrCreate(path);
                    FillRodAsset(a, t, TIERS_INDEX(t), r, ore);
                    if (exists) updated++; else made++;

                    if (t.fullChain)
                    {
                        string key = t.id + "Rod";
                        if (!chains.ContainsKey(key)) chains[key] = new List<ItemData>();
                        chains[key].Add(a);
                    }
                }
            }
        }

        // Линкуем цепочки Common → ... → Legendary
        foreach (var kvp in chains)
            for (int i = 0; i < kvp.Value.Count - 1; i++)
            {
                kvp.Value[i].nextRarityVersion = kvp.Value[i + 1];
                EditorUtility.SetDirty(kvp.Value[i]);
            }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Equip] Пилот собран: новых " + made + ", обновлено " + updated + ". Цепочек: " + chains.Count + ". Дальше: пункт 2 (лут) + код крафта/магазина.");
    }

    static ItemData GetOrCreate(string path)
    {
        ItemData a = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (a != null) return a;
        string dir = System.IO.Path.GetDirectoryName(path);
        if (!AssetDatabase.IsValidFolder(dir))
        {
            string parent = System.IO.Path.GetDirectoryName(dir);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(dir));
        }
        a = ScriptableObject.CreateInstance<ItemData>();
        AssetDatabase.CreateAsset(a, path);
        return a;
    }

    static void FillAsset(ItemData a, TierDef t, SlotDef s, int r, ItemData ore)
    {
        string adj = s.nounM != null ? t.adjM : t.adjPl;
        string noun = s.nounM ?? s.nounPl;
        // "Медный меч", редкость видна цветом рамки в UI — в имя не добавляем
        a.itemName = adj + " " + noun;
        a.itemType = s.itemType;
        a.rarity = (ItemRarity)r;
        a.equipSlot = s.equipSlot;
        a.isStackable = false;
        a.maxStack = 1;
        a.upgradeOre = ore;

        Sprite[] sprites = LoadSprites(ART + t.artFolder + "/" + s.id + ".png", s.id);
        a.icon = sprites.Length > 0 ? sprites[0] : null;
        a.worldSprite = sprites.Length > 1 ? sprites[1] : a.icon;

        // Сброс всех статов (важно при повторном запуске!)
        a.damage = 0; a.attackRange = 1; a.attackSpeed = 1;
        a.bonusHealth = 0; a.bonusAttack = 0; a.bonusDefense = 0;
        a.bonusAttackSpeed = 0; a.bonusMoveSpeed = 0;
        a.bonusCritChance = 0; a.bonusCritDamage = 0;
        a.bonusDodgeChance = 0; a.bonusBlockChance = 0;
        a.bonusAccuracy = 0; a.bonusPenetration = 0;
        a.nextRarityVersion = null;
        a.shopPrice = 0;

        float m = RARITY_MULT[r];
        if (s.isWeapon)
        {
            a.damage = Mathf.RoundToInt(t.swordBase * m);
            a.attackRange = 1f;
            a.attackSpeed = 0.7f;
            a.bonusAttack = Mathf.RoundToInt(SWORD_BONUS_ATK[r] * t.statMult);
            a.bonusCritChance = SWORD_CRIT[r] + (SWORD_CRIT[r] > 0 ? t.critBump : 0);
            a.bonusCritDamage = Mathf.RoundToInt(SWORD_CRIT_DMG[r] * t.statMult);
        a.bonusAttackSpeed = SWORD_ATK_SPD[r];
        a.bonusHealth = Mathf.RoundToInt(SWORD_HP[r] * t.statMult);
        a.bonusAccuracy = SWORD_ACC[r];
        a.bonusPenetration = Mathf.RoundToInt(SWORD_PEN[r] * t.statMult);
        a.bonusYield = 0;
            if (r == 0) a.shopPrice = t.swordPrice;
            a.description = r < 4 && ore != null
                ? "Апгрейд у кузнеца: " + UPGRADE_COUNT[r] + " таких + " + ORE_COST[r] + " " + t.oreRu + "."
                : "Максимальная редкость.";
        }
        else
        {
            int baseDef = 0, baseHp = 0;
            if (s.id == "Helmet") { baseDef = t.helmDef; baseHp = t.helmHp; }
            else if (s.id == "Chestplate") { baseDef = t.chestDef; baseHp = t.chestHp; }
            else if (s.id == "Leggings") { baseDef = t.legsDef; baseHp = t.legsHp; }
            else if (s.id == "Boots") { baseDef = t.bootsDef; baseHp = t.bootsHp; }

            a.bonusDefense = Mathf.RoundToInt(baseDef * m);
            a.bonusHealth = Mathf.RoundToInt(baseHp * m);
            a.bonusDodgeChance = ARMOR_DODGE[r];
            if (s.id == "Helmet" || s.id == "Chestplate") a.bonusBlockChance = ARMOR_BLOCK_TOP[r];
            if (s.id == "Leggings") a.bonusBlockChance = ARMOR_BLOCK_LEGS[r];
            if (s.id == "Boots") a.bonusMoveSpeed = BOOTS_MOVE[r];
            if (s.id == "Chestplate" && r == 4) a.bonusHealth += Mathf.RoundToInt(CHEST_LEG_HP * t.statMult);
            if (r == 0)
            {
                if (s.id == "Helmet") a.shopPrice = t.helmPrice;
                else if (s.id == "Chestplate") a.shopPrice = t.chestPrice;
                else if (s.id == "Leggings") a.shopPrice = t.legsPrice;
                else if (s.id == "Boots") a.shopPrice = t.bootsPrice;
            }
            a.description = r < 4 && ore != null
                ? "Апгрейд у кузнеца: " + UPGRADE_COUNT[r] + " таких + " + ORE_COST[r] + " " + t.oreRu + "."
                : "Максимальная редкость.";
        }

        EditorUtility.SetDirty(a);
    }

    static void FillToolAsset(ItemData a, TierDef t, ToolDef tool, int r, ItemData ore)
    {
        string adj = tool.feminine ? t.adjF : t.adjM;
        a.itemName = adj + " " + tool.noun;
        a.itemType = tool.itemType;
        a.rarity = (ItemRarity)r;
        a.equipSlot = EquipmentSlotType.None; // инструменты живут в хотбаре, не надеваются
        a.isStackable = false;
        a.maxStack = 1;
        a.upgradeOre = ore;
        a.toolTier = t.toolTier;
        a.bonusYield = TOOL_YIELD[r];

        Sprite[] sprites = LoadSprites(ART + t.artFolder + "/" + tool.id + ".png", tool.id);
        a.icon = sprites.Length > 0 ? sprites[0] : null;
        a.worldSprite = sprites.Length > 1 ? sprites[1] : a.icon;

        // Сброс боевых статов (важно при повторном запуске!)
        a.damage = 0; a.attackRange = 1; a.attackSpeed = 0.8f;
        a.bonusHealth = 0; a.bonusAttack = 0; a.bonusDefense = 0;
        a.bonusAttackSpeed = 0; a.bonusMoveSpeed = 0;
        a.bonusCritChance = 0; a.bonusCritDamage = 0;
        a.bonusDodgeChance = 0; a.bonusBlockChance = 0;
        a.bonusAccuracy = 0; a.bonusPenetration = 0;
        a.nextRarityVersion = null;
        a.shopPrice = 0;

        float m = RARITY_MULT[r];
        if (tool.dmgFactor > 0f)
        {
            // Топор — ещё и оружие: урон 3/4 базы меча тира
            a.damage = Mathf.RoundToInt(t.swordBase * tool.dmgFactor * m);
            a.attackRange = 1f;
        }
        if (r == 0) a.shopPrice = tool.id == "Pickaxe" ? t.pickPrice : t.axePrice;
        string yieldTxt = TOOL_YIELD[r] > 0 ? " Добыча +" + TOOL_YIELD[r] + "%." : "";
        a.description = (r < 4 && ore != null
            ? "Апгрейд у кузнеца: " + UPGRADE_COUNT[r] + " таких + " + ORE_COST[r] + " " + t.oreRu + "."
            : "Максимальная редкость.") + yieldTxt;

        EditorUtility.SetDirty(a);
    }

    static void FillRangedAsset(ItemData a, TierDef t, RangedDef rd, int r, ItemData ore, GameObject projPrefab)
    {
        a.itemName = t.adjM + " " + rd.noun;
        a.itemType = ItemType.RangedWeapon;
        a.rarity = (ItemRarity)r;
        a.equipSlot = EquipmentSlotType.Weapon; // как мечи: зеркало хотбара
        a.isStackable = false;
        a.maxStack = 1;
        a.upgradeOre = ore;
        a.isStaff = rd.isStaff;

        Sprite[] sprites = LoadSprites(ART + t.artFolder + "/" + rd.id + ".png", rd.id);
        a.icon = sprites.Length > 0 ? sprites[0] : null;
        a.worldSprite = sprites.Length > 1 ? sprites[1] : a.icon;

        // Сброс всех статов (важно при повторном запуске!)
        a.damage = 0; a.attackRange = 1; a.attackSpeed = 1;
        a.bonusHealth = 0; a.bonusAttack = 0; a.bonusDefense = 0;
        a.bonusAttackSpeed = 0; a.bonusMoveSpeed = 0;
        a.bonusCritChance = 0; a.bonusCritDamage = 0;
        a.bonusDodgeChance = 0; a.bonusBlockChance = 0;
        a.bonusAccuracy = 0; a.bonusPenetration = 0;
        a.bonusYield = 0;
        a.nextRarityVersion = null;
        a.shopPrice = 0;
        a.arrowPrefab = projPrefab;
        a.arrowSpeed = rd.projSpeed;
        a.arrowRange = rd.projRange;

        float m = RARITY_MULT[r];
        a.damage = Mathf.RoundToInt(t.swordBase * rd.dmgFactor * m);
        a.attackRange = 1f;
        a.attackSpeed = rd.cooldown;
        if (rd.isStaff)
        {
            a.bonusAttack = Mathf.RoundToInt(STAFF_ATK[r] * t.statMult);
            a.bonusCritChance = STAFF_CRIT[r] + (STAFF_CRIT[r] > 0 ? t.critBump : 0);
            a.bonusCritDamage = Mathf.RoundToInt(STAFF_CRITDMG[r] * t.statMult);
            a.bonusHealth = Mathf.RoundToInt(STAFF_HP[r] * t.statMult);
        }
        else
        {
            a.bonusAttack = Mathf.RoundToInt(BOW_ATK[r] * t.statMult);
            a.bonusAccuracy = BOW_ACC[r];
            a.bonusCritChance = BOW_CRIT[r] + (BOW_CRIT[r] > 0 ? t.critBump : 0);
            a.bonusCritDamage = Mathf.RoundToInt(BOW_CRITDMG[r] * t.statMult);
        }
        if (r == 0 && t.swordPrice > 0) a.shopPrice = t.swordPrice + rd.priceAdd;
        a.description = r < 4 && ore != null
            ? "Апгрейд у кузнеца: " + UPGRADE_COUNT[r] + " таких + " + ORE_COST[r] + " " + t.oreRu + "."
            : "Максимальная редкость.";

        EditorUtility.SetDirty(a);
    }

    /// <summary>Общий снаряд посохов: клон Arrow.prefab со спрайтом кристалла.
    /// Создаётся раз, дальше только обновляем спрайт. Спрайт меняется в префабе руками.</summary>
    static GameObject EnsureMagicBolt()
    {
        Sprite[] gems = LoadSprites(BOLT_SPRITE_TEX, "Gemstones");
        if (gems.Length == 0) { Debug.LogError("[Equip] Нет спрайтов снарядов: " + BOLT_SPRITE_TEX); return null; }

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(BOLT_PREFAB);
        if (existing != null)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(BOLT_PREFAB);
            if (contents == null) { Debug.LogError("[Equip] Не открылся " + BOLT_PREFAB); return null; }
            var srOld = contents.GetComponent<SpriteRenderer>();
            if (srOld != null) srOld.sprite = gems[0];
            PrefabUtility.SaveAsPrefabAsset(contents, BOLT_PREFAB);
            PrefabUtility.UnloadPrefabContents(contents);
            return AssetDatabase.LoadAssetAtPath<GameObject>(BOLT_PREFAB);
        }

        GameObject arrowBase = AssetDatabase.LoadAssetAtPath<GameObject>(ARROW_PREFAB);
        if (arrowBase == null) { Debug.LogError("[Equip] Нет префаба стрелы: " + ARROW_PREFAB); return null; }
        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(arrowBase);
        inst.name = "MagicBolt";
        var sr = inst.GetComponent<SpriteRenderer>();
        if (sr != null) sr.sprite = gems[0];
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(inst, BOLT_PREFAB);
        Object.DestroyImmediate(inst);
        return saved;
    }

    static int TIERS_INDEX(TierDef t)
    {
        for (int i = 0; i < TIERS.Length; i++)
            if (TIERS[i].id == t.id) return i;
        return 0;
    }

    static void FillRodAsset(ItemData a, TierDef t, int ti, int r, ItemData ore)
    {
        a.itemName = t.adjF + " удочка";
        a.itemType = ItemType.FishingRod;
        a.rarity = (ItemRarity)r;
        a.equipSlot = EquipmentSlotType.None;
        a.isStackable = false;
        a.maxStack = 1;
        a.upgradeOre = ore;
        a.toolTier = t.toolTier;
        a.fishingZoneBonus = ROD_ZONE_TIER[ti] + ROD_ZONE_RAR[r];
        a.fishingSpeedBonus = ROD_SPEED_TIER[ti] + ROD_SPEED_RAR[r];

        Sprite[] sprites = LoadSprites(ART + t.artFolder + "/Fishing Rod.png", "Fishing Rod");
        a.icon = sprites.Length > 0 ? sprites[0] : null;
        a.worldSprite = sprites.Length > 1 ? sprites[1] : a.icon;

        a.damage = 0; a.attackRange = 1; a.attackSpeed = 1f;
        a.bonusHealth = 0; a.bonusAttack = 0; a.bonusDefense = 0;
        a.bonusAttackSpeed = 0; a.bonusMoveSpeed = 0;
        a.bonusCritChance = 0; a.bonusCritDamage = 0;
        a.bonusDodgeChance = 0; a.bonusBlockChance = 0;
        a.bonusAccuracy = 0; a.bonusPenetration = 0;
        a.bonusYield = 0;
        a.nextRarityVersion = null;
        a.shopPrice = 0;

        if (r == 0) a.shopPrice = ROD_PRICE[ti];
        a.description = r < 4 && ore != null
            ? "Апгрейд у кузнеца: " + UPGRADE_COUNT[r] + " таких + " + ORE_COST[r] + " " + t.oreRu + "."
            : "Максимальная редкость.";

        EditorUtility.SetDirty(a);
    }

    static Sprite[] LoadSprites(string pngPath, string baseName)
    {
        var list = new List<Sprite>();
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(pngPath))
        {
            Sprite sp = o as Sprite;
            if (sp == null) continue;
            // Порядок: _0 (icon), _1 (world)
            if (sp.name == baseName + "_0") list.Insert(0, sp);
            else list.Add(sp);
        }
        if (list.Count == 0)
            Debug.LogWarning("[Equip] Спраты не найдены: " + pngPath);
        return list.ToArray();
    }

    // ═══════════════════════════════════════════════════════════
    [MenuItem("Tools/Equipment/2. Patch Loot (медь — слаймы, железо — гоблины)")]
    public static void PatchLoot()
    {
        LootTable slime = AssetDatabase.LoadAssetAtPath<LootTable>(SLIME_TABLE);
        if (slime == null) { Debug.LogError("[Equip] Нет таблицы: " + SLIME_TABLE); return; }

        AddDrop(slime, "CopperSword_Common", 5f);
        AddDrop(slime, "CopperHelmet_Common", 3f);
        AddDrop(slime, "CopperChestplate_Common", 3f);
        AddDrop(slime, "CopperLeggings_Common", 3f);
        AddDrop(slime, "CopperBoots_Common", 3f);
        AddDrop(slime, "CopperBow_Common", 3f);
        AddDrop(slime, "CopperStaff_Common", 3f);
        if (slime.maxDrops < 3) slime.maxDrops = 3; // слизь + шанс вещи
        EditorUtility.SetDirty(slime);

        // Таблица гоблинов (железо) — создаём раз, дальше только обновляем шансы
        LootTable goblin = AssetDatabase.LoadAssetAtPath<LootTable>(GOBLIN_TABLE);
        if (goblin == null)
        {
            goblin = ScriptableObject.CreateInstance<LootTable>();
            AssetDatabase.CreateAsset(goblin, GOBLIN_TABLE);
        }
        AddDrop(goblin, "IronSword_Common", 5f);
        AddDrop(goblin, "IronHelmet_Common", 3f);
        AddDrop(goblin, "IronChestplate_Common", 3f);
        AddDrop(goblin, "IronLeggings_Common", 3f);
        AddDrop(goblin, "IronBoots_Common", 3f);
        AddDrop(goblin, "IronBow_Common", 3f);
        AddDrop(goblin, "IronStaff_Common", 3f);
        if (goblin.maxDrops < 2) goblin.maxDrops = 2;
        EditorUtility.SetDirty(goblin);

        // Вешаем LootDrop на префабы гоблинов (у них его не было — дропа не было вообще)
        GameObject lootPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LOOT_PREFAB);
        int patched = 0;
        foreach (string prefabPath in new[] {
            "Assets/Prefab/Enemy/Goblins/GoblinSpear.prefab",
            "Assets/Prefab/Enemy/Goblins/GoblinArcher.prefab" })
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null) { Debug.LogWarning("[Equip] Префаб не найден: " + prefabPath); continue; }
            LootDrop ld = root.GetComponent<LootDrop>();
            if (ld == null) ld = root.AddComponent<LootDrop>();
            ld.lootTable = goblin;
            if (ld.lootItemPrefab == null) ld.lootItemPrefab = lootPrefab;
            ld.goldMin = 8; ld.goldMax = 20;
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            patched++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Equip] Лут пропатчен: слаймы/микоты → медь Common, гоблины → железо Common (" + patched + " префаба).");
    }

    static void AddDrop(LootTable table, string assetName, float chance)
    {
        foreach (LootEntry e in table.lootEntries)
            if (e != null && e.item != null && e.item.name == assetName) { e.dropChance = chance; return; }

        ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(OUT + TierOf(assetName) + "/" + assetName + ".asset");
        if (item == null) { Debug.LogWarning("[Equip] Нет ассета для лута: " + assetName + " (сначала пункт 1!)"); return; }
        table.lootEntries.Add(new LootEntry { item = item, dropChance = chance, minAmount = 1, maxAmount = 1 });
    }

    static string TierOf(string assetName)
    {
        if (assetName.StartsWith("Copper")) return "Copper";
        if (assetName.StartsWith("Iron")) return "Iron";
        if (assetName.StartsWith("Gold")) return "Gold";
        if (assetName.StartsWith("Platinum")) return "Platinum";
        if (assetName.StartsWith("Obsidian")) return "Obsidian";
        return "Wood";
    }
}
