using UnityEngine;

public class PlantedTree : MonoBehaviour
{
    [Header("Данные ростка")]
    public ItemData saplingData;

    private int currentStage = 0;
    private float growthTimer = 0f;
    private float timePerStage;
    private bool isFullyGrown = false;

    private SpriteRenderer sr;
    private TreeComponent treeComponent;

    public static float minTreeDistance = 1.5f;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        treeComponent = GetComponent<TreeComponent>();

        // Отключаем TreeComponent до полного роста
        if (treeComponent != null)
            treeComponent.enabled = false;

        if (saplingData != null && saplingData.growthStagesCount > 0)
            timePerStage = saplingData.treeGrowthTime / saplingData.growthStagesCount;

        UpdateSprite();
    }

    void Update()
    {
        if (isFullyGrown || saplingData == null) return;

        growthTimer += Time.deltaTime;

        if (growthTimer >= timePerStage)
        {
            growthTimer = 0f;
            currentStage++;

            if (currentStage >= saplingData.growthStagesCount)
            {
                FinishGrowth();
                return;
            }

            UpdateSprite();
        }
    }

    void UpdateSprite()
    {
        if (sr == null || saplingData == null) return;
        if (saplingData.treeGrowthStages == null || saplingData.treeGrowthStages.Length == 0)
        {
            // Если нет стадий роста дерева — используем growthStages
            if (saplingData.growthStages != null && saplingData.growthStages.Length > 0)
            {
                int idx = Mathf.Clamp(currentStage, 0, saplingData.growthStages.Length - 1);
                sr.sprite = saplingData.growthStages[idx];
            }
            return;
        }

        int i = Mathf.Clamp(currentStage, 0, saplingData.treeGrowthStages.Length - 1);
        sr.sprite = saplingData.treeGrowthStages[i];
    }

    void FinishGrowth()
    {
        isFullyGrown = true;

        if (treeComponent != null && saplingData != null)
        {
            // Передаём ItemData в TreeComponent — он возьмёт оттуда всё
            treeComponent.treeData = saplingData;
            treeComponent.isStaticTree = false;
            treeComponent.enabled = true; // запустит OnEnable → InitFromData
        }

        this.enabled = false;
        Debug.Log("Дерево выросло: " + (saplingData?.itemName ?? ""));
    }

    public static bool CanPlantHere(Vector3 pos)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, minTreeDistance);
        foreach (Collider2D hit in hits)
        {
            if (hit.GetComponentInParent<TreeComponent>() != null) return false;
            if (hit.GetComponentInParent<PlantedTree>() != null) return false;
        }
        return true;
    }
}