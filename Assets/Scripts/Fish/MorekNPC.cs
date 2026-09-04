using UnityEngine;

/// <summary>
/// Морек как нормальный NPC: диалог (реплики — в ассете Dialogue/Morek),
/// кнопки диалога дёргают панели через Custom-действия.
/// Условие "morek_norod" прячет пункт удочки после подарка.
/// </summary>
public class MorekNPC : MonoBehaviour
{
    public const string NoRodTag = "morek_norod";

    void Start()
    {
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.onDialogueAction += OnDialogueAction;
            var inter = GetComponent<NPCInteractable>();
            if (inter != null) inter.onTalk += OnTalk;
        }
    }

    void OnDestroy()
    {
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.onDialogueAction -= OnDialogueAction;
    }

    void OnTalk()
    {
        bool needRod = FishingController.Instance == null || !FishingController.Instance.HasRodGift();
        DialogueManager.Instance?.SetCondition(NoRodTag, needRod);
    }

    void OnDialogueAction(DialogueActionType action, string param)
    {
        if (action != DialogueActionType.Custom) return;
        if (DialogueManager.Instance == null || DialogueManager.Instance.currentNPC == null) return;
        if (DialogueManager.Instance.currentNPC.gameObject != gameObject) return;
        if (MorekUI.Instance == null) return;

        switch (param)
        {
            case "GiveRod": MorekUI.Instance.GiveRod(); break;
            case "SellFish":
                MorekUI.Instance.Close();
                if (SellUI.Instance != null) SellUI.Instance.OpenFish();
                break;
            case "Collection": MorekUI.Instance.ToggleCollection(); break;
        }
    }
}
