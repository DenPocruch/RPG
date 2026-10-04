using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

/// <summary>
/// Автотайлер стен дома (конструктор дома, фаза 2).
/// Чистая логика без Editor-зависимостей: работает и в рантайме (фаза 4),
/// и из билдера (кнопки Audit/Rebuild).
/// Модель (все правила сверены с реальными клетками дома, аудит 04.10):
/// - interior = пол с ПУСТЫМ слоем стен или с лентой. Лента = 2 верхних ряда.
/// - stray = пол под фоном (остатки ручной покраски) — keep.
/// - маски считаются по ЭФФЕКТИВНОМУ полу (слой пола ЕСТЬ и слой стен НЕ фон).
/// - ряд двери и ниже (y <= lockRowY) — locked, не трогаем.
/// - одиночные маски — только если соседняя клетка открыта (стена пуста/кольцо).
/// - углы 2-dir + стена на внешней диагонали = вогнутый, иначе выпуклый.
/// - поглощённое кольцо (клетки freshFloor фазы 4) — расчистить (null).
/// - остальное в скопе — фон. Значение null в результате = стереть тайл.
/// </summary>
public static class HouseAutotiler
{
    /// <summary>Самая большая связная (4-dir) область пола на карте. Для выбора комнаты игрока.</summary>
    public static HashSet<Vector3Int> LargestFloorRegion(Tilemap floorMap)
    {
        var best = new HashSet<Vector3Int>();
        if (floorMap == null) return best;
        var seen = new HashSet<Vector3Int>();
        BoundsInt b = floorMap.cellBounds;
        foreach (Vector3Int start in b.allPositionsWithin)
        {
            if (!floorMap.HasTile(start) || seen.Contains(start)) continue;
            var region = new HashSet<Vector3Int>();
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(start);
            seen.Add(start);
            Vector3Int[] dirs = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
            while (queue.Count > 0)
            {
                Vector3Int c = queue.Dequeue();
                region.Add(c);
                foreach (Vector3Int d in dirs)
                {
                    Vector3Int n = c + d;
                    if (seen.Contains(n) || !floorMap.HasTile(n)) continue;
                    seen.Add(n);
                    queue.Enqueue(n);
                }
            }
            if (region.Count > best.Count) best = region;
        }
        return best;
    }

    static bool IsBandTile(TileBase t, WallStyle st)
    {
        if (t == null || st == null) return false;
        return t == st.bandTopL || t == st.bandTopM || t == st.bandTopR
            || t == st.bandBotL || t == st.bandBotM || t == st.bandBotR;
    }

    /// <summary>
    /// Член комнаты для масок: клетка пола ИЛИ клетка ленты в слое стен.
    /// Лента живёт без пола под ней (владелец стёр пол под стенами/лентой),
    /// поэтому только пола для масок недостаточно.
    /// </summary>
    static bool Member(HashSet<Vector3Int> floor, Tilemap wallMap, WallStyle st, Vector3Int c)
    {
        if (floor.Contains(c)) return true;
        return IsBandTile(wallMap.GetTile(c), st);
    }

    /// <summary>Сосед продолжает ленту: член комнаты с пустой стеной или лентой.</summary>
    static bool BandCont(HashSet<Vector3Int> floor, Tilemap wallMap, WallStyle st, Vector3Int c)
    {
        if (!Member(floor, wallMap, st, c)) return false;
        TileBase w = wallMap.GetTile(c);
        return w == null || IsBandTile(w, st);
    }

    /// <summary>Сосед открыт для кромки: стена пуста или кольцо (не фон).</summary>
    static bool OpenForEdge(Tilemap wallMap, WallStyle st, Vector3Int c)
    {
        TileBase w = wallMap.GetTile(c);
        return w == null || w != st.bgFill;
    }

    /// <summary>
    /// Желаемые тайлы слоя стен. freshFloor — новые клетки пола фазы 4
    /// (поглощённое кольцо вокруг них расчищается).
    /// </summary>
    public static Dictionary<Vector3Int, TileBase> ComputeWanted(
        Tilemap wallMap, HashSet<Vector3Int> floor, WallStyle st, int lockRowY,
        HashSet<Vector3Int> freshFloor, List<string> unmapped)
    {
        var wanted = new Dictionary<Vector3Int, TileBase>();
        if (floor == null || floor.Count == 0 || st == null || wallMap == null) return wanted;

        int minX = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (Vector3Int c in floor)
        {
            if (c.x < minX) minX = c.x;
            if (c.x > maxX) maxX = c.x;
            if (c.y > maxY) maxY = c.y;
        }
        // Скоп = bbox пола ±4 (лента 2 ряда + кольцо 1 + запас 1).
        // Лента ВНЕ скопа (мусор удалённых комнат) — не трогаем, только ругаемся.
        const int ScopePad = 4;
        int scopeMinX = minX - ScopePad, scopeMaxX = maxX + ScopePad;
        int scopeMinY = lockRowY + 1, scopeMaxY = maxY + ScopePad;
        foreach (Vector3Int c in wallMap.cellBounds.allPositionsWithin)
        {
            TileBase t = wallMap.GetTile(c);
            if (!IsBandTile(t, st)) continue;
            if (c.x < scopeMinX || c.x > scopeMaxX || c.y < scopeMinY || c.y > scopeMaxY)
            {
                Debug.LogWarning("[House] Лента вне комнаты (остаток удалённого?): " + c + " " + t.name);
                continue;
            }
        }

        // Проход 1: лента (по стене, идемпотентно: верх/низ по соседу-ленте).
        for (int x = scopeMinX; x <= scopeMaxX; x++)
        {
            for (int y = scopeMinY; y <= scopeMaxY; y++)
            {
                var c = new Vector3Int(x, y, 0);
                if (!IsBandTile(wallMap.GetTile(c), st)) continue;
                if (IsBandTile(wallMap.GetTile(c + Vector3Int.up), st))
                    wanted[c] = BandEnd(st, floor, wallMap, c, false); // север — верх ленты: мы низ
                else
                    wanted[c] = BandEnd(st, floor, wallMap, c, true); // иначе мы верх
            }
        }

        // Проход 1б: лента для СВЕЖИХ клеток (фаза 4): верх — свежая клетка
        // с комнатой южнее и без пола севернее; низ — ряд под свежим верхом
        // (включая поглощённое кольцо: стена поверх затирается лентой).
        // Только если лента вообще есть в комнате (иначе свежий тайл —
        // обычный пол, как сейчас: владелец ленту убрал).
        if (freshFloor != null && RoomHasBand(wallMap, st, scopeMinX, scopeMaxX, scopeMinY, scopeMaxY))
        {
            var freshTops = new HashSet<Vector3Int>();
            foreach (Vector3Int c in freshFloor)
            {
                if (!floor.Contains(c)) continue;
                TileBase w = wallMap.GetTile(c);
                if (w != null && w != st.bgFill && !IsBandTile(w, st)) continue; // занято кольцом — не лента
                if (floor.Contains(c + Vector3Int.up)) continue;
                if (!Member(floor, wallMap, st, c + Vector3Int.down)) continue; // только над комнатой
                freshTops.Add(c);
                wanted[c] = BandEnd(st, floor, wallMap, c, true);
            }
            foreach (Vector3Int c in wallMap.cellBounds.allPositionsWithin)
            {
                if (wanted.ContainsKey(c)) continue;
                if (!floor.Contains(c)) continue;
                if (freshTops.Contains(c + Vector3Int.up))
                    wanted[c] = BandEnd(st, floor, wallMap, c, false);
            }
        }

        // Проход 2: кольцо + фон + расчистка поглощённого.
        for (int x = scopeMinX; x <= scopeMaxX; x++)
        {
            for (int y = scopeMinY; y <= scopeMaxY; y++)
            {
                var c = new Vector3Int(x, y, 0);
                if (wanted.ContainsKey(c)) continue;
                bool ownFloor = floor.Contains(c);
                TileBase cur = wallMap.GetTile(c);
                if (cur != null && st.lockTileNames != null && st.lockTileNames.Contains(cur.name))
                    continue; // замок по имени: дверь, порог, маркер выхода — не трогаем
                if (ownFloor && cur == st.bgFill)
                {
                    wanted[c] = st.bgFill; // stray — keep
                    continue;
                }
                if (ownFloor && cur == null)
                    continue; // чистый interior без ленты — слой стен пуст
                // Свежая клетка с фоном (не забранная лентой 1б): снести фон.
                if (freshFloor != null && freshFloor.Contains(c) && cur == st.bgFill && !wanted.ContainsKey(c))
                {
                    wanted[c] = null;
                    continue;
                }
                bool n = Member(floor, wallMap, st, c + Vector3Int.up);
                bool s = Member(floor, wallMap, st, c + Vector3Int.down);
                bool e = Member(floor, wallMap, st, c + Vector3Int.right);
                bool w = Member(floor, wallMap, st, c + Vector3Int.left);
                if (!n && !s && !e && !w)
                {
                    TileBase diag = DiagonalCorner(c, floor, wallMap, st);
                    wanted[c] = diag != null ? diag : st.bgFill;
                    continue;
                }
                // Поглощённое кольцо: стена между полами, один из которых свежий.
                if (!ownFloor && freshFloor != null && Absorbed(n, s, e, w, c, freshFloor))
                {
                    wanted[c] = null; // расчистить (фаза 4 дольёт туда пол)
                    continue;
                }
                TileBase tile = Classify(n, s, e, w, ownFloor, c, wallMap, st, unmapped);
                if (tile != null)
                    wanted[c] = tile;
                // null = UNMAPPED: не трогаем + запись в unmapped.
            }
        }
        return wanted;
    }

    /// <summary>Есть ли лента в скопе (для гейта прохода 1б).</summary>
    static bool RoomHasBand(Tilemap wallMap, WallStyle st, int x0, int x1, int y0, int y1)
    {
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (IsBandTile(wallMap.GetTile(new Vector3Int(x, y, 0)), st))
                    return true;
        return false;
    }

    static bool Absorbed(bool n, bool s, bool e, bool w, Vector3Int c, HashSet<Vector3Int> fresh)
    {
        if (!((n && s) || (e && w))) return false;
        return fresh.Contains(c + Vector3Int.up) || fresh.Contains(c + Vector3Int.down)
            || fresh.Contains(c + Vector3Int.right) || fresh.Contains(c + Vector3Int.left);
    }

    static TileBase BandEnd(WallStyle st, HashSet<Vector3Int> floor, Tilemap wallMap, Vector3Int c, bool top)
    {
        // Концы — по соседям, продолжающим ленту (член комнаты с пустой стеной или лентой).
        bool w = BandCont(floor, wallMap, st, c + Vector3Int.left);
        bool e = BandCont(floor, wallMap, st, c + Vector3Int.right);
        if (!w && e) return top ? st.bandTopL : st.bandBotL;
        if (w && !e) return top ? st.bandTopR : st.bandBotR;
        return top ? st.bandTopM : st.bandBotM;
    }

    /// <summary>
    /// Выпуклый угол по единственной диагонали (4-маска пуста).
    /// Диагональ должна смотреть в ленту/пусто, ПЛЮС обе ортогональные клетки
    /// к ней — кольцо (иначе это войд за концом ленты, там фон).
    /// </summary>
    static TileBase DiagonalCorner(Vector3Int c, HashSet<Vector3Int> floor, Tilemap wallMap, WallStyle st)
    {
        if (DiagIn(c + new Vector3Int(1, -1, 0), floor, wallMap, st)
            && IsRing(wallMap, c + Vector3Int.right, st) && IsRing(wallMap, c + Vector3Int.down, st)
            && !DiagIn(c + new Vector3Int(-1, -1, 0), floor, wallMap, st)
            && !DiagIn(c + new Vector3Int(1, 1, 0), floor, wallMap, st)
            && !DiagIn(c + new Vector3Int(-1, 1, 0), floor, wallMap, st))
            return st.cornerTL;
        if (DiagIn(c + new Vector3Int(-1, -1, 0), floor, wallMap, st)
            && IsRing(wallMap, c + Vector3Int.left, st) && IsRing(wallMap, c + Vector3Int.down, st)
            && !DiagIn(c + new Vector3Int(1, -1, 0), floor, wallMap, st)
            && !DiagIn(c + new Vector3Int(1, 1, 0), floor, wallMap, st)
            && !DiagIn(c + new Vector3Int(-1, 1, 0), floor, wallMap, st))
            return st.cornerTR;
        if (DiagIn(c + new Vector3Int(1, 1, 0), floor, wallMap, st)
            && IsRing(wallMap, c + Vector3Int.right, st) && IsRing(wallMap, c + Vector3Int.up, st)
            && !DiagIn(c + new Vector3Int(1, -1, 0), floor, wallMap, st)
            && !DiagIn(c + new Vector3Int(-1, -1, 0), floor, wallMap, st)
            && !DiagIn(c + new Vector3Int(-1, 1, 0), floor, wallMap, st))
            return st.cornerBL;
        if (DiagIn(c + new Vector3Int(-1, 1, 0), floor, wallMap, st)
            && IsRing(wallMap, c + Vector3Int.left, st) && IsRing(wallMap, c + Vector3Int.up, st)
            && !DiagIn(c + new Vector3Int(1, -1, 0), floor, wallMap, st)
            && !DiagIn(c + new Vector3Int(-1, -1, 0), floor, wallMap, st)
            && !DiagIn(c + new Vector3Int(1, 1, 0), floor, wallMap, st))
            return st.cornerBR;
        return null;
    }

    /// <summary>Диагональ годится для угла: член комнаты с пустой стеной или лентой.</summary>
    static bool DiagIn(Vector3Int d, HashSet<Vector3Int> floor, Tilemap wallMap, WallStyle st)
    {
        if (!Member(floor, wallMap, st, d)) return false;
        TileBase w = wallMap.GetTile(d);
        return w == null || IsBandTile(w, st);
    }

    static TileBase Classify(bool n, bool s, bool e, bool w, bool ownFloor, Vector3Int c,
        Tilemap wallMap, WallStyle st, List<string> unmapped)
    {
        bool eRing = IsRing(wallMap, c + Vector3Int.right, st);
        bool wRing = IsRing(wallMap, c + Vector3Int.left, st);
        // Верхняя кромка (пол южнее). Конец определяется продолжением кромки:
        // кромка уходит на запад (стена западнее) = восточный конец = TR.
        if (s && !n && !e && !w)
        {
            if (!OpenForEdge(wallMap, st, c + Vector3Int.down)) return st.bgFill;
            if (eRing && wRing) return st.straightH;
            if (wRing) return st.cornerTR;
            if (eRing) return st.cornerTL;
            return st.straightH;
        }
        // Нижняя кромка (пол севернее). Зеркально.
        if (n && !s && !e && !w)
        {
            if (!OpenForEdge(wallMap, st, c + Vector3Int.up)) return st.bgFill;
            if (eRing && wRing) return st.straightH;
            if (wRing) return st.cornerBR;
            if (eRing) return st.cornerBL;
            return st.straightH;
        }
        // Боковые кромки: продолжение стены определяет ориентацию.
        // {E} с кольцом сверху+снизу = колонна (V); с кольцом только с одной
        // стороны = кромка уходит горизонтально (H); иначе колонна.
        if (e && !w && !n && !s)
        {
            if (!OpenForEdge(wallMap, st, c + Vector3Int.right)) return st.bgFill;
            bool nR = IsRing(wallMap, c + Vector3Int.up, st);
            bool sR = IsRing(wallMap, c + Vector3Int.down, st);
            if (nR && sR) return st.straightV;
            if (nR || sR) return st.straightH;
            return st.straightV;
        }
        if (w && !e && !n && !s)
        {
            if (!OpenForEdge(wallMap, st, c + Vector3Int.left)) return st.bgFill;
            bool nR = IsRing(wallMap, c + Vector3Int.up, st);
            bool sR = IsRing(wallMap, c + Vector3Int.down, st);
            if (nR && sR) return st.straightV;
            if (nR || sR) return st.straightH;
            return st.straightV;
        }
        // Тройные маски — стена продолжается насквозь.
        if (e && w && !n && !s) return st.straightH;
        if (n && s && !e && !w) return st.straightV;
        if (n && s && w && !e) return st.straightV; // восточная колонна (пол под ней)
        if (n && s && e && !w) return st.straightV;
        if (n && e && w && !s) return st.straightH;
        if (s && e && w && !n) return st.straightH;
        if (n && s && e && w) return ownFloor ? st.straightV : null;
        // Углы 2-dir: обе ортогональные клетки СНАРУЖИ — кольцо = вогнутый;
        // только одна (стена продолжается) = прямая; иначе выпуклый.
        // (Диагональ врёт: у вогнутого и выпуклого она одинаковая.)
        if (s && e && !n && !w)
        {
            bool nR = IsRing(wallMap, c + Vector3Int.up, st);
            bool wR = IsRing(wallMap, c + Vector3Int.left, st);
            if (nR && wR) return st.ConcaveSE;
            if (nR) return st.straightV;
            return st.cornerTL;
        }
        if (s && w && !n && !e)
        {
            bool nR = IsRing(wallMap, c + Vector3Int.up, st);
            bool eR = IsRing(wallMap, c + Vector3Int.right, st);
            if (nR && eR) return st.ConcaveSW;
            if (nR) return st.straightV;
            return st.cornerTR;
        }
        if (n && e && !s && !w)
        {
            bool sR = IsRing(wallMap, c + Vector3Int.down, st);
            bool wR = IsRing(wallMap, c + Vector3Int.left, st);
            if (sR && wR) return st.ConcaveNE;
            if (sR) return st.straightV;
            return st.cornerBL;
        }
        if (n && w && !s && !e)
        {
            bool sR = IsRing(wallMap, c + Vector3Int.down, st);
            bool eR = IsRing(wallMap, c + Vector3Int.right, st);
            if (sR && eR) return st.ConcaveNW;
            if (sR) return st.straightV;
            return st.cornerBR;
        }
        unmapped?.Add(c + " mask N" + B(n) + "S" + B(s) + "E" + B(e) + "W" + B(w));
        return null;
    }

    static string B(bool v) => v ? "1" : "0";

    /// <summary>На клетке сейчас кольцевой (не фон, не пусто) тайл? Контекст — текущая карта.</summary>
    static bool IsRing(Tilemap wallMap, Vector3Int c, WallStyle st)
    {
        TileBase t = wallMap.GetTile(c);
        return t != null && t != st.bgFill;
    }

    /// <summary>Сверка желаемого с текущим (по ссылкам, null = стереть). Строки расхождений.</summary>
    public static List<string> Audit(Tilemap wallMap, Dictionary<Vector3Int, TileBase> wanted)
    {
        var diffs = new List<string>();
        if (wallMap == null || wanted == null) return diffs;
        foreach (var kvp in wanted)
        {
            TileBase cur = wallMap.GetTile(kvp.Key);
            if (cur != kvp.Value)
            {
                string cn = cur != null ? cur.name : "<пусто>";
                string wn = kvp.Value != null ? kvp.Value.name : "<пусто>";
                diffs.Add(kvp.Key + ": сейчас " + cn + " → надо " + wn);
            }
        }
        return diffs;
    }

    /// <summary>Применить желаемое на карту (null = стереть). Число записанных клеток.</summary>
    public static int Paint(Tilemap wallMap, Dictionary<Vector3Int, TileBase> wanted)
    {
        if (wallMap == null || wanted == null) return 0;
        int n = 0;
        foreach (var kvp in wanted)
        {
            if (wallMap.GetTile(kvp.Key) != kvp.Value)
            {
                wallMap.SetTile(kvp.Key, kvp.Value);
                n++;
            }
        }
        return n;
    }

    /// <summary>
    /// Гнать compute+paint+долив пола до стабильности (макс 4 прохода).
    /// Свежие углы видят соседей только после покраски — за 1 проход не сходится.
    /// floor пополняется (долив под расчищенным), fresh растёт вместе с ним.
    /// Возвращает суммарно записанных клеток стен.
    /// </summary>
    public static int Stabilize(Tilemap wallMap, Tilemap floorMap, HashSet<Vector3Int> floor,
        WallStyle st, int lockRowY, HashSet<Vector3Int> fresh, TileBase floorTile, List<string> unmappedAll)
    {
        int total = 0;
        for (int i = 0; i < 4; i++)
        {
            var wanted = ComputeWanted(wallMap, floor, st, lockRowY, fresh, unmappedAll);
            int painted = Paint(wallMap, wanted);
            total += painted;
            int floored = 0;
            foreach (var kvp in wanted)
            {
                if (kvp.Value == null && !floorMap.HasTile(kvp.Key))
                {
                    floorMap.SetTile(kvp.Key, floorTile);
                    floor.Add(kvp.Key);
                    fresh?.Add(kvp.Key);
                    floored++;
                }
            }
            if (painted == 0 && floored == 0) break;
        }
        return total;
    }
}
