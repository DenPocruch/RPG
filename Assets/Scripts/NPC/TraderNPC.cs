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
    [Header("Вторая вкладка (опц.)")]
    public ShopManager.ShopItem[] stock2;

    [Header("Заголовок окна магазина (опц.)")]
    public string shopTitle = "";

    void Awake()
    {
        // Переезд с прилавка ShopInteraction (Марта): товар забираем с собой,
        // сам прилавок удаляем — два IInteractable на объекте конфликтуют.
        // Руками перетаскивать ничего не надо.
        // NPC часто сидит ребёнком домика/прилавка — ищем и у родителя.
        var shop = GetComponentInParent<ShopInteraction>();
        if (shop != null)
        {
            if ((stock == null || stock.Length == 0) && shop.itemsForSale != null)
                stock = shop.itemsForSale;
            if ((stock2 == null || stock2.Length == 0) && shop.itemsForSaleAnimals != null)
                stock2 = shop.itemsForSaleAnimals;
            Destroy(shop);
        }
    }

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
            if (stock2 != null && stock2.Length > 0)
                ShopUI.Instance.Open(stock, stock2, title);
            else
                ShopUI.Instance.Open(stock, title);
        }
    }
}
