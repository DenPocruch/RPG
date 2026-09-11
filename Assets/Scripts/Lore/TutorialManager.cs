using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
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
        public int ver;
        public bool introSeen;
        public int progress;
        public int kills;
        public int mines;
        public int farmkills;
        public int hoes;
        public int plants;
        public int waters;
        public int harvests;
        public int metMask;
        public int debtPaid;
        public bool done;
        public bool rewardGiven;
        public bool letterSeen;
        public bool mayorMet;
        public bool swordGiven;
        public bool toolsGiven;
        public bool pickGiven;
        public bool skillsGrant;
    }

    // Путеводитель: цепочка Акт 1 (id → текст). clear/hoe/plant/water/harvest/kill/mine/city — счётчики.
    // ver=5: полный первый день — мэр → интерфейс → ферма → бой → лут → инструменты →
    // мотыга(1+5) → посадка → полив → город(знакомства+магазин) → урожай → продажа(−долг) →
    // готовка → еда → навыки → рыбалка → лес → шахта(кирка) → кузница → экипировка → свобода.
    private const int SAVE_VER = 5;
    private readonly string[] stepIds =
        { "intro", "sword", "ui_inv", "ui_hotbar", "home", "clear", "tools",
          "hoe1", "hoe5", "plant", "water", "city", "seeds",
          "harvest", "sell", "bread", "eat", "skills",
          "rod", "fish", "kill", "pickaxe", "mine", "forge", "equip" };
    private readonly string[] stepTexts =
    {
        "Приди в себя (закрой книгу)",
        "Поговори с мэром, забери меч",
        "Открой инвентарь — найди мотыгу",
        "Перетащи мотыгу в хотбар",
        "Вернись на участок №9",
        "Зачисти участок от слаймов",
        "Вернись к мэру, забери набор",
        "Вскопай 1 грядку мотыгой",
        "Вскопай ещё 5 грядок",
        "Посади 6 пшениц",
        "Набери воды из колодца и полей 6 грядок",
        "Познакомься в городе: Мира, Степан, Густав, Дрон",
        "Купи пшеницу у Марты",
        "Собери 6 пшениц серпом",
        "Продай пшеницу Дрону",
        "Отнеси пшеницу Густаву",
        "Съешь хлеб",
        "Открой книгу и возьми перк",
        "Возьми удочку у Морека на пляже",
        "Поймай 1 рыбу",
        "Убей 3 слаймов в лесу",
        "Поговори со Степаном, забери кирку",
        "Добудь 5 жил в шахте",
        "Улучши предмет у Степана",
        "Надень улучшенное",
    };
    private const int HOES_NEED = 6; // итого: hoe1(1) + hoe5(ещё 5)
    private const int PLANTS_NEED = 6;
    private const int WATERS_NEED = 6;
    private const int HARVESTS_NEED = 6;
    private const int KILLS_NEED = 3;
    private const int MINES_NEED = 5;
    private const int CITY_MASK_FULL = 15; // cook(1)+smith(2)+marta(4)+buyer(8)
    private const int DEBT_TOTAL = 500000;
    private const int DONE_REWARD = 500;

    private int ver;
    private bool introSeen;
    private int progress;
    private int kills;
    private int mines;
    private int farmkills;
    private int hoes;
    private int plants;
    private int waters;
    private int harvests;
    private int metMask;
    private int debtPaid;
    private bool done;
    private bool rewardGiven;
    private bool letterSeen;
    private bool mayorMet;
    private bool swordGiven;
    private bool toolsGiven;
    private bool pickGiven;
    private bool skillsGrant;
    private bool equipSub;
    private bool pendingMayorTalk;
    private float letterTimer = -1f;
    private const float LETTER_DELAY = 2.5f;

    public bool IntroSeen => introSeen;
    public bool IsDone => done;
    public static int ProgressIndex() => _instance != null ? _instance.progress : 0;
    public static string CurrentStep() => _instance != null ? _instance.CurrentId() : "";

    // Кирку выдаём один раз
    public static bool TakePickOnce()
    {
        if (_instance == null) return true;
        if (_instance.pickGiven) return false;
        _instance.pickGiven = true;
        SaveManager.Instance?.Save();
        return true;
    }

    // Набор выдаём один раз (иначе болтовня = бесконечные семена)
    public static bool TakeToolsOnce()
    {
        if (_instance == null) return true;
        if (_instance.toolsGiven) return false;
        _instance.toolsGiven = true;
        SaveManager.Instance?.Save();
        return true;
    }

    // Меч выдаём один раз (защита от спама кнопкой и повторных разговоров)
    public static bool TakeSwordOnce()
    {
        if (_instance == null) return true;
        if (_instance.swordGiven) return false;
        _instance.swordGiven = true;
        SaveManager.Instance?.Save();
        return true;
    }

    public static bool SwordGiven() => _instance != null && _instance.swordGiven;

    // Меч нужен, пока зачистка не закрыта
    public static bool IsSwordNeeded()
    {
        if (_instance == null || _instance.done) return false;
        int clearIdx = System.Array.IndexOf(_instance.stepIds, "clear");
        return _instance.progress <= clearIdx;
    }

    // Слизни в процессе (меч взят, но участок грязный) — время напоминаний
    public static bool IsClearPending()
    {
        if (_instance == null || _instance.done) return false;
        string cur = _instance.CurrentId();
        return cur == "home" || cur == "clear";
    }

    // Шаг tools доступен? (условие кнопки набора у мэра: mayor_tools)
    public static bool IsToolsStepOrLater()
    {
        if (_instance == null) return false;
        int toolsIdx = System.Array.IndexOf(_instance.stepIds, "tools");
        return !_instance.done && _instance.progress >= toolsIdx;
    }

    // Пак слаймов чистят, если зачистка уже в прошлом (старые сейвы, реплеи)
    public static bool IsPastClearStep()
    {
        if (_instance == null) return false;
        int clearIdx = System.Array.IndexOf(_instance.stepIds, "clear");
        return _instance.done || _instance.progress > clearIdx;
    }

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
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (m != LoadSceneMode.Single) return;
        // Отложенный автодиалог мэра после телепортации с фермы
        if (pendingMayorTalk && s.name == "City")
        {
            pendingMayorTalk = false;
            StartCoroutine(IntroTalkRoutine());
        }
        // Возврат домой засчитывается сам — по входу на ферму
        if (!done && CurrentId() == "home" && s.name == "SampleScene") Advance();
        RefreshTracker(); // внутри — UpdateArrow под новую сцену
    }

    // Пробуждение на площади: в город к мэру, диалог сам
    void DoIntroTeleport()
    {
        mayorMet = true;
        pendingMayorTalk = true;
        SaveManager.Instance?.Save();
        if (ScreenFader.Instance != null) ScreenFader.Instance.SetBlackInstant();
        SceneManager.LoadScene("City");
    }

    System.Collections.IEnumerator IntroTalkRoutine()
    {
        yield return null; // кадр на спавн сцены и NPC
        if (ScreenFader.Instance != null) ScreenFader.Instance.StartFadeIn();

        MayorNPC mayor = FindFirstObjectByType<MayorNPC>();
        GameObject player = GameObject.FindWithTag("Player");
        if (mayor == null || player == null)
        {
            pendingMayorTalk = false;
            ActionLogUI.Show("[Мэр] Эй, пациент! Я тут, в городе!");
            yield break;
        }
        // Меч выдаём СРАЗУ при входе в автодиалог: ушёл в вопросы —
        // всё равно вооружён. Дубли режет TakeSwordOnce внутри.
        mayor.GiveSword();
        // Мотыга — В РЮКЗАК (урок хотбара: найти и перетащить самому)
        if (!QuestGive.HasItem("Hoe"))
        {
            ItemData hoe = ItemDatabase.Find("Hoe");
            if (hoe != null) QuestGive.GiveToPack(hoe, 1);
        }
        // Ставим рядом (проверка дистанции в Interact не нужна — открываем напрямую)
        Vector3 p = mayor.transform.position + new Vector3(1f, 0f, 0f);
        p.z = player.transform.position.z;
        player.transform.position = p;

        var inter = mayor.GetComponent<NPCInteractable>();
        var data = Resources.Load<DialogueData>("Dialogue/Mayor_Dialogue");
        if (inter != null) inter.onTalk?.Invoke();
        if (DialogueManager.Instance != null && data != null)
        {
            // NPC замирает и смотрит на игрока, как в обычном разговоре
            var npc = mayor.GetComponent<NPCController>();
            if (npc != null) npc.aiPaused = true;
            var anim = mayor.GetComponent<NPCAnimator>();
            if (anim != null && inter != null) inter.FacePlayer();
            DialogueManager.Instance.onDialogueEnd = inter != null
                ? (System.Action)inter.EndTalk : null;
            DialogueManager.Instance.StartDialogueAt(data, 7, inter);
        }
        else pendingMayorTalk = false;
    }

    void Start()
    {
        SaveManager.Instance?.LoadInto(this);
        EnsureTrackerUI();
        RefreshTracker();
    }

    float arrowRefreshT;
    float uiPollT;

    void Update()
    {
        // Стрелка протухает (набрал воду, убил слайма, подошёл) — обновляем по таймеру
        arrowRefreshT -= Time.deltaTime;
        if (arrowRefreshT <= 0f)
        {
            arrowRefreshT = 1.5f;
            if (!done) UpdateArrow();
        }
        // Урок хотбара: мотыга переехала — шаг закрыт (проверка полисекундная, дёшево)
        if (!done && CurrentId() == "ui_hotbar")
        {
            uiPollT -= Time.deltaTime;
            if (uiPollT <= 0f)
            {
                uiPollT = 0.5f;
                if (QuestGive.HasHotbarItem("Hoe")) Advance();
            }
        }
        if (letterTimer < 0f || letterSeen || done) return;
        letterTimer -= Time.deltaTime;
        if (letterTimer > 0f) return;
        letterTimer = -1f;
        letterSeen = true;
        SaveManager.Instance?.Save();
        IntroBookUI.ShowCustom("Письмо от мэра",
            "Пациент 217! Раз очнулся — слушай, дважды не повторяю.\n\n"
            + "Участок №9 — твой, но там слизни завелись. Гони их (хоть кулаками — они слабые),\n\n"
            + "а потом приходи ко мне в город — выдам инструмент и семена. Город — через портал на юге.\n\n"
            + "— Мэр Аврелий (подпись, печать)");
    }

    // ── ВХОД: зовут хуки и книга. Null-safe — можно звать до создания. ──
    public static void Notify(string evtId, int num = 0)
    {
        if (string.IsNullOrEmpty(evtId)) return;
        if (_instance == null) return; // менеджер ещё не создан — событие до старта, игнор
        _instance.OnEvent(evtId, num);
    }

    void OnEvent(string evt, int num = 0)
    {
        if (done) return;
        if (progress < 0 || progress >= stepIds.Length) return;

        // Книга закрыта — всегда засчитываем (даже если трекер ещё не построен)
        if (evt == "intro_done")
        {
            introSeen = true;
            if (CurrentId() == "intro") Advance();
            else SaveManager.Instance?.Save();
            // Пробуждение на площади: телепорт в город к мэру + автодиалог (один раз)
            if (!mayorMet && !pendingMayorTalk) DoIntroTeleport();
            RefreshTracker();
            return;
        }

        string cur = CurrentId();
        if (evt == "hoe" && (cur == "hoe1" || cur == "hoe5"))
        {
            hoes++;
            if (cur == "hoe1" && hoes >= 1)
            {
                ActionLogUI.Show("[Обучение] Отлично! Земля готова. Теперь ещё 5.");
                Advance();
            }
            else if (cur == "hoe5" && hoes >= HOES_NEED)
            {
                Advance();
            }
            else
            {
                ActionLogUI.Show("[Обучение] Грядки: " + Mathf.Min(hoes, HOES_NEED) + "/" + HOES_NEED);
                SaveManager.Instance?.Save();
            }
            RefreshTracker();
            return;
        }
        if (evt == "plant" && cur == "plant")
        {
            plants++;
            ActionLogUI.Show("[Обучение] Посажено: " + Mathf.Min(plants, PLANTS_NEED) + "/" + PLANTS_NEED);
            if (plants >= PLANTS_NEED) Advance();
            else SaveManager.Instance?.Save();
            RefreshTracker();
            return;
        }
        if (evt == "water" && cur == "water")
        {
            waters++;
            ActionLogUI.Show("[Обучение] Полито: " + Mathf.Min(waters, WATERS_NEED) + "/" + WATERS_NEED);
            if (waters >= WATERS_NEED) Advance();
            else SaveManager.Instance?.Save();
            RefreshTracker();
            return;
        }
        if (evt == "harvest" && cur == "harvest")
        {
            harvests++;
            ActionLogUI.Show("[Обучение] Собрано: " + Mathf.Min(harvests, HARVESTS_NEED) + "/" + HARVESTS_NEED);
            if (harvests >= HARVESTS_NEED) Advance();
            else SaveManager.Instance?.Save();
            RefreshTracker();
            return;
        }
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
        if (evt == "farmkill" && cur == "clear")
        {
            farmkills++;
            int total = Mathf.Max(1, FarmSlimePack.Target);
            ActionLogUI.Show("[Обучение] Участок: " + Mathf.Min(farmkills, total) + "/" + total);
            if (farmkills == 1)
                ActionLogUI.Show("[Лут] Подбери добычу (подойди), потом загляни в инвентарь — там находка!");
            if (farmkills >= total) Advance();
            else SaveManager.Instance?.Save();
            RefreshTracker();
            return;
        }
        if (evt == "kill" && cur == "kill" && kills == 0)
            ActionLogUI.Show("[Лут] Кажется, здесь даже борьба идёт в счёт долга. Подбери всё!");
        if (evt == "sell" && cur == "sell")
        {
            debtPaid += num;
            int left = Mathf.Max(0, DEBT_TOTAL - debtPaid);
            ActionLogUI.Show("[Долг] −" + num + "g. Осталось: " + left + " кредитов.");
            Advance();
            return;
        }
        if (evt == "forge" && cur == "forge")
        {
            ActionLogUI.Show("[Обучение] Улучшено! Кузница освоена.");
            Advance();
            return;
        }
        if (evt == "skills_spent" && cur == "skills")
        {
            ActionLogUI.Show("[Обучение] Перк взят! Теперь очки капают за уровни.");
            Advance();
            return;
        }
        if (evt.StartsWith("talk_") && cur == "city")
        {
            int bit = TalkBit(evt.Substring(5));
            if (bit > 0 && (metMask & bit) == 0)
            {
                metMask |= bit;
                ActionLogUI.Show("[Обучение] Знакомства: " + CountBits(metMask) + "/4");
                if (metMask == CITY_MASK_FULL) Advance();
                else SaveManager.Instance?.Save();
                RefreshTracker();
                return;
            }
            if (bit > 0) { RefreshTracker(); return; }
        }
        if (evt == cur) Advance();
    }

    static int TalkBit(string role)
    {
        if (role == "cook") return 1;
        if (role == "smith") return 2;
        if (role == "marta") return 4;
        if (role == "buyer") return 8;
        return 0;
    }

    static int CountBits(int mask)
    {
        int n = 0;
        while (mask > 0) { n += mask & 1; mask >>= 1; }
        return n;
    }



    string CurrentId() => (progress >= 0 && progress < stepIds.Length) ? stepIds[progress] : "";

    void Advance()
    {
        SetEquipSub(false);
        progress++;
        ActionLogUI.Show("[Обучение] Готово! " + (progress < stepTexts.Length ? "Дальше: " + stepTexts[progress] : "Акт 1 пройден!"));
        if (progress >= stepIds.Length) CompleteAll();
        else
        {
            // Вход на шаг навыков: выдаём 3 очка (один раз) — есть что потратить
            if (CurrentId() == "skills" && !skillsGrant)
            {
                skillsGrant = true;
                if (PlayerLevel.Instance != null) PlayerLevel.Instance.GrantSkillPoints(3);
                ActionLogUI.Show("[Мэр] Держи 3 очка развития. Открой книгу и возьми перк Farming.");
            }
            if (CurrentId() == "equip") SetEquipSub(true);
            SaveManager.Instance?.Save();
        }
        RefreshTracker();
    }

    void SetEquipSub(bool on)
    {
        if (on && !equipSub)
        {
            if (EquipmentManager.Instance != null)
            {
                EquipmentManager.Instance.onEquipmentChanged += OnEquipChanged;
                equipSub = true;
            }
        }
        else if (!on && equipSub)
        {
            if (EquipmentManager.Instance != null)
                EquipmentManager.Instance.onEquipmentChanged -= OnEquipChanged;
            equipSub = false;
        }
    }

    void OnEquipChanged()
    {
        if (!done && CurrentId() == "equip") Advance();
    }

    void CompleteAll()
    {
        done = true;
        SetEquipSub(false);
        QuestArrow.Clear();
        if (!rewardGiven)
        {
            rewardGiven = true;
            if (CurrencyManager.Instance != null)
                CurrencyManager.Instance.AddGold(DONE_REWARD);
            ActionLogUI.Show("[Мэр] Теперь ты знаешь достаточно, пациент 217. Дальше Долина учить не будет — дальше она будет проверять. Держи " + DONE_REWARD + "g. АКТ 2 — «Долги».");
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
        // Активно ищем биндер в сцене (включая скрытые) — порядок Awake больше не важен
        if (boundRoot == null)
        {
            var found = FindObjectsByType<TutorialTrackerBinder>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (found != null && found.Length > 0 && found[0] != null)
                found[0].ApplyBind();
        }
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
            if (CurrentId() == "hoe1") text += " (" + Mathf.Min(hoes, 1) + "/1)";
            if (CurrentId() == "hoe5") text += " (" + Mathf.Min(hoes, HOES_NEED) + "/" + HOES_NEED + ")";
            if (CurrentId() == "city") text += " (" + CountBits(metMask) + "/4)";
            if (CurrentId() == "plant") text += " (" + Mathf.Min(plants, PLANTS_NEED) + "/" + PLANTS_NEED + ")";
            if (CurrentId() == "water") text += " (" + Mathf.Min(waters, WATERS_NEED) + "/" + WATERS_NEED + ")";
            if (CurrentId() == "harvest") text += " (" + Mathf.Min(harvests, HARVESTS_NEED) + "/" + HARVESTS_NEED + ")";
            if (CurrentId() == "clear")
            {
                int total = Mathf.Max(1, FarmSlimePack.Target);
                text += " (" + Mathf.Min(farmkills, total) + "/" + total + ")";
            }
            if (CurrentId() == "kill") text += " (" + Mathf.Min(kills, KILLS_NEED) + "/" + KILLS_NEED + ")";
            if (CurrentId() == "mine") text += " (" + Mathf.Min(mines, MINES_NEED) + "/" + MINES_NEED + ")";
            text += "\n<size=70%>Долг: 500 000 кредитов</size>";
        }
        trackerLabel.text = text;
        UpdateArrow();
    }

    // Перепривязка после смены сцены: Canvas пересоздался — трекер убит вместе с ним.
    void OnEnable() { RefreshTracker(); }

    // ── Стрелка путеводителя под текущий шаг ──
    void UpdateArrow()
    {
        if (done) { QuestArrow.Clear(); return; }
        string cur = CurrentId();
        string here = SceneManager.GetActiveScene().name;

        switch (cur)
        {
            case "intro":
                QuestArrow.Clear(); // книга открыта — стрелка не нужна
                break;
            case "sword":
                PointAtNPC<MayorNPC>("City", "Мэр · меч");
                break;
            case "home":
                if (here == "SampleScene") QuestArrow.Clear();
                else QuestArrow.SetRoute("SampleScene", null, "Ферма №9");
                break;
            case "clear":
                if (here == "SampleScene")
                    QuestArrow.SetTarget(() => NearestTutorialSlime(), "Слайм");
                else QuestArrow.SetRoute("SampleScene", null, "Ферма №9");
                break;
            case "tools":
                PointAtNPC<MayorNPC>("City", "Мэр · набор");
                break;
            case "ui_inv":
            case "ui_hotbar":
                QuestArrow.Clear(); // дело в меню
                break;
            case "hoe1":
            case "hoe5":
            case "plant":
            case "harvest":
                // Грядки — у метки TutorialDigSpot (ставится руками на ферме)
                PointAtDigSpot("Копай тут");
                break;
            case "water":
                PointAtWater();
                break;
            case "city":
                PointAtUnmetCityNPC();
                break;
            case "pickaxe":
            case "forge":
                PointAtNPC<BlacksmithNPC>("City", "Степан");
                break;
            case "equip":
                QuestArrow.Clear(); // дело в меню
                break;
            case "bread":
                PointAtNPC<CookNPC>("City", "Густав · хлеб");
                break;
            case "eat":
                QuestArrow.Clear(); // дело в рюкзаке
                break;
            case "seeds":
                PointAtSeedSeller();
                break;
            case "skills":
                QuestArrow.Clear(); // книга прокачки — дело в меню
                break;
            case "sell":
                PointAtNPC<BuyerNPC>("City", "Дрон · скупка");
                break;
            case "rod":
            case "fish":
                PointAtNPC<MorekNPC>("Beach", "Морек");
                break;
            case "kill":
                if (here == "Beginner Forest")
                    QuestArrow.SetTarget(() => NearestEnemy(), "Слайм");
                else QuestArrow.SetRoute("Beginner Forest", null, "Лес");
                break;
            case "mine":
                if (here == "Mine")
                    QuestArrow.SetTarget(() => NearestVein(), "Жила");
                else QuestArrow.SetRoute("Mine", null, "Шахта");
                break;
            default:
                QuestArrow.Clear();
                break;
        }
    }

    void PointAtNPC<T>(string homeScene, string caption) where T : MonoBehaviour
    {
        T npc = FindFirstObjectByType<T>();
        if (npc != null)
        {
            Transform t = npc.transform;
            string scene = t.gameObject.scene.name;
            QuestArrow.SetRoute(scene, () => t != null ? (Vector3?)t.position : null, caption);
        }
        else QuestArrow.SetRoute(homeScene, null, caption); // NPC не в этой сцене — к порталу
    }

    // Тур по городу: ведём к ближайшему непознанному (кухарь/кузнец/Марта/скупщик)
    void PointAtUnmetCityNPC()
    {
        string[] roles = { "cook", "smith", "marta", "buyer" };
        string[] captions = { "Густав", "Степан", "Марта", "Дрон" };
        GameObject player = GameObject.FindWithTag("Player");
        Vector3 pp = player != null ? player.transform.position : Vector3.zero;
        Transform best = null;
        string bestCap = "Город";
        float bestD = float.MaxValue;
        for (int i = 0; i < roles.Length; i++)
        {
            int bit = TalkBit(roles[i]);
            if (bit == 0 || (metMask & bit) != 0) continue;
            Transform t = FindRoleNPC(roles[i]);
            if (t == null) continue;
            float d = Vector2.Distance(pp, t.position);
            if (d < bestD) { bestD = d; best = t; bestCap = captions[i]; }
        }
        if (best != null)
        {
            Transform bt = best;
            string scene = bt.gameObject.scene.name;
            QuestArrow.SetRoute(scene, () => bt != null ? (Vector3?)bt.position : null, bestCap);
        }
        else QuestArrow.SetRoute("City", null, "Город");
    }

    static Transform FindRoleNPC(string role)
    {
        MonoBehaviour m = null;
        if (role == "cook") m = FindFirstObjectByType<CookNPC>();
        else if (role == "smith") m = FindFirstObjectByType<BlacksmithNPC>();
        else if (role == "buyer") m = FindFirstObjectByType<BuyerNPC>();
        else if (role == "marta") m = FindFirstObjectByType<MartaNPC>();
        else if (role == "morek") m = FindFirstObjectByType<MorekNPC>();
        else if (role == "mayor") m = FindFirstObjectByType<MayorNPC>();
        return m != null ? m.transform : null;
    }

    // Метка копа (TutorialDigSpot руками на ферме): дома — к ней, иначе — домой
    void PointAtDigSpot(string caption)
    {
        string here = SceneManager.GetActiveScene().name;
        GameObject spot = GameObject.Find("TutorialDigSpot");
        if (spot != null)
        {
            Transform t = spot.transform;
            string scene = t.gameObject.scene.name;
            QuestArrow.SetRoute(scene, () => t != null ? (Vector3?)t.position : null, caption);
        }
        else if (here == "SampleScene") QuestArrow.Clear();
        else QuestArrow.SetRoute("SampleScene", null, "Ферма №9");
    }

    // Полив: лейка пустая (или не в руках) — ведём к колодцу, набрал — к грядкам
    void PointAtWater()
    {
        string here = SceneManager.GetActiveScene().name;
        bool needWater = true;
        InventorySlot slot = HotbarManager.Instance != null ? HotbarManager.Instance.GetActiveSlot() : null;
        if (slot != null && slot.IsWateringCan() && slot.HasWater()) needWater = false;

        if (!needWater)
        {
            PointAtDigSpot("Полей тут");
            return;
        }
        WellInteraction well = FindFirstObjectByType<WellInteraction>();
        if (well != null)
        {
            Transform t = well.transform;
            string scene = t.gameObject.scene.name;
            QuestArrow.SetRoute(scene, () => t != null ? (Vector3?)t.position : null, "Колодец");
        }
        else if (here == "SampleScene") QuestArrow.Clear();
        else QuestArrow.SetRoute("SampleScene", null, "Ферма №9");
    }

    // Марта — прилавок ShopInteraction БЕЗ диалога (удар → сразу магазин).
    // Ищем по товару: у кого в первой вкладке семена — тот и Марта (у Бориса инструменты).
    void PointAtSeedSeller()
    {
        var stalls = FindObjectsByType<ShopInteraction>(FindObjectsSortMode.None);
        foreach (var st in stalls)
        {
            if (st == null) continue;
            if (HasSeed(st.itemsForSale) || HasSeed(st.itemsForSaleAnimals))
            {
                Transform t = st.transform;
                string scene = t.gameObject.scene.name;
                QuestArrow.SetRoute(scene, () => t != null ? (Vector3?)t.position : null, "Марта · семена");
                return;
            }
        }
        var seedDlg = Resources.Load<DialogueData>("Dialogue/DialogueSeedTrader");
        var inters = FindObjectsByType<NPCInteractable>(FindObjectsSortMode.None);
        foreach (var inter in inters)
        {
            if (inter == null || inter.dialogue == null) continue;
            bool isMarta = (seedDlg != null && inter.dialogue == seedDlg)
                || inter.dialogue.name == "DialogueSeedTrader";
            if (!isMarta) continue;
            Transform t = inter.transform;
            string scene = t.gameObject.scene.name;
            QuestArrow.SetRoute(scene, () => t != null ? (Vector3?)t.position : null, "Марта · семена");
            return;
        }
        // Запасной путь: торговец, у которого семена прямо в stock
        var all = FindObjectsByType<TraderNPC>(FindObjectsSortMode.None);
        foreach (var tr in all)
        {
            if (tr == null || tr.stock == null) continue;
            foreach (var s in tr.stock)
            {
                if (s != null && s.item != null && s.item.itemType == ItemType.Seed)
                {
                    Transform t = tr.transform;
                    string scene = t.gameObject.scene.name;
                    QuestArrow.SetRoute(scene, () => t != null ? (Vector3?)t.position : null, "Марта · семена");
                    return;
                }
            }
        }
        QuestArrow.SetRoute("City", null, "Марта · семена");
    }

    static bool HasSeed(ShopManager.ShopItem[] stock)
    {
        if (stock == null) return false;
        foreach (var s in stock)
            if (s != null && s.item != null && s.item.itemType == ItemType.Seed) return true;
        return false;
    }

    Vector3? NearestTutorialSlime()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return null;
        Vector3 pp = player.transform.position;
        float best = float.MaxValue;
        Vector3? res = null;
        var all = FindObjectsByType<TutorialSlime>(FindObjectsSortMode.None);
        foreach (var s in all)
        {
            if (s == null) continue;
            var h = s.GetComponent<EnemyHealth>();
            if (h != null && h.currentHealth <= 0) continue;
            float d = Vector2.Distance(pp, s.transform.position);
            if (d < best) { best = d; res = s.transform.position; }
        }
        return res;
    }

    float scanT;
    Vector3 scanPos;
    bool scanHas;
    string scanKind = "";

    Vector3? NearestEnemy()
    {
        return NearestCached("enemy");
    }

    Vector3? NearestVein()
    {
        return NearestCached("vein");
    }

    Vector3? NearestCached(string kind)
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return null;
        if (kind == scanKind && Time.time - scanT < 1f && scanHas) return scanPos;
        scanKind = kind;
        scanT = Time.time;
        scanHas = false;
        Vector3 pp = player.transform.position;
        float best = float.MaxValue;
        if (kind == "enemy")
        {
            var all = FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None);
            foreach (var e in all)
            {
                if (e == null || e.currentHealth <= 0) continue;
                float d = Vector2.Distance(pp, e.transform.position);
                if (d < best) { best = d; scanPos = e.transform.position; scanHas = true; }
            }
        }
        else
        {
            var all = FindObjectsByType<OreVeinComponent>(FindObjectsSortMode.None);
            foreach (var v in all)
            {
                if (v == null || v.IsDepleted) continue;
                float d = Vector2.Distance(pp, v.transform.position);
                if (d < best) { best = d; scanPos = v.transform.position; scanHas = true; }
            }
        }
        return scanHas ? (Vector3?)scanPos : null;
    }

    // ── Сейв ──
    public string CaptureState()
    {
        return JsonUtility.ToJson(new TutorialSave
        {
            ver = SAVE_VER,
            introSeen = introSeen, progress = progress,
            kills = kills, mines = mines, farmkills = farmkills,
            hoes = hoes, plants = plants, waters = waters, harvests = harvests,
            metMask = metMask, debtPaid = debtPaid,
            done = done, rewardGiven = rewardGiven,
            letterSeen = letterSeen, mayorMet = mayorMet, swordGiven = swordGiven,
            toolsGiven = toolsGiven, pickGiven = pickGiven, skillsGrant = skillsGrant
        });
    }

    public void RestoreState(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var s = JsonUtility.FromJson<TutorialSave>(json);
            if (s == null) return;
            // Миграция: старая цепочка (ver<2) — начинаем путеводитель заново,
            // книгу не перечитываем, пройденное обучение не трогаем
            if (s.ver != SAVE_VER && !s.done)
            {
                ver = SAVE_VER;
                introSeen = s.introSeen;
                progress = 0; kills = 0; mines = 0; farmkills = 0;
                hoes = 0; plants = 0; waters = 0; harvests = 0;
                metMask = 0; debtPaid = 0;
                rewardGiven = false; letterSeen = s.letterSeen;
                RefreshTracker();
                return;
            }
            ver = SAVE_VER;
            introSeen = s.introSeen; progress = s.progress;
            kills = s.kills; mines = s.mines; farmkills = s.farmkills;
            hoes = s.hoes; plants = s.plants; waters = s.waters; harvests = s.harvests;
            metMask = s.metMask; debtPaid = s.debtPaid;
            done = s.done; rewardGiven = s.rewardGiven;
            letterSeen = s.letterSeen; mayorMet = s.mayorMet; swordGiven = s.swordGiven;
            toolsGiven = s.toolsGiven; pickGiven = s.pickGiven; skillsGrant = s.skillsGrant;
        }
        catch (System.Exception e) { Debug.LogWarning("[Обучение] Битый сейв, начинаем заново: " + e.Message); }
        RefreshTracker();
    }
}
