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
    static bool BandContSame(HashSet<Vector3Int> floor, Tilemap wallMap, WallStyle st, Vector3Int c, bool top, HashSet<Vector3Int> freshFloor = null)
    {
        if (freshFloor != null && freshFloor.Contains(c)) return false; // свежий пол: стена под ним съедается, не продолжает ленту
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

    /// <summary>Край ленты (крышка 196/216 кирпич, 404/456 сарай).</summary>
    static bool IsBandEdge(TileBase t, WallStyle st)
    {
        if (t == null || st == null) return false;
        return t == st.bandTopEdge || t == st.bandBotEdge;
    }

    static string ShortTile(TileBase t)
    {
        if (t == null) return ".";
        string n = t.name;
        if (n.StartsWith("Tileset House_")) n = n.Substring("Tileset House_".Length);
        return n;
    }

    /// <summary>
    /// Роль тайла в контуре (для логов п.20: видно каким правилом код его
    /// поставил — лента/вилка/прямая/угол/поворот). westTurnB(37) совпадает
    /// с forkRight — считается вилкой; вогнутые совпадают с углами.
    /// </summary>
    public static string RoleOf(WallStyle st, TileBase t)
    {
        if (t == null) return ".";
        if (st == null) return "?";
        if (t == st.bgFill) return "bg";
        if (t == st.bandTopL || t == st.bandTopM || t == st.bandTopR || t == st.bandTopEdge) return "bandT";
        if (t == st.bandBotL || t == st.bandBotM || t == st.bandBotR || t == st.bandBotEdge) return "bandB";
        if (t == st.forkTop || t == st.forkBottom || t == st.forkLeft || t == st.forkRight || t == st.forkCenter) return "fork";
        if (t == st.straightH || t == st.straightV) return "line";
        if (t == st.cornerTL || t == st.cornerTR || t == st.cornerBL || t == st.cornerBR) return "corner";
        if (t == st.westTurnA || t == st.columnMid0) return "turnW";
        if (t == st.floorTile) return "floor";
        if (t == st.doorJambL || t == st.doorJambR || t == st.doorThreshold) return "door";
        return "?";
    }

    /// <summary>
    /// Отладка маски клетки для логов п.20: соседи-члены N/S/E/W (пол ИЛИ
    /// лента), свой пол, текущий тайл. Формат: (-88,9) N0S1E1W1 F0=2.
    /// </summary>
    public static string DebugMask(Tilemap wallMap, HashSet<Vector3Int> floor, WallStyle st, Vector3Int c)
    {
        bool n = Member(floor, wallMap, st, c + Vector3Int.up);
        bool s = Member(floor, wallMap, st, c + Vector3Int.down);
        bool e = Member(floor, wallMap, st, c + Vector3Int.right);
        bool w = Member(floor, wallMap, st, c + Vector3Int.left);
        bool f = floor.Contains(c);
        return c + " N" + (n ? "1" : "0") + "S" + (s ? "1" : "0") +
               "E" + (e ? "1" : "0") + "W" + (w ? "1" : "0") +
               " F" + (f ? "1" : "0") + "=" + ShortTile(wallMap.GetTile(c));
    }

    /// <summary>
    /// Желаемые тайлы слоя стен. freshFloor — новые клетки пола фазы 4
    /// (поглощённое кольцо вокруг них расчищается).
    /// </summary>
    public static Dictionary<Vector3Int, TileBase> ComputeWanted(
        Tilemap wallMap, HashSet<Vector3Int> floor, WallStyle st, int lockRowY,
        HashSet<Vector3Int> freshFloor, List<string> unmapped, HashSet<Vector3Int> virginBg = null)
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
                // Эталоны сбоку живут вне скопа специально — тихо пропускаем.
                continue;
            }
        }

        // Проход 1: лента (по стене, идемпотентно: верх/низ по соседу-ленте).
        // Флип над свежим полом оставлен в 1б (строка 180 уже пропускает такие
        // клетки здесь); отдельный пре-флип бесполезен — соседи читают карту,
        // а не wanted, и видят дофлиповые значения до покраски.
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
                    wanted[c] = BandEnd(st, floor, wallMap, c, false, freshFloor); // север — верх ленты: мы низ
                else
                    wanted[c] = BandEnd(st, floor, wallMap, c, true, freshFloor); // иначе мы верх
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
            }
            // Верхние ряды отдельным проходом: низ (устье) всегда раньше верха,
            // иначе дальняя крышка первой захватит клетку ближней
            // ((-89,8): k=2 дал 196 вместо 216' — порядок HashSet).
            foreach (Vector3Int t in freshFloor)
            {
                if (!floor.Contains(t) || t.y <= roomMaxY) continue;
                if (floor.Contains(t + Vector3Int.up)) continue;
                SeedFarBand(wallMap, st, floor, wanted, t + Vector3Int.up * 2, true, scopeMinX, scopeMaxX, scopeMinY, scopeMaxY);
            }
            // Боковые крышки: над боковым выступом (свежий пол НЕ выше комнаты)
            // две клетки вверх — крышки низа/верха (эталон: 216/196 над полом).
            // Ленту и замки не трогаем (повороты — ручная работа).
            // k=1 (устье) раньше k=2 (верх) — ближняя крышка бьёт дальнюю.
            for (int k = 1; k <= 2; k++)
            {
                foreach (Vector3Int t in freshFloor)
                {
                    if (!floor.Contains(t) || t.y > roomMaxY) continue;
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
                            bool bw = BandContSame(floor, wallMap, st, c + Vector3Int.left, false, freshFloor);
                            bool be = BandContSame(floor, wallMap, st, c + Vector3Int.right, false, freshFloor);
                            if (!bw && !be)
                                wanted[c] = st.bandBotEdge != null ? st.bandBotEdge : st.bandBotM;
                            else
                                wanted[c] = BandEnd(st, floor, wallMap, c, false, freshFloor);
                        }
                        else
                        {
                            // Флип-бэк: низ над низом без пола (остаток сноса) —
                            // обратно верх. Свежие низы и ступени (юг — голый
                            // пол) сюда не попадают.
                            TileBase ts1 = wallMap.GetTile(s1);
                            if (IsBotBand(cur, st) && IsBotBand(ts1, st) && !floor.Contains(s1))
                            {
                                bool fw2 = BandContSame(floor, wallMap, st, c + Vector3Int.left, true, freshFloor);
                                bool fe2 = BandContSame(floor, wallMap, st, c + Vector3Int.right, true, freshFloor);
                            if (!fw2 && !fe2)
                                wanted[c] = st.bandTopM;
                            else
                                wanted[c] = BandEnd(st, floor, wallMap, c, true, freshFloor);
                            }
                        }
                        continue; // остальную ленту ведёт проход 1
                    }
                    Vector3Int s2 = c + Vector3Int.down * 2;
                    if (!IsBandTile(wallMap.GetTile(s1), st)) continue;
                    if (floor.Contains(s1)) continue; // над устьем с полом — зона поворотов, не конверсия
                    if (!floor.Contains(s2) || freshFloor == null || !freshFloor.Contains(s2)) continue;
                    bool w = BandContSame(floor, wallMap, st, c + Vector3Int.left, true, freshFloor);
                    bool e = BandContSame(floor, wallMap, st, c + Vector3Int.right, true, freshFloor);
                    if (!w && !e)
                        wanted[c] = st.bandTopEdge != null ? st.bandTopEdge : st.bandTopM;
                    else
                        wanted[c] = BandEnd(st, floor, wallMap, c, true, freshFloor);
                }
            }
        }

        // Проход 1в: L-колонка разделителя (эталон владельца, как пункт 23):
        // L-уголок из 3 полов + вторая масса рядом = колонка 34/67/196/216/
        // пол/0/34. Только большие комнаты (эскизы не трогаем).
        if (floor.Count > 12)
        {
            BuildBridgeColumns(wallMap, floor, st, wanted, scopeMinX, scopeMaxX, scopeMinY, scopeMaxY);
        }

        // Проход 2: кольцо + фон + расчистка поглощённого.
        for (int x = scopeMinX; x <= scopeMaxX; x++)
        {
            for (int y = scopeMinY; y <= scopeMaxY; y++)
            {
                var c = new Vector3Int(x, y, 0);
                if (wanted.ContainsKey(c)) continue;
                // Ниже ряда двери: дорисовка пустоты; сложившееся кольцо базы
                // (не-фон ДО стройки) не перезаписываем. Свежепокрашенное из фона
                // (virginBg) доводим до стабильности, а не морозим транзиент.
                // Исключение: ряд двери тоже зеркалит Т-стыки нового контекста
                // ((-88,4) → 69 как зеркало (-88,11) → 2); без нового Т — мороз.
                if (c.y < lockRowY)
                {
                    TileBase cur0 = wallMap.GetTile(c);
                    if (cur0 != null && cur0 != st.bgFill
                        && (virginBg == null || !virginBg.Contains(c)))
                    {
                        if (c.y == lockRowY - 1)
                        {
                            TileBase gf0 = GeometricFork(wallMap, st, c);
                            if (gf0 != null) { wanted[c] = gf0; continue; }
                        }
                        continue;
                    }
                }
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
                    if (diag != null) { wanted[c] = diag; continue; }
                    // Голый контур без членов: Т-стык кольцом (вилка по открытию).
                    TileBase gf = GeometricFork(wallMap, st, c);
                    wanted[c] = gf != null ? gf : st.bgFill;
                    continue;
                }
                // Поглощённое кольцо: стена между полами, один из которых свежий.
                if (!ownFloor && freshFloor != null && Absorbed(n, s, e, w, c, floor, freshFloor))
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
                        TileBase back = Classify(n, s, e, w, false, c, wallMap, st, unmapped, freshFloor);
                        if (back != null) { wanted[c] = back; continue; }
                        // null — падаем ниже на общий путь (unmapped запишется раз)
                    }
                }
                TileBase tile = Classify(n, s, e, w, ownFloor, c, wallMap, st, unmapped, freshFloor);
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

    /// <summary>
    /// Поглощённое кольцо: стена между полами, один из которых свежий.
    /// Но если противоположные полы уже связаны в обход (стена — разделитель
    /// между соединёнными областями, как (-88,6/7) между крылом и домом через
    /// мост y=8) — не трогаем, иначе долив съедает задуманную стену.
    /// </summary>
    static bool Absorbed(bool n, bool s, bool e, bool w, Vector3Int c,
        HashSet<Vector3Int> floor, HashSet<Vector3Int> fresh)
    {
        if (!((n && s) || (e && w))) return false;
        if (!(fresh.Contains(c + Vector3Int.up) || fresh.Contains(c + Vector3Int.down)
            || fresh.Contains(c + Vector3Int.right) || fresh.Contains(c + Vector3Int.left)))
            return false;
        if (n && s && ConnectedExcept(floor, c + Vector3Int.up, c + Vector3Int.down, c, 400))
            return false;
        if (e && w && ConnectedExcept(floor, c + Vector3Int.right, c + Vector3Int.left, c, 400))
            return false;
        return true;
    }

    /// <summary>Соединены ли клетки полом в обход запрещённой ( BFS по полу).</summary>
    static bool ConnectedExcept(HashSet<Vector3Int> floor, Vector3Int a, Vector3Int b,
        Vector3Int avoid, int limit)
    {
        if (a.Equals(b)) return true;
        if (a.Equals(avoid) || b.Equals(avoid)) return false;
        var seen = new HashSet<Vector3Int> { a };
        var q = new Queue<Vector3Int>();
        q.Enqueue(a);
        Vector3Int[] dirs = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        while (q.Count > 0 && seen.Count < limit)
        {
            Vector3Int cur = q.Dequeue();
            foreach (Vector3Int d in dirs)
            {
                Vector3Int nxt = cur + d;
                if (nxt.Equals(b)) return true;
                if (nxt.Equals(avoid) || seen.Contains(nxt) || !floor.Contains(nxt)) continue;
                seen.Add(nxt);
                q.Enqueue(nxt);
            }
        }
        return false;
    }

    /// <summary>Свежий проём: клетка свежего пола без стены (пробитый проход).</summary>
    static bool IsFreshGap(HashSet<Vector3Int> floor, Tilemap wallMap,
        HashSet<Vector3Int> freshFloor, Vector3Int c)
    {
        if (freshFloor == null || !freshFloor.Contains(c)) return false;
        if (floor == null || !floor.Contains(c)) return false;
        return wallMap.GetTile(c) == null;
    }

    static TileBase BandEnd(WallStyle st, HashSet<Vector3Int> floor, Tilemap wallMap,
        Vector3Int c, bool top, HashSet<Vector3Int> freshFloor)
    {
        // Ряд: [L][M...][R] в пределах своего типа (ступеньки лесенкой).
        // Одиночка: нарисованную художником не трогаем (крышки 196/216 живут
        // на ступеньках как одиночки), свежей даём середину.
        bool w = BandContSame(floor, wallMap, st, c + Vector3Int.left, top, freshFloor);
        bool e = BandContSame(floor, wallMap, st, c + Vector3Int.right, top, freshFloor);
        TileBase L = top ? st.bandTopL : st.bandBotL;
        TileBase M = top ? st.bandTopM : st.bandBotM;
        TileBase R = top ? st.bandTopR : st.bandBotR;
        if (!w && !e)
        {
            TileBase cur = wallMap.GetTile(c);
            if (!IsBandTile(cur, st)) return M;
            TileBase edge = top ? st.bandTopEdge : st.bandBotEdge;
            // Свежий пол по бокам съедает свою стену — это конец ленты.
            bool sideFresh = freshFloor != null
                && (freshFloor.Contains(c + Vector3Int.left) || freshFloor.Contains(c + Vector3Int.right));
            if ((cur == L || cur == M || cur == R)
                && (IsFreshGap(floor, wallMap, freshFloor, c + Vector3Int.left)
                    || IsFreshGap(floor, wallMap, freshFloor, c + Vector3Int.right)
                    || sideFresh))
                return edge != null ? edge : cur;
            // Обе стороны оборвались (сосед перевернулся или съеден свежим полом) —
            // изолированная клетка ленты = крышка, а не конец старого ряда.
            if (cur == L || cur == M || cur == R)
                return edge != null ? edge : cur;
            return cur;
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
        Tilemap wallMap, WallStyle st, List<string> unmapped, HashSet<Vector3Int> freshFloor)
    {
        bool eRing = IsRing(wallMap, c + Vector3Int.right, st);
        bool wRing = IsRing(wallMap, c + Vector3Int.left, st);
        // Тупик (раньше всех: геометрический Т ниже его бы перекрыл вилкой).
        // Тройник открытием на север, под ним крышка верха (196) — колонна
        // крышек упирается и глохнет (67). Нет тупика — фолбэк на прямую.
        if (s && e && w && !n)
        {
            TileBase sT2 = wallMap.GetTile(c + Vector3Int.down);
            if (sT2 == st.bandTopEdge)
                return st.westTurnA != null ? st.westTurnA : st.straightV;
        }
        // Вертикальный косяк над тупиком: горизонталь есть, но снизу тупик 67 —
        // вертикаль важнее ((-88,10): юг 67 → 34, а не 117 и не вилка).
        if (e && w && !n && !s && wallMap.GetTile(c + Vector3Int.down) == st.westTurnA)
            return st.straightV;
        // Середина под полом (раньше тупика): тройник открытием на юг, сверху
        // пол моста (член без стены), а снизу обычная стена — колонка уходит
        // под пол (0): ((-88,7) при мосте y=8 → 0). Без колонки — фолбэк.
        // (В Classify нет сета пола: пол сверху = n-член с пустой стеной.)
        if (n && e && w && !s && wallMap.GetTile(c + Vector3Int.up) == null)
        {
            TileBase sT0 = wallMap.GetTile(c + Vector3Int.down);
            if (RingU(wallMap, st, c + Vector3Int.down) && !IsBandTile(sT0, st))
                return st.columnMid0 != null ? st.columnMid0 : st.straightH;
        }
        // Заглушка вместо Т: тройник открытием на юг, а под ним стена
        // (разделитель продолжается вниз) — глохнем тупиком (67), а не вилкой.
        // Сюда попадают случаи без пола сверху (мост выше уже разобран).
        // Юг пусто/лента — вилка как была. Нет тупика — фолбэк (прямая).
        if (n && e && w && !s && RingU(wallMap, st, c + Vector3Int.down))
            return st.westTurnA != null ? st.westTurnA : st.straightH;
        // Перемычка портала (раньше геометрии!): апрон с косяком на востоке.
        // Косяк — замок-невидимка для RingU, без этого геометрия видела бы
        // здесь Rc=3 (N,S,W) и ставила вилку 37 раньше перемычки.
        // ((-83,11): E==118 → 69). Косяк только свежим проходом — базу не трогает.
        if (s && !n && !e && !w && wallMap.GetTile(c + Vector3Int.right) == st.doorJambL)
            return st.forkBottom != null ? st.forkBottom : st.straightH;
        // Геометрический Т (и для одиночных членов): см. GeometricFork.
        // Стоит раньше краёв/прямых — Т важнее апрона.
        {
            TileBase gfC = GeometricFork(wallMap, st, c);
            if (gfC != null) return gfC;
        }
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
            // Зеркало перемычки сверху: апрон непосредственно восточнее
            // правого косяка — вилка верх ((-79,11): запад 116 → 2).
            if (wallMap.GetTile(c + Vector3Int.left) == st.doorJambR)
                return st.forkTop != null ? st.forkTop : st.straightH;
            if (eRing && wRing) return st.straightH;
            if (wRing) return st.cornerBR;
            if (eRing) return st.cornerBL;
            return st.straightH;
        }
        // Одиночные боковые маски: интерьер сбоку = вертикальная грань
        // (колонна/стык/торец выступа). Горизонталь здесь давала кривые углы
        // (паттерн (-121,9): колонна с веткой пола — строго V).
        // Западный Т-переход: под клеткой тупик 67, с севера и запада стены
        // (продолжение + нуб), восток — проход (пол/ряд): контур идёт через
        // тупик (37). Без тупика снизу — прямая, как была.
        if (e && !w && !n && !s)
        {
            if (wallMap.GetTile(c + Vector3Int.down) == st.westTurnA
                && IsRing(wallMap, c + Vector3Int.up, st)
                && IsRing(wallMap, c + Vector3Int.left, st))
                return st.forkRight != null ? st.forkRight : st.straightV;
            return OpenForEdge(wallMap, st, c + Vector3Int.right) ? st.straightV : st.bgFill;
        }
        if (w && !e && !n && !s)
        {
            // Зеркало западного Т-перехода: под клеткой тупик 67, с севера
            // и востока стены, запад — проход: контур идёт через тупик (35).
            if (wallMap.GetTile(c + Vector3Int.down) == st.westTurnA
                && IsRing(wallMap, c + Vector3Int.up, st)
                && IsRing(wallMap, c + Vector3Int.right, st))
                return st.forkLeft != null ? st.forkLeft : st.straightV;
            return OpenForEdge(wallMap, st, c + Vector3Int.left) ? st.straightV : st.bgFill;
        }
        // Тройные маски — Т-стыки 3x3 (вилки). Открытие = недостающая сторона:
        // нет E → forkRight (37), нет W → forkLeft (35), нет S → forkBottom (69),
        // нет N → forkTop (2). Нет вилки в стиле (сарай) — фолбэк на прямые.
        // Вертикальный разделитель: полы E+W скрыты, а N+S — стены: прогон
        // идёт N-S ((-88,6) между крылом и домом) → 34, а не 117.
        // (Обычный e+w выше — видимые N+S, горизонталь.)
        if (e && w && !n && !s
            && IsRing(wallMap, c + Vector3Int.up, st)
            && IsRing(wallMap, c + Vector3Int.down, st))
            return st.straightV;
        if (e && w && !n && !s) return st.straightH;
        // Верхняя перемычка (зеркало нижней 69): двойка N+S, западнее правый
        // косяк — сверху комната, снизу ряд: вилка верх (2), а не прямая
        // ((-79,11): запад 116 → 2). Нет вилки — прямая как была.
        if (n && s && !e && !w && wallMap.GetTile(c + Vector3Int.left) == st.doorJambR)
            return st.forkTop != null ? st.forkTop : st.straightV;
        if (n && s && !e && !w) return st.straightV;
        // Портал в свежем проходе (раньше вилок): вертикальный пробой 1 в ширину
        // сквозь ряды — бока прохода затыкаются дверными косяками, как проём
        // двери: клетка ЗАПАДНЕЕ прохода (пропуск восток, gap-член) → 118,
        // клетка ВОСТОЧНЕЕ (пропуск запад) → 116
        // ((-82,11) → 118, (-80,11) → 116). Gap — свежий пол: в аудите
        // (fresh=null) и в базе не срабатывает. Нет косяков — прямые.
        if (n && s && e && !w && freshFloor != null && freshFloor.Contains(c + Vector3Int.right))
            return st.doorJambL != null ? st.doorJambL : st.straightV;
        if (n && s && w && !e && freshFloor != null && freshFloor.Contains(c + Vector3Int.left))
            return st.doorJambR != null ? st.doorJambR : st.straightV;
        if (n && s && w && !e) return st.forkRight != null ? st.forkRight : st.straightV;
        if (n && s && e && !w) return st.forkLeft != null ? st.forkLeft : st.straightV;
        if (n && e && w && !s) return st.forkBottom != null ? st.forkBottom : st.straightH;
        if (s && e && w && !n) return st.forkTop != null ? st.forkTop : st.straightH;
        if (n && s && e && w) return st.forkCenter != null ? st.forkCenter : (ownFloor ? st.straightV : null);
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

    /// <summary>Соединены ли две клетки пола через пол (по 4-направлениям, с лимитом).</summary>
    public static bool ConnectedThruFloor(HashSet<Vector3Int> floor, Vector3Int a, Vector3Int b, int limit)
    {
        if (a == b) return true;
        var seen = new HashSet<Vector3Int> { a };
        var q = new Queue<Vector3Int>();
        q.Enqueue(a);
        Vector3Int[] dirs = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        while (q.Count > 0 && seen.Count < limit)
        {
            Vector3Int c = q.Dequeue();
            foreach (Vector3Int d in dirs)
            {
                Vector3Int n = c + d;
                if (n == b) return true;
                if (seen.Contains(n) || !floor.Contains(n)) continue;
                seen.Add(n);
                q.Enqueue(n);
            }
        }
        return false;
    }

    /// <summary>
    /// L-колонка разделителя (как пункт 23): L-уголок + вторая масса рядом
    /// дают колонку 34/67/196/216/пол/0/34. Без второй массы (угол в пустоту)
    /// не строим — там обычное кольцо.
    /// </summary>
    static void BuildBridgeColumns(Tilemap wallMap, HashSet<Vector3Int> floor, WallStyle st,
        Dictionary<Vector3Int, TileBase> wanted, int x0, int x1, int y0, int y1)
    {
        if (st.straightV == null || st.westTurnA == null || st.bandTopEdge == null
            || st.bandBotEdge == null || st.columnMid0 == null)
            return; // стиль без колонок (сарай) — пропускаем
        TileBase[] col = { st.straightV, st.westTurnA, st.bandTopEdge, st.bandBotEdge, null, st.columnMid0, st.straightV };
        int[] dys = { 4, 3, 2, 1, 0, -1, -2 };
        Vector3Int[] card = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                Vector3Int[] sq = {
                    new Vector3Int(x, y, 0), new Vector3Int(x + 1, y, 0),
                    new Vector3Int(x, y + 1, 0), new Vector3Int(x + 1, y + 1, 0) };
                int missing = -1, have = 0;
                for (int k = 0; k < 4; k++)
                {
                    if (floor.Contains(sq[k])) have++;
                    else missing = k;
                }
                if (have != 3) continue;
                Vector3Int bend = sq[3 - missing];
                if (!floor.Contains(bend)) continue;
                var arms = new List<Vector3Int>();
                foreach (Vector3Int d in card)
                {
                    Vector3Int n = bend + d;
                    if (n == sq[missing]) continue;
                    if (floor.Contains(n)) arms.Add(d);
                }
                if (arms.Count != 2) continue;
                Vector3Int vArm = Vector3Int.zero, hArm = Vector3Int.zero;
                foreach (Vector3Int a in arms)
                {
                    if (a == Vector3Int.up || a == Vector3Int.down) vArm = a;
                    else hArm = a;
                }
                if (vArm != Vector3Int.up || hArm == Vector3Int.zero) continue; // S-варианты позже
                int sx = hArm == Vector3Int.right ? 1 : -1;
                Vector3Int anchor = bend + new Vector3Int(sx, 0, 0);
                if (!floor.Contains(anchor)) continue;
                // Вторая масса: пол за колонкой, но НЕ та же комната (иначе угол
                // своей же комнаты — не строим, там обычное кольцо).
                Vector3Int farCell = default;
                bool farMass = false;
                for (int fx = bend.x + sx * 2; fx != bend.x + sx * 4 && !farMass; fx += sx)
                {
                    for (int fy = bend.y - 1; fy <= bend.y + 1; fy++)
                    {
                        var fc = new Vector3Int(fx, fy, 0);
                        if (floor.Contains(fc)) { farMass = true; farCell = fc; break; }
                    }
                }
                if (!farMass) continue;
                if (ConnectedThruFloor(floor, bend, farCell, 400)) continue;
                bool clear = true;
                for (int i = 0; i < dys.Length; i++)
                {
                    if (i == 4) continue;
                    if (floor.Contains(new Vector3Int(bend.x + sx, bend.y + dys[i], 0))) { clear = false; break; }
                }
                if (!clear) continue;
                for (int i = 0; i < dys.Length; i++)
                {
                    if (i == 4) continue;
                    Vector3Int d = new Vector3Int(bend.x + sx, bend.y + dys[i], 0);
                    if (d.x < x0 || d.x > x1 || d.y < y0 || d.y > y1) continue;
                    if (wanted.ContainsKey(d)) continue;
                    TileBase cur = wallMap.GetTile(d);
                    if (cur != null && st.lockTileNames != null && st.lockTileNames.Contains(cur.name)) continue;
                if (wallMap.GetTile(d) != col[i])
                {
                    wanted[d] = col[i];
                }
            }
        // Прямая вертикальная стена: колонна справа от стены (для линейного роста плиткой по одной).
        // Ставит колонну в wanted, чтобы Stabilize применил и колонна не "ездила" между расширениями.
        // Паттерн снизу вверх: bandBotEdge, bandTopEdge, westTurnA, westTurnB.
        if (st.bandBotEdge == null || st.bandTopEdge == null || st.westTurnA == null || st.westTurnB == null)
            return;
        TileBase[] straightColTiles = { st.bandBotEdge, st.bandTopEdge, st.westTurnA, st.westTurnB };
        for (int cx = x0; cx <= x1 - 1; cx++)
        {
            int runStart = int.MinValue;
            for (int cy0 = y0; cy0 <= y1; cy0++)
            {
                var c = new Vector3Int(cx, cy0, 0);
                TileBase cur = wallMap.GetTile(c);
                bool isWall = cur != null && cur != st.bgFill
                    && (IsBandTile(cur, st) || cur == st.straightV || cur == st.westTurnA || cur == st.westTurnB);
                if (isWall && runStart == int.MinValue) runStart = cy0;
                if ((!isWall || cy0 == y1) && runStart != int.MinValue)
                {
                    int colX = cx + 1;
                    if (colX <= x1)
                    {
                        for (int k = 0; k < straightColTiles.Length; k++)
                        {
                            int cy = runStart + k;
                            if (cy > y1) break;
                            if (straightColTiles[k] == null) continue;
                            var colPos = new Vector3Int(colX, cy, 0);
                            if (wanted.ContainsKey(colPos))
                            {
                                var existing = wanted[colPos];
                                if (existing == st.straightV || existing == st.westTurnA || existing == st.westTurnB
                                    || existing == st.bandTopEdge || existing == st.bandBotEdge)
                                    continue; // уже колонна — не перебиваем
                            }
                            TileBase colCur = wallMap.GetTile(colPos);
                            if (colCur != null && colCur != st.bgFill) continue;
                            if (st.lockTileNames != null && st.lockTileNames.Contains(colCur.name)) continue;
                            wanted[colPos] = straightColTiles[k];
                        }
                    }
                    runStart = int.MinValue;
                }
            }
        }
    }
    }
    }

    /// <summary>
    /// L-колонка разделителя (пункт 23): L-уголок + вторая масса nearby дают колонку
    /// 34/67/196/216/пол/0/34. Аналог BuildCorridorColumnCore из HouseBuilders.cs,
    /// но без AssetDatabase — работает в рантайме.
    /// </summary>
    public static int BuildCorridorColumnCore(Tilemap walls, Tilemap floors,
        HashSet<Vector3Int> region, WallStyle brick, int lockY, out int skipped)
    {
        skipped = 0;
        if (walls == null || floors == null || region == null || brick == null) return 0;
        TileBase tileH0 = brick.columnMid0;
        if (tileH0 == null) return 0;
        // Ряд колонки сверху вниз (эталон): 34, 67, 196, 216, ПОЛ, 0, 34.
        TileBase[] col = {
            brick.straightV, brick.westTurnA, brick.bandTopEdge,
            brick.bandBotEdge, null, tileH0, brick.straightV };
        int[] dys = { 4, 3, 2, 1, 0, -1, -2 };
        for (int i = 0; i < col.Length; i++)
        {
            if (i == 4) continue;
            if (col[i] == null)
            {
                Debug.LogError("[House] Нет тайла колонки (№" + i + ") — прогони пункт 5 / проверь Tileset House_0.");
                return 0;
            }
        }
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        foreach (Vector3Int c in region)
        {
            if (c.x < minX) minX = c.x; if (c.x > maxX) maxX = c.x;
            if (c.y < minY) minY = c.y; if (c.y > maxY) maxY = c.y;
        }
        int built = 0;
        Vector3Int[] card = { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right };
        for (int x = minX - 1; x <= maxX + 1; x++)
        {
            for (int y = minY - 1; y <= maxY + 1; y++)
            {
                Vector3Int[] sq = {
                    new Vector3Int(x, y, 0), new Vector3Int(x + 1, y, 0),
                    new Vector3Int(x, y + 1, 0), new Vector3Int(x + 1, y + 1, 0) };
                int missing = -1, have = 0;
                for (int k = 0; k < 4; k++)
                {
                    if (region.Contains(sq[k])) have++;
                    else missing = k;
                }
                if (have != 3)
                {
                    Debug.Log("[House] Column: skip 2x2 at (" + x + "," + y + ") have=" + have + " (need 3 floors for L-изгиб)");
                    continue;
                }
                Vector3Int bend = sq[3 - missing];
                if (!region.Contains(bend)) { Debug.Log("[House] Column: skip bend not in region"); continue; }
                var arms = new List<Vector3Int>();
                foreach (Vector3Int d in card)
                {
                    Vector3Int n = bend + d;
                    if (n == sq[missing]) continue;
                    if (region.Contains(n)) arms.Add(d);
                }
                if (arms.Count != 2) { Debug.Log("[House] Column: skip arms=" + arms.Count + " (need 2)"); continue; }
                Vector3Int vArm = Vector3Int.zero, hArm = Vector3Int.zero;
                foreach (Vector3Int a in arms)
                {
                    if (a == Vector3Int.up || a == Vector3Int.down) vArm = a;
                    else hArm = a;
                }
                if (vArm != Vector3Int.up || hArm == Vector3Int.zero)
                {
                    Debug.Log("[House] Column: skip vArm=" + vArm + " hArm=" + hArm + " (need N-рукав + E/W-рукав)");
                    continue;
                }
                int sx = hArm == Vector3Int.right ? 1 : -1;
                if (bend.y - 2 < lockY) { skipped++; continue; }
                Vector3Int anchor = bend + new Vector3Int(sx, 0, 0);
                if (!region.Contains(anchor)) { skipped++; continue; }
                Vector3Int farCell = default;
                bool farMass = false;
                for (int fx = bend.x + sx * 2; fx != bend.x + sx * 4 && !farMass; fx += sx)
                {
                    for (int fy = bend.y - 1; fy <= bend.y + 1; fy++)
                    {
                        var fc = new Vector3Int(fx, fy, 0);
                        if (region.Contains(fc)) { farMass = true; farCell = fc; break; }
                    }
                }
                if (!farMass) { Debug.Log("[House] Column: skip no farMass at sx=" + sx); skipped++; continue; }
                if (ConnectedThruFloor(region, bend, farCell, 400)) { Debug.Log("[House] Column: skip connected through floor"); skipped++; continue; }
                bool ok = true;
                for (int i2 = 0; i2 < dys.Length; i2++)
                {
                    if (i2 == 4) continue;
                    if (floors.HasTile(new Vector3Int(bend.x + sx, bend.y + dys[i2], 0))) { ok = false; break; }
                }
                if (!ok) { Debug.Log("[House] Column: skip floor under column cells"); skipped++; continue; }
                bool same = true;
                for (int i2 = 0; i2 < dys.Length; i2++)
                {
                    if (i2 == 4) continue;
                    if (walls.GetTile(new Vector3Int(bend.x + sx, bend.y + dys[i2], 0)) != col[i2]) { same = false; break; }
                }
                if (same) { Debug.Log("[House] Column: skip already same"); skipped++; continue; }
                for (int i2 = 0; i2 < dys.Length; i2++)
                {
                    if (i2 == 4) continue;
                    Vector3Int d = new Vector3Int(bend.x + sx, bend.y + dys[i2], 0);
                    TileBase cur = walls.GetTile(d);
                    if (cur != null && brick.lockTileNames != null && brick.lockTileNames.Contains(cur.name)) continue;
                    walls.SetTile(d, col[i2]);
                    built++;
                }
            }
        }
        return built;
    }

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

    /// <summary>Кольцо без замков (дверь не считается стеной для геометрии Т).</summary>
    static bool RingU(Tilemap wallMap, WallStyle st, Vector3Int c)
    {
        TileBase t = wallMap.GetTile(c);
        if (t == null || t == st.bgFill) return false;
        return st.lockTileNames == null || !st.lockTileNames.Contains(t.name);
    }

    /// <summary>
    /// Геометрический Т-стык контура (кольцом, без опоры на члены): стены
    /// кольцом (без замков) ровно с трёх сторон — вилка по открытию
    /// ((-88,11): S34,E117,W3 → 2). Ножка Т (сторона напротив открытия)
    /// не должна быть лентой — иначе это апрон ((-89,11): ножка-юг 199).
    /// Без вилок в стиле — null (дальше как было).
    /// </summary>
    static TileBase GeometricFork(Tilemap wallMap, WallStyle st, Vector3Int c)
    {
        bool rnU = RingU(wallMap, st, c + Vector3Int.up);
        bool rsU = RingU(wallMap, st, c + Vector3Int.down);
        bool reU = RingU(wallMap, st, c + Vector3Int.right);
        bool rwU = RingU(wallMap, st, c + Vector3Int.left);
        int rcU = (rnU ? 1 : 0) + (rsU ? 1 : 0) + (reU ? 1 : 0) + (rwU ? 1 : 0);
        if (rcU != 3) return null;
        TileBase stemU = !rnU ? wallMap.GetTile(c + Vector3Int.down)
            : (!rsU ? wallMap.GetTile(c + Vector3Int.up)
            : (!reU ? wallMap.GetTile(c + Vector3Int.left) : wallMap.GetTile(c + Vector3Int.right)));
        if (IsBandTile(stemU, st)) return null;
        if (!rnU) return st.forkTop;
        if (!rsU) return st.forkBottom;
        if (!reU) return st.forkRight;
        return st.forkLeft;
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
    /// Гнать compute+paint+долив пола до стабильности (макс 6 проходов).
    /// Свежие углы видят соседей только после покраски — за 1 проход не сходится.
    /// Каскады (3 → 2 → 117) требуют до 4-5 проходов.
    /// floor пополняется (долив под расчищенным), fresh растёт вместе с ним.
    /// Возвращает суммарно записанных клеток стен.
    /// </summary>
    public static int Stabilize(Tilemap wallMap, Tilemap floorMap, HashSet<Vector3Int> floor,
        WallStyle st, int lockRowY, HashSet<Vector3Int> fresh, TileBase floorTile, List<string> unmappedAll)
    {
        // Слепок пустоты на входе: ниже ряда двери морозим только базу,
        // свежепокрашенное из фона доводим до стабильности.
        var virginBg = new HashSet<Vector3Int>();
        foreach (Vector3Int c in wallMap.cellBounds.allPositionsWithin)
        {
            TileBase t = wallMap.GetTile(c);
            if (t == null || t == st.bgFill) virginBg.Add(c);
        }
        int total = 0;
        for (int i = 0; i < 6; i++)
        {
            var wanted = ComputeWanted(wallMap, floor, st, lockRowY, fresh, unmappedAll, virginBg);
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
