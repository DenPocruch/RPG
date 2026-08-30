using UnityEngine;

/// <summary>
/// Доска объявлений в городе: удар по доске открывает панель ордеров.
/// Вешается на объект доски в сцене City. Объект должен стоять на слое 8
/// «Interactable» и иметь дочернюю trigger-зону InteractZone (как у NPC) —
/// иначе удар по доске не сработает.
/// </summary>
public class OrderBoardInteraction : MonoBehaviour, IInteractable
{
    public void Interact(GameObject player)
    {
        var ui = OrderBoardUI.EnsureInstance();
        if (ui != null) ui.Open();
    }

    public Transform GetTransform() => transform;
}
