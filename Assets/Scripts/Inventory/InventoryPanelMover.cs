using UnityEngine;

/// <summary>
/// Единственный объект который двигает InventoryPanel.
/// ChestUI и EquipmentUI вызывают SetTargetX() — больше никто не трогает панель.
/// </summary>
public class InventoryPanelMover : MonoBehaviour
{
    public static InventoryPanelMover Instance;

    public RectTransform inventoryRect;
    public float panelY = 47f;
    public float shiftSpeed = 8f;

    private Vector2 targetPos;

    void Awake()
    {
        Instance = this;
        targetPos = new Vector2(0, panelY);
    }

    void Start()
    {
        if (inventoryRect != null)
            inventoryRect.anchoredPosition = new Vector2(0, panelY);
    }

    void Update()
    {
        if (inventoryRect == null) return;
        inventoryRect.anchoredPosition = Vector2.Lerp(
            inventoryRect.anchoredPosition,
            targetPos,
            Time.deltaTime * shiftSpeed
        );
    }

    /// <summary>Сдвинуть рюкзак на offsetX от центра.</summary>
    public void SetOffsetX(float offsetX)
    {
        targetPos = new Vector2(offsetX, panelY);
    }

    /// <summary>Вернуть рюкзак в центр.</summary>
    public void ResetPosition()
    {
        targetPos = new Vector2(0, panelY);
    }
}