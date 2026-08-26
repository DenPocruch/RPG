using UnityEngine;

/// <summary>
/// Менеджер создания попапов урона. Один на всю сцену.
/// Используй DamagePopupManager.Instance.Spawn(...) из любого скрипта.
/// </summary>
public class DamagePopupManager : MonoBehaviour
{
    public static DamagePopupManager Instance;

    [Header("Префаб попапа")]
    public GameObject popupPrefab;

    // Смещение теперь задаётся на каждом EnemyHealth/PlayerHealth отдельно

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Создать попап у позиции цели.</summary>
    public void Spawn(Vector3 worldPos, float damage, DamagePopup.PopupType type)
    {
        if (popupPrefab == null)
        {
            Debug.LogWarning("[DamagePopupManager] popupPrefab не назначен!");
            return;
        }

        GameObject popup = Instantiate(popupPrefab, worldPos, Quaternion.identity);

        DamagePopup dp = popup.GetComponent<DamagePopup>();
        if (dp != null) dp.Setup(damage, type);
    }
}