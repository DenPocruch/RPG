using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Показывает диалоговое окно (портрет + имя + текст + кнопки ответов).
/// Ведёт по дереву DialogueData. Варианты с действием шлют событие
/// onDialogueAction — NPC (повар) подписывается и реагирует.
/// Эффект печатной машинки; тап по тексту — мгновенно показать реплику.
/// </summary>
public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("UI")]
    public GameObject dialoguePanel;
    public Image portraitImage;
    public TMP_Text nameText;
    public TMP_Text bodyText;

    [Header("Кнопки ответов")]
    public GameObject optionButtonPrefab; // Button + TMP_Text внутри
    public Transform optionsContainer;

    [Header("Печатная машинка")]
    public bool useTypewriter = true;
    public float charsPerSecond = 40f;

    [Header("Кнопка «дальше» / область тапа для пропуска печати")]
    public Button advanceButton; // невидимая кнопка на весь экран текста (опц.)

    // Событие действия: (тип, параметр). NPC подписывается сюда.
    public System.Action<DialogueActionType, string> onDialogueAction;
    // Событие окончания диалога.
    public System.Action onDialogueEnd;

    /// <summary>С каким NPC сейчас говорят (обработчики действий сверяются,
    /// чтобы реакция сработала только у нужного торговца/NPC).</summary>
    public NPCInteractable currentNPC { get; set; }

    private DialogueData current;
    private DialogueNode currentNode;
    private bool isTyping = false;
    private string fullText = "";
    private Coroutine typeCo;
    private List<GameObject> spawnedButtons = new List<GameObject>();

    // Условия показа вариантов (NPC может выставлять теги)
    private HashSet<string> activeConditions = new HashSet<string>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (dialoguePanel != null) dialoguePanel.SetActive(false);

        if (advanceButton != null)
            advanceButton.onClick.AddListener(OnAdvanceClicked);
    }

    public bool IsOpen => dialoguePanel != null && dialoguePanel.activeSelf;

    // ═══════════════════════════════════════════════════════════
    // УСЛОВИЯ (NPC выставляет какие варианты доступны)
    // ═══════════════════════════════════════════════════════════
    public void SetCondition(string tag, bool on)
    {
        if (string.IsNullOrEmpty(tag)) return;
        if (on) activeConditions.Add(tag);
        else activeConditions.Remove(tag);
    }

    public void ClearConditions() => activeConditions.Clear();

    // ═══════════════════════════════════════════════════════════
    // ЗАПУСК
    // ═══════════════════════════════════════════════════════════
    public void StartDialogue(DialogueData data)
    {
        if (data == null) return;
        current = data;

        // Блокируем движение игрока пока говорим
        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        if (dialoguePanel != null) dialoguePanel.SetActive(true);

        if (nameText != null) nameText.text = data.npcName;
        if (portraitImage != null && data.portrait != null)
            portraitImage.sprite = data.portrait;

        ShowNode(data.GetStartNode());
    }

    void ShowNode(DialogueNode node)
    {
        if (node == null) { EndDialogue(); return; }
        currentNode = node;

        // Портрет для конкретной реплики (если задан)
        if (portraitImage != null && node.portraitOverride != null)
            portraitImage.sprite = node.portraitOverride;
        else if (portraitImage != null && current.portrait != null)
            portraitImage.sprite = current.portrait;

        ClearOptions();

        fullText = node.text;
        if (useTypewriter)
        {
            if (typeCo != null) StopCoroutine(typeCo);
            typeCo = StartCoroutine(TypeText(node.text));
        }
        else
        {
            if (bodyText != null) bodyText.text = node.text;
            BuildOptions(node);
        }
    }

    IEnumerator TypeText(string text)
    {
        isTyping = true;
        if (bodyText != null) bodyText.text = "";

        float delay = 1f / Mathf.Max(1f, charsPerSecond);
        for (int i = 0; i < text.Length; i++)
        {
            if (bodyText != null) bodyText.text += text[i];
            yield return new WaitForSeconds(delay);
        }

        isTyping = false;
        BuildOptions(currentNode);
    }

    // Тап по области текста: если печатается — показать сразу; иначе игнор
    void OnAdvanceClicked()
    {
        if (isTyping)
        {
            if (typeCo != null) StopCoroutine(typeCo);
            if (bodyText != null) bodyText.text = fullText;
            isTyping = false;
            BuildOptions(currentNode);
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ВАРИАНТЫ ОТВЕТА
    // ═══════════════════════════════════════════════════════════
    void BuildOptions(DialogueNode node)
    {
        ClearOptions();
        if (optionButtonPrefab == null || optionsContainer == null) return;

        // Нет вариантов → одна кнопка "Закрыть"
        if (node.options == null || node.options.Length == 0)
        {
            SpawnButton("Закрыть", () => EndDialogue());
            return;
        }

        foreach (DialogueOption opt in node.options)
        {
            // Фильтр по условию
            if (!string.IsNullOrEmpty(opt.conditionTag) &&
                !activeConditions.Contains(opt.conditionTag))
                continue;

            DialogueOption captured = opt; // замыкание
            SpawnButton(opt.text, () => OnOptionSelected(captured));
        }
    }

    void SpawnButton(string text, UnityEngine.Events.UnityAction onClick)
    {
        GameObject obj = Instantiate(optionButtonPrefab, optionsContainer);
        obj.transform.localScale = Vector3.one;

        TMP_Text label = obj.GetComponentInChildren<TMP_Text>();
        if (label != null) label.text = text;

        Button btn = obj.GetComponent<Button>();
        if (btn != null) btn.onClick.AddListener(onClick);

        spawnedButtons.Add(obj);
    }

    void ClearOptions()
    {
        foreach (GameObject b in spawnedButtons)
            if (b != null) Destroy(b);
        spawnedButtons.Clear();
    }

    void OnOptionSelected(DialogueOption opt)
    {
        if (opt.nextNodeId < 0)
        {
            // Вариант завершает диалог: СНАЧАЛА закрываем окно, ПОТОМ действие
            // (иначе панель готовки открывается поверх ещё висящего диалога,
            // а повар начинает двигаться до закрытия окна)
            EndDialogue();
            if (opt.action != DialogueActionType.None)
                onDialogueAction?.Invoke(opt.action, opt.actionParam);
        }
        else
        {
            // Переход к следующей реплике: действие можно сразу
            if (opt.action != DialogueActionType.None)
                onDialogueAction?.Invoke(opt.action, opt.actionParam);
            ShowNode(current.GetNode(opt.nextNodeId));
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ЗАВЕРШЕНИЕ
    // ═══════════════════════════════════════════════════════════
    public void EndDialogue()
    {
        ClearOptions();
        if (dialoguePanel != null) dialoguePanel.SetActive(false);

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;

        onDialogueEnd?.Invoke();
        current = null;
        currentNode = null;
    }
}