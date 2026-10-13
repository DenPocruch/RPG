using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

/// <summary>
/// Мебель дома (конструктор дома, фаза 5): валидация, постановка, уборка на склад,
/// сброс настенного декора при перестройке, спавн из сейва.
/// Правила (финал от владельца):
/// - Floor: якорь на полу КОМНАТЫ (не на полах боковых паттернов); футпринт
///   (BoxCollider2D) не лезет в стены и чужие коллайдеры; игрок/животные не мешают.
///   Без коллайдера (свечи/мелочь) — только точка привязки на полу.
/// - Both (ковры): слой декора поверх пола — якорь на полу, футпринт не смотрим.
/// - Wall: ТОЛЬКО лента-лицо (allowlist ниже), вся спрайтом целиком.
///   Кирпич: верх 197/198/199 + крышка 196, низ 217/218/219 + крышка 216.
///   Сарай: верх 405/406/407 + крышка 404, низ 457/458/459 + крышка 456.
///   Запрещены: 76, 0, 34, 67, 116, 117, 118, 1, 2, 3, 35, 36, 37, 68, 69, 70
///   (покрывается allowlist'ом: всё вне списка — отказ).
/// - Плюс: только рядом с комнатой (≤2 от пола) — контур/паттерны/пустота мимо.
/// Всё статикой по тайлмапам (паттерн HouseReskin/HouseExpansion): сцену руками
/// не трогаем, менеджер dormant — тесты идут через Tools → House.
/// Ключ склада мебели = имя префаба (совпадает с HousePlacedFurniture.prefabName).
/// </summary>
public static class HouseFurniture
{
    [System.Serializable]
    public struct SavedPiece
    {
        public string prefabName;
        public float x;
        public float y;
    }

    /// <summary>Поверхность установки префаба (таблица, фолбэк по имени).</summary>
    public static FurnitureMount MountOfPrefab(string prefabName)
    {
        FurnitureMountTable table = FurnitureMountTable.Load();
        if (table != null)
            return table.GetMount(prefabName);
        return FurnitureMountTable.DefaultForPrefabName(prefabName);
    }

    /// <summary>Найти префаб по имени среди записей каталога (рантайм-спавн из сейва).
    /// Префабы лежат в Assets/Prefab/Interior (не в Resources) — каталог и есть реестр.</summary>
    public static GameObject ResolvePrefab(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName)) return null;
        HouseCatalog catalog = HouseCatalog.Load();
        if (catalog == null || catalog.entries == null) return null;
        foreach (HouseCatalog.Entry e in catalog.entries)
        {
            if (e != null && e.prefab != null && e.prefab.name == prefabName)
                return e.prefab;
        }
        return null;
    }

    /// <summary>Есть ли твёрдый футпринт (BoxCollider2D, не триггер).</summary>
    public static bool Footprint(GameObject prefab, out Vector2 size, out Vector2 offset)
    {
        size = Vector2.zero;
        offset = Vector2.zero;
        if (prefab == null) return false;
        BoxCollider2D col = prefab.GetComponent<BoxCollider2D>();
        if (col == null || !col.enabled || col.isTrigger) return false;
        size = col.size;
        offset = col.offset;
        return size.x > 0f && size.y > 0f;
    }

    /// <summary>Размер спрайта в мировых единицах (scale 1). Нет — false.</summary>
    public static bool SpriteSize(GameObject prefab, out Vector2 size, out Vector2 center)
    {
        size = Vector2.zero;
        center = Vector2.zero;
        if (prefab == null) return false;
        SpriteRenderer sr = prefab.GetComponent<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return false;
        // Renderer world bounds = sprite.bounds, сдвинутые на позицию трансформа.
        size = sr.sprite.bounds.size;
        center = sr.sprite.bounds.center;
        return size.x > 0f && size.y > 0f;
    }

    // Лента-лицо (allowlist владельца): кирпич верх 197/198/199+196, низ
    // 217/218/219+216; сарай верх 405/406/407+404, низ 457/458/459+456.
    // Всё остальное (76, 0, 34, 67, 116, 117, 118, углы, вилки) — отказ.
    // Сравнение по ССЫЛКАМ (слоты, не номера): перекраска кирпич↔сарай сама
    // меняет тайлы внутри списка. Кэша нет (14 сравнений — дёшево), stale быть не может.
    static bool IsBandTile(TileBase t)
    {
        if (t == null) return false;
        foreach (string id in new[] { "Brick", "Barn" })
        {
            WallStyle st = WallStyle.Load(id);
            if (st == null) continue;
            if (t == st.bandTopL || t == st.bandTopM || t == st.bandTopR || t == st.bandTopEdge
             || t == st.bandBotL || t == st.bandBotM || t == st.bandBotR || t == st.bandBotEdge)
                return true;
        }
        return false;
    }

    public static void InvalidateHangCache()
    {
        // Оставлено для совместимости вызовов из билдеров (п.5/8): кэша больше нет.
    }

    /// <summary>Клетка рядом с комнатой (≤2 от пола): контур/паттерны/пустота — нет.</summary>
    static bool NearRoom(HashSet<Vector3Int> region, Vector3Int c)
    {
        foreach (Vector3Int f in region)
        {
            if (Mathf.Abs(f.x - c.x) <= 2 && Mathf.Abs(f.y - c.y) <= 2) return true;
        }
        return false;
    }

    static void CellsOfRect(Tilemap map, Vector2 center, Vector2 size, List<Vector3Int> outCells)
    {
        outCells.Clear();
        Vector3 min = new Vector3(center.x - size.x * 0.5f, center.y - size.y * 0.5f, 0f);
        Vector3 max = new Vector3(center.x + size.x * 0.5f, center.y + size.y * 0.5f, 0f);
        Vector3Int c0 = map.WorldToCell(min);
        Vector3Int c1 = map.WorldToCell(max);
        for (int x = c0.x; x <= c1.x; x++)
            for (int y = c0.y; y <= c1.y; y++)
                outCells.Add(new Vector3Int(x, y, 0));
    }

    static readonly List<Vector3Int> cellScratch = new List<Vector3Int>();

    /// <summary>
    /// Можно ли поставить префаб в точку (mount — явно; Both идёт как декор пола).
    /// region — пол комнаты (LargestFloorRegion): привязка только к комнате,
    /// внешний контур/полы паттернов не катят (иначе декор висит не в комнате).
    /// ignore — несомый предмет (драг): себя не считаем препятствием.
    /// </summary>
    public static bool CanPlaceAt(Tilemap walls, Tilemap floors, HashSet<Vector3Int> region,
        GameObject prefab, Vector3 worldPos, FurnitureMount mount, out string reason,
        GameObject ignore = null)
    {
        reason = "";
        if (walls == null || floors == null) { reason = "нет тайлмапов"; return false; }
        if (prefab == null) { reason = "нет префаба"; return false; }
        if (region == null || region.Count == 0) { reason = "нет комнаты"; return false; }

        if (mount == FurnitureMount.Wall)
        {
            if (!SpriteSize(prefab, out Vector2 size, out Vector2 center))
            { reason = "нет спрайта"; return false; }
            // Строго лента-лицо целиком + рядом с комнатой (allowlist владельца).
            CellsOfRect(walls, (Vector2)worldPos + center, size, cellScratch);
            if (cellScratch.Count == 0) { reason = "вне карты"; return false; }
            foreach (Vector3Int c in cellScratch)
            {
                if (!IsBandTile(walls.GetTile(c))) { reason = "только на стену комнаты"; return false; }
                if (!NearRoom(region, c)) { reason = "только в комнате"; return false; }
            }
            return true;
        }

        // Floor и Both: якорь строго на полу КОМНАТЫ (не на полах боковых паттернов).
        Vector3Int anchorF = floors.WorldToCell(worldPos);
        if (!region.Contains(anchorF) || !floors.HasTile(anchorF)) { reason = "только на пол"; return false; }

        if (mount == FurnitureMount.Both) return true; // ковёр: декор поверх, футпринт не смотрим

        // Floor с коллайдером: футпринт не в стены + не в чужие коллайдеры.
        if (Footprint(prefab, out Vector2 fsize, out Vector2 foffset))
        {
            Vector2 fc = (Vector2)worldPos + foffset;
            CellsOfRect(walls, fc, fsize, cellScratch);
            foreach (Vector3Int c in cellScratch)
            {
                if (walls.HasTile(c)) { reason = "футпринт лезет в стену"; return false; }
            }
            Collider2D[] hits = Physics2D.OverlapBoxAll(fc, fsize * 0.95f, 0f);
            foreach (Collider2D h in hits)
            {
                if (h == null || h.isTrigger) continue;
                if (ignore != null && (h.transform == ignore.transform || h.transform.IsChildOf(ignore.transform))) continue; // сам несомый
                if (h.CompareTag("Player")) continue;
                if (h.GetComponentInParent<AnimalController>() != null) continue;
                reason = "место занято (" + h.name + ")";
                return false;
            }
        }
        return true;
    }

    /// <summary>Поставить: валидация → списание со склада → инстанс + маркер → сейв.</summary>
    public static GameObject TryPlace(Tilemap walls, Tilemap floors, GameObject prefab,
        Vector3 worldPos, bool useStock, out string reason)
    {
        reason = "";
        if (prefab == null) { reason = "нет префаба"; return null; }
        FurnitureMount mount = MountOfPrefab(prefab.name);
        HashSet<Vector3Int> region = HouseAutotiler.LargestFloorRegion(floors);
        if (!CanPlaceAt(walls, floors, region, prefab, worldPos, mount, out reason))
            return null;
        // В редакторе (useStock=false) склад не трогаем: иначе ленивый синглтон
        // создал бы лишний объект в открытой сцене (паттерн TryExpand free).
        if (useStock)
        {
            if (HouseStock.Instance == null) { reason = "нет склада"; return null; }
            if (!HouseStock.Instance.Remove(prefab.name))
            { reason = "нет на складе: " + prefab.name; return null; }
        }
        GameObject go = Object.Instantiate(prefab, worldPos, Quaternion.identity);
        go.name = prefab.name;
        HousePlacedFurniture marker = go.AddComponent<HousePlacedFurniture>();
        marker.prefabName = prefab.name;
        SaveManager.Instance?.Save();
        return go;
    }

    /// <summary>Убрать на склад (молоток/перестройка/драг на панель в фазе 7).</summary>
    public static bool TryStore(HousePlacedFurniture f, bool toStock, out string reason)
    {
        reason = "";
        if (f == null) { reason = "нет объекта"; return false; }
        string id = string.IsNullOrEmpty(f.prefabName) ? f.gameObject.name : f.prefabName;
        if (toStock)
        {
            if (HouseStock.Instance == null) { reason = "нет склада"; return false; }
            HouseStock.Instance.Add(id, 1);
        }
        DestroyPlaced(f);
        SaveManager.Instance?.Save();
        return true;
    }

    static void DestroyPlaced(HousePlacedFurniture f)
    {
        if (f == null) return;
        if (Application.isPlaying) Object.Destroy(f.gameObject);
        else Object.DestroyImmediate(f.gameObject);
    }

    /// <summary>
    /// Перепроверить всю расставленную мебель (после перекраски/пристройки/сноса):
    /// невалидная едет на склад (toStock) или уничтожается (тесты редактора).
    /// Возвращает число сброшенных.
    /// </summary>
    public static int RefreshPlaced(Tilemap walls, Tilemap floors, bool toStock)
    {
        if (walls == null || floors == null) return 0;
        HousePlacedFurniture[] all = Object.FindObjectsByType<HousePlacedFurniture>(FindObjectsSortMode.None);
        HashSet<Vector3Int> region = HouseAutotiler.LargestFloorRegion(floors);
        int dropped = 0;
        foreach (HousePlacedFurniture f in all)
        {
            if (f == null) continue;
            GameObject prefab = ResolvePrefab(f.prefabName);
            // Префаб вне каталога (старый сейв/ручной инстанс): валидируем маркер
            // по текущему коллайдеру как Floor — снесли из-под него пол/стену, упадёт.
            FurnitureMount mount = !string.IsNullOrEmpty(f.prefabName)
                ? MountOfPrefab(f.prefabName) : FurnitureMount.Floor;
            bool ok;
            if (prefab != null)
                ok = CanPlaceAt(walls, floors, region, prefab, f.transform.position, mount, out _);
            else
                ok = floors.HasTile(floors.WorldToCell(f.transform.position));
            if (ok) continue;
            string id = string.IsNullOrEmpty(f.prefabName) ? f.gameObject.name : f.prefabName;
            if (toStock && HouseStock.Instance != null)
                HouseStock.Instance.Add(id, 1);
            Debug.LogWarning("[House] Декор снят (" + id + "): под ним перестроили" +
                             (toStock ? ", убран на склад." : "."));
            DestroyPlaced(f);
            dropped++;
        }
        if (dropped > 0)
            SaveManager.Instance?.Save();
        return dropped;
    }

    /// <summary>Восстановить мебель из сейва (каталог = реестр префабов).</summary>
    public static int SpawnSaved(List<SavedPiece> pieces, Tilemap walls, Tilemap floors,
        out List<string> missing)
    {
        missing = new List<string>();
        int spawned = 0;
        if (pieces == null || walls == null || floors == null) return 0;
        foreach (SavedPiece p in pieces)
        {
            if (string.IsNullOrEmpty(p.prefabName)) continue;
            GameObject prefab = ResolvePrefab(p.prefabName);
            if (prefab == null) { missing.Add(p.prefabName); continue; }
            GameObject go = Object.Instantiate(prefab, new Vector3(p.x, p.y, 0f), Quaternion.identity);
            go.name = p.prefabName;
            HousePlacedFurniture marker = go.AddComponent<HousePlacedFurniture>();
            marker.prefabName = p.prefabName;
            spawned++;
        }
        return spawned;
    }

    /// <summary>Вся расставленная мебель в сцене (для меню/панели).</summary>
    public static HousePlacedFurniture[] FindPlaced()
    {
        return Object.FindObjectsByType<HousePlacedFurniture>(FindObjectsSortMode.None);
    }
}
