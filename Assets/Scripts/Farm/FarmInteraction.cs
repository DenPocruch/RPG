using UnityEngine;

public class FarmInteraction : MonoBehaviour
{
    [Header("����� ��������������")]
    public Transform hoePoint;
    public Transform wateringPoint;

    [Header("���� �� ����������")]
    public int xpTill = 2;   // ������� �����
    public int xpPlant = 3;   // ������� ������
    public int xpWater = 1;   // �����
    public int xpHarvest = 10;  // ���� ������

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

        // Удобрение: активным предметом по грядке с растением
        if (activeItem != null && activeItem.isFertilizer)
        {
            if (FarmManager.Instance.FertilizeCrop(pos))
                ConsumeActiveItem();
            else
                ActionLogUI.Show("[Ферма] Здесь удобрять нечего (или уже удобрено)");
            return;
        }

        if (activeItem != null && activeItem.itemType == ItemType.Seed)
            PlantSeed(activeItem, pos);
    }

    public void CheckHarvest()
    {
        // ���������� �� Attack
    }

    void TillGround()
    {
        if (FarmManager.Instance == null) return;
        Vector3 pos = hoePoint != null ? hoePoint.position : transform.position;
        bool success = FarmManager.Instance.TillSoil(pos);

        // ���� �� �������
        if (success && PlayerLevel.Instance != null)
            PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Farming, xpTill);
    }

    void WaterGround()
    {
        if (FarmManager.Instance == null) return;

        InventorySlot activeSlot = HotbarManager.Instance?.GetActiveSlot();
        if (activeSlot == null || !activeSlot.HasWater())
        {
            Debug.Log("��� ���� � �����!");
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

            // ���� �� �����
            if (PlayerLevel.Instance != null)
                PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Farming, xpWater);

            Debug.Log("������! �������� ����: " + activeSlot.currentWater);
        }
    }

    void PlantSeed(ItemData seedData, Vector3 pos)
    {
        if (FarmManager.Instance == null) return;
        bool success = FarmManager.Instance.PlantSeed(pos, seedData);
        if (success)
        {
            // ���� �� �������
            if (PlayerLevel.Instance != null)
                PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Farming, xpPlant);

            Debug.Log("��������: " + seedData.itemName);
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
        ItemData harvest = FarmManager.Instance.HarvestCrop(pos, out int quality);
        if (harvest == null) return;

        // Качество урожая: подменяем на звёздный вариант (Carrot → Carrot Silver и т.д.)
        if (quality > 0)
        {
            string suffix = quality == 1 ? " Silver" : quality == 2 ? " Gold" : " Purple";
            ItemData qualityItem = ItemDatabase.Find(harvest.name + suffix);
            if (qualityItem != null)
            {
                harvest = qualityItem;
                ActionLogUI.Show("[Ферма] Качественный урожай: " + harvest.itemName);
            }
        }

        // ���� �������� ������ �� ��������
        int harvestAmount = harvest.harvestAmount;
        if (SkillTreeManager.Instance != null)
        {
            float doubleChance = SkillTreeManager.Instance.GetDoubleHarvestChance();
            if (doubleChance > 0 && Random.Range(0f, 100f) < doubleChance)
            {
                harvestAmount *= 2;
                Debug.Log("[�����] ������� ������!");

                // ����� �������� ������
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
                Debug.Log("�������: " + harvest.itemName + " x" + harvestAmount);
            }
            else
                Debug.Log("��������� �����!");
        }
    }
}