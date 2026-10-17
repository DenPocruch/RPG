using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

/// <summary>
/// Расширение дома (конструктор дома, фаза 4): +1 тайл вбок/вверх.
/// Правила (зафиксированы 03–04.10):
/// - ghost: пустая клетка (нет пола; стена пуста/фон), смежная с комнатой,
///   y >= нижнего ряда пола (дверь и низ запрещены). Кап 100 клеток пола.
/// - цена HousePricing по счётчику покупок; дерево — из рюкзака+хотбара
///   (свой аккуратный учёт: штатный RemoveItem багнут — AGENTS.md),
///   голда — CurrencyManager.SpendGold. Лесоруб не трогаем.
/// - поглощение: старое кольцо в проходе расчищается, пол доливается,
///   автотайлер дотягивает стены. Счётчик покупок — в складе дома
///   (ключ $expanded: переживает сцены, менеджер подхватит в фазе 7).
/// - free=true (меню редактора): без списаний, геометрия в сцену + Ctrl+S.
/// </summary>
public static class HouseExpansion
{
    const string WoodItemName = "Wood"; // Resources/Items/Wood/Wood.asset
    const string ExpandedKey = "$expanded"; // служебный счётчик (в UI не показываем)

    // Билет расширения (ДОМ-ПАНЕЛЬ-01): покупается в магазине панели за голду
    // (лесенка HousePricing, дерево прикрутим позже), лежит на house_stock,
    // драг из склада на ghost-клетку строит +1 тайл без доп. списаний.
    public const string TicketKey = "expansion"; // билеты на руках
    const string BoughtKey = "$expansion_bought"; // билетов куплено всего (шаг лесенки)

    public static int PurchasedCount()
    {
        if (HouseStock.Instance == null) return 0;
        return HouseStock.Instance.Count(ExpandedKey);
    }

    static void AddPurchased()
    {
        if (HouseStock.Instance == null) return;
        HouseStock.Instance.Add(ExpandedKey, 1);
    }

    /// <summary>Сколько билетов расширения на руках.</summary>
    public static int TicketCount()
    {
        if (HouseStock.Instance == null) return 0;
        return HouseStock.Instance.Count(TicketKey);
    }

    /// <summary>Цена следующего билета (пока только голда; дерево позже).</summary>
    public static int NextTicketGold()
    {
        int bought = 0;
        if (HouseStock.Instance != null)
            bought = HouseStock.Instance.Count(BoughtKey);
        HousePricing.CostFor(bought, out _, out int gold);
        return gold;
    }

    /// <summary>Купить билет за голду (лесенка по числу купленных). Деньги уже проверены? Нет — проверяем сами.</summary>
    public static bool BuyTicket(out string reason)
    {
        reason = "";
        int price = NextTicketGold();
        if (CurrencyManager.Instance == null) { reason = "нет кассы"; return false; }
        if (CurrencyManager.Instance.Gold < price)
        {
            reason = "надо " + price + "g";
            Debug.Log("[House] Билет расширения: не хватает голды (" + reason + ").");
            return false;
        }
        if (!CurrencyManager.Instance.SpendGold(price)) { reason = "не списалось"; return false; }
        if (HouseStock.Instance == null) { reason = "нет склада"; return false; }
        HouseStock.Instance.Add(TicketKey, 1);
        HouseStock.Instance.Add(BoughtKey, 1);
        Debug.Log("[House] Куплен билет расширения (" + price + "g, на руках " +
                  HouseStock.Instance.Count(TicketKey) + "). Тащи его в дом.");
        return true;
    }

    /// <summary>Дерева в рюкзаке+хотбаре (по имени ассета, как ItemDatabase).</summary>
    public static int CountWood()
    {
        int n = 0;
        if (InventoryUI.Instance != null)
            foreach (InventorySlot s in InventoryUI.Instance.slots)
                if (s != null && !s.IsEmpty() && s.currentItem.name == WoodItemName)
                    n += s.quantity;
        if (HotbarManager.Instance != null)
            foreach (InventorySlot s in HotbarManager.Instance.slots)
                if (s != null && !s.IsEmpty() && s.currentItem.name == WoodItemName)
                    n += s.quantity;
        return n;
    }

    /// <summary>Списать дерево (с проверкой достаточности; хотбар тоже).</summary>
    public static bool TakeWood(int amount)
    {
        if (amount <= 0) return true;
        if (CountWood() < amount) return false;
        int rest = amount;
        foreach (InventorySlot s in AllPlayerSlots())
        {
            if (rest <= 0) break;
            if (s == null || s.IsEmpty() || s.currentItem.name != WoodItemName) continue;
            int take = Mathf.Min(rest, s.quantity);
            rest -= take;
            if (s.quantity > take)
            {
                s.quantity -= take;
                s.UpdateUI();
            }
            else
            {
                s.ClearSlot();
            }
        }
        return rest <= 0;
    }

    static IEnumerable<InventorySlot> AllPlayerSlots()
    {
        if (InventoryUI.Instance != null)
            foreach (InventorySlot s in InventoryUI.Instance.slots) yield return s;
        if (HotbarManager.Instance != null)
            foreach (InventorySlot s in HotbarManager.Instance.slots) yield return s;
    }

    /// <summary>
    /// Пол вплотную (продолжение пола): сосед по 4-направлениям — пол комнаты.
    /// Расширение идёт строго смежно (как рукой: новый пол продолжает старый),
    /// башен через пустоту больше нет.
    /// </summary>
    static bool AdjacentToFloor(HashSet<Vector3Int> floor, Vector3Int c)
    {
        return floor.Contains(c + Vector3Int.up) || floor.Contains(c + Vector3Int.down)
            || floor.Contains(c + Vector3Int.right) || floor.Contains(c + Vector3Int.left);
    }

    /// <summary>Можно ли строиться на клетку (без учёта цены)?</summary>
    public static bool CanExpandAt(Tilemap walls, Tilemap floors, HashSet<Vector3Int> floor,
        WallStyle st, int lockRowY, Vector3Int target)
    {
        if (walls == null || floors == null || st == null) return false;
        if (target.y <= lockRowY) return false; // ряд двери и ниже: новый пол ставить нельзя
        if (floors.HasTile(target)) return false; // уже пол
        TileBase w = walls.GetTile(target);
        // Дверь (косяки/порог по именам) менять нельзя — только остальное.
        if (w != null && st.lockTileNames != null && st.lockTileNames.Contains(w.name)) return false;
        // Стену (кольцо/ленту/фон) есть можно: верхний тайл снимается под пол.
        return AdjacentToFloor(floor, target);
    }

    /// <summary>
    /// Все валидные ghost-клетки вокруг комнаты (для меню/панели).
    /// Скоп bbox пола ±4: север за 2 рядами ленты + кольцом — ghost там
    /// на +4 от пола (пол 8 → лента 9,10 → кольцо 11 → ghost 12).
    /// </summary>
    public static List<Vector3Int> ValidTargets(Tilemap walls, Tilemap floors,
        HashSet<Vector3Int> floor, WallStyle st, int lockRowY)
    {
        var list = new List<Vector3Int>();
        if (walls == null || floors == null || st == null || floor == null || floor.Count == 0) return list;
        int minX = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Vector3Int c in floor)
        {
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        for (int x = minX - 4; x <= maxX + 4; x++)
            for (int y = lockRowY; y <= maxY + 4; y++)
            {
                var t = new Vector3Int(x, y, 0);
                if (CanExpandAt(walls, floors, floor, st, lockRowY, t))
                    list.Add(t);
            }
        return list;
    }

    /// <summary>Диагностика для меню: почему нет валидных клеток (сводка + примеры).</summary>
    public static string TargetsDebug(Tilemap walls, Tilemap floors,
        HashSet<Vector3Int> floor, WallStyle st, int lockRowY)
    {
        if (walls == null || floors == null || st == null) return "нет тайлмапов";
        if (floor == null || floor.Count == 0) return "пустой регион";
        int minX = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Vector3Int c in floor)
        {
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        int total = 0, hasFloor = 0, wallBusy = 0, lowY = 0, notAdj = 0, ok = 0;
        string samples = "";
        int shown = 0;
        Vector3Int[] dirs = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        for (int x = minX - 4; x <= maxX + 4; x++)
        {
            for (int y = lockRowY; y <= maxY + 4; y++)
            {
                Vector3Int t = new Vector3Int(x, y, 0);
                total++;
                string why;
                if (t.y <= lockRowY) { lowY++; why = "низ"; }
                else if (floors.HasTile(t)) { hasFloor++; why = "пол"; }
                else
                {
                    TileBase w = walls.GetTile(t);
                    if (w != null && st.lockTileNames != null && st.lockTileNames.Contains(w.name)) { wallBusy++; why = "замок:" + w.name; }
                    else if (!AdjacentToFloor(floor, t)) { notAdj++; why = "не-смежн"; }
                    else { ok++; why = "ОК"; }
                }
                if (shown < 14) { samples += t + "=" + why + " "; shown++; }
            }
        }
        return "соседей:" + total + " пол:" + hasFloor + " стена:" + wallBusy +
               " низ:" + lowY + " несмежн:" + notAdj + " ОК:" + ok + " | " + samples;
    }

    /// <summary>Доминирующий стиль пола комнаты (новичок красится им).</summary>
    public static string DominantFloorStyle(Tilemap floors, HashSet<Vector3Int> floor)
    {
        var counts = new Dictionary<string, int>();
        foreach (Vector3Int c in floor)
        {
            TileBase t = floors.GetTile(c);
            if (t == null) continue;
            string id = HouseReskin.WhichFloorStyle(t);
            counts.TryGetValue(id, out int n);
            counts[id] = n + 1;
        }
        string best = HouseReskin.BaseStyleId;
        int bestN = -1;
        foreach (var kvp in counts)
        {
            if (kvp.Value > bestN)
            {
                bestN = kvp.Value;
                best = kvp.Key;
            }
        }
        return best;
    }

    /// <summary>
    /// Построить тайл. Возвращает true при успехе. Логирует итог.
    /// Порядок: валидация → кап → цена → пол → стены → долив → финал → списание.
    /// </summary>
    public static bool TryExpand(Vector3Int target, bool free)
    {
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
        {
            Debug.LogError("[House] Нет комнаты (InteriorWalls/InteriorFloor).");
            return false;
        }
        WallStyle brick = WallStyle.Load(HouseReskin.BaseStyleId);
        if (brick == null)
        {
            Debug.LogError("[House] Нет WallStyle_Brick (пункт 5).");
            return false;
        }
        if (!CanExpandAt(walls, floors, region, brick, lockY, target))
        {
            Debug.LogWarning("[House] Сюда строиться нельзя: " + target + ".");
            return false;
        }

        // В редакторе (free) склад не трогаем вообще: иначе ленивый синглтон
        // создал бы лишний объект в открытой сцене.
        int bought = free ? 0 : PurchasedCount();
        HousePricing.CostFor(bought, out int needWood, out int needGold);

        // Симуляция: сколько клеток добавится (таргет + поглощённые без пола).
        if (!SimulateGrowth(region, walls, floors, brick, lockY, target, out var newCells))
        {
            Debug.LogWarning("[House] Кап " + HousePricing.MaxFloorTiles + ": не влезет (сейчас " +
                              region.Count + ", надо +" + newCells.Count + ").");
            return false;
        }
        if (!free)
        {
            if (CountWood() < needWood || CurrencyManager.Instance == null ||
                CurrencyManager.Instance.Gold < needGold)
            {
                Debug.LogWarning("[House] Не хватает: надо " + needWood + " дерева и " + needGold +
                                 "g (дерева в рюкзаке " + CountWood() + ").");
                return false;
            }
        }

        // 1. Пол на таргет (стиль комнаты).
        string styleId = DominantFloorStyle(floors, region);
        WallStyle fst = WallStyle.Load(styleId);
        TileBase floorTile = fst != null && fst.floorTile != null ? fst.floorTile : brick.floorTile;

        // 2–4. Стабилизация: кольцо + долив пола + углы — до сходимости.
        // Свежая клетка могла съесть стену под настенным декором — сбросить (фаза 5).
        // free (тест редактора): склад не трогаем, только снести с варнингом.
        // Честная стройка: снапшот стен до (журнал сноса) + коммит после.
        var mgr = HouseInteriorManager.Live();
        if (!free && mgr != null)
            mgr.BeginJournal(target, walls, HouseInteriorManager.JournalScopeCells(region, lockY));
        BuildGeometry(target, walls, floors, region, lockY, brick, floorTile,
            !free, out int painted, out var diffs, out var unmapped,
            out int floorNow, out int addedCount);

        if (!free)
        {
            if (!TakeWood(needWood) || !CurrencyManager.Instance.SpendGold(needGold))
            {
                Debug.LogError("[House] Ошибка списания ПОСЛЕ стройки — проверь дерево/голду!");
                return false;
            }
            AddPurchased();
            HouseInteriorManager.NoteGrown(target); // честная стройка переживает рестарт
            if (mgr != null) mgr.CommitJournal(target, walls);
        }

        Debug.Log("[House] Пристройка " + target + " (" + styleId + "): +" + addedCount +
                   " пола (теперь " + floorNow + "), записано стен " + painted +
                   ", расхождений " + diffs.Count + ", unmapped " + unmapped.Count +
                   (free ? " (бесплатно, тест)" : " (снято " + needWood + " дерева + " + needGold + "g, покупка №" + (bought + 1) + ")"));
        foreach (string d in diffs)
            Debug.LogWarning("[House] Остаток: " + d);
        foreach (string u in unmapped)
            Debug.LogWarning("[House] UNMAPPED: " + u);
        return diffs.Count == 0;
    }

    /// <summary>
    /// Снести пристроенный пол за возврат билета (лонг-пресс в планировке).
    /// Основу дома (нетрекнутые клетки) снести нельзя. Кольцо смыкается той же
    /// операцией (рантайм-аналог п.21), мебель без опоры падает на склад.
    /// </summary>
    public static bool TryDemolishTicket(Vector3Int target)
    {
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
        {
            Debug.LogError("[House] Нет комнаты (InteriorWalls/InteriorFloor).");
            return false;
        }
        WallStyle brick = WallStyle.Load(HouseReskin.BaseStyleId);
        if (brick == null)
        {
            Debug.LogError("[House] Нет WallStyle_Brick (пункт 5).");
            return false;
        }
        if (!region.Contains(target) || !floors.HasTile(target))
        {
            Debug.LogWarning("[House] Тут нет пола комнаты: " + target + ".");
            return false;
        }
        var mgr = HouseInteriorManager.Live();
        if (mgr == null || !mgr.IsGrown(target))
        {
            Debug.Log("[House] Это основа дома — снести можно только пристроенные тайлы.");
            return false;
        }
        // Откат стен по журналу (как п.21): вернуть "как было до стройки" там,
        // где нет пола. Без записи (legacy) — только Stabilize ниже.
        bool hadJournal = mgr.UndoJournal(target, walls, floors);
        floors.SetTile(target, null);
        mgr.ForgetGrown(target);
        int painted = mgr.RebuildAfterDemolish(out int unmapped, out int diffs);
        if (HouseStock.Instance != null)
            HouseStock.Instance.Add(TicketKey, 1);
        SaveManager.Instance?.Save();
        Debug.Log("[House] Пол убран: " + target + " (билет +1 на складе, журнал: " +
                  (hadJournal ? "откат" : "нет записи") + ", стен переписано " +
                  painted + ", расхождений " + diffs + ", unmapped " + unmapped + ").");
        return diffs == 0;
    }

    /// <summary>
    /// Построить тайл за билет со склада (драг из панели, ДОМ-ПАНЕЛЬ-01).
    /// Дерево/голда НЕ списываются (уплачены при покупке билета).
    /// </summary>
    public static bool TryExpandTicket(Vector3Int target)
    {
        if (!HouseReskin.FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY))
        {
            Debug.LogError("[House] Нет комнаты (InteriorWalls/InteriorFloor).");
            return false;
        }
        WallStyle brick = WallStyle.Load(HouseReskin.BaseStyleId);
        if (brick == null)
        {
            Debug.LogError("[House] Нет WallStyle_Brick (пункт 5).");
            return false;
        }
        if (!CanExpandAt(walls, floors, region, brick, lockY, target))
        {
            Debug.LogWarning("[House] Сюда строиться нельзя: " + target + ".");
            return false;
        }
        if (!SimulateGrowth(region, walls, floors, brick, lockY, target, out var newCells))
        {
            Debug.LogWarning("[House] Кап " + HousePricing.MaxFloorTiles + ": не влезет (сейчас " +
                              region.Count + ", надо +" + newCells.Count + ").");
            return false;
        }
        if (HouseStock.Instance == null || !HouseStock.Instance.Remove(TicketKey))
        {
            Debug.LogWarning("[House] Нет билета расширения — купи в магазине панели.");
            return false;
        }

        string styleId = DominantFloorStyle(floors, region);
        WallStyle fst = WallStyle.Load(styleId);
        TileBase floorTile = fst != null && fst.floorTile != null ? fst.floorTile : brick.floorTile;

        var mgr = HouseInteriorManager.Live();
        if (mgr != null)
            mgr.BeginJournal(target, walls, HouseInteriorManager.JournalScopeCells(region, lockY));
        BuildGeometry(target, walls, floors, region, lockY, brick, floorTile,
            true, out int painted, out var diffs, out var unmapped,
            out int floorNow, out int addedCount);
        if (mgr != null) mgr.CommitJournal(target, walls);
        HouseInteriorManager.NoteGrown(target); // билетная стройка переживает рестарт

        Debug.Log("[House] Пристройка " + target + " (" + styleId + "): +" + addedCount +
                   " пола (теперь " + floorNow + "), записано стен " + painted +
                   ", расхождений " + diffs.Count + ", unmapped " + unmapped.Count +
                   " (билет расширения).");
        foreach (string d in diffs)
            Debug.LogWarning("[House] Остаток: " + d);
        foreach (string u in unmapped)
            Debug.LogWarning("[House] UNMAPPED: " + u);
        return diffs.Count == 0;
    }

    /// <summary>Симуляция роста: таргет + поглощённые без пола. False = кап превышен.</summary>
    static bool SimulateGrowth(HashSet<Vector3Int> region, Tilemap walls, Tilemap floors,
        WallStyle brick, int lockY, Vector3Int target, out HashSet<Vector3Int> newCells)
    {
        var simFloor = new HashSet<Vector3Int>(region) { target };
        var simWanted = HouseAutotiler.ComputeWanted(walls, simFloor, brick, lockY,
            new HashSet<Vector3Int> { target }, new List<string>());
        newCells = new HashSet<Vector3Int> { target };
        foreach (var kvp in simWanted)
        {
            if (kvp.Value == null && !floors.HasTile(kvp.Key))
                newCells.Add(kvp.Key);
        }
        return region.Count + newCells.Count <= HousePricing.MaxFloorTiles;
    }

    /// <summary>Геометрия пристройки: пол → стабилизация → аудит → сброс декора.</summary>
    static void BuildGeometry(Vector3Int target, Tilemap walls, Tilemap floors,
        HashSet<Vector3Int> region, int lockY, WallStyle brick, TileBase floorTile,
        bool decorToStock, out int painted, out List<string> diffs, out List<string> unmapped,
        out int floorNow, out int addedCount)
    {
        floors.SetTile(target, floorTile);
        var floorLive = new HashSet<Vector3Int>(region) { target };
        var freshLive = new HashSet<Vector3Int> { target };
        var unmappedLive = new List<string>();
        painted = HouseAutotiler.Stabilize(walls, floors, floorLive, brick, lockY,
            freshLive, floorTile, unmappedLive);
        unmapped = unmappedLive;
        addedCount = new HashSet<Vector3Int>(freshLive).Count;
        diffs = HouseAutotiler.Audit(walls,
            HouseAutotiler.ComputeWanted(walls, floorLive, brick, lockY, null, new List<string>()));
        HouseFurniture.RefreshPlaced(walls, floors, decorToStock);
        floorNow = floorLive.Count;
    }
}
