using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Часть 2 лора: обучение Акта 1 (АКТ «ПАЛАТА»).
/// Ленивый DontDestroyOnLoad-синглтон + ISaveable (ключ "lore_tutorial").
/// Трекер строится КОДОМ (паттерн SellUI/FeedUI) — ссылки в инспекторе не нужны.
/// Шаги закрываются через статичный Notify("id") — хуки по 1 строке стоят в:
/// FarmInteraction (hoe/plant/water/harvest), BuyerManager (sell),
/// MorekUI (rod), FishingController (fish), EnemyHealth (kill),
/// OreVeinComponent (mine). Книга зовёт Notify("intro_done").
/// </summary>
public class TutorialManager : MonoBehaviour, ISaveable
{
    public static TutorialManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("TutorialManager");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<TutorialManager>();
            }
            return _instance;
        }
    }
    private static TutorialManager _instance;

    public string SaveKey => "lore_tutorial";

    [System.Serializable]
    private class TutorialSave
    {
        public bool introSeen;
        public int progress;
        public int kills;
        public int mines;
        public bool done;
        public bool rewardGiven;
    }

    // Шаги Акта 1 (id → текст). kill/mine — счётчики.
    private readonly string[] stepIds =
        { "intro", "hoe", "plant", "water", "harvest", "sell", "rod", "fish", "kill", "mine" };
    private readonly string[] stepTexts =
    {
        "Приди в себя (закрой книгу)",
        "Вскопай 1 грядку мотыгой",
        "Посади семена",
        "Полей грядку (вода — из колодца)",
        "Собери урожай серпом",
        "Продай урожай Дрону в городе",
        "Возьми удочку у Морека на пляже",
        "Поймай 1 рыбу",
        "Убей слаймов в лесу",
        "Добудь руду в шахте",
    };
    private const int KILLS_NEED = 3;
    private const int MINES_NEED = 5;
    private const int DONE_REWARD = 500;

    private bool introSeen;
    private int progress;
    private int kills;
    private int mines;
    private bool done;
    private bool rewardGiven;

    public bool IntroSeen => introSeen;
    public bool IsDone => done;

    // ── Трекер UI (код) ──
    private GameObject trackerRoot;
    private TMP_Text trackerLabel;
    private bool uiBuilt;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        SaveManager.Instance?.Register(this);
    }

    void OnDestroy() { SaveManager.Instance?.Unregister(this); }

    void Start()
    {
        SaveManager.Instance?.LoadInto(this);
        EnsureTrackerUI();
        RefreshTracker();
    }

    // ── ВХОД: зовут хуки и книга. Null-safe — можно звать до создания. ──
    public static void Notify(string evtId)
    {
        if (string.IsNullOrEmpty(evtId)) return;
        if (_instance == null) return; // менеджер ещё не создан — событие до старта, игнор
        _instance.OnEvent(evtId);
    }

    void OnEvent(string evt)
    {
        if (done) return;
        if (progress < 0 || progress >= stepIds.Length) return;

        // Книга закрыта — всегда засчитываем (даже если трекер ещё не построен)
        if (evt == "intro_done")
        {
            introSeen = true;
            if (CurrentId() == "intro") Advance();
            else SaveManager.Instance?.Save();
            RefreshTracker();
            return;
        }

        string cur = CurrentId();
        if (evt == "kill" && cur == "kill")
        {
            kills++;
            ActionLogUI.Show("[Обучение] Слаймы: " + Mathf.Min(kills, KILLS_NEED) + "/" + KILLS_NEED);
            if (kills >= KILLS_NEED) Advance();
            else SaveManager.Instance?.Save();
            RefreshTracker();
            return;
        }
        if (evt == "mine" && cur == "mine")
        {
            mines++;
            ActionLogUI.Show("[Обучение] Жилы: " + Mathf.Min(mines, MINES_NEED) + "/" + MINES_NEED);
            if (mines >= MINES_NEED) Advance();
            else SaveManager.Instance?.Save();
            RefreshTracker();
            return;
        }
        if (evt == cur) Advance();
    }

    string CurrentId() => (progress >= 0 && progress < stepIds.Length) ? stepIds[progress] : "";

    void Advance()
    {
        progress++;
        ActionLogUI.Show("[Обучение] Готово! " + (progress < stepTexts.Length ? "Дальше: " + stepTexts[progress] : "Акт 1 пройден!"));
        if (progress >= stepIds.Length) CompleteAll();
        else SaveManager.Instance?.Save();
        RefreshTracker();
    }

    void CompleteAll()
    {
        done = true;
        if (!rewardGiven)
        {
            rewardGiven = true;
            if (CurrencyManager.Instance != null)
                CurrencyManager.Instance.AddGold(DONE_REWARD);
            ActionLogUI.Show("[Мэр] Отлично, пациент! Держи премию " + DONE_REWARD + "g. Заглядывай — расскажу про шахту и лес.");
        }
        SaveManager.Instance?.Save();
        RefreshTracker();
    }

    // Кнопка «×» на трекере — пропустить обучение (мобилки).
    public void SkipTutorial()
    {
        if (done) return;
        introSeen = true;
        CompleteAll();
    }

    // ── Привязка сценового трекера (как конструктор): зовёт TutorialTrackerBinder.
    // Если бинда нет — строится кодовый фолбэк как раньше. ──
    private GameObject boundRoot;
    private TMP_Text boundLabel;

    public void BindTracker(GameObject root, TMP_Text label, Button skip)
    {
        boundRoot = root;
        boundLabel = label;
        if (skip != null) skip.onClick.AddListener(SkipTutorial);
        uiBuilt = false; // заставить EnsureTrackerUI подхватить бинд
        RefreshTracker();
    }

    // ── Трекер UI кодом ──
    void EnsureTrackerUI()
    {
        // Сценовая панель привязана — используем её
        if (boundRoot != null)
        {
            trackerRoot = boundRoot;
            trackerLabel = boundLabel;
            uiBuilt = true;
            return;
        }
        if (uiBuilt) return;
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        trackerRoot = new GameObject("TutorialTracker (auto)");
        trackerRoot.transform.SetParent(canvas.transform, false);

        RectTransform rt = trackerRoot.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(12f, -12f);
        rt.sizeDelta = new Vector2(460f, 84f);

        Image bg = trackerRoot.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.55f);

        GameObject labelGo = new GameObject("Label");
        labelGo.transform.SetParent(trackerRoot.transform, false);
        RectTransform lrt = labelGo.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = new Vector2(1f, 1f);
        lrt.offsetMin = new Vector2(12f, 6f);
        lrt.offsetMax = new Vector2(-44f, -6f);

        trackerLabel = labelGo.AddComponent<TextMeshProUGUI>();
        trackerLabel.fontSize = 22f;
        trackerLabel.color = new Color(1f, 0.95f, 0.75f);
        trackerLabel.textWrappingMode = TextWrappingModes.Normal;
        trackerLabel.overflowMode = TextOverflowModes.Ellipsis;

        // Крестик «пропустить»
        GameObject xGo = new GameObject("Skip");
        xGo.transform.SetParent(trackerRoot.transform, false);
        RectTransform xrt = xGo.AddComponent<RectTransform>();
        xrt.anchorMin = new Vector2(1f, 1f);
        xrt.anchorMax = new Vector2(1f, 1f);
        xrt.pivot = new Vector2(1f, 1f);
        xrt.anchoredPosition = Vector2.zero;
        xrt.sizeDelta = new Vector2(40f, 40f);
        Button xBtn = xGo.AddComponent<Button>();
        xBtn.onClick.AddListener(SkipTutorial);

        GameObject xLabelGo = new GameObject("Label");
        xLabelGo.transform.SetParent(xGo.transform, false);
        RectTransform xlrt = xLabelGo.AddComponent<RectTransform>();
        xlrt.anchorMin = Vector2.zero;
        xlrt.anchorMax = Vector2.one;
        xlrt.offsetMin = Vector2.zero;
        xlrt.offsetMax = Vector2.zero;
        TMP_Text xLabel = xLabelGo.AddComponent<TextMeshProUGUI>();
        xLabel.fontSize = 28f;
        xLabel.alignment = TextAlignmentOptions.Center;
        xLabel.color = new Color(1f, 1f, 1f, 0.7f);
        xLabel.text = "×";

        uiBuilt = true;
    }

    void RefreshTracker()
    {
        if (!uiBuilt)
        {
            EnsureTrackerUI();
            if (!uiBuilt) return;
        }
        if (done)
        {
            // Обучение пройдено — гасим трекер (мир дальше ведут квесты ч.4)
            if (trackerRoot != null) trackerRoot.SetActive(false);
            return;
        }
        if (trackerRoot != null && !trackerRoot.activeSelf) trackerRoot.SetActive(true);
        if (trackerLabel == null) return;

        string text = "";
        if (progress >= 0 && progress < stepTexts.Length)
        {
            text = "Дело: " + stepTexts[progress];
            if (CurrentId() == "kill") text += " (" + Mathf.Min(kills, KILLS_NEED) + "/" + KILLS_NEED + ")";
            if (CurrentId() == "mine") text += " (" + Mathf.Min(mines, MINES_NEED) + "/" + MINES_NEED + ")";
            text += "\n<size=70%>Долг: 500 000 кредитов</size>";
        }
        trackerLabel.text = text;
    }

    // Перепривязка после смены сцены: Canvas пересоздался — трекер убит вместе с ним.
    void OnEnable() { RefreshTracker(); }

    // ── Сейв ──
    public string CaptureState()
    {
        return JsonUtility.ToJson(new TutorialSave
        {
            introSeen = introSeen, progress = progress,
            kills = kills, mines = mines, done = done, rewardGiven = rewardGiven
        });
    }

    public void RestoreState(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var s = JsonUtility.FromJson<TutorialSave>(json);
            if (s == null) return;
            introSeen = s.introSeen; progress = s.progress;
            kills = s.kills; mines = s.mines; done = s.done; rewardGiven = s.rewardGiven;
        }
        catch (System.Exception e) { Debug.LogWarning("[Обучение] Битый сейв, начинаем заново: " + e.Message); }
        RefreshTracker();
    }
}
