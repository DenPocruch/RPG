using UnityEngine;

public class CropTile : MonoBehaviour
{
    [Header("Данные растения")]
    public ItemData cropData;        // данные семян
    public int currentStage = 0;     // текущая стадия роста
    public bool isWatered = false;   // полито?
    public bool isReady = false;     // готово к сбору?

    private SpriteRenderer spriteRenderer;
    private float plantedTime;
    private float lastStageTime;
    private float timePerStage;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        plantedTime = Time.time;
        lastStageTime = Time.time;
        UpdateSprite();
        CalculateGrowthTime();
    }

    void CalculateGrowthTime()
    {
        if (cropData == null) return;
        float totalTime = isWatered ? cropData.growthTimeWatered : cropData.growthTimeNormal;
        timePerStage = totalTime / cropData.growthStagesCount;
    }

    void Update()
    {
        if (isReady || cropData == null) return;

        // Пересчитываем время если изменилось состояние полива
        CalculateGrowthTime();

        // Проверяем переход на следующую стадию
        if (Time.time - lastStageTime >= timePerStage)
        {
            lastStageTime = Time.time;
            GrowToNextStage();
        }
    }

    void GrowToNextStage()
    {
        if (cropData.growthStages == null) return;

        currentStage++;

        if (currentStage >= cropData.growthStagesCount)
        {
            // Растение выросло!
            currentStage = cropData.growthStagesCount - 1;
            isReady = true;
        }

        UpdateSprite();
    }

    // Обновить спрайт по текущей стадии
    void UpdateSprite()
    {
        if (spriteRenderer == null || cropData == null) return;
        if (cropData.growthStages == null || cropData.growthStages.Length == 0) return;

        int spriteIndex = Mathf.Clamp(currentStage, 0, cropData.growthStages.Length - 1);
        spriteRenderer.sprite = cropData.growthStages[spriteIndex];
    }

    // Полить растение
    public void Water()
    {
        isWatered = true;
        CalculateGrowthTime();
    }

    // Собрать урожай — возвращает предмет урожая
    public ItemData Harvest()
    {
        if (!isReady) return null;
        return cropData.harvestItem;
    }
}