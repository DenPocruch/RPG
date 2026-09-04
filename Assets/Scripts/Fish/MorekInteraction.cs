using UnityEngine;

/// <summary>
/// Морек (рыбак): удар рядом → панель (удочка/продажа/коллекция).
/// Объект с этим скриптом + InteractZone юзер ставит в Beach руками.
/// </summary>
public class MorekInteraction : MonoBehaviour, IInteractable
{
    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        if (MorekUI.Instance != null)
            MorekUI.Instance.Open();
    }
}
