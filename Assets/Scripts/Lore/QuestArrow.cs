using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

public class QuestArrow : MonoBehaviour
{
    public static QuestArrow Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("QuestArrow");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<QuestArrow>();
            }
            return _instance;
        }
    }
    private static QuestArrow _instance;

    private System.Func<Vector3?> getter;
    private string destScene;          // сцена цели (null = текущая)
    private System.Func<Vector3?> destGetter; // точка в целевой сцене
    private string label = "";

    private GameObject root;
    private RectTransform arrowRect;
    private RectTransform labelRect;
    private bool moveLabel; // true = кодовая подпись едет за стрелкой; сценовую двигаешь сам (дочкой стрелки)
    private Image arrowImg;
    private TMP_Text distLabel;
    private bool uiBuilt;

    // Привязка сценовой стрелки (как книга/трекер): зовёт QuestArrowBinder.
    // Нет бинда — строится кодовый треугольник как раньше.
    private static GameObject boundRoot;
    private static Image boundArrow;
    private static TMP_Text boundLabel;

    public static void Bind(GameObject rootObj, Image arrow, TMP_Text label)
    {
        boundRoot = rootObj;
        boundArrow = arrow;
        boundLabel = label;
        if (_instance != null) _instance.uiBuilt = false;
        Debug.Log("[Arrow] Bind: стрелка привязана (" + (rootObj != null ? rootObj.name : "null") + ")");
    }

    private const float DEFAULT_HIDE_DIST = 2.5f;
    private const float EDGE_MARGIN = 90f;
    private const float UI_GAP = 30f;
    private readonly Vector3[] uiCorners = new Vector3[4];

    private float worldHideDistance = DEFAULT_HIDE_DIST;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public static void SetTarget(System.Func<Vector3?> worldGetter, string caption, float hideDistance = DEFAULT_HIDE_DIST)
    {
        var q = Instance;
        RestoreUITargetColor();
        uiTarget = null;
        q.getter = worldGetter;
        q.destScene = null;
        q.destGetter = null;
        q.label = caption ?? "";
        q.worldHideDistance = Mathf.Max(0f, hideDistance);
    }

    public static void SetRoute(string scene, System.Func<Vector3?> getterInScene, string caption)
    {
        var q = Instance;
        RestoreUITargetColor();
        uiTarget = null;
        q.destScene = scene;
        q.destGetter = getterInScene;
        q.label = caption ?? "";
        q.getter = null;
        q.worldHideDistance = DEFAULT_HIDE_DIST;
    }

    private static RectTransform uiTarget;
    private static Image uiTargetImage;
    private static Color uiTargetOriginalColor;
    private static bool uiTargetColorSaved;

    public static void SetUITarget(RectTransform t, string caption)
    {
        var q = Instance;
        if (uiTarget != t)
        {
            RestoreUITargetColor();
            uiTarget = t;
        }
        q.getter = null;
        q.destScene = null;
        q.destGetter = null;
        q.label = caption ?? "";
        q.worldHideDistance = DEFAULT_HIDE_DIST;
        if (t == null)
        {
            Clear();
            return;
        }
        CaptureUITargetColor();
    }

    static void RestoreUITargetColor()
    {
        if (uiTargetImage != null && uiTargetColorSaved)
            uiTargetImage.color = uiTargetOriginalColor;
        uiTargetImage = null;
        uiTargetColorSaved = false;
    }

    static void CaptureUITargetColor()
    {
        if (uiTargetImage != null && uiTargetColorSaved) return;
        uiTargetImage = uiTarget != null ? uiTarget.GetComponent<Image>() : null;
        uiTargetColorSaved = uiTargetImage != null;
        if (uiTargetColorSaved) uiTargetOriginalColor = uiTargetImage.color;
    }

    void OnDisable()
    {
        if (_instance != this) return;
        RestoreUITargetColor();
        if (root != null) root.SetActive(false);
    }

    void OnDestroy()
    {
        if (_instance != this) return;
        RestoreUITargetColor();
        uiTarget = null;
        if (root != null) root.SetActive(false);
        _instance = null;
    }

    public static void Clear()
    {
        RestoreUITargetColor();
        uiTarget = null;
        if (_instance == null) return;
        _instance.getter = null;
        _instance.destScene = null;
        _instance.destGetter = null;
        _instance.label = "";
        _instance.worldHideDistance = DEFAULT_HIDE_DIST;
        if (_instance.root != null) _instance.root.SetActive(false);
    }

    float dbgT;
    void Dbg(string msg)
    {
        if (Time.time - dbgT < 2f) return;
        dbgT = Time.time;
        Debug.Log("[Arrow] " + msg);
    }

    void Update()
    {
        if (uiTarget != null)
        {
            if (!uiTarget.gameObject.activeInHierarchy)
            {
                RestoreUITargetColor();
                if (root != null) root.SetActive(false);
                return;
            }
            CaptureUITargetColor();
            if (uiTargetColorSaved)
            {
                float pulse = 0.125f * (1f + Mathf.Sin(Time.unscaledTime * 4f));
                Color tint = new Color(1f, 0.9f, 0.5f, uiTargetOriginalColor.a);
                uiTargetImage.color = Color.Lerp(uiTargetOriginalColor, tint, pulse);
            }
            EnsureUI(false);
            if (!uiBuilt || !PlaceUIArrow())
            {
                if (root != null) root.SetActive(false);
            }
            return;
        }
        RestoreUITargetColor();
        uiTarget = null;
        ResolveRoute();
        if (getter == null)
        {
            Dbg("спрятана: нет цели (" + label + ") " + routeDiag);
            if (root != null && root.activeSelf) root.SetActive(false);
            return;
        }
        Vector3? tw = getter();
        if (!tw.HasValue)
        {
            if (root != null && root.activeSelf) root.SetActive(false);
            return;
        }
        EnsureUI();
        if (!uiBuilt) return;

        GameObject player = GameObject.FindWithTag("Player");
        Camera cam = Camera.main;
        if (player == null || cam == null) { root.SetActive(false); return; }

        Vector3 target = tw.Value;
        float dist = Vector2.Distance(player.transform.position, target);
        if (dist < worldHideDistance)
        {
            Dbg("спрятана: близко " + dist.ToString("F1") + "м (" + label + ")");
            root.SetActive(false);
            return;
        }

        Vector3 sp = cam.WorldToScreenPoint(target);
        bool behind = sp.z < 0f;
        Vector2 scr = new Vector2(sp.x, sp.y);
        if (behind) scr = new Vector2(Screen.width - scr.x, Screen.height - scr.y);

        PlaceArrow(scr, behind, dist);
        return;
    }

    static bool TryGetCanvasCamera(RectTransform rect, out Camera camera)
    {
        camera = null;
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        if (canvas == null) return false;
        canvas = canvas.rootCanvas;
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return true;
        camera = canvas.worldCamera;
        return camera != null;
    }

    bool PlaceUIArrow()
    {
        if (root == null || arrowRect == null || uiTarget == null) return false;
        RectTransform parent = arrowRect.parent as RectTransform;
        if (parent == null || !TryGetCanvasCamera(uiTarget, out Camera targetCamera)
            || !TryGetCanvasCamera(arrowRect, out Camera arrowCamera)) return false;

        if (!root.activeSelf) root.SetActive(true);
        uiTarget.GetWorldCorners(uiCorners);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < uiCorners.Length; i++)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(targetCamera, uiCorners[i]);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parent, screen, arrowCamera, out Vector2 point)) return false;
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        Vector2 targetCenter = (min + max) * 0.5f;
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parent, screenCenter, arrowCamera, out Vector2 viewCenter)) return false;
        bool above = targetCenter.y <= viewCenter.y;
        float halfHeight = arrowRect.rect.height * 0.5f; // без учёта пульсации масштаба (иначе позиция дрожит)
        Vector2 position = new Vector2(targetCenter.x,
            above ? max.y + UI_GAP + halfHeight : min.y - UI_GAP - halfHeight);
        arrowRect.localRotation = Quaternion.Euler(0f, 0f, above ? 180f : 0f);
        Vector3 pivotOffset = arrowRect.localRotation * Vector3.Scale(
            (Vector3)arrowRect.rect.center, arrowRect.localScale);
        arrowRect.localPosition = (Vector3)position - pivotOffset;
        // Пульсация как у мировой стрелки — UI-цель должно быть видно
        float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f);
        arrowRect.localScale = Vector3.one * pulse;
        if (distLabel != null) distLabel.text = label;
        return true;
    }

    void PlaceArrow(Vector2 scr, bool behind, float dist)
    {
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 dir = scr - center;
        if (dir.sqrMagnitude < 1f) dir = Vector2.up;
        dir.Normalize();

        float maxX = Screen.width * 0.5f - EDGE_MARGIN;
        float maxY = Screen.height * 0.5f - EDGE_MARGIN;
        float k = Mathf.Min(maxX / Mathf.Max(1f, Mathf.Abs(dir.x)),
                            maxY / Mathf.Max(1f, Mathf.Abs(dir.y)));
        Vector2 pos = center + dir * k * 0.85f;

        if (!root.activeSelf) root.SetActive(true);
        Canvas canvas = root.GetComponentInParent<Canvas>();
        float scale = 1f;
        var scaler = canvas != null ? canvas.GetComponent<CanvasScaler>() : null;
        if (scaler != null && scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            scale = Screen.width / scaler.referenceResolution.x;

        RectTransform canvasRt = canvas != null ? canvas.transform as RectTransform : null;
        Vector2 anchored = pos;
        if (canvasRt != null)
            anchored = new Vector2(pos.x / scale - canvasRt.rect.width * 0.5f,
                                   pos.y / scale - canvasRt.rect.height * 0.5f);
        arrowRect.anchoredPosition = anchored;
        float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
        arrowRect.localRotation = Quaternion.Euler(0f, 0f, ang);

        Dbg("показ: " + label + (dist >= 0f ? " dist=" + dist.ToString("F1") : " (UI)") + " scr=" + pos + " behind=" + behind);
        if (moveLabel && labelRect != null) labelRect.anchoredPosition = anchored + new Vector2(0f, -56f);
        float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 5f);
        arrowRect.localScale = Vector3.one * pulse;
        if (distLabel != null)
        {
            if (dist < 0f) distLabel.text = label;
            else distLabel.text = string.IsNullOrEmpty(label) ? ((int)dist + "м")
                : (label + " · " + (int)dist + "м");
        }
    }

    string routeDiag = "";

    // Если цель в другой сцене — ведём к порталу туда (хаб — City).
    // Маршрут без конечной точки (просто «иди в сцену») тоже ищет портал.
    void ResolveRoute()
    {
        if (string.IsNullOrEmpty(destScene)) return;
        string cur = SceneManager.GetActiveScene().name;
        if (cur == destScene)
        {
            getter = destGetter; // null = пришли, конечной точки нет — спрячемся
            destScene = null;
            destGetter = null;
            return;
        }
        var all = FindObjectsByType<SceneTransition>(FindObjectsSortMode.None);
        int n = 0;
        Vector3? portal = null;
        foreach (var t in all)
        {
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            n++;
            if (!portal.HasValue && t.targetScene == destScene) portal = t.transform.position;
        }
        if (!portal.HasValue)
        {
            foreach (var t in all) // через хаб
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                if (t.targetScene == "City") { portal = t.transform.position; break; }
            }
        }
        if (!portal.HasValue && n > 0)
        {
            foreach (var t in all) // любой портал
            {
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                portal = t.transform.position;
                break;
            }
        }
        routeDiag = cur + ": порталов " + n + (portal.HasValue ? "" : " (нужного нет)");
        Vector3? p = portal;
        getter = () => p;
    }

    Vector3? FindPortalTo(string scene)
    {
        var all = FindObjectsByType<SceneTransition>(FindObjectsSortMode.None);
        foreach (var t in all)
        {
            if (t == null) continue;
            if (t.targetScene == scene) return t.transform.position;
        }
        return null;
    }

    Vector3? FindAnyPortal()
    {
        var all = FindObjectsByType<SceneTransition>(FindObjectsSortMode.None);
        foreach (var t in all)
            if (t != null) return t.transform.position;
        return null;
    }

    void EnsureUI(bool allowFallback = true)
    {
        if (root == null || arrowRect == null) uiBuilt = false;
        // Сценовая стрелка привязана — используем её
        if (boundRoot == null)
        {
            var found = FindObjectsByType<QuestArrowBinder>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (found != null && found.Length > 0 && found[0] != null)
                found[0].ApplyBind();
        }
        if (boundRoot != null)
        {
            root = boundRoot;
            arrowImg = boundArrow;
            arrowRect = boundArrow != null ? boundArrow.transform as RectTransform : null;
            distLabel = boundLabel;
            labelRect = boundLabel != null ? boundLabel.transform as RectTransform : null;
            moveLabel = false;
            uiBuilt = arrowRect != null;
            if (uiBuilt) return;
        }
        if (!allowFallback)
        {
            uiBuilt = root != null && arrowRect != null;
            return;
        }
        if (uiBuilt) return;
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        root = new GameObject("QuestArrow (auto)");
        root.transform.SetParent(canvas.transform, false);
        RectTransform rrt = root.AddComponent<RectTransform>();
        // Корень — на весь экран: дети с якорем 0.5 считают от центра экрана
        rrt.anchorMin = Vector2.zero;
        rrt.anchorMax = Vector2.one;
        rrt.pivot = new Vector2(0.5f, 0.5f);
        rrt.offsetMin = Vector2.zero;
        rrt.offsetMax = Vector2.zero;

        GameObject aGo = new GameObject("Arrow");
        aGo.transform.SetParent(root.transform, false);
        arrowRect = aGo.AddComponent<RectTransform>();
        arrowRect.anchorMin = new Vector2(0.5f, 0.5f);
        arrowRect.anchorMax = new Vector2(0.5f, 0.5f);
        arrowRect.pivot = new Vector2(0.5f, 0.5f);
        arrowRect.anchoredPosition = Vector2.zero;
        arrowRect.sizeDelta = new Vector2(64f, 64f);
        arrowImg = aGo.AddComponent<Image>();
        arrowImg.sprite = MakeTriangleSprite();
        arrowImg.color = new Color(1f, 0.85f, 0.2f, 0.95f);

        GameObject lGo = new GameObject("Label");
        lGo.transform.SetParent(root.transform, false);
        RectTransform lrt = lGo.AddComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0.5f, 0.5f);
        lrt.anchorMax = new Vector2(0.5f, 0.5f);
        lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0f, -40f);
        lrt.sizeDelta = new Vector2(300f, 30f);
        distLabel = lGo.AddComponent<TextMeshProUGUI>();
        distLabel.fontSize = 22f;
        distLabel.alignment = TextAlignmentOptions.Center;
        distLabel.color = new Color(1f, 0.95f, 0.7f);
        labelRect = lrt;
        moveLabel = true;

        root.SetActive(false);
        uiBuilt = true;
    }

    // Жёлтый треугольник кодом (своего спрайта стрелки в проекте нет)
    static Sprite triangleSprite;
    static Sprite MakeTriangleSprite()
    {
        if (triangleSprite != null) return triangleSprite;
        int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.ARGB32, false);
        tex.filterMode = FilterMode.Point;
        Color clear = new Color(0f, 0f, 0f, 0f);
        Color fill = Color.white;
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                // Треугольник остриём вверх: |x - S/2| <= (S - y) * 0.45
                float half = (S - y) * 0.45f;
                tex.SetPixel(x, y, Mathf.Abs(x - S * 0.5f) <= half ? fill : clear);
            }
        tex.Apply();
        triangleSprite = Sprite.Create(tex, new Rect(0f, 0f, S, S), new Vector2(0.5f, 0.5f), 100f);
        return triangleSprite;
    }
}
