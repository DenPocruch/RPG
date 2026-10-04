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

    /// <summary>Смежная с комнатой клетка? (пол/лента/кольцо рядом).</summary>
    static bool AdjacentToRoom(HashSet<Vector3Int> floor, Tilemap walls, WallStyle st, Vector3Int c)
    {
        Vector3Int[] dirs = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        foreach (Vector3Int d in dirs)
        {
            Vector3Int n = c + d;
            if (floor.Contains(n)) return true;
            TileBase w = walls.GetTile(n);
            if (w != null && w != st.bgFill) return true;
        }
        return false;
    }

    /// <summary>Можно ли строиться на клетку (без учёта цены)?</summary>
    public static bool CanExpandAt(Tilemap walls, Tilemap floors, HashSet<Vector3Int> floor,
        WallStyle st, int lockRowY, Vector3Int target)
    {
        if (walls == null || floors == null || st == null) return false;
        if (target.y < lockRowY) return false; // дверь и низ — нельзя
        if (floors.HasTile(target)) return false; // уже пол
        TileBase w = walls.GetTile(target);
        if (w != null && w != st.bgFill) return false; // занято стеной
        return AdjacentToRoom(floor, walls, st, target);
    }

    /// <summary>
    /// Все валидные ghost-клетки вокруг комнаты (для меню/панели).
    /// Ищем по скопу (bbox пола ±3, не ниже двери), а не только у пола:
    /// север после возврата ленты — в двух рядах (там кольцо), запад/восток —
    /// за кольцом. Иначе эти направления вообще не находятся.
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
        for (int x = minX - 3; x <= maxX + 3; x++)
            for (int y = lockRowY; y <= maxY + 3; y++)
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
        for (int x = minX - 3; x <= maxX + 3; x++)
        {
            for (int y = lockRowY; y <= maxY + 3; y++)
            {
                Vector3Int t = new Vector3Int(x, y, 0);
                total++;
                string why;
                if (t.y < lockRowY) { lowY++; why = "низ"; }
                else if (floors.HasTile(t)) { hasFloor++; why = "пол"; }
                else
                {
                    TileBase w = walls.GetTile(t);
                    if (w != null && w != st.bgFill) { wallBusy++; why = "стена:" + w.name; }
                    else if (!AdjacentToRoom(floor, walls, st, t)) { notAdj++; why = "не-смежн"; }
                    else { ok++; why = "ОК"; }
                }
                if (shown < 14) { samples += t + "=" + why + " "; shown++; }
            }
        }
        return "соседей:" + total + " пол:" + hasFloor + " стена:" + wallBusy +
               " низ:" + lowY + " несмежн:" + notAdj + " ОК:" + ok + " | " + samples;
    }

    /// <summary>Доминирующий стиль пола комнаты (новичок красится им).</summary>
    static string DominantFloorStyle(Tilemap floors, HashSet<Vector3Int> floor)
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
        var simFloor = new HashSet<Vector3Int>(region) { target };
        var simUnmapped = new List<string>();
        var simWanted = HouseAutotiler.ComputeWanted(walls, simFloor, brick, lockY,
            new HashSet<Vector3Int> { target }, simUnmapped);
        var newCells = new HashSet<Vector3Int> { target };
        foreach (var kvp in simWanted)
        {
            if (kvp.Value == null && !floors.HasTile(kvp.Key))
                newCells.Add(kvp.Key);
        }
        if (region.Count + newCells.Count > HousePricing.MaxFloorTiles)
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
        floors.SetTile(target, floorTile);

        // 2–4. Стабилизация: кольцо + долив пола + углы — до сходимости.
        var floorLive = new HashSet<Vector3Int>(region) { target };
        var freshLive = new HashSet<Vector3Int> { target };
        var unmappedLive = new List<string>();
        int painted = HouseAutotiler.Stabilize(walls, floors, floorLive, brick, lockY,
            freshLive, floorTile, unmappedLive);
        var unmapped = unmappedLive;
        var allNew = new HashSet<Vector3Int>(freshLive);
        var diffs = HouseAutotiler.Audit(walls,
            HouseAutotiler.ComputeWanted(walls, floorLive, brick, lockY, null, new List<string>()));

        if (!free)
        {
            if (!TakeWood(needWood) || !CurrencyManager.Instance.SpendGold(needGold))
            {
                Debug.LogError("[House] Ошибка списания ПОСЛЕ стройки — проверь дерево/голду!");
                return false;
            }
            AddPurchased();
        }

        Debug.Log("[House] Пристройка " + target + " (" + styleId + "): +" + allNew.Count +
                  " пола (теперь " + floorLive.Count + "), записано стен " + painted +
                  ", расхождений " + diffs.Count + ", unmapped " + unmapped.Count +
                  (free ? " (бесплатно, тест)" : " (снято " + needWood + " дерева + " + needGold + "g, покупка №" + (bought + 1) + ")"));
        foreach (string d in diffs)
            Debug.LogWarning("[House] Остаток: " + d);
        foreach (string u in unmapped)
            Debug.LogWarning("[House] UNMAPPED: " + u);
        return diffs.Count == 0;
    }
}
