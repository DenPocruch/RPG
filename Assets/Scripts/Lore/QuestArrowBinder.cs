using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Биндер сценовой стрелки путеводителя (как книга/трекер: всё перетаскиванием).
/// Вешается на QuestArrow-панель под персистентным Canvas.
/// Поля: root = сам объект стрелки, arrowImage = картинка стрелки (ТВОЙ спрайт),
/// label = подпись с метрами. Остриё спрайта должно смотреть ВВЕРХ (код докрутит).
/// </summary>
public class QuestArrowBinder : MonoBehaviour
{
    [Header("Корень стрелки (сам объект)")]
    public GameObject root;
    [Header("Картинка стрелки (твой спрайт, остриём вверх)")]
    public Image arrowImage;
    [Header("Подпись с метрами")]
    public TMP_Text label;

    void Awake()
    {
        ApplyBind();
    }

    public void ApplyBind()
    {
        QuestArrow.Bind(root != null ? root : gameObject, arrowImage, label);
    }
}
