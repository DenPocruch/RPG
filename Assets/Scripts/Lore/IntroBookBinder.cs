using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Биндер сценовой панели книги (как конструктор: всё перетаскиванием).
/// Вешается на IntroBookPanel под персистентным Canvas.
/// Поля: root = сама панель, title/body, back/next/skip кнопки.
/// Awake срабатывает даже на неактивной панели — бинд успеет до показа.
/// </summary>
public class IntroBookBinder : MonoBehaviour
{
    [Header("Корень панели (сама IntroBookPanel)")]
    public GameObject root;
    [Header("Тексты")]
    public TMP_Text title;
    public TMP_Text body;
    [Header("Кнопки")]
    public Button backButton;
    public Button nextButton;
    public Button skipButton;

    void Awake()
    {
        ApplyBind();
    }

    public void ApplyBind()
    {
        GameObject r = root != null ? root : gameObject;
        IntroBookUI.Bind(r, title, body, backButton, nextButton, skipButton);
    }
}
