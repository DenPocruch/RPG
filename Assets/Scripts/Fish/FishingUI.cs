using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Мини-игра вываживания: держишь экран — зелёная зона ползёт вверх,
/// отпустил — падает. Держи рыбу в зоне: прогресс растёт, иначе тает.
/// Строится кодом под Canvas (паттерн SellUI/FeedUI).
/// </summary>
public class FishingUI : MonoBehaviour
{
    public static FishingUI Instance
    {
        get
        {
            if (_instance == null)
            {
                Canvas canvas = GameObject.Find("Canvas") != null
                    ? GameObject.Find("Canvas").GetComponent<Canvas>()
                    : FindFirstObjectByType<Canvas>();
                if (canvas == null) return null;
                var go = new GameObject("FishingUI");
                go.transform.SetParent(canvas.transform, false);
                _instance = go.AddComponent<FishingUI>();
            }
            return _instance;
        }
    }
    private static FishingUI _instance;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        BuildUI();
        root.SetActive(false);
    }

    private GameObject root;
    private TMP_Text titleText;
    private RectTransform trackRect;
    private RectTransform zoneRect;
    private RectTransform fishRect;
    private Image fishImage;
    private RectTransform progressFill;
    private float trackH = 380f;

    private bool active;
    private float zoneW;   // ширина зоны (доля)
    private float gain;    // прогресс/сек
    private float decay;   // убыль/сек
    private float zoneY;   // центр зоны 0-1
    private float fishY;
    private float fishTarget;
    private float fishSpeed;
    private float fishTimer;
    private float progress;

    const float FISH_H = 0.07f; // высота маркера рыбы (доля шкалы)

    public bool IsOpen() => active;

    public void Open(FishData fish, float zone, float gainRate, float decayRate)
    {
        if (root == null) BuildUI();
        zoneW = Mathf.Clamp01(zone);
        gain = gainRate;
        decay = decayRate;
        zoneY = 0.3f;
        fishY = 0.7f;
        progress = 0.25f;
        fishSpeed = 0.35f + (fish != null ? fish.difficulty : 0) * 0.4f;
        fishTarget = fishY;
        fishTimer = 0f;

        if (titleText != null) titleText.text = fish != null ? fish.fishName : "???";
        if (fishImage != null && fish != null && fish.icon != null) fishImage.sprite = fish.icon;
        Layout();
        root.SetActive(true);
        active = true;
    }

    public void Close()
    {
        active = false;
        if (root != null) root.SetActive(false);
    }

    void Update()
    {
        if (!active) return;

        bool hold = Input.GetMouseButton(0) || Input.touchCount > 0;

        // Зона: держишь — вверх, иначе падает
        zoneY += (hold ? 1.1f : -0.9f) * Time.deltaTime;
        zoneY = Mathf.Clamp(zoneY, zoneW / 2f, 1f - zoneW / 2f);

        // Рыба: дёргается к случайным точкам
        fishTimer -= Time.deltaTime;
        if (fishTimer <= 0f || Mathf.Abs(fishY - fishTarget) < 0.02f)
        {
            fishTarget = Random.Range(0f, 1f);
            fishTimer = Random.Range(0.4f, 1.2f);
        }
        float dir = Mathf.Sign(fishTarget - fishY);
        fishY += dir * fishSpeed * Time.deltaTime;
        fishY = Mathf.Clamp01(fishY);

        // Прогресс
        bool inside = Mathf.Abs(fishY - zoneY) < (zoneW + FISH_H) / 2f;
        progress += (inside ? gain : -decay) * Time.deltaTime;

        if (progress >= 1f)
        {
            Close();
            FishingController.Instance?.FinishMinigame(true);
            return;
        }
        if (progress <= 0f)
        {
            Close();
            FishingController.Instance?.FinishMinigame(false);
            return;
        }

        Layout();
    }

    void Layout()
    {
        if (zoneRect != null)
        {
            zoneRect.sizeDelta = new Vector2(zoneRect.sizeDelta.x, zoneW * trackH);
            zoneRect.anchoredPosition = new Vector2(0, (zoneY - 0.5f) * trackH);
        }
        if (fishRect != null)
            fishRect.anchoredPosition = new Vector2(0, (fishY - 0.5f) * trackH);
        if (progressFill != null)
            progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
    }

    // ═══════════════════════════════════════════════════════════
    // ПОСТРОЙКА
    // ═══════════════════════════════════════════════════════════
    void BuildUI()
    {
        root = new GameObject("FishingPanel");
        root.transform.SetParent(transform, false);
        var rootRt = root.AddComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.sizeDelta = new Vector2(420f, 560f);
        rootRt.anchoredPosition = Vector2.zero;

        var bg = root.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.85f);

        titleText = MakeText(root.transform, "Title", 34, new Vector2(0, 240), new Vector2(400, 60));

        GameObject track = new GameObject("Track");
        track.transform.SetParent(root.transform, false);
        trackRect = track.AddComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0.5f, 0.5f);
        trackRect.anchorMax = new Vector2(0.5f, 0.5f);
        trackRect.sizeDelta = new Vector2(70f, trackH);
        trackRect.anchoredPosition = new Vector2(-80f, 0);
        var trackImg = track.AddComponent<Image>();
        trackImg.color = new Color(0.1f, 0.15f, 0.3f, 1f);

        GameObject zone = new GameObject("Zone");
        zone.transform.SetParent(track.transform, false);
        zoneRect = zone.AddComponent<RectTransform>();
        zoneRect.anchorMin = new Vector2(0.5f, 0.5f);
        zoneRect.anchorMax = new Vector2(0.5f, 0.5f);
        zoneRect.sizeDelta = new Vector2(70f, 100f);
        var zoneImg = zone.AddComponent<Image>();
        zoneImg.color = new Color(0.2f, 0.9f, 0.3f, 0.6f);

        GameObject fish = new GameObject("Fish");
        fish.transform.SetParent(track.transform, false);
        fishRect = fish.AddComponent<RectTransform>();
        fishRect.anchorMin = new Vector2(0.5f, 0.5f);
        fishRect.anchorMax = new Vector2(0.5f, 0.5f);
        fishRect.sizeDelta = new Vector2(56f, 30f);
        fishImage = fish.AddComponent<Image>();
        fishImage.color = Color.white;

        GameObject barBg = new GameObject("ProgressBg");
        barBg.transform.SetParent(root.transform, false);
        var barBgRt = barBg.AddComponent<RectTransform>();
        barBgRt.anchorMin = new Vector2(0.5f, 0.5f);
        barBgRt.anchorMax = new Vector2(0.5f, 0.5f);
        barBgRt.sizeDelta = new Vector2(320f, 26f);
        barBgRt.anchoredPosition = new Vector2(0, -225f);
        var barBgImg = barBg.AddComponent<Image>();
        barBgImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);

        GameObject fill = new GameObject("ProgressFill");
        fill.transform.SetParent(barBg.transform, false);
        progressFill = fill.AddComponent<RectTransform>();
        progressFill.anchorMin = Vector2.zero;
        progressFill.anchorMax = new Vector2(0.25f, 1f);
        progressFill.offsetMin = Vector2.zero;
        progressFill.offsetMax = Vector2.zero;
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = new Color(0.3f, 0.9f, 0.4f, 1f);

        MakeText(root.transform, "Hint", 24, new Vector2(0, -265f), new Vector2(400, 40))
            .text = "Держи экран!";
    }

    TMP_Text MakeText(Transform parent, string name, int size, Vector2 pos, Vector2 dims)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = dims;
        rt.anchoredPosition = pos;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        return tmp;
    }
}
