using UnityEngine;

/// <summary>
/// Кузнец-NPC с двумя режимами:
///
/// РАБОТАЕТ ЗА СТАНКОМ (Working): стоит у станка, играет анимация ковки.
///   Игрок подходит + кнопка атаки → сразу открывается ковка (без диалога),
///   как было раньше.
///
/// ГУЛЯЕТ (Wandering): патрулирует по сети дорог.
///   Игрок подходит + атака → диалог. Вариант "Выкуй мне вещь" (LeadToCraft)
///   → кузнец идёт к станку, игрок идёт следом. Когда кузнец у станка И игрок
///   рядом → ковка открывается автоматически.
///
/// Кузнец сам чередует работу и прогулки по таймеру.
/// Требует: NPCController, NPCInteractable, NPCAnimator.
/// </summary>
[RequireComponent(typeof(NPCController))]
[RequireComponent(typeof(NPCInteractable))]
[RequireComponent(typeof(NPCAnimator))]
public class BlacksmithNPC : MonoBehaviour
{
    [Header("Станок")]
    [Tooltip("Точка сети у станка (куда идёт нога)")]
    public Waypoint stationWaypoint;
    [Tooltip("Позиция во время КОВКИ — выверена под спрайт ковки (с наковальней)")]
    public Transform workSpot;
    [Tooltip("Позиция когда НЕ кует — выверена под обычный спрайт (Idle). Отсюда уходит гулять.")]
    public Transform restSpot;
    [Tooltip("В какую сторону смотрит кузнец за станком")]
    public NPCAnimator.AnimDir workFacing = NPCAnimator.AnimDir.Down;

    [Header("Тайминги (сек)")]
    public float minWorkTime = 15f;
    public float maxWorkTime = 30f;
    public float minWalkTime = 10f;
    public float maxWalkTime = 20f;

    [Header("Авто-открытие ковки")]
    [Tooltip("Насколько близко должен быть игрок чтобы ковка открылась когда кузнец привёл его к станку")]
    public float craftOpenRadius = 2f;

    private NPCController npc;
    private NPCInteractable interactable;
    private NPCAnimator anim;
    private Transform player;

    private enum BState { Working, GoingToStation, Wandering, LeadingPlayer }
    private BState bState;

    private float phaseTimer;

    void Start()
    {
        npc = GetComponent<NPCController>();
        interactable = GetComponent<NPCInteractable>();
        anim = GetComponent<NPCAnimator>();

        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;

        interactable.onDirectInteract += OpenCraftDirect;
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.onDialogueAction += OnDialogueAction;

        // Стартуем через кадр — чтобы NPCController.Start успел собрать сеть точек
        // (иначе GoTo падает: allWaypoints ещё пустой)
        Invoke(nameof(GoToStation), 0.1f);
    }

    void OnDestroy()
    {
        if (interactable != null) interactable.onDirectInteract -= OpenCraftDirect;
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.onDialogueAction -= OnDialogueAction;
    }

    // ═══════════════════════════════════════════════════════════
    // ВЗАИМОДЕЙСТВИЕ
    // ═══════════════════════════════════════════════════════════

    // Кузнец у станка → атака открывает ковку сразу (без диалога)
    void OpenCraftDirect()
    {
        if (CraftingUI.Instance != null) CraftingUI.Instance.Open();
    }

    // Из диалога (кузнец гуляет) выбрали "Выкуй мне вещь"
    void OnDialogueAction(DialogueActionType action, string param)
    {
        if (action != DialogueActionType.LeadToCraft) return;
        if (stationWaypoint == null) return;

        bState = BState.LeadingPlayer;
        npc.manualControl = true;
        npc.externalAnimation = false;
        npc.aiPaused = false;
        npc.GoTo(stationWaypoint);
        Debug.Log("[Кузнец] Ведёт игрока к станку");
    }

    // ═══════════════════════════════════════════════════════════
    // ЦИКЛ
    // ═══════════════════════════════════════════════════════════
    void Update()
    {
        switch (bState)
        {
            case BState.Working: TickWorking(); break;
            case BState.GoingToStation: TickGoingToStation(); break;
            case BState.Wandering: TickWandering(); break;
            case BState.LeadingPlayer: TickLeadingPlayer(); break;
        }
    }

    void TickWorking()
    {
        // Играем анимацию ковки (externalAnimation не даёт контроллеру перебить)
        anim.PlayState(NPCAnimator.AnimState.Work, workFacing);

        // Атака у станка → прямая ковка (диалог выключен)
        interactable.dialogueEnabled = false;

        phaseTimer -= Time.deltaTime;
        if (phaseTimer <= 0f)
            StartWandering();
    }

    void TickGoingToStation()
    {
        interactable.dialogueEnabled = true; // по пути с ним можно поговорить
        if (npc.IsAtWaypoint(stationWaypoint))
            StartWorking();
    }

    void TickWandering()
    {
        interactable.dialogueEnabled = true; // гуляет → диалог

        phaseTimer -= Time.deltaTime;
        if (phaseTimer <= 0f)
            GoToStation();
    }

    void TickLeadingPlayer()
    {
        interactable.dialogueEnabled = true;

        // Кузнец дошёл до станка?
        if (npc.IsAtWaypoint(stationWaypoint))
        {
            StartWorking();

            // Игрок рядом → открываем ковку автоматически
            if (player != null &&
                Vector2.Distance(transform.position, player.position) <= craftOpenRadius)
            {
                if (CraftingUI.Instance != null && !CraftingUI.Instance.IsOpen())
                    CraftingUI.Instance.Open();
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ПЕРЕХОДЫ
    // ═══════════════════════════════════════════════════════════
    void GoToStation()
    {
        bState = BState.GoingToStation;
        npc.manualControl = true;
        npc.externalAnimation = false;
        npc.aiPaused = false;
        npc.GoTo(stationWaypoint);
    }

    void StartWorking()
    {
        bState = BState.Working;
        phaseTimer = Random.Range(minWorkTime, maxWorkTime);

        // Встаём на позицию КОВКИ (выверена под спрайт с наковальней)
        Vector3 spot = transform.position;
        if (workSpot != null) spot = workSpot.position;
        else if (stationWaypoint != null) spot = stationWaypoint.transform.position;
        transform.position = new Vector3(spot.x, spot.y, transform.position.z);

        npc.aiPaused = true;  // стоим
        npc.externalAnimation = true;  // сами играем анимацию ковки
    }

    void StartWandering()
    {
        bState = BState.Wandering;
        phaseTimer = Random.Range(minWalkTime, maxWalkTime);

        // Перед уходом встаём на позицию покоя (выверена под обычный спрайт) —
        // так при смене спрайта ковки на Idle кузнец не "прыгнет"
        if (restSpot != null)
            transform.position = new Vector3(restSpot.position.x, restSpot.position.y, transform.position.z);

        npc.aiPaused = false;
        npc.externalAnimation = false;
        npc.manualControl = false; // включаем авто-патруль
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, craftOpenRadius);
    }
}