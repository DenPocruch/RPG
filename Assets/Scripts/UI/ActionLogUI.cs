using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Строка подсказок для игрока (мини-лог действий) поверх экрана.
/// UI строится КОДОМ — ссылки в инспекторе не нужны.
/// Сам создаётся при старте игры под первым найденным Canvas.
/// Вызов из любого места: ActionLogUI.Show("Посеяно: Пшеница");
/// </summary>
public class ActionLogUI : MonoBehaviour
{
    public static ActionLogUI Instance { get; private set; }

    [Header("Настройки")]
    [Tooltip("Сколько секунд висит сообщение после последнего")]
    public float showDuration = 3.5f;
    [Tooltip("Максимум строк в логе")]
    public int maxLines = 4;
    [Tooltip("Размер шрифта")]
    public float fontSize = 22f;

    TMP_Text label;
    CanvasGroup canvasGroup;
    readonly List<string> lines = new List<string>();
    float hideTimer;

    // ═══════════════════════════════════════════════════════════
    // АВТОСОЗДАНИЕ (без правок сцен)
    // ═══════════════════════════════════════════════════════════

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null) return; // пережил смену сцены (DontDestroyOnLoad)

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return; // UI проекта не загружен — молча выходим

        GameObject go = new GameObject("ActionLog (auto)");
        go.transform.SetParent(canvas.transform, false);
        go.AddComponent<ActionLogUI>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureBuiltUI();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ═══════════════════════════════════════════════════════════
    // ПОКАЗ СООБЩЕНИЯ
    // ═══════════════════════════════════════════════════════════

    /// <summary>Показать подсказку игроку (пишется и в консоль).</summary>
    public static void Show(string message)
    {
        Debug.Log("[Лог] " + message);
        if (Instance == null) return;
        Instance.Push(message);
    }

    void Push(string message)
    {
        lines.Add(message);
        while (lines.Count > maxLines) lines.RemoveAt(0);
        label.text = string.Join("\n", lines);
        hideTimer = showDuration;
        if (canvasGroup != null) canvasGroup.alpha = 1f;
    }

    void Update()
    {
        if (canvasGroup == null || hideTimer <= 0f) return;

        hideTimer -= Time.deltaTime;
        if (hideTimer <= 0f) canvasGroup.alpha = 0f;
        else if (hideTimer < 1f) canvasGroup.alpha = hideTimer; // плавное затухание
    }

    // ═══════════════════════════════════════════════════════════
    // ПОСТРОЕНИЕ UI КОДОМ
    // ═══════════════════════════════════════════════════════════

    void EnsureBuiltUI()
    {
        if (label != null) return;

        RectTransform rt = gameObject.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -60f);
        rt.sizeDelta = new Vector2(760f, 130f);

        canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false; // не мешает кликам по UI

        GameObject textGo = new GameObject("Label");
        textGo.transform.SetParent(transform, false);
        RectTransform textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        label = textGo.AddComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Top;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;

        label.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.85f);
        label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
    }
}
