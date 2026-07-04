using UnityEngine;

public class FarmInteraction : MonoBehaviour
{
    [Header("Точки взаимодействия")]
    public Transform hoePoint;
    public Transform wateringPoint;

    [Header("Опыт за фермерство")]
    public int xpTill = 2;   // вспашка земли
    public int xpPlant = 3;   // посадка семени
    public int xpWater = 1;   // полив
    public int xpHarvest = 10;  // сбор урожая

    public void UseFarmTool(ItemType toolType, float dirX, float dirY)
    {
        switch (toolType)
        {
            case ItemType.Hoe:
                TillGround();
                break;
            case ItemType.WateringCan:
                WaterGround();
                break;
        }
    }

    public void TryPlantOrHarvest()
    {
        if (FarmManager.Instance == null) return;
        Vector3 pos = hoePoint != null ? hoePoint.position : transform.position;

        ItemData activeItem = HotbarManager.Instance?.GetActiveItem();

        if (activeItem != null && activeItem.itemType == ItemType.Sickle)
        {
            if (FarmManager.Instance.IsCropReady(pos))
                HarvestCrop(pos);
            return;
        }

        if (activeItem != null && activeItem.itemType == ItemType.Seed)
            PlantSeed(activeItem, pos);
    }

    public void CheckHarvest()
    {
        // Вызывается из Attack
    }

    void TillGround()
    {
        if (FarmManager.Instance == null) return;
        Vector3 pos = hoePoint != null ? hoePoint.position : transform.position;
        bool success = FarmManager.Instance.TillSoil(pos);

        // Опыт за вспашку
        if (success && PlayerLevel.Instance != null)
            PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Farming, xpTill);
    }

    void WaterGround()
    {
        if (FarmManager.Instance == null) return;

        InventorySlot activeSlot = HotbarManager.Instance?.GetActiveSlot();
        if (activeSlot == null || !activeSlot.HasWater())
        {
            Debug.Log("Нет воды в лейке!");
            WaterBar waterBar = FindFirstObjectByType<WaterBar>();
            if (waterBar != null) waterBar.PlayEmptyEffect();
            return;
        }

        Vector3 pos = wateringPoint != null ? wateringPoint.position
                    : hoePoint != null ? hoePoint.position
                    : transform.position;

        bool watered = FarmManager.Instance.WaterSoil(pos);
        if (watered)
        {
            activeSlot.UseWater();

            // Опыт за полив
            if (PlayerLevel.Instance != null)
                PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Farming, xpWater);

            Debug.Log("Полито! Осталось воды: " + activeSlot.currentWater);
        }
    }

    void PlantSeed(ItemData seedData, Vector3 pos)
    {
        if (FarmManager.Instance == null) return;
        bool success = FarmManager.Instance.PlantSeed(pos, seedData);
        if (success)
        {
            // Опыт за посадку
            if (PlayerLevel.Instance != null)
                PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Farming, xpPlant);

            Debug.Log("Посажено: " + seedData.itemName);
            ConsumeActiveItem();
        }
    }

    void ConsumeActiveItem()
    {
        if (HotbarManager.Instance == null) return;
        InventorySlot activeSlot = HotbarManager.Instance.GetActiveSlot();
        if (activeSlot == null) return;

        if (activeSlot.quantity > 1)
        {
            activeSlot.quantity--;
            activeSlot.UpdateUI();
        }
        else
        {
            activeSlot.ClearSlot();
        }
    }

    void HarvestCrop(Vector3 pos)
    {
        ItemData harvest = FarmManager.Instance.HarvestCrop(pos);
        if (harvest == null) return;

        // Шанс двойного урожая от прокачки
        int harvestAmount = harvest.harvestAmount;
        if (SkillTreeManager.Instance != null)
        {
            float doubleChance = SkillTreeManager.Instance.GetDoubleHarvestChance();
            if (doubleChance > 0 && Random.Range(0f, 100f) < doubleChance)
            {
                harvestAmount *= 2;
                Debug.Log("[Ферма] Двойной урожай!");

                // Попап двойного урожая
                if (DamagePopupManager.Instance != null)
                    DamagePopupManager.Instance.Spawn(
                        transform.position + Vector3.up,
                        harvestAmount,
                        DamagePopup.PopupType.Heal);
            }
        }

        if (InventoryUI.Instance != null)
        {
            bool added = InventoryUI.Instance.AddItem(harvest, harvestAmount);
            if (added)
            {
                if (PlayerLevel.Instance != null)
                    PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Farming, xpHarvest);
                Debug.Log("Собрано: " + harvest.itemName + " x" + harvestAmount);
            }
            else
                Debug.Log("Инвентарь полон!");
        }
    }
}