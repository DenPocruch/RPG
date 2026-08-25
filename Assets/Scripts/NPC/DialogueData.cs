using UnityEngine;

/// <summary>
/// Что делает вариант ответа помимо перехода к следующей реплике.
/// Диалог-менеджер шлёт это событием, а NPC (повар и т.д.) реагирует.
/// </summary>
public enum DialogueActionType
{
    None,          // просто перейти дальше / закрыть
    OpenCook,      // открыть панель готовки (заказать блюдо)
    CollectDishes, // забрать готовую еду
    LeadToCraft,   // кузнец ведёт игрока к станку → авто-открытие ковки
    GiveQuest,     // выдать квест (на будущее)
    OpenShop,      // открыть магазин этого торговца (TraderNPC)
    Custom         // произвольное действие по actionParam
}

/// <summary>Вариант ответа игрока.</summary>
[System.Serializable]
public class DialogueOption
{
    public string text;                          // текст кнопки
    public int nextNodeId = -1;               // -1 = закончить диалог
    public DialogueActionType action = DialogueActionType.None;
    public string actionParam = "";              // доп. параметр для Custom

    [Tooltip("Показывать этот вариант только если выполнено условие (задаётся кодом NPC). Пусто = всегда.")]
    public string conditionTag = "";
}

/// <summary>Одна реплика NPC + варианты ответа на неё.</summary>
[System.Serializable]
public class DialogueNode
{
    public int id;
    [TextArea(2, 4)]
    public string text;                 // что говорит NPC
    public Sprite portraitOverride;     // сменить портрет для этой реплики (опц.)
    public DialogueOption[] options;    // если пусто — кнопка "Закрыть"
}

/// <summary>
/// Диалог целиком. Создаётся через Assets → Create → RPG → Dialogue.
/// Дерево из узлов, связанных по id. Переиспользуется между NPC.
/// </summary>
[CreateAssetMenu(fileName = "NewDialogue", menuName = "RPG/Dialogue")]
public class DialogueData : ScriptableObject
{
    [Header("NPC")]
    public string npcName = "NPC";
    public Sprite portrait;

    [Header("Дерево диалога")]
    public int startNodeId = 0;
    public DialogueNode[] nodes;

    public DialogueNode GetNode(int id)
    {
        if (nodes == null) return null;
        foreach (DialogueNode n in nodes)
            if (n.id == id) return n;
        return null;
    }

    public DialogueNode GetStartNode() => GetNode(startNodeId);
}