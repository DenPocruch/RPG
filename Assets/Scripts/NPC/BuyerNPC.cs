using UnityEngine;

/// <summary>
/// NPC-скупщик урожая. Вешается рядом с NPCInteractable на объект скупщика.
/// Диалог скупщика (DialogueData) содержит вариант ответа с действием
/// OpenSell — после диалога открывается окно продажи.
/// </summary>
public class BuyerNPC : MonoBehaviour
{
    void Start()
    {
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.onDialogueAction += OnDialogueAction;
    }

    void OnDestroy()
    {
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.onDialogueAction -= OnDialogueAction;
    }

    void OnDialogueAction(DialogueActionType action, string param)
    {
        if (action != DialogueActionType.OpenSell) return;

        // Реагируем только на СВОЙ диалог
        if (DialogueManager.Instance == null || DialogueManager.Instance.currentNPC == null)
            return;
        if (DialogueManager.Instance.currentNPC.gameObject != gameObject) return;

        SellUI.Instance?.Open();
    }
}
