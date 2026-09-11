using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Маленький счётчик FPS справа вверху экрана.
/// Сам создаётся при старте игры, цепляется к персистентному Canvas,
/// переживает смены сцен. Кодовый UI (паттерн TutorialTracker/SellUI) —
/// сцену править не нужно. Клики не перехватывает (raycast выключен).
/// </summary>
public class FpsCounter : MonoBehaviour
{
    private static FpsCounter _instance;

    [Header("Вид")]
    public int fontSize = 24;
    public Color textColor = new Color(1f, 1f, 1f, 0.9f);
    public Color bgColor = new Color(0f, 0f, 0f, 0.45f);
    public Vector2 margin = new Vector2(12f, 12f);
    public Vector2 boxSize = new Vector2(130f, 40f);

    [Header("Обновление")]
    [Tooltip("Как часто обновлять цифру (сек). Реже — стабильнее показания.")]
    public float refreshInterval = 0.5f;

    private TMP_Text label;
    private float accum;
    private int frames;
    private float nextUpdate;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("FpsCounter");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<FpsCounter>();
    }

    void Awake()
    {
        // Защита от дубликатов (стандарт проекта для синглтонов)
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    void Start()
    {
        AttachToCanvas();
    }

    void Update()
    {
        if (label == null)
        {
            // Canvas мог появиться позже — пробуем подцепиться
            AttachToCanvas();
            if (label == null) return;
        }

        accum += Time.unscaledDeltaTime;
        frames++;

        if (Time.unscaledTime >= nextUpdate)
        {
            float fps = accum > 0f ? frames / accum : 0f;
            label.text = Mathf.RoundToInt(fps) + " FPS";
            accum = 0f;
            frames = 0;
            nextUpdate = Time.unscaledTime + refreshInterval;
        }
    }

    void AttachToCanvas()
    {
        Canvas canvas = FindPersistentCanvas();
        if (canvas == null) return;

        transform.SetParent(canvas.transform, false);

        RectTransform rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-margin.x, -margin.y);
        rt.sizeDelta = boxSize;

        // Фон: на Image нельзя вешать текст (два Graphic запрещено) —
        // текст только дочерним объектом «Label»
        Image bg = GetComponent<Image>();
        if (bg == null) bg = gameObject.AddComponent<Image>();
        bg.color = bgColor;
        bg.raycastTarget = false;

        Transform labelT = transform.Find("Label");
        GameObject labelGo = labelT != null ? labelT.gameObject : new GameObject("Label");
        labelGo.transform.SetParent(transform, false);

        RectTransform lrt = labelGo.GetComponent<RectTransform>();
        if (lrt == null) lrt = labelGo.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(6f, 2f);
        lrt.offsetMax = new Vector2(-6f, -2f);

        label = labelGo.GetComponent<TextMeshProUGUI>();
        if (label == null) label = labelGo.AddComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.color = textColor;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        label.text = "-- FPS";
    }

    // Ищем корневой экранный Canvas, предпочтителен персистентный «Canvas»
    static Canvas FindPersistentCanvas()
    {
        var all = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Canvas fallback = null;
        foreach (Canvas c in all)
        {
            if (c == null || !c.isRootCanvas) continue;
            if (c.renderMode == RenderMode.WorldSpace) continue;
            if (c.gameObject.name == "Canvas") return c;
            if (fallback == null) fallback = c;
        }
        return fallback;
    }
}
