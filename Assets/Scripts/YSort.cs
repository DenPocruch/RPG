using UnityEngine;

public class YSort : MonoBehaviour
{
    [Header("�������� ����������")]
    public int sortingOffset = 0; // ��� Tool ����� +10 ����� ��������� ������ ���������

    private SpriteRenderer spriteRenderer;
    public const int BASE_OFFSET = 5000;

    /// <summary>Порядок сортировки для позиции (иконки/лейблы над объектами — +1 поверх).</summary>
    public static int GetOrder(Vector3 pos, int offset = 0)
        => BASE_OFFSET - (int)(pos.y * 10) + offset;

    void Start()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    void LateUpdate()
    {
        if (spriteRenderer != null)
            spriteRenderer.sortingOrder = GetOrder(transform.position, sortingOffset);
    }
}