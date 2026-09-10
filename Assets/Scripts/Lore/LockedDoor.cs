using UnityEngine;

/// <summary>
/// Часть 3 лора: запертая дверь (маяк циклопа сейчас, часовня/прочее позже).
/// Удар = сообщение. Самонастройка: слой Interactable(8) + триггер.
/// Ключ и открытие — в ч.5 вместе с циклопом.
/// </summary>
public class LockedDoor : MonoBehaviour, IInteractable
{
    [TextArea(2, 3)]
    public string lockedMessage = "Заперто. Изнутри пахнет... большим. И табаком.";

    void Awake()
    {
        gameObject.layer = 8; // Interactable
        var col = GetComponent<BoxCollider2D>();
        if (col == null) col = gameObject.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        if (col.size.magnitude < 0.5f) col.size = new Vector2(1f, 1.5f);
    }

    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        ActionLogUI.Show(lockedMessage);
    }
}
