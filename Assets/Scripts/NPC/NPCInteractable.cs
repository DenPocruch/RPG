using UnityEngine;

/// <summary>
/// Обнаружение игрока рядом с NPC + индикатор "!" над головой.
/// Когда игрок входит в радиус — появляется восклицательный знак (NPC заметил).
/// Реализует IInteractable — при взаимодействии (кнопка атаки рядом) запускается
/// диалог. Сам диалог подключим на Этапе 3, пока — заглушка/событие.
/// </summary>
public class NPCInteractable : MonoBehaviour, IInteractable
{
    [Header("Обнаружение игрока")]
    public float detectRadius = 3f;
    [Tooltip("Насколько близко нужно подойти чтобы можно было заговорить")]
    public float talkRadius = 1.5f;

    [Header("Индикатор над головой")]
    public GameObject alertIcon;        // объект со спрайтом "!" (дочерний, выключен по старту)
    public float iconBobAmount = 0.1f;   // лёгкое покачивание
    public float iconBobSpeed = 3f;

    [Header("Диалог")]
    public DialogueData dialogue; // ассет диалога этого NPC
    [Tooltip("Если false — вместо диалога срабатывает onDirectInteract (напр. кузнец у станка открывает ковку сразу)")]
    public bool dialogueEnabled = true;

    // Прямое взаимодействие без диалога (BlacksmithNPC подпишется чтобы открыть ковку)
    public System.Action onDirectInteract;

    [Header("Реакция NPC")]
    [Tooltip("Останавливать патруль когда игрок замечен (NPC поворачивается к игроку)")]
    public bool pauseWhenSpotted = false;

    // Событие — с NPC начали говорить (Этап 3 подпишет сюда открытие диалога)
    public System.Action onTalk;

    private Transform player;
    private NPCController npc;
    private NPCAnimator npcAnim;
    private bool playerInRange = false;
    private Vector3 iconBasePos;

    void Start()
    {
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;

        npc = GetComponent<NPCController>();
        npcAnim = GetComponent<NPCAnimator>();

        if (alertIcon != null)
        {
            iconBasePos = alertIcon.transform.localPosition;
            alertIcon.SetActive(false);
        }
    }

    void Update()
    {
        if (player == null) return;

        float dist = Vector2.Distance(transform.position, player.position);
        bool inRange = dist <= detectRadius;

        // Появление/исчезновение знака
        if (inRange && !playerInRange) OnPlayerSpotted();
        else if (!inRange && playerInRange) OnPlayerLost();
        playerInRange = inRange;

        // Покачивание знака
        if (alertIcon != null && alertIcon.activeSelf)
        {
            float bob = Mathf.Sin(Time.time * iconBobSpeed) * iconBobAmount;
            alertIcon.transform.localPosition = iconBasePos + Vector3.up * bob;
        }
    }

    void OnPlayerSpotted()
    {
        if (alertIcon != null) alertIcon.SetActive(true);

        if (pauseWhenSpotted && npc != null)
            npc.aiPaused = true; // остановиться и смотреть на игрока
    }

    void OnPlayerLost()
    {
        if (alertIcon != null) alertIcon.SetActive(false);

        if (pauseWhenSpotted && npc != null)
            npc.aiPaused = false; // продолжить патруль
    }

    // ── IInteractable ──────────────────────────────────────────
    public Transform GetTransform() => transform;

    public void Interact(GameObject playerObj)
    {
        // Проверяем что игрок достаточно близко для разговора
        if (player != null)
        {
            float dist = Vector2.Distance(transform.position, player.position);
            if (dist > talkRadius)
            {
                Debug.Log("[NPC] Подойди ближе чтобы поговорить");
                return;
            }
        }

        // Режим прямого взаимодействия (без диалога) — напр. кузнец у станка
        if (!dialogueEnabled)
        {
            onDirectInteract?.Invoke();
            return;
        }

        // Замораживаем NPC на время разговора и поворачиваем к игроку
        if (npc != null) npc.aiPaused = true;
        FacePlayer();

        onTalk?.Invoke(); // NPC-специфичная логика (повар выставит условия диалога)

        // Открываем диалог
        if (dialogue != null && DialogueManager.Instance != null)
        {
            // Запоминаем с кем говорят — обработчики действий (TraderNPC и т.д.)
            // сверяют себя с этим, чтобы реагировать только на свой диалог
            DialogueManager.Instance.currentNPC = this;

            // По окончании диалога — вернуть NPC к патрулю
            DialogueManager.Instance.onDialogueEnd = EndTalk;
            DialogueManager.Instance.StartDialogue(dialogue);
        }
        else
        {
            Debug.Log("[NPC] Нет диалога — просто разговор");
        }
    }
    // ───────────────────────────────────────────────────────────

    // Повернуть NPC лицом к игроку
    public void FacePlayer()
    {
        if (player == null || npcAnim == null) return;

        Vector2 toPlayer = (player.position - transform.position);
        NPCAnimator.AnimDir dir;

        if (Mathf.Abs(toPlayer.x) > 0.3f)
            dir = toPlayer.x > 0 ? NPCAnimator.AnimDir.Right : NPCAnimator.AnimDir.Left;
        else
            dir = toPlayer.y > 0 ? NPCAnimator.AnimDir.Up : NPCAnimator.AnimDir.Down;

        npcAnim.PlayState(NPCAnimator.AnimState.Idle, dir, true);
    }

    // Вызывается когда диалог закончился — вернуть NPC к патрулю
    public void EndTalk()
    {
        if (npc != null) npc.aiPaused = false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, detectRadius);
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, talkRadius);
    }
}