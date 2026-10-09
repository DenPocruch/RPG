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
        return t == st.bandTopL || t == st.bandTopM || t == st.bandTopR || t == st.bandTopEdge
            || t == st.bandBotL || t == st.bandBotM || t == st.bandBotR || t == st.bandBotEdge;
    }

    /// <summary>Верхний ряд ленты (включая крышку).</summary>
    static bool IsTopBand(TileBase t, WallStyle st)
    {
        if (t == null || st == null) return false;
        return t == st.bandTopL || t == st.bandTopM || t == st.bandTopR || t == st.bandTopEdge;
    }

    /// <summary>Нижний ряд ленты (включая крышку).</summary>
    static bool IsBotBand(TileBase t, WallStyle st)
    {
        if (t == null || st == null) return false;
        return t == st.bandBotL || t == st.bandBotM || t == st.bandBotR || t == st.bandBotEdge;
    }

    /// <summary>
    /// Сосед продолжает ряд МОЕГО типа (верх/низ): член комнаты, стена пуста
    /// или лента того же ряда. Чужая ступенька ряд обрывает (ступеньки идут
    /// лесенкой: в одном y верх с одной стороны, низ с другой — снято с
    /// ручных выступов владельца, коммит 975b41e2).
    /// </summary>
    /// <summary>
    /// Сосед продолжает ряд МОЕГО типа: на нём лента того же ряда
    /// (верх/низ). Голый пол, пустота, кольцо и чужая ступенька ряд
    /// обрывают — устья коридоров получают концы (пристройка идёт встык:
    /// +1 пол продолжает пол, ряды рвутся у устья).
    /// </summary>
    static bool BandContSame(HashSet<Vector3Int> floor, Tilemap wallMap, WallStyle st, Vector3Int c, bool top)
    {
        if (!Member(floor, wallMap, st, c)) return false;
        TileBase t = wallMap.GetTile(c);
        if (t == null || t == st.bgFill) return false;
        if (!IsBandTile(t, st)) return false;
        return top ? IsTopBand(t, st) : IsBotBand(t, st);
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
        // Скоп = bbox пола ±4, вниз до ряда двери ВКЛЮЧИТЕЛЬНО (кольцо ряда
        // двери и вокруг двери дорисовывается; сама дверь под замком по именам,
        // ниже двери не идём. Таргеты ниже lockRowY запрещены (CanExpandAt).
        const int ScopePad = 4;
        int scopeMinX = minX - ScopePad, scopeMaxX = maxX + ScopePad;
        int scopeMinY = lockRowY - 1, scopeMaxY = maxY + ScopePad;
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
                Vector3Int sdn = c + Vector3Int.down;
                if (freshFloor != null && floor.Contains(sdn) && freshFloor.Contains(sdn)) continue; // флип верх→низ в 1б, не трогаем здесь
                if (freshFloor != null && freshFloor.Contains(c) && floor.Contains(c)) continue; // свежая: верхний тайл снимается (съедение), не лента
                if (IsBandTile(wallMap.GetTile(c + Vector3Int.up), st))
                    wanted[c] = BandEnd(st, floor, wallMap, c, false); // север — верх ленты: мы низ
                else
                    wanted[c] = BandEnd(st, floor, wallMap, c, true); // иначе мы верх
            }
        }

        // Проход 1б: лента ВПЕРЕДИ свежих клеток (фаза 4, только север).
        // Свежий пол чистый (как в эталоне: пол продолжает пол, верхний тайл
        // снят правилом выше). Дальние ряды сеются на клетки без пола впереди
        // выступа; бывшая кольцевая клетка прямо над свежей стопкой
        // (юг — лента, юг-юг — свежий пол) становится верхом ленты.
        // Только если лента вообще есть в комнате.
        if (freshFloor != null && RoomHasBand(wallMap, st, scopeMinX, scopeMaxX, scopeMinY, scopeMaxY))
        {
            int roomMaxY = int.MinValue;
            foreach (Vector3Int c in floor)
            {
                if (freshFloor.Contains(c)) continue;
                if (c.y > roomMaxY) roomMaxY = c.y;
            }
            foreach (Vector3Int t in freshFloor)
            {
                if (!floor.Contains(t) || t.y <= roomMaxY) continue; // только выступ наверх
                if (floor.Contains(t + Vector3Int.up)) continue; // над свежей есть пол — не верх
                SeedFarBand(wallMap, st, floor, wanted, t + Vector3Int.up, false, scopeMinX, scopeMaxX, scopeMinY, scopeMaxY);
                SeedFarBand(wallMap, st, floor, wanted, t + Vector3Int.up * 2, true, scopeMinX, scopeMaxX, scopeMinY, scopeMaxY);
            }
            // Боковые крышки: над боковым выступом (свежий пол НЕ выше комнаты)
            // две клетки вверх — крышки низа/верха (эталон: 216/196 над полом).
            // Ленту и замки не трогаем (повороты — ручная работа).
            foreach (Vector3Int t in freshFloor)
            {
                if (!floor.Contains(t) || t.y > roomMaxY) continue;
                for (int k = 1; k <= 2; k++)
                {
                    Vector3Int c = t + Vector3Int.up * k;
                    if (c.x < scopeMinX || c.x > scopeMaxX || c.y < scopeMinY || c.y > scopeMaxY) continue;
                    if (wanted.ContainsKey(c)) continue;
                    if (floor.Contains(c)) continue;
                    TileBase cur = wallMap.GetTile(c);
                    if (cur != null && IsBandTile(cur, st)) continue;
                    if (cur != null && st.lockTileNames != null && st.lockTileNames.Contains(cur.name)) continue;
                    TileBase edge = k == 1
                        ? (st.bandBotEdge != null ? st.bandBotEdge : st.bandBotM)
                        : (st.bandTopEdge != null ? st.bandTopEdge : st.bandTopM);
                    wanted[c] = edge;
                }
            }
            // Конверсия: клетка без пола, под ней лента, под лентой свежий пол
            // (бывшее кольцо над новой стопкой) — верх ленты, конец по контексту
            // (одиночка на свежаке — крышка, как у владельца).
            for (int x = scopeMinX; x <= scopeMaxX; x++)
            {
                for (int y = scopeMinY; y <= scopeMaxY; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (wanted.ContainsKey(c)) continue;
                    if (floor.Contains(c)) continue;
                    TileBase cur = wallMap.GetTile(c);
                    if (cur != null && st.lockTileNames != null && st.lockTileNames.Contains(cur.name)) continue;
                    Vector3Int s1 = c + Vector3Int.down;
                    // Флип верх→низ: лента прямо над свежим полом становится
                    // низом у устья ((-85..-83,10) → 199/216/197). Только где
                    // лента уже есть — боковые выступы лишнего не хватают.
                    if (cur != null && IsBandTile(cur, st))
                    {
                        if (floor.Contains(s1) && freshFloor != null && freshFloor.Contains(s1))
                        {
                            bool bw = BandContSame(floor, wallMap, st, c + Vector3Int.left, false);
                            bool be = BandContSame(floor, wallMap, st, c + Vector3Int.right, false);
                            if (!bw && !be)
                                wanted[c] = st.bandBotEdge != null ? st.bandBotEdge : st.bandBotM;
                            else
                                wanted[c] = BandEnd(st, floor, wallMap, c, false);
                        }
                        else
                        {
                            // Флип-бэк: низ над низом без пола (остаток сноса) —
                            // обратно верх. Свежие низы и ступени (юг — голый
                            // пол) сюда не попадают.
                            TileBase ts1 = wallMap.GetTile(s1);
                            if (IsBotBand(cur, st) && IsBotBand(ts1, st) && !floor.Contains(s1))
                            {
                                bool fw2 = BandContSame(floor, wallMap, st, c + Vector3Int.left, true);
                                bool fe2 = BandContSame(floor, wallMap, st, c + Vector3Int.right, true);
                                if (!fw2 && !fe2)
                                    wanted[c] = st.bandTopM;
                                else
                                    wanted[c] = BandEnd(st, floor, wallMap, c, true);
                            }
                        }
                        continue; // остальную ленту ведёт проход 1
                    }
                    Vector3Int s2 = c + Vector3Int.down * 2;
                    if (!IsBandTile(wallMap.GetTile(s1), st)) continue;
                    if (!floor.Contains(s2) || freshFloor == null || !freshFloor.Contains(s2)) continue;
                    bool w = BandContSame(floor, wallMap, st, c + Vector3Int.left, true);
                    bool e = BandContSame(floor, wallMap, st, c + Vector3Int.right, true);
                    if (!w && !e)
                        wanted[c] = st.bandTopEdge != null ? st.bandTopEdge : st.bandTopM;
                    else
                        wanted[c] = BandEnd(st, floor, wallMap, c, true);
                }
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
                bool isFresh = freshFloor != null && freshFloor.Contains(c);
                // Свежая клетка ВНУТРИ стены (пристройка вплотную): верхний
                // тайл (лента/кольцо/фон) снимается, идёт чистый пол — как
                // рукой (эталон владельца: пол продолжает пол).
                if (isFresh && ownFloor && cur != null)
                {
                    wanted[c] = null;
                    continue;
                }
                if (ownFloor && cur == st.bgFill && !isFresh)
                {
                    wanted[c] = st.bgFill; // stray — keep (но не свежая: у свежей фон сносится ниже)
                    continue;
                }
                if (ownFloor && cur == null)
                    continue; // чистый interior без ленты — слой стен пуст
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
                // Выбоина в ряду ленты (стёртый пол): с одной стороны голый
                // пол-устье, с другой — лента того же ряда → закрыть концом
                // (иначе 7 её не видит: со всех сторон члены = unmapped).
                if (!ownFloor && (cur == null || cur == st.bgFill))
                {
                    TileBase eb = wallMap.GetTile(c + Vector3Int.right);
                    TileBase wb = wallMap.GetTile(c + Vector3Int.left);
                    bool mouthW = floor.Contains(c + Vector3Int.left) && wallMap.GetTile(c + Vector3Int.left) == null;
                    bool mouthE = floor.Contains(c + Vector3Int.right) && wallMap.GetTile(c + Vector3Int.right) == null;
                    if (mouthW && !mouthE)
                    {
                        if (IsBotBand(eb, st)) { wanted[c] = st.bandBotL; continue; }
                        if (IsTopBand(eb, st)) { wanted[c] = st.bandTopL; continue; }
                    }
                    if (mouthE && !mouthW)
                    {
                        if (IsBotBand(wb, st)) { wanted[c] = st.bandBotR; continue; }
                        if (IsTopBand(wb, st)) { wanted[c] = st.bandTopR; continue; }
                    }
                }
                // Снос зуба: крышка/край ленты без опоры — остаток снесённого
                // выступа (дом уменьшается как при расширении, только наоборот).
                // Цепочка строго вниз по ленте без пола: голый пол/замок/край
                // скопа — живёт, пустота/фон/кольцо — сносить в обычное кольцо.
                // Свежие зубья (над свежим полом) и ступени (внизу голый
                // пол) не трогаем: их цепочки доходят до пола.
                if (!ownFloor && (cur == st.bandTopEdge || cur == st.bandBotEdge))
                {
                    if (IsDoomedTooth(wallMap, floor, st, c, scopeMinX, scopeMaxX, scopeMinY, scopeMaxY))
                    {
                        TileBase back = Classify(n, s, e, w, false, c, wallMap, st, unmapped);
                        if (back != null) { wanted[c] = back; continue; }
                        // null — падаем ниже на общий путь (unmapped запишется раз)
                    }
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

    /// <summary>
    /// Посеять дальний ряд ленты (1б): клетка без пола впереди выступа.
    /// Сеем только в пустоту/фон (занятое кольцо, замки, пол не трогаем);
    /// одиночка на свежаке — сразу крышка (как у владельца).
    /// </summary>
    static void SeedFarBand(Tilemap wallMap, WallStyle st, HashSet<Vector3Int> floor,
        Dictionary<Vector3Int, TileBase> wanted,
        Vector3Int c, bool top, int x0, int x1, int y0, int y1)
    {
        if (c.x < x0 || c.x > x1 || c.y < y0 || c.y > y1) return;
        if (wanted.ContainsKey(c)) return;
        if (floor.Contains(c)) return; // на пол не сеем
        TileBase cur = wallMap.GetTile(c);
        if (cur != null && cur != st.bgFill) return; // занято (кольцо/лента/замок) — не сеем
        TileBase edge = top ? st.bandTopEdge : st.bandBotEdge;
        wanted[c] = edge != null ? edge : (top ? st.bandTopM : st.bandBotM);
    }

    static bool Absorbed(bool n, bool s, bool e, bool w, Vector3Int c, HashSet<Vector3Int> fresh)
    {
        if (!((n && s) || (e && w))) return false;
        return fresh.Contains(c + Vector3Int.up) || fresh.Contains(c + Vector3Int.down)
            || fresh.Contains(c + Vector3Int.right) || fresh.Contains(c + Vector3Int.left);
    }

    static TileBase BandEnd(WallStyle st, HashSet<Vector3Int> floor, Tilemap wallMap, Vector3Int c, bool top)
    {
        // Ряд: [L][M...][R] в пределах своего типа (ступеньки лесенкой).
        // Одиночка: нарисованную художником не трогаем (крышки 196/216 живут
        // на ступеньках как одиночки), свежей даём середину.
        bool w = BandContSame(floor, wallMap, st, c + Vector3Int.left, top);
        bool e = BandContSame(floor, wallMap, st, c + Vector3Int.right, top);
        TileBase L = top ? st.bandTopL : st.bandBotL;
        TileBase M = top ? st.bandTopM : st.bandBotM;
        TileBase R = top ? st.bandTopR : st.bandBotR;
        if (!w && !e)
        {
            TileBase cur = wallMap.GetTile(c);
            return IsBandTile(cur, st) ? cur : M;
        }
        if (!w) return L; // западный край
        if (!e) return R; // восточный край
        return M;
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
        // Одиночные боковые маски: интерьер сбоку = вертикальная грань
        // (колонна/стык/торец выступа). Горизонталь здесь давала кривые углы
        // (паттерн (-121,9): колонна с веткой пола — строго V).
        if (e && !w && !n && !s)
            return OpenForEdge(wallMap, st, c + Vector3Int.right) ? st.straightV : st.bgFill;
        if (w && !e && !n && !s)
            return OpenForEdge(wallMap, st, c + Vector3Int.left) ? st.straightV : st.bgFill;
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

    /// <summary>
    /// Зуб без опоры: крышка/край ленты, под которой строго вниз по ленте
    /// без пола нет голого пола (остаток снесённого выступа). Цепочка:
    /// лента без пола — дальше вниз; голый пол, замок или край скопа —
    /// живёт; пустота/фон/кольцо — сносить.
    /// </summary>
    static bool IsDoomedTooth(Tilemap wallMap, HashSet<Vector3Int> floor, WallStyle st,
        Vector3Int c, int x0, int x1, int y0, int y1)
    {
        Vector3Int p = c + Vector3Int.down;
        while (true)
        {
            if (p.x < x0 || p.x > x1 || p.y < y0 || p.y > y1) return false;
            TileBase t = wallMap.GetTile(p);
            bool fl = floor.Contains(p);
            if (fl && !IsBandTile(t, st)) return false; // голый пол — живёт
            if (fl && IsBandTile(t, st)) return false; // устье/ступень с полом — живёт
            if (!fl && IsBandTile(t, st)) { p += Vector3Int.down; continue; }
            if (t != null && st.lockTileNames != null && st.lockTileNames.Contains(t.name)) return false;
            return true; // пустота/фон/кольцо — опоры нет
        }
    }

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
