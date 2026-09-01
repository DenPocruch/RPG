using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Кнопка одного рецепта в списке книги рецептов.
/// Клик → CookUI показывает детали этого рецепта.
/// </summary>
public class RecipeButtonUI : MonoBehaviour
{
    [Header("UI")]
    public Image iconImage;    // иконка блюда
    public TMP_Text nameText;     // название рецепта
    public Image lockIcon;     // замочек для закрытых рецептов
    public Image background;

    [Header("Цвета")]
    public Color colorUnlocked = new Color(1f, 1f, 1f, 1f);
    public Color colorLocked = new Color(0.4f, 0.4f, 0.4f, 1f);
    public Color colorSelected = new Color(0.9f, 0.8f, 0.3f, 1f);
    public Color colorNormal = new Color(1f, 1f, 1f, 0.15f);

    private RecipeData recipe;
    private bool isUnlocked;

    public void Setup(RecipeData r)
    {
        recipe = r;
        isUnlocked = r.IsUnlocked();

        if (iconImage != null)
        {
            iconImage.sprite = r.outputItem != null ? r.outputItem.icon : null;
            iconImage.color = isUnlocked ? colorUnlocked : colorLocked;
        }

        if (nameText != null)
        {
            nameText.text = isUnlocked ? r.recipeName : "???";
        }

        if (lockIcon != null) lockIcon.enabled = !isUnlocked;

        SetSelected(false);
    }

    public void OnClick()
    {
        if (CookUI.Instance != null)
            CookUI.Instance.SelectRecipe(recipe);
    }

    public void SetSelected(bool selected)
    {
        if (background != null)
            background.color = selected ? colorSelected : colorNormal;
    }

    public RecipeData GetRecipe() => recipe;
}