using UnityEngine;
using UnityEditor;

/// <summary>
/// Путеводитель: квестовые предметы. Tools → Lore → 2. Build Quest Items.
/// Повторный запуск ОБНОВЛЯЕТ in place (ручные правки полей НЕ трёт,
/// только создаёт недостающее).
/// </summary>
public static class LoreQuestItemsBuilder
{
    [MenuItem("Tools/Lore/2. Build Quest Items")]
    public static void BuildAll()
    {
        BuildBread();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Lore] Квестовые предметы собраны: Хлеб.");
    }

    static void BuildBread()
    {
        const string path = "Assets/Resources/Items/Bread.asset";
        var bread = AssetDatabase.LoadAssetAtPath<ItemData>(path);
        if (bread == null)
        {
            bread = ScriptableObject.CreateInstance<ItemData>();
            bread.itemName = "Хлеб";
            bread.description = "Тёплый хлеб от Густава. Лечит царапины и бодрит.";
            bread.itemType = ItemType.Consumable;
            bread.isStackable = false;
            bread.maxStack = 1;
            bread.healAmount = 25;
            // Бафф — чтобы съедался даже при полном HP (иначе шаг eat встанет)
            bread.foodBuffType = FoodBuffType.MoveSpeed;
            bread.foodBuffValue = 0.5f;
            bread.foodBuffDuration = 60f;
            AssetDatabase.CreateAsset(bread, path);
        }
        // Иконка — всегда обновляем (дешёвая и наглядная)
        var icon = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/Icons/Food Icons/Bread 2.png");
        if (icon != null)
        {
            bread.icon = icon;
            if (bread.worldSprite == null) bread.worldSprite = icon;
        }
        else Debug.LogWarning("[Lore] Нет иконки Bread 2.png — поставь руками в Bread.asset");
        EditorUtility.SetDirty(bread);
    }
}
