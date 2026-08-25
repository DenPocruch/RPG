using UnityEngine;

public class CropTile : MonoBehaviour
{
    [Header("Состояние растения")]
    public ItemData cropData;        // данные семян
    public int currentStage = 0;     // текущая стадия роста
    public bool isWatered = false;   // полито?
    public bool isReady = false;     // готово к сбору?
    public bool isFertilized = false; // удобрено? (рост ×2)

    private SpriteRenderer spriteRenderer;
    private float plantedTime;
    private float lastStageTime;
    private float timePerStage;
    private bool restoredFromSave = false;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        // Если растение восстановлено из сохранения — не сбрасываем таймер
        if (!restoredFromSave)
        {
            plantedTime = Time.time;
            lastStageTime = Time.time;
        }

        UpdateSprite();
        CalculateGrowthTime();
    }

    void CalculateGrowthTime()
    {
        if (cropData == null) return;
        float totalTime = isWatered ? cropData.growthTimeWatered : cropData.growthTimeNormal;

        // Перки на скорость роста + удобрение (×2)
        float mult = 1f;
        if (SkillTreeManager.Instance != null)
            mult = SkillTreeManager.Instance.GetCropGrowthMultiplier();
        if (isFertilized) mult *= 2f;

        timePerStage = (totalTime / cropData.growthStagesCount) / mult;
    }

    void Update()
    {
        if (isReady || cropData == null) return;

        // ������������� ����� ���� ���������� ��������� ������
        CalculateGrowthTime();

        // ��������� ������� �� ��������� ������
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
            // �������� �������!
            currentStage = cropData.growthStagesCount - 1;
            isReady = true;
        }

        UpdateSprite();
    }

    // �������� ������ �� ������� ������
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

    // Удобрить — рост ×2 (навсегда, до сбора)
    public void Fertilize()
    {
        if (isFertilized || isReady) return;
        isFertilized = true;
        CalculateGrowthTime();
        Debug.Log("[Ферма] Растение удобрено — растёт вдвое быстрее!");
    }

    // Качество урожая: 0=обычное, 1=серебро, 2=золото, 3=пурпур.
    // Шансы качаются перками (по 10 рангов на звезду), удобрение даёт бонус.
    // При максимальной прокачке пурпур достигает 100%.
    public int RollQuality()
    {
        float silver = 10f, gold = 4f, purple = 0f;
        bool purpleUnlocked = false;
        if (SkillTreeManager.Instance != null)
        {
            silver += SkillTreeManager.Instance.GetSilverQualityBonus();
            gold += SkillTreeManager.Instance.GetGoldQualityBonus();
            purple += SkillTreeManager.Instance.GetPurpleQualityBonus();
            purpleUnlocked = SkillTreeManager.Instance.IsNodeUnlockedByFeature("quality_purple");
        }
        if (isFertilized) { silver += 10f; gold += 4f; }
        if (!purpleUnlocked) purple = 0f;

        float r = Random.Range(0f, 100f);
        if (r < purple) return 3;
        if (r < purple + gold) return 2;
        if (r < purple + gold + silver) return 1;
        return 0;
    }
    // ─── Оффлайн-рост (растёт в городе и при закрытой игре) ───

    /// <summary>Сколько секунд осталось до следующей стадии и полная длительность стадии.</summary>
    public void GetGrowthInfo(out float remain, out float stageTime)
    {
        remain = Mathf.Max(0f, timePerStage - (Time.time - lastStageTime));
        stageTime = timePerStage;
    }

    /// <summary>Применить время, прошедшее пока игрок отсутствовал.</summary>
    public void ApplyOfflineGrowth(double seconds, float remainToNextStage, float savedStageTime)
    {
        if (isReady || cropData == null || seconds <= 0) return;

        if (savedStageTime <= 0f)
        {
            CalculateGrowthTime();
            savedStageTime = timePerStage;
        }
        if (savedStageTime <= 0f) return;

        int maxStage = cropData.growthStagesCount - 1;

        // Сколько уже «накоплено» в текущей стадии + сколько прошло оффлайн
        float progressedInStage = savedStageTime - Mathf.Clamp(remainToNextStage, 0f, savedStageTime);
        double total = seconds + progressedInStage;

        int stagesAdvanced = (int)(total / savedStageTime);
        float remainder = (float)(total % savedStageTime);

        currentStage += stagesAdvanced;
        if (currentStage >= maxStage)
        {
            currentStage = maxStage;
            isReady = true;
            lastStageTime = Time.time;
        }
        else
        {
            // Сохраняем остаток — Start() не должен его сбросить
            restoredFromSave = true;
            lastStageTime = Time.time - remainder;
        }

        UpdateSprite();
    }

    // ������� ������ � ���������� ������� ������
    public ItemData Harvest()
    {
        if (!isReady) return null;
        return cropData.harvestItem;
    }
}