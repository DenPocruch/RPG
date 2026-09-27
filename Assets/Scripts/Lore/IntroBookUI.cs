using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Часть 2 лора: интро-книга (3 страницы из 01-world.md).
/// Показывается ОДИН раз на первом запуске, потом флаг в TutorialManager.
/// UI строится КОДОМ (паттерн SellUI) — ссылки в инспекторе не нужны.
/// Панель обязана жить под ПЕРСИСТЕНТНЫМ Canvas (как FishingUI-панель).
/// </summary>
public static class IntroBookUI
{
    private static GameObject root;
    private static TMP_Text titleText;
    private static TMP_Text bodyText;
    private static Button backBtn;
    private static Button nextBtn;
    private static Button skipBtn;
    private static TMP_Text nextLabel;
    private static int page;

    private static readonly string[] Titles =
    {
        "Книга I. Авария",
        "Книга II. Палата",
        "Книга III. Пробуждение",
    };

    private static readonly string[] Pages =
    {
        "Дождь. Остановка «Заводская». Илья смотрит на часы — 23:47. До дома 10 минут.\n\nСвет фар сбоку. Звук, которого он не успел испугаться.\n\n— Пациент... стабилен... подключайте...\n\nГолоса как через воду.",
        "Писк приборов.\n\n— ...кома, переломы, разрыв... Полный курс — пятьсот тысяч. Без «Эдема» он не встанет.\n\n— Он согласен? Родственники?\n\n— Некому спрашивать. Подключаем по экстренному. Если очнётся — сам заработает.\n\nШум. Запах озона. Свет.",
        "Пахнет сеном. Кричит петух.\n\nТы лежишь на траве у чужой фермы. Руки — чужие, молодые, в рубахе. Над тобой стоит толстый дядька в костюме с папкой:\n\n— Отлично! Пульс есть, рефлексы есть! Добро пожаловать в Долину Зари, пациент 217! Я — мэр Аврелий. Пойдёмте, объясню, за что вы нам должны полмиллиона.",
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        // Менеджер создаём сразу и восстанавливаем синхронно —
        // иначе не узнаем, показывать ли книгу (Start ещё не отработал).
        var tm = TutorialManager.Instance;
        if (SaveManager.Instance != null) SaveManager.Instance.LoadInto(tm);
        if (tm.IntroSeen || tm.IsDone) return;

        // Canvas может ещё не существовать на этом кадре — пробуем позже.
        if (Object.FindFirstObjectByType<Canvas>() == null)
        {
            HookDelayedShow();
            return;
        }
        Show();
    }

    static void HookDelayedShow()
    {
        var go = new GameObject("IntroBookWaiter");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<IntroBookWaiter>();
    }

    private class IntroBookWaiter : MonoBehaviour
    {
        float t;
        void Update()
        {
            t += Time.deltaTime;
            if (Object.FindFirstObjectByType<Canvas>() != null || t > 10f)
            {
                var tm = TutorialManager.Instance;
                if (t <= 10f && !tm.IntroSeen && !tm.IsDone) Show();
                Destroy(gameObject);
            }
        }
    }

    // ── Привязка сценовой панели (как конструктор): зовёт IntroBookBinder
    // из Awake. Если бинда нет — Show() построит кодовый фолбэк как раньше. ──
    private static bool usingBoundUI;
    private static bool boundWired;

    public static void Bind(GameObject boundRoot, TMP_Text title, TMP_Text body,
        Button back, Button next, Button skip)
    {
        root = boundRoot;
        titleText = title;
        bodyText = body;
        backBtn = back;
        nextBtn = next;
        skipBtn = skip;
        // Первый бинд побеждает: поздний дубль (вторая панель/биндер в сцене)
        // не должен уводить статику на скрытый объект
        if (usingBoundUI && root != null)
        {
            Debug.LogWarning("[IntroBook] Bind: дубль! Уже привязано к '" + root.name
                + "', повтор от '" + (boundRoot != null ? boundRoot.name : "null")
                + "' проигнорирован. Поищи второй IntroBookBinder через t:IntroBookBinder и удали.");
            return;
        }
        usingBoundUI = true;
        boundWired = false;
        if (root != null) root.SetActive(false);
        Debug.Log("[IntroBook] Bind: панель привязана (" + (root != null ? root.name : "null") + ")");
    }

    public static void Show()
    {
        // Активно ищем биндер в сцене (включая скрытые) — порядок Awake больше не важен
        DiscoverBinder();
        // Сценовая панель привязана — показываем её
        Debug.Log("[IntroBook] Show: usingBoundUI=" + usingBoundUI + " root=" + (root != null ? root.name : "null"));
        if (usingBoundUI && root != null)
        {
            page = 0;
            WireBoundOnce();
            root.SetActive(true);
            ShowPage(0);
            FreezePlayer(true);
            return;
        }
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null || root != null) return;
        usingBoundUI = false;
        page = 0;
        BuildUI(canvas);
        ShowPage(0);
        FreezePlayer(true);
    }

    static void DiscoverBinder()
    {
        if (usingBoundUI && root != null) return; // уже привязано и живо
        var found = Object.FindObjectsByType<IntroBookBinder>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (found != null && found.Length > 0 && found[0] != null)
            found[0].ApplyBind();
    }

    static void FreezePlayer(bool frozen)
    {
        PlayerMovement pm = Object.FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = !frozen;
    }

    static void WireBoundOnce()
    {
        if (boundWired) return;
        boundWired = true;
        if (backBtn != null) backBtn.onClick.AddListener(() => ShowPage(page - 1));
        if (nextBtn != null) nextBtn.onClick.AddListener(OnNext);
        if (skipBtn != null) skipBtn.onClick.AddListener(Close);
        if (nextBtn != null) nextLabel = nextBtn.GetComponentInChildren<TMP_Text>();
    }

    static void BuildUI(Canvas canvas)
    {
        root = new GameObject("IntroBook (auto)");
        root.transform.SetParent(canvas.transform, false);
        RectTransform rrt = root.AddComponent<RectTransform>();
        rrt.anchorMin = Vector2.zero;
        rrt.anchorMax = Vector2.one;
        rrt.offsetMin = Vector2.zero;
        rrt.offsetMax = Vector2.zero;

        GameObject dim = new GameObject("Dim");
        dim.transform.SetParent(root.transform, false);
        RectTransform dimRt = dim.AddComponent<RectTransform>();
        dimRt.anchorMin = Vector2.zero;
        dimRt.anchorMax = Vector2.one;
        dimRt.offsetMin = Vector2.zero;
        dimRt.offsetMax = Vector2.zero;
        dim.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);

        GameObject panel = new GameObject("Panel");
        panel.transform.SetParent(root.transform, false);
        RectTransform prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f);
        prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(650f, 650f);
        panel.AddComponent<Image>().color = new Color(0.13f, 0.1f, 0.07f, 1f);

        GameObject titleGo = new GameObject("Title");
        titleGo.transform.SetParent(panel.transform, false);
        RectTransform trt = titleGo.AddComponent<RectTransform>();
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, -24f);
        trt.sizeDelta = new Vector2(-48f, 60f);
        titleText = titleGo.AddComponent<TextMeshProUGUI>();
        titleText.fontSize = 34f;
        titleText.alignment = TextAlignmentOptions.Center;
        titleText.color = new Color(1f, 0.9f, 0.6f);

        GameObject bodyGo = new GameObject("Body");
        bodyGo.transform.SetParent(panel.transform, false);
        RectTransform brt = bodyGo.AddComponent<RectTransform>();
        brt.anchorMin = new Vector2(0f, 0f);
        brt.anchorMax = new Vector2(1f, 1f);
        brt.offsetMin = new Vector2(36f, 110f);
        brt.offsetMax = new Vector2(-36f, -100f);
        bodyText = bodyGo.AddComponent<TextMeshProUGUI>();
        bodyText.fontSize = 26f;
        bodyText.color = new Color(0.95f, 0.9f, 0.8f);
        bodyText.textWrappingMode = TextWrappingModes.Normal;

        backBtn = MakeButton(panel, "Назад", new Vector2(0f, 0f), new Vector2(12f, 20f));
        backBtn.onClick.AddListener(() => ShowPage(page - 1));
        nextBtn = MakeButton(panel, "Дальше", new Vector2(1f, 0f), new Vector2(-12f, 20f));
        nextBtn.onClick.AddListener(OnNext);
        nextLabel = nextBtn.GetComponentInChildren<TMP_Text>();

        Button skip = MakeButton(panel, "Пропустить", new Vector2(0.5f, 0f), new Vector2(0f, 20f));
        skip.onClick.AddListener(Close);
        skipBtn = skip;
    }

    static Button MakeButton(GameObject parent, string text, Vector2 anchor, Vector2 pos)
    {
        GameObject go = new GameObject("Btn_" + text);
        go.transform.SetParent(parent.transform, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(200f, 64f);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.35f, 0.25f, 0.12f, 1f);
        Button btn = go.AddComponent<Button>();

        GameObject lgo = new GameObject("Label");
        lgo.transform.SetParent(go.transform, false);
        RectTransform lrt = lgo.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        TMP_Text label = lgo.AddComponent<TextMeshProUGUI>();
        label.fontSize = 26f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.text = text;
        return btn;
    }

    // ── Письмо (один экран поверх книги): тот же красивый бинд ──
    private static bool customMode;
    private static string customTitle = "";
    private static string customPage = "";

    public static void ShowCustom(string title, string body)
    {
        DiscoverBinder();
        customMode = true;
        customTitle = title;
        customPage = body;
        if (usingBoundUI && root != null)
        {
            page = 0;
            WireBoundOnce();
            root.SetActive(true);
            ShowPage(0);
            FreezePlayer(true);
            return;
        }
        // Кодового фолбэка для писем нет — без сценовой панели показываем в лог
        customMode = false;
        ActionLogUI.Show("[" + title + "] " + body);
    }

    static void ShowPage(int i)
    {
        if (customMode)
        {
            if (titleText != null) titleText.text = customTitle;
            if (bodyText != null) bodyText.text = customPage;
            if (backBtn != null) backBtn.gameObject.SetActive(false);
            if (nextLabel != null) nextLabel.text = "Понятно";
            return;
        }
        page = Mathf.Clamp(i, 0, Pages.Length - 1);
        if (titleText != null) titleText.text = Titles[page];
        if (bodyText != null) bodyText.text = Pages[page];
        if (backBtn != null) backBtn.gameObject.SetActive(page > 0);
        if (nextLabel != null) nextLabel.text = (page == Pages.Length - 1) ? "Начать игру" : "Дальше";
    }

    static void OnNext()
    {
        if (customMode) { Close(); return; }
        if (page < Pages.Length - 1) ShowPage(page + 1);
        else Close();
    }

    static void Close()
    {
        customMode = false;
        if (usingBoundUI)
        {
            if (root != null) root.SetActive(false); // сценовая панель живёт дальше
        }
        else
        {
            if (root != null) Object.Destroy(root);
            root = null;
        }

        FreezePlayer(false);

        TutorialManager.Notify("intro_done");
    }
}
