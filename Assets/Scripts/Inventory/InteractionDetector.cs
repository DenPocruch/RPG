using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ����� �� ������ AttackArea_Down/Up/Right/Left (������� �����).
/// ��� ��������� ���� IInteractable � ����� ���� � �������� Interact().
/// ���� ��������� � �������� ��������� � ������.
/// </summary>
public class InteractionDetector : MonoBehaviour
{
    [Header("���� ������������� ��������")]
    public LayerMask interactableLayer;

    [Header("������ ���� ��������")]
    public Vector2 boxSize = new Vector2(1f, 1f);

    [Header("�������� ����")]
    public Vector2 boxOffset = Vector2.zero; // X � �����/������, Y � �����/����

    private Transform playerTransform;

    void Awake()
    {
        // ВАЖНО: transform.root даёт PersistentRoot (игрок — его ребёнок, сам он в 0,0,0),
        // из-за чего «ближайший интерактив» выбирался по дистанции от центра мира.
        // Ищем именно игрока по тегу.
        GameObject player = GameObject.FindWithTag("Player");
        playerTransform = player != null ? player.transform : transform.root;
    }

    /// <summary>
    /// ���������� �� PlayerMovement ����� ����� �������� �����.
    /// ���������� true ���� ����� � ������������ ������������� ������.
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

        // ���� ��������� ������������� ������
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
            // Отладка телепортов: показываем, кого именно поймал удар
            if (closest is DoorTeleport || closest is SceneTransition)
                Debug.Log("[Удар] Пойман " + closest.GetTransform().name +
                          " (" + (Vector2)closest.GetTransform().position + "), дистанция " +
                          Vector2.Distance(playerTransform.position, closest.GetTransform().position).ToString("F2"));
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
        // ����� ������
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(center, 0.05f);
    }
}