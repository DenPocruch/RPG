using UnityEngine;

/// <summary>
/// Вешай на объект дома/здания.
/// Когда игрок заходит за здание — оно становится полупрозрачным.
/// Совместим с YSort.cs
/// </summary>
public class BuildingTransparency : MonoBehaviour
{
    [Header("Прозрачность")]
    public float transparentAlpha = 0.4f;  // насколько прозрачный (0-1)
    public float fadeSpeed = 6f;            // скорость fade

    [Header("Зона проверки (высота здания в юнитах)")]
    public float buildingHeight = 2f;  // насколько вверх проверяем игрока

    private SpriteRenderer sr;
    private Transform player;
    private float targetAlpha = 1f;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr == null)
            sr = GetComponentInChildren<SpriteRenderer>();

        GameObject p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
    }

    void LateUpdate()
    {
        if (sr == null || player == null) return;

        // Игрок "за" зданием если:
        // 1. Его Y выше нижней части здания (player.y > building.y)
        // 2. Его Y ниже верхней части здания (player.y < building.y + height)
        // 3. Он примерно по X совпадает со зданием
        bool playerBehind = IsPlayerBehind();

        targetAlpha = playerBehind ? transparentAlpha : 1f;

        Color c = sr.color;
        c.a = Mathf.Lerp(c.a, targetAlpha, Time.deltaTime * fadeSpeed);
        sr.color = c;
    }

    bool IsPlayerBehind()
    {
        if (player == null) return false;

        // Получаем bounds спрайта для точной проверки по X
        Bounds bounds = sr.bounds;

        float playerX = player.position.x;
        float playerY = player.position.y;

        float buildingBottom = transform.position.y;
        float buildingTop = transform.position.y + buildingHeight;
        float buildingLeft = bounds.min.x;
        float buildingRight = bounds.max.x;

        bool inXRange = playerX > buildingLeft && playerX < buildingRight;
        bool inYRange = playerY > buildingBottom && playerY < buildingTop;

        return inXRange && inYRange;
    }

    // Показать зону проверки в редакторе
    void OnDrawGizmosSelected()
    {
        SpriteRenderer s = GetComponent<SpriteRenderer>();
        if (s == null) s = GetComponentInChildren<SpriteRenderer>();
        if (s == null) return;

        Bounds bounds = s.bounds;
        Vector3 center = new Vector3(
            transform.position.x,
            transform.position.y + buildingHeight * 0.5f,
            0
        );
        Vector3 size = new Vector3(
            bounds.size.x,
            buildingHeight,
            0
        );

        Gizmos.color = new Color(0f, 0.5f, 1f, 0.3f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = new Color(0f, 0.5f, 1f, 0.8f);
        Gizmos.DrawWireCube(center, size);
    }
}