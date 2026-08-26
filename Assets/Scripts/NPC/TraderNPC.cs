using UnityEngine;

/// <summary>
/// NPC-торговец. Вешается рядом с NPCInteractable на объект торговца.
/// Товары задаются прямо здесь (у каждого торговца свой ассортимент).
///
/// Диалог торговца (DialogueData) содержит вариант ответа с действием
/// OpenShop — когда игрок его выбирает, открывается магазин ЭТОГО торговца.
///
/// Настройка диалога: вариант "Показать товар" → action = OpenShop,
/// nextNodeId = -1 (диалог закроется, потом откроется магазин).
/// </summary>
public class TraderNPC : MonoBehaviour
{
    [Header("Товары этого торговца")]
    public ShopManager.ShopItem[] stock;

    [Header("Заголовок окна магазина (опц.)")]
    public string shopTitle = "";

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
        if (action != DialogueActionType.OpenShop) return;

        // Реагируем только на СВОЙ диалог (не на диалог другого торговца)
        if (DialogueManager.Instance == null || DialogueManager.Instance.currentNPC == null)
            return;
        if (DialogueManager.Instance.currentNPC.gameObject != gameObject) return;

        if (ShopUI.Instance != null)
        {
            // Заголовок: из параметра диалога, иначе из поля компонента
            string title = !string.IsNullOrEmpty(param) ? param : shopTitle;
            ShopUI.Instance.Open(stock, title);
        }
    }
}
