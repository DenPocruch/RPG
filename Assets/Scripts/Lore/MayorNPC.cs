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
        // Диалог мэра ставим ВСЕГДА (дубликат тащит чужой диалог повара/Дрона).
        // Мэр патрулирует вместе со всеми — так город живее.
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
        var inter = GetComponent<NPCInteractable>();
        if (inter != null) inter.onTalk += OnTalkStart;
    }

    void OnDestroy()
    {
        if (DialogueManager.Instance != null)
            DialogueManager.Instance.onDialogueAction -= OnDialogueAction;
        var inter = GetComponent<NPCInteractable>();
        if (inter != null) inter.onTalk -= OnTalkStart;
    }

    // Умный диалог: кнопки по стадиям (старое пропадает, новое открывается)
    // + входной узел по стадии: мэр встречает текущим делом, а не хабом
    void OnTalkStart()
    {
        var inter = GetComponent<NPCInteractable>();
        if (inter != null)
        {
            string cur = TutorialManager.CurrentStep();
            if (cur == "sword")
            {
                bool granted = GiveSword();
                inter.forceStartNode = granted ? 7 : 9; // 9 — запасной узел повтора (практически недостижим: выдача в рюкзак)
            }
            else if (cur == "home" || cur == "clear") inter.forceStartNode = 8; // напоминание
            else if (cur == "tools") inter.forceStartNode = 6;  // хвала + набор
            else inter.forceStartNode = -1;                     // дальше — свободный хаб
        }
        if (DialogueManager.Instance == null) return;
        DialogueManager.Instance.SetCondition("mayor_sword", TutorialManager.IsSwordNeeded() && !TutorialManager.SwordGiven());
        DialogueManager.Instance.SetCondition("mayor_clear", TutorialManager.IsClearPending());
        DialogueManager.Instance.SetCondition("mayor_tools", TutorialManager.IsToolsStepOrLater());
    }

    // Кнопки Custom — только свой диалог
    void OnDialogueAction(DialogueActionType action, string param)
    {
        if (action != DialogueActionType.Custom) return;
        if (DialogueManager.Instance == null || DialogueManager.Instance.currentNPC == null) return;
        if (DialogueManager.Instance.currentNPC.gameObject != gameObject) return;

        if (param == "GiveSword")
        {
            GiveSword();
            return;
        }
        if (param != "GiveTools") return;
        if (!TutorialManager.TakeToolsOnce())
        {
            ActionLogUI.Show("[Мэр] Набор я тебе уже выдал, пациент! Глянь хотбар.");
            return;
        }

        if (!QuestGive.HasItem("Hoe")) Give("Hoe", 1, true); // мотыгу мог потерять — вернём
        Give("Sickle", 1);
        Give("WateringCan", 1);
        Give("Wheat Seeds", 6);
        if (CurrencyManager.Instance != null) CurrencyManager.Instance.AddGold(100);
        ActionLogUI.Show("[Мэр] Серп, лейка и 6 пшениц — в хотбаре! Плюс 100g на семена у Марты. Вскопай 6 грядок, посади, полей.");
        TutorialManager.Notify("tools");
    }

    // Выдача меча: меч падает В РЮКЗАК (а не в руки) — уроки ui_inv/ui_hotbar
    // учат найти его и перетащить в хотбар (стандарт жанра: награда → учим пользоваться).
    // Зовётся при ВХОДЕ в разговор (ушёл в вопросы — всё равно вооружён... то есть «орюкзачен»).
    bool GiveSword()
    {
        ItemData sword = ItemDatabase.Find("WoodSword_Common");
        if (sword == null)
        {
            ActionLogUI.Show("[Мэр] Мечей на складе нет... (Tools → Equipment → 1)");
            return false;
        }
        if (TutorialManager.SwordGiven())
        {
            TutorialManager.SwordReady(); // шаг закроется по закрытию диалога
            return true;
        }
        // В рюкзаке лежит ТОЛЬКО меч: урок ui_inv — найти один предмет.
        // Мотыга выдаётся позже, с набором (шаг tools).
        if (!TutorialManager.TakeSwordOnce()) return true;
        QuestGive.GiveToPack(sword, 1);
        SaveManager.Instance?.Save();
        ActionLogUI.Show("[Мэр] Меч уже в твоём рюкзаке! Открой его, перетащи меч в нижний ряд и нажми на него. Потом — на участок.");
        TutorialManager.SwordReady(); // шаг закроется по закрытию диалога
        return true;
    }

    void Give(string assetName, int count, bool select = false)
    {
        ItemData item = ItemDatabase.Find(assetName);
        if (item == null)
        {
            ActionLogUI.Show("[Мэр] На складе нет: " + assetName);
            return;
        }
        QuestGive.Give(item, count, select);
    }
}
