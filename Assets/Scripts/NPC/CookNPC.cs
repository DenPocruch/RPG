using UnityEngine;

/// <summary>
/// Полный цикл повара с интерьером.
/// Заказ → идёт к двери дома → телепорт в интерьер → идёт к плите →
/// стоит ждёт пока готовится → идёт к двери интерьера → телепорт наружу →
/// продолжает патруль (носит еду с собой). Игрок находит повара и через
/// диалог забирает еду.
///
/// Требует: NPCController, NPCInteractable.
/// </summary>
[RequireComponent(typeof(NPCController))]
[RequireComponent(typeof(NPCInteractable))]
public class CookNPC : MonoBehaviour
{
    [Header("Снаружи")]
    [Tooltip("Точка сети у входной двери дома (снаружи)")]
    public Waypoint exteriorDoorWaypoint;
    [Tooltip("Куда встать снаружи после выхода из дома")]
    public Transform exteriorSpawn;

    [Header("Интерьер")]
    [Tooltip("Точка появления внутри дома (у двери интерьера)")]
    public Transform interiorSpawn;
    [Tooltip("Точка сети у двери ВНУТРИ дома (куда идти чтобы выйти)")]
    public Waypoint interiorDoorWaypoint;
    [Tooltip("Точка сети у плиты (куда идти готовить)")]
    public Waypoint stoveWaypoint;

    [Header("Что прятать/показывать (повар в интерьере не виден на улице)")]
    public SpriteRenderer cookRenderer;
    public GameObject interactZone;
    public GameObject alertIcon;

    [Header("Условие диалога 'еда готова'")]
    public string foodReadyCondition = "food_ready";

    private NPCController npc;
    private NPCInteractable interactable;

    private enum CookState
    {
        Free,           // патрулирует (обычная жизнь)
        ToExteriorDoor, // идёт к входной двери снаружи
        ToStove,        // (в интерьере) идёт к плите
        Cooking,        // стоит у плиты, готовится
        ToInteriorDoor, // (в интерьере) идёт к двери на выход
    }
    private CookState cookState = CookState.Free;

    private bool cookUIWasOpen = false;

    void Start()
    {
        npc = GetComponent<NPCController>();
        interactable = GetComponent<NPCInteractable>();
        if (cookRenderer == null) cookRenderer = GetComponent<SpriteRenderer>();

        interactable.onTalk += OnTalkStart;
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.onDialogueAction += OnDialogueAction;
    }

    void OnDestroy()
    {
        if (interactable != null) interactable.onTalk -= OnTalkStart;
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.onDialogueAction -= OnDialogueAction;
    }

    // ═══════════════════════════════════════════════════════════
    // ДИАЛОГ
    // ═══════════════════════════════════════════════════════════
    void OnTalkStart()
    {
        bool ready = CookStorage.Instance != null &&
                     CookStorage.Instance.GetOutputSlot() != null &&
                     !CookStorage.Instance.GetOutputSlot().IsEmpty();

        if (DialogueManager.Instance != null)
            DialogueManager.Instance.SetCondition(foodReadyCondition, ready);
    }

    // Действие приходит УЖЕ после закрытия диалога (см. DialogueManager) —
    // можно открывать панель сразу, без задержек
    void OnDialogueAction(DialogueActionType action, string param)
    {
        if (action == DialogueActionType.OpenCook || action == DialogueActionType.CollectDishes)
        {
            if (CookUI.Instance != null) CookUI.Instance.Open();
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ЦИКЛ СОСТОЯНИЙ
    // ═══════════════════════════════════════════════════════════
    void Update()
    {
        bool uiOpen = CookUI.Instance != null && CookUI.Instance.IsOpen();
        if (cookUIWasOpen && !uiOpen) OnCookUIClosed();
        cookUIWasOpen = uiOpen;

        switch (cookState)
        {
            case CookState.ToExteriorDoor:
                if (npc.IsAtWaypoint(exteriorDoorWaypoint))
                    TeleportIntoHouse();
                break;

            case CookState.ToStove:
                if (npc.IsAtWaypoint(stoveWaypoint))
                    StartCooking();
                break;

            case CookState.Cooking:
                if (CookStorage.Instance == null || CookStorage.Instance.GetQueueCount() == 0)
                    GoToExit();
                break;

            case CookState.ToInteriorDoor:
                if (npc.IsAtWaypoint(interiorDoorWaypoint))
                    TeleportOutOfHouse();
                break;
        }
    }

    void OnCookUIClosed()
    {
        // Заказали блюда и повар свободен → отправляем к дому
        if (cookState != CookState.Free) return;
        if (CookStorage.Instance == null) return;

        if (CookStorage.Instance.GetQueueCount() > 0 && exteriorDoorWaypoint != null)
        {
            cookState = CookState.ToExteriorDoor;
            npc.manualControl = true; // берём управление, без авто-патруля
            npc.GoTo(exteriorDoorWaypoint);
            Debug.Log("[Повар] Пошёл к дому");
        }
    }

    void TeleportIntoHouse()
    {
        if (interiorSpawn == null || stoveWaypoint == null) return;

        // Прячем с улицы, переносим в интерьер
        npc.TeleportTo(interiorSpawn.position, interiorDoorWaypoint);
        cookState = CookState.ToStove;
        npc.GoTo(stoveWaypoint);
        Debug.Log("[Повар] Вошёл в дом, идёт к плите");
    }

    void StartCooking()
    {
        cookState = CookState.Cooking;
        npc.aiPaused = true; // стоит у плиты
        Debug.Log("[Повар] Готовит у плиты...");
    }

    void GoToExit()
    {
        npc.aiPaused = false;
        cookState = CookState.ToInteriorDoor;
        npc.GoTo(interiorDoorWaypoint);
        Debug.Log("[Повар] Доготовил, идёт на выход");
    }

    void TeleportOutOfHouse()
    {
        if (exteriorSpawn == null) return;

        // Возвращаемся наружу, к двери дома снаружи
        npc.TeleportTo(exteriorSpawn.position, exteriorDoorWaypoint);
        npc.manualControl = false; // возвращаем авто-патруль
        cookState = CookState.Free;
        Debug.Log("[Повар] Вышел из дома, продолжает дела (носит еду с собой)");
    }

    // На всякий случай — API если захочешь прятать повара (сейчас телепорт
    // в интерьер уводит его с улицы, отдельно прятать не нужно)
    void SetVisible(bool visible)
    {
        if (cookRenderer != null) cookRenderer.enabled = visible;
        if (interactZone != null) interactZone.SetActive(visible);
        if (alertIcon != null && !visible) alertIcon.SetActive(false);
    }
}