using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Вешай на каждый AttackArea_Down/Up/Right/Left (хитбокс атаки).
/// При активации ищет IInteractable в своей зоне и вызывает Interact().
/// Если несколько — выбирает ближайший к игроку.
/// </summary>
public class InteractionDetector : MonoBehaviour
{
    [Header("Слой интерактивных объектов")]
    public LayerMask interactableLayer;

    [Header("Размер зоны проверки")]
    public Vector2 boxSize = new Vector2(1f, 1f);

    [Header("Смещение зоны")]
    public Vector2 boxOffset = Vector2.zero; // X — влево/вправо, Y — вверх/вниз

    private Transform playerTransform;

    void Awake()
    {
        // Ищем игрока (родительский объект Player)
        playerTransform = transform.root;
    }

    /// <summary>
    /// Вызывается из PlayerMovement когда игрок нажимает атаку.
    /// Возвращает true если нашли и активировали интерактивный объект.
    /// </summary>
    public bool TryInteract()
    {
        Vector2 boxCenter = (Vector2)transform.position + boxOffset;
        Collider2D[] hits = Physics2D.OverlapBoxAll(
            boxCenter,
            boxSize,
            0f,
            interactableLayer
        );

        if (hits.Length == 0) return false;

        // Ищем ближайший интерактивный объект
        IInteractable closest = null;
        float minDist = float.MaxValue;

        foreach (Collider2D hit in hits)
        {
            IInteractable interactable = hit.GetComponentInParent<IInteractable>();
            if (interactable == null) continue;

            float dist = Vector2.Distance(
                playerTransform.position,
                interactable.GetTransform().position
            );

            if (dist < minDist)
            {
                minDist = dist;
                closest = interactable;
            }
        }

        if (closest != null)
        {
            closest.Interact(playerTransform.gameObject);
            return true;
        }

        return false;
    }

    void OnDrawGizmosSelected()
    {
        Vector2 center = (Vector2)transform.position + boxOffset;
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawCube(center, boxSize);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(center, boxSize);
        // Точка центра
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(center, 0.05f);
    }
}