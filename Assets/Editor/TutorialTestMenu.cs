#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;

/// <summary>
/// Дебаг-меню для прогона туториала через MCP (агент сам водит игрока).
/// Работает и в Edit, и в Play. Все пункты — статичные MenuItem,
/// агент вызывает их через mcp-unity execute_menu_item.
/// </summary>
public static class TutorialTestMenu
{
    const string ROOT = "RPG Test/Туториал/";

    static GameObject Player() => GameObject.FindWithTag("Player");

    static void TeleportNear(Component target, float dx = 1f)
    {
        var p = Player();
        if (p == null) { Debug.LogWarning("[RPG Test] Нет игрока на сцене"); return; }
        if (target == null) { Debug.LogWarning("[RPG Test] Цель не найдена"); return; }
        p.transform.position = target.transform.position + new Vector3(dx, 0f, 0f);
        Debug.Log("[RPG Test] Игрок у: " + target.name);
    }

    static void GivePack(string assetName, int count = 1)
    {
        ItemData item = ItemDatabase.Find(assetName);
        if (item == null) { Debug.LogWarning("[RPG Test] Нет предмета: " + assetName); return; }
        QuestGive.GiveToPack(item, count);
    }

    static void GiveHotbar(string assetName, int count = 1, bool select = false)
    {
        ItemData item = ItemDatabase.Find(assetName);
        if (item == null) { Debug.LogWarning("[RPG Test] Нет предмета: " + assetName); return; }
        QuestGive.Give(item, count, select);
    }

    // ── Состояние ──
    [MenuItem(ROOT + "00 Текущий шаг в консоль")]
    static void LogStep()
    {
        Debug.Log("[RPG Test] Шаг: " + TutorialManager.CurrentStep()
            + " (idx " + TutorialManager.ProgressIndex() + "), done=" + TutorialManager.Instance.IsDone);
    }

    // ── Телепорты ──
    [MenuItem(ROOT + "Телепорт/К мэру")]
    static void ToMayor() => TeleportNear(Object.FindFirstObjectByType<MayorNPC>());
    [MenuItem(ROOT + "Телепорт/К колодцу")]
    static void ToWell() => TeleportNear(Object.FindFirstObjectByType<WellInteraction>());
    [MenuItem(ROOT + "Телепорт/К грядкам (DigSpot)")]
    static void ToDig()
    {
        var spot = GameObject.Find("TutorialDigSpot");
        if (spot == null) { Debug.LogWarning("[RPG Test] Нет TutorialDigSpot"); return; }
        TeleportNear(spot.transform);
    }
    [MenuItem(ROOT + "Телепорт/К Марте")]
    static void ToMarta() => TeleportNear(Object.FindFirstObjectByType<MartaNPC>());
    [MenuItem(ROOT + "Телепорт/К Дрону")]
    static void ToBuyer() => TeleportNear(Object.FindFirstObjectByType<BuyerNPC>());
    [MenuItem(ROOT + "Телепорт/К Густаву")]
    static void ToCook() => TeleportNear(Object.FindFirstObjectByType<CookNPC>());
    [MenuItem(ROOT + "Телепорт/К Степану")]
    static void ToSmith() => TeleportNear(Object.FindFirstObjectByType<BlacksmithNPC>());

    // ── Сцены (автовходы town/forest/mine срабатывают сами) ──
    [MenuItem(ROOT + "Сцены/На ферму")]
    static void ToFarm() => SceneManager.LoadScene("SampleScene");
    [MenuItem(ROOT + "Сцены/В город")]
    static void ToCity() => SceneManager.LoadScene("City");
    [MenuItem(ROOT + "Сцены/В лес")]
    static void ToForest() => SceneManager.LoadScene("Beginner Forest");
    [MenuItem(ROOT + "Сцены/В шахту")]
    static void ToMine() => SceneManager.LoadScene("Mine");
    [MenuItem(ROOT + "Сцены/На пляж")]
    static void ToBeach() => SceneManager.LoadScene("Beach");

    // ── Выдача ──
    [MenuItem(ROOT + "Выдать/Меч в рюкзак")]
    static void GvSword() => GivePack("WoodSword_Common");
    [MenuItem(ROOT + "Выдать/Мотыга в руки")]
    static void GvHoe() => GiveHotbar("Hoe", 1, true);
    [MenuItem(ROOT + "Выдать/Набор (серп+лейка+6 семян)")]
    static void GvKit()
    {
        GiveHotbar("Sickle", 1);
        GiveHotbar("WateringCan", 1);
        GiveHotbar("Wheat Seeds", 6);
    }
    [MenuItem(ROOT + "Выдать/Серп в руки")]
    static void GvSickle() => GiveHotbar("Sickle", 1, true);
    [MenuItem(ROOT + "Выдать/Лейка в руки")]
    static void GvCan() => GiveHotbar("WateringCan", 1, true);
    [MenuItem(ROOT + "Выдать/Кирка в руки")]
    static void GvPick() => GiveHotbar("Pickaxe", 1, true);
    [MenuItem(ROOT + "Выдать/Хлеб в руки")]
    static void GvBread() => GiveHotbar("Bread", 1, true);
    [MenuItem(ROOT + "Выдать/2 пшеницы")]
    static void GvWheat() => GivePack("Wheat", 2);

    // ── Форс событий (эмуляция действий без анимаций) ──
    [MenuItem(ROOT + "Форс/hoe (вскопать)")]
    static void FHoe() => TutorialManager.Notify("hoe");
    [MenuItem(ROOT + "Форс/plant (посадить)")]
    static void FPlant() => TutorialManager.Notify("plant");
    [MenuItem(ROOT + "Форс/water (полить)")]
    static void FWater() => TutorialManager.Notify("water");
    [MenuItem(ROOT + "Форс/harvest (собрать)")]
    static void FHarvest() => TutorialManager.Notify("harvest");
    [MenuItem(ROOT + "Форс/loot (подобрать)")]
    static void FLoot() => TutorialManager.Notify("loot");
    [MenuItem(ROOT + "Форс/pickup_harvest")]
    static void FPick() => TutorialManager.Notify("pickup_harvest");
    [MenuItem(ROOT + "Форс/farmkill")]
    static void FKill() => TutorialManager.Notify("farmkill");
    [MenuItem(ROOT + "Форс/kill (лес)")]
    static void FKillF() => TutorialManager.Notify("kill");
    [MenuItem(ROOT + "Форс/mine (жила)")]
    static void FMine() => TutorialManager.Notify("mine");

    // ── Управление ──
    [MenuItem(ROOT + "Пропустить обучение")]
    static void Skip() => TutorialManager.Instance.SkipTutorial();
}
#endif
