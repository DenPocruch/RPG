using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

/// <summary>
/// Перекраска дома (конструктор дома, фаза 3): стены и пол по тайлам со свопом
/// склада (старый тайл возвращается, новый списывается — как в Hay Day).
/// Стили: Brick (база) и Barn (пол 65 + лента 405-407/457-459, кольцо общее).
/// Ключи склада: "wall_&lt;id&gt;" / "floor_&lt;id&gt;" в нижнем регистре.
/// useStock=false (пункты меню в редакторе) — бесплатная покраска для визуала;
/// useStock=true (фаза 7, UI) — честная экономика.
/// </summary>
public static class HouseReskin
{
    public const string BaseStyleId = "Brick";

    // TODO(фаза 3.5): реестр стилей вместо списка (когда цветов станет больше двух).
    static readonly string[] KnownStyleIds = { "Brick", "Barn" };

    public static string WallKey(string styleId) => "wall_" + styleId.ToLowerInvariant();
    public static string FloorKey(string styleId) => "floor_" + styleId.ToLowerInvariant();

    /// <summary>Найти комнату игрока в открытой сцене (как в билдере).</summary>
    public static bool FindRoom(out Tilemap walls, out Tilemap floors,
        out HashSet<Vector3Int> region, out int lockRowY)
    {
        walls = null;
        floors = null;
        region = new HashSet<Vector3Int>();
        lockRowY = int.MinValue;
        GameObject wgo = GameObject.Find("InteriorWalls");
        GameObject fgo = GameObject.Find("InteriorFloor");
        if (wgo == null || fgo == null) return false;
        walls = wgo.GetComponent<Tilemap>();
        floors = fgo.GetComponent<Tilemap>();
        if (walls == null || floors == null) return false;
        region = HouseAutotiler.LargestFloorRegion(floors);
        if (region.Count == 0) return false;
        lockRowY = int.MaxValue;
        foreach (Vector3Int c in region)
            if (c.y < lockRowY) lockRowY = c.y;
        return true;
    }

    static WallStyle StyleOf(string styleId) => WallStyle.Load(styleId);

    /// <summary>Какой стиль владеет тайлом пола (по ссылке). Не найден — база.</summary>
    public static string WhichFloorStyle(TileBase tile)
    {
        if (tile == null) return BaseStyleId;
        foreach (string id in KnownStyleIds)
        {
            WallStyle st = StyleOf(id);
            if (st != null && st.floorTile == tile) return id;
        }
        return BaseStyleId;
    }

    /// <summary>Какой стиль владеет тайлом стены (кольцо общее — совпадёт первый).</summary>
    public static string WhichWallStyle(TileBase tile)
    {
        if (tile == null) return BaseStyleId;
        foreach (string id in KnownStyleIds)
        {
            WallStyle st = StyleOf(id);
            if (st != null && st.ContainsWallTile(tile)) return id;
        }
        return BaseStyleId;
    }

    /// <summary>Тот же слот в новом стиле (угол→угол, лента→лента...). Null = нет маппинга.</summary>
    static TileBase MapWallSlot(TileBase current, WallStyle from, WallStyle to)
    {
        if (current == null || from == null || to == null) return null;
        if (current == from.cornerTL) return to.cornerTL;
        if (current == from.cornerTR) return to.cornerTR;
        if (current == from.cornerBL) return to.cornerBL;
        if (current == from.cornerBR) return to.cornerBR;
        if (current == from.straightH) return to.straightH;
        if (current == from.straightV) return to.straightV;
        if (current == from.bandTopL) return to.bandTopL;
        if (current == from.bandTopM) return to.bandTopM;
        if (current == from.bandTopR) return to.bandTopR;
        if (current == from.bandBotL) return to.bandBotL;
        if (current == from.bandBotM) return to.bandBotM;
        if (current == from.bandBotR) return to.bandBotR;
        if (current == from.ConcaveNW) return to.ConcaveNW;
        if (current == from.ConcaveNE) return to.ConcaveNE;
        if (current == from.ConcaveSW) return to.ConcaveSW;
        if (current == from.ConcaveSE) return to.ConcaveSE;
        if (current == from.doorJambL) return to.doorJambL;
        if (current == from.doorJambR) return to.doorJambR;
        if (current == from.doorThreshold) return to.doorThreshold;
        return null;
    }

    /// <summary>Перекрасить стены комнаты (кольцо общее — реально меняются лента и порог).</summary>
    public static int PaintRoomWalls(string styleId, bool useStock, int maxCells = int.MaxValue)
    {
        if (!FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY)) return 0;
        WallStyle to = StyleOf(styleId);
        if (to == null)
        {
            Debug.LogError("[House] Нет стиля стен: " + styleId);
            return 0;
        }
        int painted = 0;
        // Скоп = bbox пола ±4 (как у автотайлера): мусор удалённых комнат не красим.
        int minX = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Vector3Int c in region)
        {
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        foreach (Vector3Int c in walls.cellBounds.allPositionsWithin)
        {
            if (painted >= maxCells) break;
            if (c.y <= lockY) continue; // дверь и ниже — locked
            if (c.x < minX - 4 || c.x > maxX + 4 || c.y > maxY + 4) continue;
            TileBase cur = walls.GetTile(c);
            if (cur == null || cur == to.bgFill) continue;
            if (to.lockTileNames != null && to.lockTileNames.Contains(cur.name)) continue;
            string oldId = WhichWallStyle(cur);
            WallStyle from = StyleOf(oldId);
            TileBase next = MapWallSlot(cur, from, to);
            if (next == null || next == cur) continue; // shared ring / уже этот стиль
            if (useStock)
            {
                if (!HouseStock.Instance.Remove(WallKey(styleId))) break; // кончились — стоп
                HouseStock.Instance.Add(WallKey(oldId));
            }
            walls.SetTile(c, next);
            painted++;
        }
        Debug.Log("[House] Стены → " + styleId + ": перекрашено " + painted + (useStock ? " (со склада)" : " (бесплатно, тест)"));
        return painted;
    }

    /// <summary>Перекрасить пол комнаты.</summary>
    public static int PaintRoomFloor(string styleId, bool useStock, int maxCells = int.MaxValue)
    {
        if (!FindRoom(out Tilemap walls, out Tilemap floors, out var region, out int lockY)) return 0;
        WallStyle to = StyleOf(styleId);
        if (to == null || to.floorTile == null)
        {
            Debug.LogError("[House] Нет пола стиля: " + styleId);
            return 0;
        }
        int painted = 0;
        foreach (Vector3Int c in region)
        {
            if (painted >= maxCells) break;
            TileBase cur = floors.GetTile(c);
            if (cur == null || cur == to.floorTile) continue;
            string oldId = WhichFloorStyle(cur);
            if (useStock)
            {
                if (!HouseStock.Instance.Remove(FloorKey(styleId))) break;
                HouseStock.Instance.Add(FloorKey(oldId));
            }
            floors.SetTile(c, to.floorTile);
            painted++;
        }
        Debug.Log("[House] Пол → " + styleId + ": перекрашено " + painted + (useStock ? " (со склада)" : " (бесплатно, тест)"));
        return painted;
    }
}
