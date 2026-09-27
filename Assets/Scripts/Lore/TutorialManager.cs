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
        public bool movementLearned;
    }

    // Путеводитель: цепочка Акт 1 (id → текст). town/forest/mine — автовход по сцене;
    // loot/pickup_harvest — подбор LootItem (урожай = farmingXpReward>0);
    // ore — счётчик жил через существующий хук Notify("mine").
    // Внутренние id ui_inv/seeds/pickaxe/mine оставлены как были (меньше churn),
    // маппинг на дизайн-документ: town=прийти в City,
    // mine=войти в шахту, ore=5 жил, act1_finish=доклад мэру.
    // ver=7: убран ранний interface (панель характеристик учили раньше, чем она
    // понадобилась). Полный Акт 1 (28 позиций дизайна: hoe1+hoe5 = один пункт) —
    // меч → рюкзак → хотбар → ферма → лут → инструменты → грядки → город →
    // урожай (+подбор) → продажа → готовка → еда → навыки → рыбалка → лес →
    // шахта → руда → кузница → экипировка → возврат к мэру.
    private const int SAVE_VER = 7;
    private readonly string[] stepIds =
        { "intro", "sword", "ui_inv", "ui_hotbar", "home", "clear", "loot", "tools",
          "hoe1", "hoe5", "plant", "water", "town", "city", "seeds",
          "harvest", "pickup_harvest", "sell", "bread", "eat", "skills",
          "rod", "fish", "forest", "kill", "pickaxe", "mine", "ore", "forge", "equip", "act1_finish" };
    private readonly string[] stepTexts =
    {
        "Приди в себя (закрой книгу)",
        "Поговори с мэром, забери меч",
        "Открой инвентарь — найди меч",
        "Перетащи меч в хотбар",
        "Вернись на участок №9",
        "Зачисти участок от слаймов",
        "Подбери добычу со слаймов (подойди ближе)",
        "Вернись к мэру за инструментами для посадки",
        "Вскопай 1 грядку мотыгой",
        "Вскопай ещё 5 грядок",
        "Посади 6 пшениц",
        "Набери воды из колодца и полей 6 грядок",
        "Отправляйся в город Заря",
        "Познакомься в городе: Мира, Степан, Густав, Дрон",
        "Купи пшеницу у Марты",
        "Собери 6 пшениц серпом",
        "Подбери урожай с земли",
        "Продай кости Дрону",
        "Отнеси пшеницу Густаву",
        "Съешь хлеб",
        "Открой книгу и возьми перк",
        "Возьми удочку у Морека на пляже",
        "Поймай 1 рыбу",
        "Иди в Лес Новичков",
        "Убей 3 слаймов в лесу",
        "Поговори со Степаном, забери кирку",
        "Войди в шахту",
        "Добудь 5 жил в шахте",
        "Улучши предмет у Степана",
        "Надень улучшенное",
        "Вернись к мэру с докладом",
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
    private bool movementLearned;
    private bool movementSampleReady;
    private Vector2 movementSample;
    private float movementDistance;
    private PlayerMovement tutorialPlayer;
    private RectTransform attackButton;
    private string controlHint = "";
    // Кэш UI-цели стрелки: переискать только при смене шага или потере объекта
    private string uiArrowStep = "";
    private RectTransform uiArrowTarget;    private float letterTimer = -1f;
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
        return true;
    }

    // Меч засчитывается по ЗАКРЫТИЮ диалога, а не по входу: иначе трекер, стрелка
    // и хинт убегают вперёд, пока игрок ещё читает страницы. Выдача предмета —
    // по-прежнему при входе (GiveSword), тут только флаг готовности шага.
    private bool swordPending;
    public static void SwordReady()
    {
        if (_instance != null) _instance.swordPending = true;
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
        movementSampleReady = false;
        movementDistance = 0f;
        // Автовходы: дом, город, лес, шахта засчитываются сами по прибытию
        if (!done)
        {
            string cur = CurrentId();
            if (cur == "home" && s.name == "SampleScene") Advance();
            else if (cur == "town" && s.name == "City") Advance();
            else if (cur == "forest" && s.name == "Beginner Forest") Advance();
            else if (cur == "mine" && s.name == "Mine") Advance();
        }
        RefreshTracker(); // внутри — UpdateArrow под новую сцену
    }

    void DoIntroTeleport()
    {
        if (SceneTransition.PortalTransitionActive) return;
        mayorMet = true;
        SaveManager.Instance?.Save();
        if (ScreenFader.Instance != null) ScreenFader.Instance.SetBlackInstant();
        SceneTransition.LoadAtSpawn("City", "FromFarm");
    }

    void UpdateControlLesson()
    {
        if (done || CurrentId() != "sword" || SceneManager.GetActiveScene().name != "City")
        {
            movementSampleReady = false;
            return;
        }
        // Разговор открыт — стрелку не ставим (см. UpdateArrow): игрок читает
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsOpen)
        {
            movementSampleReady = false;
            return;
        }
        if (tutorialPlayer == null) tutorialPlayer = FindFirstObjectByType<PlayerMovement>();
        if (tutorialPlayer == null) return;
        Vector2 position = tutorialPlayer.transform.position;
        if (!tutorialPlayer.isActiveAndEnabled || tutorialPlayer.isAttacking || tutorialPlayer.isFishing
            || SceneTransition.PortalTransitionActive)
        {
            movementSampleReady = false;
            return;
        }
        if (movementSampleReady && !movementLearned)
        {
            bool hasInput = (tutorialPlayer.joystick != null && tutorialPlayer.joystick.Direction.sqrMagnitude > 0.01f)
                || Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.1f
                || Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.1f;
            float distance = Vector2.Distance(position, movementSample);
            if (hasInput && distance < 1f) movementDistance += distance;
            if (movementDistance >= 0.6f)
            {
                movementLearned = true;
                SaveManager.Instance?.Save();
            }
        }
        movementSample = position;
        movementSampleReady = true;
        PointAtMayorLesson();
    }

    void SetControlHint(string hint)
    {
        if (controlHint == hint) return;
        controlHint = hint;
        if (trackerLabel != null && CurrentId() == "sword")
            trackerLabel.text = "Дело: " + hint + "\n<size=70%>Долг: 500 000 кредитов</size>";
    }

    void PointAtMayorLesson()
    {
        if (SceneManager.GetActiveScene().name != "City")
        {
            SetControlHint("Отправляйся в город к мэру");
            QuestArrow.SetRoute("City", null, "Мэр");
            return;
        }
        if (tutorialPlayer == null) tutorialPlayer = FindFirstObjectByType<PlayerMovement>();
        if (tutorialPlayer == null || !tutorialPlayer.isActiveAndEnabled)
        {
            QuestArrow.Clear();
            return;
        }
        if (!movementLearned)
        {
            SetControlHint("Потяни джойстик, чтобы идти. На ПК — WASD или стрелки");
            if (tutorialPlayer.joystick != null)
                QuestArrow.SetUITarget(tutorialPlayer.joystick.transform as RectTransform, "Движение");
            else QuestArrow.Clear();
            return;
        }
        var mayor = FindFirstObjectByType<MayorNPC>();
        if (mayor == null)
        {
            SetControlHint("Найди мэра в городе");
            QuestArrow.Clear();
            return;
        }
        var inter = mayor.GetComponent<NPCInteractable>();
        var detector = tutorialPlayer.ActiveDetector;
        bool near = inter != null && Vector2.Distance(tutorialPlayer.transform.position, mayor.transform.position) <= inter.talkRadius;
        bool canTalk = near && detector != null && ReferenceEquals(detector.FindClosestInteractable(), inter);
        if (canTalk)
        {
            SetControlHint("Нажми кнопку атаки рядом с мэром — это разговор. На ПК — пробел");
            if (attackButton == null)
            {
                Canvas canvas = trackerRoot != null ? trackerRoot.GetComponentInParent<Canvas>() : null;
                if (canvas != null)
                    foreach (var button in canvas.GetComponentsInChildren<Button>(true))
                        if (button.name == "AttackButton") { attackButton = button.transform as RectTransform; break; }
            }
            if (attackButton != null) QuestArrow.SetUITarget(attackButton, "Поговорить");
            else QuestArrow.SetTarget(() => mayor != null ? (Vector3?)mayor.transform.position : null, "Поговорить", 0f);
        }
        else
        {
            SetControlHint(near ? "Повернись лицом к мэру" : "Подойди к мэру по стрелке");
            QuestArrow.SetTarget(() => mayor != null ? (Vector3?)mayor.transform.position : null, "Мэр", 0f);
        }
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
        UpdateControlLesson();
        // Шаг меча закрывается по факту закрытия диалога (см. SwordReady)
        if (!done && swordPending
            && (DialogueManager.Instance == null || !DialogueManager.Instance.IsOpen))
        {
            swordPending = false;
            OnEvent("sword");
        }
        // Стрелка протухает (набрал воду, убил слайма, подошёл) — обновляем по таймеру
        arrowRefreshT -= Time.deltaTime;
        if (arrowRefreshT <= 0f)
        {
            arrowRefreshT = 1.5f;
            if (!done) UpdateArrow();
            RefreshHint(); // дотянуть хинт после закрытия диалога и т.п.
        }
        // Урок хотбара: меч (или мотыга) переехал вниз — шаг закрыт (опрос полисекундный, дёшево)
        if (!done && CurrentId() == "ui_hotbar")
        {
            uiPollT -= Time.deltaTime;
            if (uiPollT <= 0f)
            {
                uiPollT = 0.5f;
                if (QuestGive.HasHotbarItem("WoodSword_Common") || QuestGive.HasHotbarItem("Hoe")) Advance();
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
            if (!mayorMet) DoIntroTeleport();
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
        if (evt == "mine" && cur == "ore")
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
        // Открытие книги без покупки перка шаг не закрывает — только skills_spent.
        // (Раньше общий evt==cur внизу закрывал этап сразу при открытии.)
        if (evt == "skills" && cur == "skills") { RefreshTracker(); return; }
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
        // Финал Акта 1: доклад мэру (роль mayor шлёт talk_mayor из NPCInteractable)
        if (evt == "talk_mayor" && cur == "act1_finish") { Advance(); return; }
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

    // ── Контекстные подсказки (панель TutorialHint) ──
    private GameObject boundHintRoot;
    private TMP_Text boundHintText;
    private string hintStep = "";
    private bool hintBuilt;
    private bool hintDlgWasOpen;

    public void BindTracker(GameObject root, TMP_Text label, Button skip)
    {
        boundRoot = root;
        boundLabel = label;
        if (skip != null) skip.onClick.AddListener(SkipTutorial);
        uiBuilt = false; // заставить EnsureTrackerUI подхватить бинд
        RefreshTracker();
    }

    // Панель-подсказка: короткий текст что делать + закрытие по клику.
    // Показывается один раз на шаг (закрыл — молчит до следующего шага).
    public void BindHint(GameObject root, TMP_Text text, Button close)
    {
        boundHintRoot = root;
        boundHintText = text;
        if (close != null) close.onClick.AddListener(HideHintByUser);
        hintStep = ""; // заставить показать заново
        hintBuilt = true;
        RefreshHint();
    }

    public void HideHint()
    {
        if (boundHintRoot != null) boundHintRoot.SetActive(false);
    }

    // Закрытие игроком (кнопка): помечаем шаг показанным — после диалогов не воскресать.
    public void HideHintByUser()
    {
        hintStep = CurrentId();
        HideHint();
    }

    // Синхронно из DialogueManager при открытии разговора — иначе хинт, показанный
    // в onTalk (до StartDialogue), мигает ~1.5с поверх диалога до таймера RefreshHint.
    // Заодно гасим стрелку сразу (иначе до 1.5с висит старая цель, напр. рюкзак,
    // пока игрок ещё читает диалог).
    public static void HideHintStatic()
    {
        if (_instance != null) _instance.HideHint();
        QuestArrow.Clear();
    }

    void RefreshHint()
    {
        if (boundHintRoot == null)
        {
            EnsureHintUI();
            if (boundHintRoot == null) return;
        }
        if (done)
        {
            if (boundHintRoot.activeSelf) boundHintRoot.SetActive(false);
            return;
        }
        string cur = CurrentId();
        // Диалог открыт — хинт прячем и «показанным» НЕ считаем. Флаг былОткрыт нужен:
        // onTalk срабатывает ДО StartDialogue, и пометка, поставленная в тот же кадр,
        // иначе убила бы показ после закрытия разговора.
        bool dlg = DialogueManager.Instance != null && DialogueManager.Instance.IsOpen;
        if (dlg)
        {
            if (boundHintRoot.activeSelf) boundHintRoot.SetActive(false);
            hintDlgWasOpen = true;
            return;
        }
        if (hintDlgWasOpen) { hintDlgWasOpen = false; hintStep = ""; } // разговор закрылся — показать шаг
        if (hintStep == cur)
        {
            // Тот же шаг, но текст мог измениться (хинт clear после первой добычи) —
            // обновляем живьём, закрытый вручную не трогаем.
            if (boundHintRoot.activeSelf && boundHintText != null)
                boundHintText.text = HintFor(cur);
            return;
        }
        hintStep = cur;
        string h = HintFor(cur);
        if (string.IsNullOrEmpty(h))
        {
            if (boundHintRoot.activeSelf) boundHintRoot.SetActive(false);
            return;
        }
        if (boundHintText != null) boundHintText.text = h;
        if (!boundHintRoot.activeSelf) boundHintRoot.SetActive(true);
    }

    // Короткие подсказки по шагам: хинт говорит КАК делать, трекер — ЧТО.
    // Не static: текст clear зависит от счётчика (с первой добычи — про подбор).
    string HintFor(string step)
    {
        switch (step)
        {
            case "intro": return ""; // книга открыта — хинт не нужен
            case "sword": return "Подойди к мэру вплотную и жми кнопку атаки — это разговор.";
            case "ui_inv": return "Нажми на подсвеченный рюкзак и найди там меч.";
            case "ui_hotbar": return "Зажми меч в рюкзаке и тащи его в нижний ряд. Потом нажми на меч там.";
            case "home": return "Иди на юг города — там портал на ферму.";
            case "clear": return farmkills > 0
                ? "Выпала добыча! Подойди к ней вплотную, потом добей остальных."
                : "Выбери меч в нижнем ряду и бей слаймов кнопкой атаки.";
            case "loot": return "Подойди к добыче вплотную — она подберётся сама.";
            case "tools": return "Вернись к мэру за инструментами для посадки.";
            case "hoe1":
            case "hoe5": return "Мотыга уже в руках. Бей ею по земле кнопкой атаки (на ПК — пробел).";
            case "plant": return "Выбери семена в хотбаре и посади их во вскопанные грядки кнопкой атаки.";
            case "water": return WaterHint();
            case "town": return "Иди в город Заря через портал.";
            case "city": return "Поговори со всеми: Мира, Степан, Густав, Дрон.";
            case "seeds": return "Поговори с Мартой и купи пшеницу.";
            case "harvest": return HarvestHint();
            case "pickup_harvest": return "Подойди к срезанной пшенице — она подберётся сама.";
            case "sell": return "Поговори с Дроном — продай кости кнопками ×1 / Всё. Пшеницу береги для хлеба.";
            case "bread": return "Отдай Густаву 2 пшеницы — он испечёт хлеб.";
            case "eat": return "Открой рюкзак и нажми на хлеб, чтобы съесть.";
            case "skills": return "Открой книгу навыков и возьми любой перк.";
            case "rod": return "Найди Морека на пляже и забери удочку.";
            case "fish": return "Встань лицом к воде, ударь с удочкой и жди «КЛЮЁТ!».";
            case "forest": return "Иди в Лес Новичков через портал.";
            case "kill": return "Убей 3 слаймов в лесу. Не давай себя окружить.";
            case "pickaxe": return "Поговори со Степаном и забери кирку.";
            case "mine": return "Войди в шахту.";
            case "ore": return "Бей жилы киркой. Нужно 5 штук.";
            case "forge": return "Открой кузницу у Степана и улучши предмет.";
            case "equip": return "Открой экипировку и надень улучшенное.";
            case "act1_finish": return "Вернись к мэру с докладом.";
            default: return "";
        }
    }

    // Хинт полива по факту: лейка полна — поливай; лейка в руках пустая — к колодцу
    // (там два нажатия: поднять ведро, потом забрать воду — второе подсказывает
    // сам колодец: "[Колодец] Ведро поднято! Нажми ещё раз..."); лейки в руках нет — выбери.
    string WaterHint()
    {
        var hb = HotbarManager.Instance;
        var slot = hb != null ? hb.GetActiveSlot() : null;
        if (slot != null && slot.IsWateringCan() && slot.HasWater())
            return "Лейка полна. Полей посаженные грядки.";
        if (slot != null && slot.IsWateringCan())
            return "Возьми лейку, подойди к колодцу и нажми атаку. Как ведро поднимется — нажми ещё раз, чтобы набрать воду.";
        return "Выбери лейку в хотбаре в руки.";
    }

    // Хинт сбора по факту: не созрело — сказать ждать, а не гнать с серпом
    string HarvestHint()
    {
        if (FarmManager.Instance != null && !FarmManager.Instance.HasRipeCrop())
            return "Пшеница ещё растёт. Подожди — она доспеет сама.";
        return "Возьми серп в руки и срежь спелую пшеницу кнопкой атаки.";
    }

    void EnsureHintUI()
    {
        if (hintBuilt && boundHintRoot != null) return;
        // 1) Сценовой биндер
        var found = FindObjectsByType<TutorialHintBinder>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (found != null && found.Length > 0 && found[0] != null)
        {
            found[0].ApplyBind();
            if (boundHintRoot != null) return;
        }
        // 2) Объекты по именам (владелец собрал руками без биндера)
        GameObject byName = FindUIByName("TutorialHint");
        if (byName != null)
        {
            TMP_Text t = null;
            Button c = null;
            foreach (var tmp in byName.GetComponentsInChildren<TMP_Text>(true))
                if (tmp.name.Trim() == "TutorialHintText") { t = tmp; break; }
            if (t == null) t = byName.GetComponentInChildren<TMP_Text>(true);
            foreach (var b in byName.GetComponentsInChildren<Button>(true))
                if (b.name.Trim() == "TutorialHintClose") { c = b; break; }
            BindHint(byName, t, c);
            if (boundHintRoot != null) return;
        }
        // 3) Кодовый фолбэк: панель справа
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;
        GameObject root = new GameObject("TutorialHint (auto)");
        root.transform.SetParent(canvas.transform, false);
        RectTransform rt = root.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0.5f);
        rt.anchorMax = new Vector2(1f, 0.5f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-12f, 40f);
        rt.sizeDelta = new Vector2(440f, 150f);
        Image bg = root.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);
        Button btn = root.AddComponent<Button>(); // клик куда-нибудь по панели = закрыть
        btn.onClick.AddListener(HideHintByUser);
        GameObject tGo = new GameObject("TutorialHintText");
        tGo.transform.SetParent(root.transform, false);
        RectTransform trt = tGo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(14f, 10f);
        trt.offsetMax = new Vector2(-14f, -10f);
        TMP_Text label = tGo.AddComponent<TextMeshProUGUI>();
        label.fontSize = 20f;
        label.color = new Color(1f, 0.97f, 0.85f);
        label.textWrappingMode = TextWrappingModes.Normal;
        label.raycastTarget = false; // клики уходят в кнопку панели
        BindHint(root, label, btn);
    }

    static GameObject FindUIByName(string name)
    {
        var all = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var r in all)
            if (r != null && r.name.Trim() == name) return r.gameObject;
        return null;
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
            text = "Дело: " + (CurrentId() == "sword" && !string.IsNullOrEmpty(controlHint) ? controlHint : stepTexts[progress]);
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
            if (CurrentId() == "ore") text += " (" + Mathf.Min(mines, MINES_NEED) + "/" + MINES_NEED + ")";
            text += "\n<size=70%>Долг: 500 000 кредитов</size>";
        }
        trackerLabel.text = text;
        UpdateArrow();
        RefreshHint();
    }

    // Перепривязка после смены сцены: Canvas пересоздался — трекер убит вместе с ним.
    void OnEnable() { RefreshTracker(); }

    // ── Стрелка путеводителя под текущий шаг ──
    void UpdateArrow()
    {
        if (done) { QuestArrow.Clear(); return; }
        // Разговор открыт — стрелка молчит (иначе убегает вперёд по шагу, пока читают)
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsOpen)
        {
            QuestArrow.Clear();
            return;
        }
        string cur = CurrentId();
        string here = SceneManager.GetActiveScene().name;

        switch (cur)
        {
            case "intro":
                QuestArrow.Clear(); // книга открыта — стрелка не нужна
                break;
            case "sword":
                PointAtMayorLesson();
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
                // Одна кнопка на рюкзак+экипировку (дубликат-рюкзак скрыт в сцене):
                // EquipmentUI.Open тянет за собой и инвентарь.
                PointAtUIOpener("ui_inv", "Рюкзак",
                    () => FindOpenerByClick<EquipmentUI>("Toggle"), "EquipmentButton", "InventoryButton");
                break;
            case "ui_hotbar":
                PointAtUIOpener("ui_hotbar", "Хотбар", null, "Hotbar");
                break;
            case "loot":
                if (here == "SampleScene")
                    QuestArrow.SetTarget(() => NearestLoot(), "Добыча");
                else QuestArrow.SetRoute("SampleScene", null, "Ферма №9");
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
            case "town":
                if (here == "City") QuestArrow.Clear();
                else QuestArrow.SetRoute("City", null, "Город Заря");
                break;
            case "pickaxe":
            case "forge":
                PointAtNPC<BlacksmithNPC>("City", "Степан");
                break;
            case "equip":
                PointAtUIOpener("equip", "Экипировка",
                    () => FindOpenerByClick<EquipmentUI>("Toggle"), "EquipmentButton");
                break;
            case "bread":
                PointAtNPC<CookNPC>("City", "Густав · хлеб");
                break;
            case "eat":
                PointAtUIOpener("eat", "Рюкзак · хлеб",
                    () => FindOpenerByClick<EquipmentUI>("Toggle"), "EquipmentButton", "InventoryButton");
                break;
            case "seeds":
                PointAtSeedSeller();
                break;
            case "skills":
                // Кнопка книги — SkillNodeButton (в имени хвостовой пробел, ищем по Trim)
                PointAtUIOpener("skills", "Книга навыков",
                    () => FindOpenerByClick<SkillTreeUI>("Toggle"), "SkillNodeButton");
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
            case "forest":
                if (here == "Beginner Forest") QuestArrow.Clear();
                else QuestArrow.SetRoute("Beginner Forest", null, "Лес Новичков");
                break;
            case "mine":
                if (here == "Mine") QuestArrow.Clear();
                else QuestArrow.SetRoute("Mine", null, "Шахта");
                break;
            case "ore":
                if (here == "Mine")
                    QuestArrow.SetTarget(() => NearestVein(), "Жила");
                else QuestArrow.SetRoute("Mine", null, "Шахта");
                break;
            case "pickup_harvest":
                QuestArrow.SetTarget(() => NearestHarvestLoot(), "Урожай");
                break;
            case "act1_finish":
                PointAtNPC<MayorNPC>("City", "Мэр · доклад");
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

    // Стрелка на кнопку HUD (панель/рюкзак/хотбар/книга/экипировка).
    // Сначала ищем по обработчику клика (переименования не страшны):
    // StatsUI.Open, InventoryUI.ToggleInventory, SkillTreeUI.Toggle, EquipmentUI.Toggle.
    // Запасной путь — по имени (StatsPanel-виджет, InventoryButton, Hotbar,
    // SkillNodeButton с хвостовым пробелом, EquipmentButton).
    // Окно-однофамилец (StatsPanel-окно) скрыто — берём только видимый объект,
    // при нескольких совпадениях предпочитаем тот, у кого Button на себе.
    void PointAtUIOpener(string step, string caption, System.Func<RectTransform> primary, params string[] names)
    {
        if (uiArrowStep != step || uiArrowTarget == null || !uiArrowTarget.gameObject.activeInHierarchy)
        {
            uiArrowStep = step;
            uiArrowTarget = null;
            try { uiArrowTarget = primary != null ? primary() : null; } catch { uiArrowTarget = null; }
            if (uiArrowTarget == null) uiArrowTarget = FindActiveUI(names);
        }
        if (uiArrowTarget != null) QuestArrow.SetUITarget(uiArrowTarget, caption);
        else QuestArrow.Clear();
    }

    static RectTransform FindOpenerByClick<T>(string method) where T : MonoBehaviour
    {
        var btns = FindObjectsByType<Button>(FindObjectsSortMode.None); // только активные
        foreach (var b in btns)
        {
            if (b == null) continue;
            var clicks = b.onClick;
            int n = clicks.GetPersistentEventCount();
            for (int i = 0; i < n; i++)
            {
                if (clicks.GetPersistentMethodName(i) != method) continue;
                if (clicks.GetPersistentTarget(i) is T)
                    return b.transform as RectTransform;
            }
        }
        return null;
    }

    static RectTransform FindActiveUI(string[] names)
    {
        var all = FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var n in names)
        {
            string tn = n.Trim();
            RectTransform fallback = null;
            int total = 0, inactive = 0;
            foreach (var r in all)
            {
                if (r == null || r.name.Trim() != tn) continue;
                total++;
                if (!r.gameObject.activeInHierarchy) { inactive++; continue; }
                if (r.GetComponent<Button>() != null) return r; // кнопка-открывашка, не окно
                if (fallback == null) fallback = r;
            }
            if (fallback != null) return fallback;
            // Второй шанс: частичное совпадение (переименовали с опечаткой) + диагностика
            foreach (var r in all)
            {
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                if (r.name.IndexOf(tn, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                Debug.LogWarning("[Обучение] Кнопка '" + tn + "' точно не найдена — беру похожую '" + r.name + "'.");
                return r;
            }
            Debug.LogWarning("[Обучение] Кнопка '" + tn + "' не найдена (совпадений: " + total + ", скрытых: " + inactive + ").");
        }
        return null;
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

    // Ближайший лежащий лут (шаг loot); урожайный — с farmingXpReward>0 (шаг pickup_harvest).
    // Пусто (всё подобрано) → null → стрелка прячется, текст трекера ведёт сам.
    Vector3? NearestLoot() => NearestLootInternal(false);
    Vector3? NearestHarvestLoot() => NearestLootInternal(true);

    Vector3? NearestLootInternal(bool harvestOnly)
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return null;
        Vector3 pp = player.transform.position;
        float best = float.MaxValue;
        Vector3? res = null;
        var all = FindObjectsByType<LootItem>(FindObjectsSortMode.None);
        foreach (var l in all)
        {
            if (l == null || l.itemData == null) continue;
            if (harvestOnly && l.farmingXpReward <= 0) continue;
            float d = Vector2.Distance(pp, l.transform.position);
            if (d < best) { best = d; res = l.transform.position; }
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
            toolsGiven = toolsGiven, pickGiven = pickGiven, skillsGrant = skillsGrant,
            movementLearned = movementLearned
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
            movementLearned = s.movementLearned;
            movementSampleReady = false;
            movementDistance = 0f;
            controlHint = "";
        }
        catch (System.Exception e) { Debug.LogWarning("[Обучение] Битый сейв, начинаем заново: " + e.Message); }
        RefreshTracker();
    }
}
