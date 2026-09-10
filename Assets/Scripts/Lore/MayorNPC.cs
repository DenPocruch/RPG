using UnityEngine;

/// <summary>
/// Часть 3 лора: мэр Аврелий.
/// Вешается на дубликат любого городского NPC (там уже есть NPCController,
/// NPCAnimator, NPCInteractable, InteractZone, AlertIcon).
/// Сам подтягивает диалог Mayor_Dialogue из Resources — в инспекторе
/// ничего биндить не надо. Прогони Tools → Lore → 1. Build Dialogues.
/// </summary>
[RequireComponent(typeof(NPCInteractable))]
public class MayorNPC : MonoBehaviour
{
    const string DIALOGUE_PATH = "Dialogue/Mayor_Dialogue";

    void Awake()
    {
        var inter = GetComponent<NPCInteractable>();
        if (inter != null && inter.dialogue == null)
        {
            var d = Resources.Load<DialogueData>(DIALOGUE_PATH);
            if (d != null) inter.dialogue = d;
            else Debug.LogWarning("[Мэр] Нет ассета " + DIALOGUE_PATH + " — прогони Tools → Lore → 1. Build Dialogues.");
        }
        // Мэр патрулирует вместе со всеми (EndTalk в NPCInteractable всё равно
        // снял бы паузу после первого разговора) — так город живее.
    }
}
