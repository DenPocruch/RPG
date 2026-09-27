using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Биндер сценовой панели контекстных подсказок обучения (как трекер: всё перетаскиванием).
/// Вешается на TutorialHint под персистентным Canvas.
/// Поля: panel = сама панель, text = текст подсказки, closeButton = кнопка «Понятно».
/// Без биндера TutorialManager построит простую панель кодом (фолбэк).
/// Имена для авто-поиска без биндера: TutorialHint / TutorialHintText / TutorialHintClose.
/// </summary>
public class TutorialHintBinder : MonoBehaviour
{
    [Header("Панель подсказки (сам TutorialHint)")]
    public GameObject panel;
    [Header("Текст подсказки")]
    public TMP_Text text;
    [Header("Кнопка закрытия")]
    public Button closeButton;

    void Awake()
    {
        ApplyBind();
    }

    public void ApplyBind()
    {
        GameObject p = panel != null ? panel : gameObject;
        TutorialManager.Instance.BindHint(p, text, closeButton);
    }
}
