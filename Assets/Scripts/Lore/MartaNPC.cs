using UnityEngine;

/// <summary>
/// Марта, торговка семенами. Была прилавком ShopInteraction БЕЗ диалога —
/// переведена на диалог (паттерн мэра): NPCInteractable + TraderNPC.
/// Товар переезжает сам (TraderNPC.Awake забирает массивы у ShopInteraction).
/// Диалог DialogueSeedTrader подтягивается сам.
/// На шаге seeds открывает сразу совет (узел 12).
/// </summary>
[RequireComponent(typeof(NPCInteractable))]
public class MartaNPC : MonoBehaviour
{
    const string DIALOGUE_PATH = "Dialogue/DialogueSeedTrader";

    void Awake()
    {
        var inter = GetComponent<NPCInteractable>();
        if (inter != null && inter.dialogue == null)
        {
            var d = Resources.Load<DialogueData>(DIALOGUE_PATH);
            if (d != null) inter.dialogue = d;
            else Debug.LogWarning("[Марта] Нет ассета " + DIALOGUE_PATH + " — прогони Tools → Lore → 1. Build Dialogues.");
        }
    }

    void Start()
    {
        var inter = GetComponent<NPCInteractable>();
        if (inter != null) inter.onTalk += OnTalkStart;
    }

    void OnDestroy()
    {
        var inter = GetComponent<NPCInteractable>();
        if (inter != null) inter.onTalk -= OnTalkStart;
    }

    void OnTalkStart()
    {
        var inter = GetComponent<NPCInteractable>();
        if (inter != null)
            inter.forceStartNode = TutorialManager.CurrentStep() == "seeds" ? 12 : -1;
    }
}
