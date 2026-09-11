using UnityEngine;

/// <summary>
/// Путеводитель: мэр на ферме (вторая копия, у дома).
/// КАК СТАВИТЬ: в City дублируй мэра → Cut → открой SampleScene → Paste
/// рядом с домом → переименуй в FarmMayor. Диалог и набор подтянутся сами.
/// Появляется только на шаге tools ("приходит" после зачистки),
/// после обучения уходит обратно в город.
/// Набор новичка: мотыга + серп + лейка + 3 семени пшеницы.
/// </summary>
[RequireComponent(typeof(NPCInteractable))]
public class FarmMayorNPC : MonoBehaviour
{
    const string DIALOGUE_PATH = "Dialogue/MayorFarm_Dialogue";

    void Awake()
    {
        var inter = GetComponent<NPCInteractable>();
        if (inter != null)
        {
            var d = Resources.Load<DialogueData>(DIALOGUE_PATH);
            if (d != null) inter.dialogue = d;
            else Debug.LogWarning("[Мэр] Нет ассета " + DIALOGUE_PATH + " — прогони Tools → Lore → 1. Build Dialogues.");
        }
    }

    void Start()
    {
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.onDialogueAction += OnDialogueAction;
        StartCoroutine(ApplyVisibility());
    }

    void OnDestroy()
    {
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.onDialogueAction -= OnDialogueAction;
    }

    // Видимость по шагу: приходит после зачистки, уходит после обучения
    System.Collections.IEnumerator ApplyVisibility()
    {
        yield return null;
        yield return null;
        gameObject.SetActive(TutorialManager.IsToolsStepOrLater());
    }

    // Кнопка "Забрать набор" (Custom GiveTools) — только свой диалог
    void OnDialogueAction(DialogueActionType action, string param)
    {
        if (action != DialogueActionType.Custom || param != "GiveTools") return;
        if (DialogueManager.Instance == null || DialogueManager.Instance.currentNPC == null) return;
        if (DialogueManager.Instance.currentNPC.gameObject != gameObject) return;

        Give("Hoe", 1);
        Give("Sickle", 1);
        Give("WateringCan", 1);
        Give("Wheat Seeds", 3);
        ActionLogUI.Show("[Мэр] Мотыга, серп, лейка и семена пшеницы — владей! Вскопай, посади, полей.");
        TutorialManager.Notify("tools");
    }

    void Give(string assetName, int count)
    {
        ItemData item = ItemDatabase.Find(assetName);
        if (item == null)
        {
            ActionLogUI.Show("[Мэр] На складе нет: " + assetName);
            return;
        }
        if (InventoryUI.Instance != null) InventoryUI.Instance.AddItem(item, count);
    }
}
