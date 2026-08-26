using UnityEngine;

/// <summary>
/// ”ниверсальный интерфейс дл€ всех объектов с которыми может взаимодействовать игрок.
/// –еализуют: WellInteraction, ChestInteraction, SiloInteraction и будущие
/// (NPC, верстак, печь и т.д.)
/// </summary>
public interface IInteractable
{
    void Interact(GameObject player);
    Transform GetTransform();
}