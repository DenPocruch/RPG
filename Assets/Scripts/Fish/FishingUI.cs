using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Мини-игра ВЫВАЖИВАНИЯ (по мотивам «Рыбного места»): детерминированная шкала
/// натяжения 0..1 + фиксированная дистанция (100м). Держишь экран — шкала
/// растёт, рыба ближе (1с держания = 20м, итого 5с чистого держания на любую
/// рыбу). Отпустил — шкала падает, дистанция стоит. Никаких авто-рывков:
/// шкала отвечает только на палец, но скорость отклика зависит от НАГРУЗКИ
/// (вес рыбы / лимит удочки): мелочь (200г) — шкала ползёт, зажал на 5с
/// и вытащил; рыба под лимит — шкала ракета, только короткие тапы, каждый
/// даёт чуть дистанции (тапы 0.2с + 0.3с копятся в те же 5с). Удочка выше
/// тиром ту же рыбу держит легче (шкала медленнее), время поимки то же.
/// Перевес (рыба тяжелее лимита удочки: 2/5/10/20/50/100кг по тирам 1-6) —
/// леска рвётся от любого тапа. Края = МГНОВЕННЫЙ проигрыш:
/// 0 — слабина, рыба сошла; 1 — перетяг, леска лопнула.
/// Цифры — в FishingTuning. Строится кодом под Canvas (паттерн SellUI/FeedUI),
/// иерархия со стабильными именами:
/// FishingPanel/TensionTrack/{ZoneRedBottom,ZoneGreen,ZoneYellow,ZoneRed,TensionFill},
/// ProgressBg/ProgressFill, Title, Hint, SnapAlarm, FishIcon.
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

    // ── Режим сценовой панели (нарисованной руками) ──
    private bool sceneMode;
    private GameObject panelRoot;
    private Image tensionImage;
    private int fillMode; // 0=Filled(fillAmount), 1=Anchored(anchorMax.x), 2=Width(sizeDelta.x)
    private float fillSpan = 1f;
    private float fillMinX;
    private float fillFullW;
    private Color fillBaseColor = Color.white;
    private RectTransform marker;
    private RectTransform markerTrack;
    private float markerY;
    private float markerLeft;
    private float markerRight;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        if (TryBindScene())
        {
            panelRoot.SetActive(false);
        }
        else
        {
            BuildUI();
            root.SetActive(false);
        }
    }

    /// <summary>Ищем нарисованную панель под Canvas. true — играем на ней, кодовая не строится.</summary>
    private bool TryBindScene()
    {
        sceneMode = false;
        FishingTuning t = FishingTuning.Instance;
        Canvas canvas = GameObject.Find("Canvas") != null
            ? GameObject.Find("Canvas").GetComponent<Canvas>()
            : FindFirstObjectByType<Canvas>();
        if (canvas == null || t == null) return false;

        Transform prvot = canvas.transform.Find(t.panelRootName);
        GameObject prv = prvot != null ? prvot.gameObject : null;
        if (prv == null)
        {
            // запасной поиск вглубь (если панель вложена)
            Transform deep = FindDeep(canvas.transform, t.panelRootName);
            if (deep != null) prv = deep.gameObject;
        }
        if (prv == null) return false;

        Transform fillT = FindDeep(prv.transform, t.tensionFillName);
        Transform markT = FindDeep(prv.transform, t.progressMarkerName);
        if (fillT == null || markT == null)
        {
            Debug.LogWarning("[Рыбалка] Панель '" + t.panelRootName + "' найдена, но внутри нет '"
                + t.tensionFillName + "'/" + t.progressMarkerName + "' — строю кодовую.");
            return false;
        }

        Image img = fillT.GetComponent<Image>();
        if (img == null)
        {
            Debug.LogWarning("[Рыбалка] У '" + t.tensionFillName + "' нет Image — строю кодовую.");
            return false;
        }
        RectTransform mrt = markT as RectTransform;
        if (mrt == null) return false;

        // Режим заливки полоски натяжения
        RectTransform frt = fillT as RectTransform;
        if (img.type == Image.Type.Filled) fillMode = 0;
        else if (frt != null && frt.anchorMax.x - frt.anchorMin.x > 0.001f) fillMode = 1;
        else fillMode = 2;
        if (frt != null)
        {
            fillMinX = frt.anchorMin.x;
            fillSpan = Mathf.Max(0.001f, frt.anchorMax.x - frt.anchorMin.x);
            fillFullW = frt.sizeDelta.x;
        }
        fillBaseColor = img.color;

        // Трек маркера
        Transform trackT = !string.IsNullOrEmpty(t.progressTrackName)
            ? FindDeep(prv.transform, t.progressTrackName) : null;
        markerTrack = (trackT as RectTransform) ?? (mrt.parent as RectTransform);
        marker = mrt;
        markerY = mrt.anchoredPosition.y;
        if (markerTrack != null)
        {
            float w = markerTrack.rect.width;
            markerLeft = -w / 2f + t.markerInsetLeft;
            markerRight = w / 2f - t.markerInsetRight;
        }

        panelRoot = prv;
        tensionImage = img;
        sceneMode = true;
        Debug.Log("[Рыбалка] Панель сцены: fill='" + t.tensionFillName + "' mode=" + fillMode
            + ", marker='" + t.progressMarkerName + "' trackW="
            + (markerTrack != null ? markerTrack.rect.width.ToString("F0") : "?"));
        return true;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        foreach (Transform c in root.GetComponentsInChildren<Transform>(true))
            if (c.name == name) return c;
        return null;
    }

    private GameObject root;
    private TMP_Text titleText;
    private TMP_Text hintText;
    private TMP_Text snapText;
    private RectTransform tensionFill;
    private RectTransform zoneRedBottom;
    private RectTransform zoneGreen;
    private RectTransform zoneYellow;
    private RectTransform zoneRed;
    private Image fishImage;
    private RectTransform progressFill;
    private Image snapFlash;

    private bool active;
    private float tension;    // 0-1 шкала (0 слабина/сход, 1 перетяг/обрыв)
    private float progress;   // 0-1 дистанция пройдена (только вперёд)
    private float fightTimer; // длительность боя
    private float escapeTop;  // 0..escapeTop — красная зона слабины (визуал)
    private float greenTop;
    private float redStart;   // redStart..1 — красная зона перетяга (визуал)
    private float riseRate;   // рост шкалы пока держишь (по нагрузке вес/лимит)
    private float fallRate;   // спад шкалы когда отпустил (один на всех)
    private float reelRate;   // скорость дистанции пока держишь (20м/с → 5с на рыбу)
    private float load;       // нагрузка 0..1 (вес рыбы / лимит удочки)
    private float rodLimitKg; // лимит текущей удочки (кг)
    private float fishWeightKg; // вес текущей рыбы (кг)
    private bool overweight;  // рыба тяжелее лимита — обрыв от любого тапа
    private float distTotal;  // дистанция боя в метрах (для показа)
    private string fishTitle; // имя рыбы для заголовка
    private float logTimer;

    public bool IsOpen() => active;

    public void Open(FishData fish, ItemData rod, float weightKg)
    {
        FishingTuning t = FishingTuning.Instance;
        // Панель могли уничтожить после привязки (переход сцен при неперсистентном
        // Canvas) — тогда перепривязываемся или падаем на кодовую. Без этого
        // SetActive на мёртвом объекте роняет весь Hook (панель не открывается,
        // состояние виснет в Minigame, игрок заморожен).
        if (sceneMode && panelRoot == null) sceneMode = false;
        if (!sceneMode && !TryBindScene() && root == null) BuildUI();

        float rodZone = rod != null ? rod.fishingZoneBonus : 0f;

        // Безопасная зона расширяется с обеих сторон (бонус удочки)
        escapeTop = Mathf.Clamp01(t.escapeTop - (t.rodWidensSafeZone ? rodZone : 0f));
        greenTop = Mathf.Clamp01(t.greenTop + (t.rodWidensSafeZone ? rodZone : 0f));
        redStart = Mathf.Clamp01(t.redStart + (t.rodWidensSafeZone ? rodZone : 0f));
        if (greenTop < escapeTop + 0.05f) greenTop = escapeTop + 0.05f;
        if (redStart < greenTop + 0.05f) redStart = greenTop + 0.05f;

        // НАГРУЗКА: вес рыбы / лимит удочки (2/5/10/20/50/100кг по тирам 1-6).
        // 200г на дереве (лимит 2кг): load=0.1 — шкала ползёт, жми 5с подряд.
        // Рыба под лимит: load~1 — шкала ракета (тап 0.2с ≈ +0.22), только тапы.
        // Та же рыба удочкой выше тиром: лимит больше → load меньше → шкала
        // медленнее, а время поимки то же (5с чистого держания).
        int rodTier = rod != null ? Mathf.Max(1, rod.toolTier) : 1;
        int difficulty = fish != null ? fish.difficulty : 0;
        rodLimitKg = t.RodLimitKg(rodTier);
        fishWeightKg = Mathf.Max(0f, weightKg);
        overweight = fishWeightKg > rodLimitKg;
        load = Mathf.Clamp01(fishWeightKg / rodLimitKg);

        // Шкала: нагрузка по кривой (мелочь почти не тянет, под лимитом ракета),
        // редкость сверху небольшим бонусом. Откат один на всех — паузы между тапами.
        riseRate = Mathf.Max(t.holdRiseMin,
            t.holdRiseAtLimit * Mathf.Pow(load, Mathf.Max(0.5f, t.riseCurve))
            * (1f + difficulty * t.diffRiseBonus));
        fallRate = Mathf.Max(0.05f, t.relaxFall);
        // Дистанция одна на всех: метры за секунду держания (20м/с из 100м = 5с).
        distTotal = Mathf.Max(1f, t.distanceMeters);
        reelRate = Mathf.Max(0.005f, t.metersPerSecond / distTotal);
        fishTitle = fish != null ? fish.fishName : "???";

        tension = 0.35f;
        progress = 0f;
        fightTimer = 0f;

        if (sceneMode)
        {
            panelRoot.SetActive(true);
        }
        else
        {
            if (fishImage != null)
            {
                if (fish != null && fish.icon != null) { fishImage.sprite = fish.icon; fishImage.enabled = true; }
                else fishImage.enabled = false;
            }
            root.SetActive(true);
        }
        if (t.debugLog)
            Debug.Log("[FishDbg] open '" + fishTitle + "' " + fishWeightKg.ToString("0.##")
                + "кг / лимит " + rodLimitKg.ToString("0.##") + "кг load=" + load.ToString("0.00")
                + (overweight ? " ПЕРЕВЕС" : "")
                + " rise=" + riseRate.ToString("0.00") + " reel=" + reelRate.ToString("0.000"));
        Layout(t);
        active = true;
    }

    public void Close()
    {
        active = false;
        if (sceneMode)
        {
            if (panelRoot != null) panelRoot.SetActive(false);
            if (tensionImage != null) tensionImage.color = fillBaseColor;
        }
        else if (root != null) root.SetActive(false);
    }

    void Update()
    {
        if (!active) return;
        FishingTuning t = FishingTuning.Instance;

        bool hold = Input.GetMouseButton(0) || Input.touchCount > 0;
        fightTimer += Time.deltaTime;

        // Перевес: рыба тяжелее лимита удочки — леска рвётся от любого тапа
        if (overweight && hold)
        {
            Close();
            PlayClip(t.snapClip);
            if (t.vibrateOnSnap) Vibrate();
            FishingController.Instance?.FinishMinigame(false, "Леска лопнула! Рыба ("
                + FishData.FormatWeight(fishWeightKg) + ") тяжелее лимита удочки ("
                + FishData.FormatWeight(rodLimitKg) + ")!");
            return;
        }

        // Шкала отвечает ТОЛЬКО на палец: держишь — вверх, отпустил — вниз.
        // Скорость — по силе рыбы (мелочь вялая, монстр резкий). Линейно, без рывков.
        tension += (hold ? riseRate : -fallRate) * Time.deltaTime;

        // Дистанция тает ТОЛЬКО пока держишь — ровно, без замедлений по зонам.
        // В красной зоне тянуть тоже можно (дистанция идёт), но рядом обрыв —
        // в этом риск. Держать вечно нельзя: шкала уйдёт в 1 и леска лопнет.
        if (hold) progress += reelRate * Time.deltaTime;

        // Края — мгновенный проигрыш
        if (tension <= 0f)
        {
            Close();
            PlayClip(t.snapClip);
            if (t.vibrateOnSnap) Vibrate();
            FishingController.Instance?.FinishMinigame(false, "Слабина — рыба сошла!");
            return;
        }
        if (tension >= 1f)
        {
            Close();
            PlayClip(t.snapClip);
            if (t.vibrateOnSnap) Vibrate();
            FishingController.Instance?.FinishMinigame(false, "Леска лопнула!");
            return;
        }
        tension = Mathf.Clamp01(tension);

        if (progress >= 1f)
        {
            Close();
            PlayClip(t.catchClip);
            FishingController.Instance?.FinishMinigame(true);
            return;
        }
        if (t.fightTimeout > 0f && fightTimer >= t.fightTimeout)
        {
            Close();
            FishingController.Instance?.FinishMinigame(false, "Долго возишься — рыба ушла!");
            return;
        }

        if (t.debugLog)
        {
            logTimer -= Time.deltaTime;
            if (logTimer <= 0f)
            {
                logTimer = 0.5f;
                Debug.Log("[FishDbg] t=" + fightTimer.ToString("0.0")
                    + " ten=" + tension.ToString("0.00")
                    + " hold=" + (hold ? 1 : 0)
                    + " prog=" + progress.ToString("0.00"));
            }
        }

        Layout(t);
    }

    private void Vibrate()
    {
        try { Handheld.Vibrate(); } catch { }
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip == null) return;
        Camera cam = Camera.main;
        Vector3 pos = cam != null ? cam.transform.position : Vector3.zero;
        AudioSource.PlayClipAtPoint(clip, pos);
    }

    void Layout(FishingTuning t)
    {
        if (sceneMode) { LayoutScene(t); return; }
        float trackH = t.trackSize.y;
        // Зоны от низа трека: красная слабина → зелень → жёлтая → красная перетяга
        if (zoneRedBottom != null)
        {
            zoneRedBottom.sizeDelta = new Vector2(zoneRedBottom.sizeDelta.x, escapeTop * trackH);
            zoneRedBottom.anchoredPosition = new Vector2(0, -trackH / 2f + escapeTop * trackH / 2f);
        }
        if (zoneGreen != null)
        {
            float h = (greenTop - escapeTop) * trackH;
            zoneGreen.sizeDelta = new Vector2(zoneGreen.sizeDelta.x, h);
            zoneGreen.anchoredPosition = new Vector2(0, -trackH / 2f + escapeTop * trackH + h / 2f);
        }
        if (zoneYellow != null)
        {
            float h = (redStart - greenTop) * trackH;
            zoneYellow.sizeDelta = new Vector2(zoneYellow.sizeDelta.x, h);
            zoneYellow.anchoredPosition = new Vector2(0, -trackH / 2f + greenTop * trackH + h / 2f);
        }
        if (zoneRed != null)
        {
            float h = (1f - redStart) * trackH;
            zoneRed.sizeDelta = new Vector2(zoneRed.sizeDelta.x, h);
            zoneRed.anchoredPosition = new Vector2(0, trackH / 2f - h / 2f);
        }
        // Пульс опасной зоны: сверху — трещит, снизу — слабина
        bool topDanger = tension >= redStart;
        bool bottomDanger = tension <= escapeTop;
        if (zoneRed != null)
        {
            var img = zoneRed.GetComponent<Image>();
            if (img != null)
            {
                float pulse = topDanger ? 0.75f + 0.25f * Mathf.Sin(Time.time * 12f) : 1f;
                img.color = t.redColor * pulse;
            }
        }
        if (zoneRedBottom != null)
        {
            var img = zoneRedBottom.GetComponent<Image>();
            if (img != null)
            {
                float pulse = bottomDanger ? 0.75f + 0.25f * Mathf.Sin(Time.time * 12f) : 1f;
                img.color = t.redColor * pulse;
            }
        }
        // Столбик натяжения от низа трека (якорь 0.5/0, пивот 0.5/0)
        if (tensionFill != null)
        {
            float h = Mathf.Max(0f, tension * trackH - 8f);
            tensionFill.sizeDelta = new Vector2(tensionFill.sizeDelta.x, h);
            tensionFill.anchoredPosition = new Vector2(0, 4f);
        }
        if (progressFill != null)
            progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        // Заголовок: имя + остаток дистанции в метрах
        if (titleText != null)
        {
            float left = Mathf.Max(0f, (1f - progress) * distTotal);
            titleText.text = fishTitle + " · " + left.ToString("0") + " м";
        }
        // Тревога: сверху — отпусти, снизу — тяни
        if (snapText != null)
        {
            if (topDanger) { snapText.enabled = true; snapText.text = "ЛЕСКА ТРЕЩИТ! ОТПУСТИ!"; }
            else if (bottomDanger) { snapText.enabled = true; snapText.text = "СЛАБИНА! ТЯНИ!"; }
            else snapText.enabled = false;
        }
        if (snapFlash != null)
        {
            Color c = snapFlash.color;
            c.a = (topDanger || bottomDanger) ? 0.25f + 0.15f * Mathf.Sin(Time.time * 12f) : 0f;
            snapFlash.color = c;
        }
    }

    // ── Отрисовка на нарисованной панели: ширина полоски = натяжение,
    // рыбка едет к финишу с прогрессом, в опасных зонах полоска пульсирует ──
    void LayoutScene(FishingTuning t)
    {
        if (tensionImage != null)
        {
            if (fillMode == 0) tensionImage.fillAmount = Mathf.Clamp01(tension);
            else if (fillMode == 1)
            {
                RectTransform frt = tensionImage.rectTransform;
                Vector2 mx = frt.anchorMax;
                mx.x = fillMinX + Mathf.Clamp01(tension) * fillSpan;
                frt.anchorMax = mx;
            }
            else
            {
                RectTransform frt = tensionImage.rectTransform;
                Vector2 sd = frt.sizeDelta;
                sd.x = Mathf.Clamp01(tension) * fillFullW;
                frt.sizeDelta = sd;
            }
            // Опасные зоны с обеих сторон — пульс красным
            bool danger = tension >= redStart || tension <= escapeTop;
            if (danger)
            {
                float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 12f);
                tensionImage.color = Color.Lerp(fillBaseColor, t.redColor, pulse);
            }
            else tensionImage.color = fillBaseColor;
        }
        if (marker != null)
        {
            // Маркер прогресса — мёртво стоит: только вперёд, никакой тряски
            // (тряска ±4px каждый кадр и читалась как «ездка вперёд-назад»).
            float x = Mathf.Lerp(markerLeft, markerRight, Mathf.Clamp01(progress));
            marker.anchoredPosition = new Vector2(x, markerY);
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ПОСТРОЙКА (размеры/цвета/позиции — из FishingTuning)
    // ═══════════════════════════════════════════════════════════
    void BuildUI()
    {
        FishingTuning t = FishingTuning.Instance;
        root = new GameObject("FishingPanel");
        root.transform.SetParent(transform, false);
        var rootRt = root.AddComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.sizeDelta = t.panelSize;
        rootRt.anchoredPosition = Vector2.zero;

        var bg = root.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.85f);

        // Красная вспышка по краям при опасности (сход или обрыв)
        GameObject flash = new GameObject("SnapFlash");
        flash.transform.SetParent(root.transform, false);
        var flashRt = flash.AddComponent<RectTransform>();
        flashRt.anchorMin = Vector2.zero;
        flashRt.anchorMax = Vector2.one;
        flashRt.offsetMin = Vector2.zero;
        flashRt.offsetMax = Vector2.zero;
        snapFlash = flash.AddComponent<Image>();
        snapFlash.color = new Color(1f, 0f, 0f, 0f);
        snapFlash.raycastTarget = false;

        titleText = MakeText(root.transform, "Title", t.titleFontSize, new Vector2(0, 240), new Vector2(400, 60));

        // Иконка рыбы рядом с треком
        GameObject icon = new GameObject("FishIcon");
        icon.transform.SetParent(root.transform, false);
        var iconRt = icon.AddComponent<RectTransform>();
        iconRt.anchorMin = new Vector2(0.5f, 0.5f);
        iconRt.anchorMax = new Vector2(0.5f, 0.5f);
        iconRt.sizeDelta = new Vector2(56f, 56f);
        iconRt.anchoredPosition = new Vector2(110f, 120f);
        fishImage = icon.AddComponent<Image>();
        fishImage.raycastTarget = false;

        GameObject track = new GameObject("TensionTrack");
        track.transform.SetParent(root.transform, false);
        var trackRect = track.AddComponent<RectTransform>();
        trackRect.anchorMin = new Vector2(0.5f, 0.5f);
        trackRect.anchorMax = new Vector2(0.5f, 0.5f);
        trackRect.sizeDelta = t.trackSize;
        trackRect.anchoredPosition = t.trackPos;
        var trackImg = track.AddComponent<Image>();
        trackImg.color = new Color(0.1f, 0.15f, 0.3f, 1f);

        zoneRedBottom = MakeZone(track.transform, "ZoneRedBottom", t.redColor);
        zoneGreen = MakeZone(track.transform, "ZoneGreen", t.greenColor);
        zoneYellow = MakeZone(track.transform, "ZoneYellow", t.yellowColor);
        zoneRed = MakeZone(track.transform, "ZoneRed", t.redColor);

        GameObject fill = new GameObject("TensionFill");
        fill.transform.SetParent(track.transform, false);
        tensionFill = fill.AddComponent<RectTransform>();
        tensionFill.anchorMin = new Vector2(0.5f, 0f);
        tensionFill.anchorMax = new Vector2(0.5f, 0f);
        tensionFill.pivot = new Vector2(0.5f, 0f);
        tensionFill.sizeDelta = new Vector2(t.trackSize.x - 8f, 0f);
        tensionFill.anchoredPosition = new Vector2(0, 4f);
        var fillImg = fill.AddComponent<Image>();
        fillImg.color = t.tensionColor;
        fillImg.raycastTarget = false;

        GameObject barBg = new GameObject("ProgressBg");
        barBg.transform.SetParent(root.transform, false);
        var barBgRt = barBg.AddComponent<RectTransform>();
        barBgRt.anchorMin = new Vector2(0.5f, 0.5f);
        barBgRt.anchorMax = new Vector2(0.5f, 0.5f);
        barBgRt.sizeDelta = t.progressSize;
        barBgRt.anchoredPosition = t.progressPos;
        var barBgImg = barBg.AddComponent<Image>();
        barBgImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);

        GameObject pfill = new GameObject("ProgressFill");
        pfill.transform.SetParent(barBg.transform, false);
        progressFill = pfill.AddComponent<RectTransform>();
        progressFill.anchorMin = Vector2.zero;
        progressFill.anchorMax = new Vector2(0f, 1f);
        progressFill.offsetMin = Vector2.zero;
        progressFill.offsetMax = Vector2.zero;
        var pfillImg = pfill.AddComponent<Image>();
        pfillImg.color = new Color(0.3f, 0.9f, 0.4f, 1f);
        pfillImg.raycastTarget = false;

        snapText = MakeText(root.transform, "SnapAlarm", 30, new Vector2(0, 180), new Vector2(400, 44));
        snapText.text = "ЛЕСКА ТРЕЩИТ! ОТПУСТИ!";
        snapText.color = new Color(1f, 0.3f, 0.25f);
        snapText.enabled = false;

        hintText = MakeText(root.transform, "Hint", t.hintFontSize, new Vector2(0, -265f), new Vector2(420, 40));
        hintText.text = "Держи — тяни! Не урони в края!";
    }

    RectTransform MakeZone(Transform parent, string name, Color c)
    {
        FishingTuning t = FishingTuning.Instance;
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(t.trackSize.x, 100f);
        var img = go.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return rt;
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
        tmp.raycastTarget = false;
        return tmp;
    }
}
