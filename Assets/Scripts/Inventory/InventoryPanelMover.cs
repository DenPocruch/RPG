using UnityEngine;

/// <summary>
/// ������������ ������ ������� ������� InventoryPanel.
/// ChestUI � EquipmentUI �������� SetTargetX() � ������ ����� �� ������� ������.
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
        // Защита от дубликата: при возврате в сцену её копия PersistentRoot
        // создаёт второй экземпляр — уничтожаем копию, оригинал продолжает жить.
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        targetPos = new Vector2(0, panelY);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
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

    /// <summary>�������� ������ �� offsetX �� ������.</summary>
    public void SetOffsetX(float offsetX)
    {
        targetPos = new Vector2(offsetX, panelY);
    }

    /// <summary>������� ������ � �����.</summary>
    public void ResetPosition()
    {
        targetPos = new Vector2(0, panelY);
    }
}