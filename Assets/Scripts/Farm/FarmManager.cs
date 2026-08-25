using UnityEngine;
using UnityEngine.Tilemaps;
using System;
using System.Collections.Generic;

public class FarmManager : MonoBehaviour, ISaveable
{
    public static FarmManager Instance;

    [Header("Тайлмапы")]
    public Tilemap farmTilemap;
    public Tilemap waterTilemap;

    [Header("Тайлы")]
    public TileBase tilledSoilTile;
    public TileBase wateredSoilTile;

    [Header("Префаб растения")]
    public GameObject cropPrefab;

    [Header("Радиус поиска цветка при копке")]
    public float flowerClearRadius = 0.4f;

    [Header("Увядание грядки (минуты)")]
    [Tooltip("Через сколько минут политая грядка высыхает если ничего не посадили")]
    public float dryTimeMinutes = 10f;
    [Tooltip("Через сколько минут вскопанная грядка зарастает в траву если ничего не посадили. Полив сбрасывает этот таймер.")]
    public float grassTimeMinutes = 20f;

    [Header("Лимит грядок")]
    [Tooltip("Сколько грядок игрок может вскопать без прокачки")]
    public int basePlotLimit = 10;

    // Словарь: позиция → растение
    private Dictionary<Vector3Int, CropTile> crops = new Dictionary<Vector3Int, CropTile>();

    // Таймеры увядания пустых (без растения) грядок
    private class PlotTimer
    {
        public float grassTimer; // до зарастания в траву
        public float dryTimer;   // до высыхания (если полито)
        public bool watered;
    }
    private Dictionary<Vector3Int, PlotTimer> plotTimers = new Dictionary<Vector3Int, PlotTimer>();

    void Awake()
    {
        // FarmManager живёт в сцене (не в PersistentRoot). При смене сцены
        // старый экземпляр ещё жив в течение кадра загрузки — новый должен
        // занять его место. Уничтожаем только дубликат в той же сцене.
        if (Instance != null && Instance != this && Instance.gameObject.scene == gameObject.scene)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        SaveManager.Instance?.Register(this);
    }

    void Start()
    {
        // Грузим сохранённое состояние фермы (вспаханные/политые клетки + растения)
        SaveManager.Instance?.LoadInto(this);
    }

    void Update()
    {
        // Если тайлмап уничтожен (смена сцены) — ничего не делаем
        if (farmTilemap == null) return;
        TickPlotDecay();
    }

    void OnDestroy()
    {
        // Отписываемся от сохранения при выгрузке сцены
        SaveManager.Instance?.Unregister(this);
        // Чистим ссылку, чтобы после смены сцены Instance не указывал
        // на уничтоженный объект (аналог MissingReferenceException)
        if (Instance == this) Instance = null;
    }

    // ═══════════════════════════════════════════════════════════
    // УВЯДАНИЕ ГРЯДОК (высыхание → зарастание)
    // ═══════════════════════════════════════════════════════════
    void TickPlotDecay()
    {
        if (plotTimers.Count == 0) return;

        float dt = Time.deltaTime;
        // Копируем ключи — можем удалять во время обхода
        var cells = new List<Vector3Int>(plotTimers.Keys);

        foreach (Vector3Int cell in cells)
        {
            // Если на клетке растёт растение — увядание не идёт
            if (crops.ContainsKey(cell)) continue;

            PlotTimer pt = plotTimers[cell];

            // Высыхание политой грядки
            if (pt.watered)
            {
                pt.dryTimer -= dt;
                if (pt.dryTimer <= 0f)
                {
                    pt.watered = false;
                    waterTilemap.SetTile(cell, null); // убираем воду — грядка сухая
                }
            }

            // Зарастание в траву
            pt.grassTimer -= dt;
            if (pt.grassTimer <= 0f)
            {
                // Клетка зарастает: убираем вспашку и воду, снизу проступает трава
                farmTilemap.SetTile(cell, null);
                waterTilemap.SetTile(cell, null);
                plotTimers.Remove(cell);
            }
        }
    }

    // Регистрируем/сбрасываем таймеры для пустой грядки
    void RegisterPlot(Vector3Int cell, bool watered)
    {
        plotTimers[cell] = new PlotTimer
        {
            grassTimer = grassTimeMinutes * 60f,
            dryTimer = watered ? dryTimeMinutes * 60f : 0f,
            watered = watered
        };
    }

    // ═══════════════════════════════════════════════════════════
    // ISaveable — сохраняем вспаханные/политые клетки и растения.
    // Посаженные деревья сохраняет TreeSaveManager (есть в каждой сцене).
    // ═══════════════════════════════════════════════════════════
    [System.Serializable]
    private class TilledCellSave
    {
        public int x;
        public int y;
        public bool watered;
        public float grassTimer; // остаток до зарастания (сек), -1 если клетка под растением
        public float dryTimer;   // остаток до высыхания (сек)
    }

    [System.Serializable]
    private class CropSave
    {
        public int x;
        public int y;
        public string itemName;
        public int stage;
        public bool watered;
        public bool ready;
        public bool fertilized;
        public float remainToNextStage; // остаток до следующей стадии
        public float stageTime;         // полная длительность стадии
    }

    [System.Serializable]
    private class FarmSave
    {
        public long savedAtTicks; // реальное время сохранения — для оффлайн-роста
        public List<TilledCellSave> tilled = new List<TilledCellSave>();
        public List<CropSave> crops = new List<CropSave>();
    }

    public string SaveKey => "farm";

    public string CaptureState()
    {
        FarmSave save = new FarmSave
        {
            savedAtTicks = DateTime.UtcNow.Ticks
        };

        // Все вспаханные клетки (и заодно — политы ли)
        BoundsInt bounds = farmTilemap.cellBounds;
        TileBase[] allTiles = farmTilemap.GetTilesBlock(bounds);

        for (int x = 0; x < bounds.size.x; x++)
        {
            for (int y = 0; y < bounds.size.y; y++)
            {
                TileBase tile = allTiles[x + y * bounds.size.x];
                if (tile == null) continue;

                Vector3Int cellPos = new Vector3Int(bounds.xMin + x, bounds.yMin + y, 0);
                bool watered = waterTilemap.GetTile(cellPos) != null;

                // Таймеры (если клетка отслеживается как пустая грядка)
                float gTimer = -1f, dTimer = 0f;
                if (plotTimers.TryGetValue(cellPos, out PlotTimer pt))
                {
                    gTimer = pt.grassTimer;
                    dTimer = pt.dryTimer;
                }

                save.tilled.Add(new TilledCellSave
                {
                    x = cellPos.x,
                    y = cellPos.y,
                    watered = watered,
                    grassTimer = gTimer,
                    dryTimer = dTimer
                });
            }
        }

        // Все растения (+ остаток времени до следующей стадии)
        foreach (var kvp in crops)
        {
            CropTile crop = kvp.Value;
            if (crop == null || crop.cropData == null) continue;

            crop.GetGrowthInfo(out float remain, out float stageTime);

            save.crops.Add(new CropSave
            {
                x = kvp.Key.x,
                y = kvp.Key.y,
                itemName = crop.cropData.name,
                stage = crop.currentStage,
                watered = crop.isWatered,
                ready = crop.isReady,
                fertilized = crop.isFertilized,
                remainToNextStage = remain,
                stageTime = stageTime
            });
        }

        // Все посаженные деревья сохраняет TreeSaveManager

        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        FarmSave save = JsonUtility.FromJson<FarmSave>(json);
        if (save == null) return;

        // Убираем текущие растения (на случай повторной загрузки в этой же сессии)
        foreach (CropTile crop in crops.Values)
            if (crop != null) Destroy(crop.gameObject);
        crops.Clear();

        // Восстанавливаем вспаханные/политые клетки
        plotTimers.Clear();
        foreach (TilledCellSave tc in save.tilled)
        {
            Vector3Int cellPos = new Vector3Int(tc.x, tc.y, 0);
            farmTilemap.SetTile(cellPos, tilledSoilTile);
            if (tc.watered)
                waterTilemap.SetTile(cellPos, wateredSoilTile);

            // Восстанавливаем таймер увядания (если клетка была пустой грядкой)
            if (tc.grassTimer >= 0f)
            {
                plotTimers[cellPos] = new PlotTimer
                {
                    grassTimer = tc.grassTimer,
                    dryTimer = tc.dryTimer,
                    watered = tc.watered
                };
            }

            // Важно: при Stop/Play в редакторе сцена перезагружается и декоративные
            // цветы (обычные объекты сцены) появляются заново. Раньше их убирал
            // только TillSoil() через ClearFlowerAt — при восстановлении из
            // сохранения это нужно делать так же, иначе цветок виснет поверх
            // уже вспаханной земли.
            ClearFlowerAt(farmTilemap.GetCellCenterWorld(cellPos));
        }

        // Восстанавливаем растения + оффлайн-рост
        // (пока игрока не было — в городе или при закрытой игре — растения растут)
        double elapsed = save.savedAtTicks > 0
            ? (DateTime.UtcNow.Ticks - save.savedAtTicks) / (double)TimeSpan.TicksPerSecond
            : 0;
        if (elapsed < 0) elapsed = 0;

        foreach (CropSave cs in save.crops)
        {
            ItemData seedData = ItemDatabase.Find(cs.itemName);
            if (seedData == null)
            {
                Debug.LogWarning("[Save] Семя не найдено: " + cs.itemName);
                continue;
            }

            Vector3Int cellPos = new Vector3Int(cs.x, cs.y, 0);
            Vector3 worldCenter = farmTilemap.GetCellCenterWorld(cellPos);

            GameObject cropObj = Instantiate(cropPrefab, worldCenter, Quaternion.identity);
            CropTile crop = cropObj.GetComponent<CropTile>();

            // ВАЖНО: устанавливаем поля ДО того как у объекта отработает Start()
            // (Instantiate не вызывает Start синхронно) — поэтому Start() потом
            // сам подхватит currentStage/isWatered и корректно покажет спрайт.
            crop.cropData = seedData;
            crop.currentStage = cs.stage;
            crop.isWatered = cs.watered;
            crop.isReady = cs.ready;
            crop.isFertilized = cs.fertilized;

            crops[cellPos] = crop;

            // Оффлайн-рост: догоняем прошедшее время (учтён перк Селекционер
            // и удобрение — они уже внутри timePerStage)
            if (!cs.ready && elapsed > 0 && cs.stageTime > 0)
            {
                crop.ApplyOfflineGrowth(elapsed, cs.remainToNextStage, cs.stageTime);
            }
        }

        Debug.Log("[Save] Ферма восстановлена: " + save.tilled.Count + " клеток, " + save.crops.Count + " растений");
    }

    // Вскопать землю
    public bool TillSoil(Vector3 worldPos)
    {
        Vector3Int cellPos = farmTilemap.WorldToCell(worldPos);
        if (farmTilemap.GetTile(cellPos) != null) return false;

        // Лимит грядок: пустые (увядающие) + под растениями
        int limit = basePlotLimit + (SkillTreeManager.Instance != null
            ? SkillTreeManager.Instance.GetPlotLimitBonus() : 0);
        if (plotTimers.Count + crops.Count >= limit)
        {
            Debug.Log("[Ферма] Лимит грядок: " + limit + ". Прокачай «Расширение участка» в дереве навыков!");
            return false;
        }

        farmTilemap.SetTile(cellPos, tilledSoilTile);

        // Удаляем цветок если есть в этой клетке
        ClearFlowerAt(worldPos);

        // Запускаем таймер зарастания (пока сухая)
        RegisterPlot(cellPos, false);

        return true;
    }

    // Удалить цветок в точке копки
    void ClearFlowerAt(Vector3 worldPos)
    {
        // Ищем все объекты с тегом Flower рядом с точкой копки
        Collider2D[] hits = Physics2D.OverlapCircleAll(worldPos, flowerClearRadius);
        foreach (Collider2D hit in hits)
        {
            if (hit.CompareTag("Flower"))
            {
                Destroy(hit.gameObject);
                break; // один цветок на клетку
            }
        }
    }

    // Полить грядку
    public bool WaterSoil(Vector3 worldPos)
    {
        Vector3Int cellPos = farmTilemap.WorldToCell(worldPos);
        if (farmTilemap.GetTile(cellPos) == null) return false;
        waterTilemap.SetTile(cellPos, wateredSoilTile);

        if (crops.ContainsKey(cellPos))
            crops[cellPos].Water();

        // Полив продлевает жизнь грядки: сбрасываем таймеры высыхания и зарастания
        if (plotTimers.ContainsKey(cellPos))
        {
            plotTimers[cellPos].watered = true;
            plotTimers[cellPos].dryTimer = dryTimeMinutes * 60f;
            plotTimers[cellPos].grassTimer = grassTimeMinutes * 60f;
        }

        return true;
    }

    // Удобрить растение на клетке (рост ×2)
    public bool FertilizeCrop(Vector3 worldPos)
    {
        Vector3Int cellPos = farmTilemap.WorldToCell(worldPos);
        if (!crops.ContainsKey(cellPos)) return false;

        CropTile crop = crops[cellPos];
        if (crop == null || crop.isReady) return false;
        if (crop.isFertilized) return false; // уже удобрено

        crop.Fertilize();
        return true;
    }

    // Посадить семена
    public bool PlantSeed(Vector3 worldPos, ItemData seedData)    {
        Vector3Int cellPos = farmTilemap.WorldToCell(worldPos);

        if (farmTilemap.GetTile(cellPos) == null)
        {
            Debug.Log("Нужна вскопанная земля!");
            return false;
        }

        if (crops.ContainsKey(cellPos))
        {
            Debug.Log("Здесь уже что-то растёт!");
            return false;
        }

        Vector3 worldCenter = farmTilemap.GetCellCenterWorld(cellPos);
        GameObject cropObj = Instantiate(cropPrefab, worldCenter, Quaternion.identity);
        CropTile crop = cropObj.GetComponent<CropTile>();
        crop.cropData = seedData;
        crop.isWatered = waterTilemap.GetTile(cellPos) != null;

        crops[cellPos] = crop;
        plotTimers.Remove(cellPos); // растение посажено — грядка больше не увядает

        Debug.Log("Посеяно: " + seedData.itemName);
        return true;
    }

    // Собрать урожай
    public ItemData HarvestCrop(Vector3 worldPos, out int quality)
    {
        quality = 0;
        Vector3Int cellPos = farmTilemap.WorldToCell(worldPos);

        if (!crops.ContainsKey(cellPos)) return null;

        CropTile crop = crops[cellPos];
        ItemData harvest = crop.Harvest();

        if (harvest == null)
        {
            quality = 0;
            Debug.Log("Растение ещё не выросло!");
            return null;
        }

        quality = crop.RollQuality();

        Destroy(crop.gameObject);
        crops.Remove(cellPos);
        waterTilemap.SetTile(cellPos, null);

        // Грядка снова пустая — запускаем таймер зарастания заново (сухая)
        RegisterPlot(cellPos, false);

        // Сейв по событию: урожай собран
        SaveManager.Instance?.Save();

        Debug.Log("Собрано: " + harvest.itemName);
        return harvest;
    }

    // Проверки
    public bool IsTilled(Vector3 worldPos)
    {
        return farmTilemap.GetTile(farmTilemap.WorldToCell(worldPos)) != null;
    }

    public bool IsWatered(Vector3 worldPos)
    {
        return waterTilemap.GetTile(waterTilemap.WorldToCell(worldPos)) != null;
    }

    public bool HasCrop(Vector3 worldPos)
    {
        return crops.ContainsKey(farmTilemap.WorldToCell(worldPos));
    }

    public bool IsCropReady(Vector3 worldPos)
    {
        Vector3Int cellPos = farmTilemap.WorldToCell(worldPos);
        return crops.ContainsKey(cellPos) && crops[cellPos].isReady;
    }
}