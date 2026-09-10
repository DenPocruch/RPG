using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Биндер сценового трекера обучения (как конструктор: всё перетаскиванием).
/// Вешается на TutorialTracker под персистентным Canvas.
/// Поля: root = сам трекер, label = текст задачи, skipButton = крестик.
/// </summary>
public class TutorialTrackerBinder : MonoBehaviour
{
    [Header("Корень трекера (сам TutorialTracker)")]
    public GameObject root;
    [Header("Текст задачи")]
    public TMP_Text label;
    [Header("Кнопка пропустить (крестик)")]
    public Button skipButton;

    void Awake()
    {
        if (root == null) root = gameObject;
        TutorialManager.Instance.BindTracker(root, label, skipButton);
    }
}
