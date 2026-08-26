using UnityEngine;

public class YSort : MonoBehaviour
{
    [Header("Смещение сортировки")]
    public int sortingOffset = 0; // для Tool ставь +10 чтобы рисовался поверх персонажа

    private SpriteRenderer spriteRenderer;
    private const int BASE_OFFSET = 5000;

    void Start()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    void LateUpdate()
    {
        if (spriteRenderer != null)
            spriteRenderer.sortingOrder = BASE_OFFSET - (int)(transform.position.y * 10) + sortingOffset;
    }
}