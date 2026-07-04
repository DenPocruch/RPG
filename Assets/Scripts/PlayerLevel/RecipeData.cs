using UnityEngine;

[System.Serializable]
public class RecipeIngredient
{
    public ItemData item;
    public int amount = 1;
}

/// <summary>
/// Рецепт блюда для повара. Создаётся через Assets → Create → RPG → Recipe.
/// Несколько ингредиентов → 1 блюдо, готовится в фоне за cookTime секунд.
/// </summary>
[CreateAssetMenu(fileName = "NewRecipe", menuName = "RPG/Recipe")]
public class RecipeData : ScriptableObject
{
    [Header("Основное")]
    public string recipeName = "Блюдо";
    [TextArea(2, 3)]
    public string description = "";

    [Header("Результат")]
    public ItemData outputItem;   // готовое блюдо (Consumable с полями еды)
    public int outputAmount = 1;

    [Header("Ингредиенты")]
    public RecipeIngredient[] ingredients;

    [Header("Готовка")]
    public float cookTime = 30f;  // секунд
    public int goldCost = 0;    // оплата повару
    public int xpReward = 10;   // Crafting XP за блюдо

    [Header("Разблокировка")]
    public bool unlockedByDefault = true;
    [Tooltip("Тег фичи из дерева навыков (SkillNode.unlocksFeature), если рецепт открывается прокачкой")]
    public string unlockFeatureTag = "";

    public bool IsUnlocked()
    {
        if (unlockedByDefault) return true;
        if (!string.IsNullOrEmpty(unlockFeatureTag) && SkillTreeManager.Instance != null)
            return SkillTreeManager.Instance.IsNodeUnlockedByFeature(unlockFeatureTag);
        return false;
    }
}