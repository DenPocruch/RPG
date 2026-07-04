using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

public class FarmManager : MonoBehaviour
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

    // Словарь: позиция → растение
    private Dictionary<Vector3Int, CropTile> crops = new Dictionary<Vector3Int, CropTile>();

    void Awake()
    {
        Instance = this;
    }

    // Вскопать землю
    public bool TillSoil(Vector3 worldPos)
    {
        Vector3Int cellPos = farmTilemap.WorldToCell(worldPos);
        if (farmTilemap.GetTile(cellPos) != null) return false;

        farmTilemap.SetTile(cellPos, tilledSoilTile);

        // Удаляем цветок если есть в этой клетке
        ClearFlowerAt(worldPos);

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

        return true;
    }

    // Посадить семена
    public bool PlantSeed(Vector3 worldPos, ItemData seedData)
    {
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
        Debug.Log("Посеяно: " + seedData.itemName);
        return true;
    }

    // Собрать урожай
    public ItemData HarvestCrop(Vector3 worldPos)
    {
        Vector3Int cellPos = farmTilemap.WorldToCell(worldPos);

        if (!crops.ContainsKey(cellPos)) return null;

        CropTile crop = crops[cellPos];
        ItemData harvest = crop.Harvest();

        if (harvest == null)
        {
            Debug.Log("Растение ещё не выросло!");
            return null;
        }

        Destroy(crop.gameObject);
        crops.Remove(cellPos);
        waterTilemap.SetTile(cellPos, null);

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