using UnityEngine;
using UnityEngine.UI;

public class PlayerHealthBar : MonoBehaviour
{
    [Header("UI")]
    public Image fillImage;      // красная заполненная часть
    public Text hpText;          // текст "100/100" (опционально)

    [Header("Цвета")]
    public Color fullColor = Color.red;
    public Color lowColor = new Color(0.6f, 0f, 0f);

    private PlayerHealth playerHealth;

    void Start()
    {
        playerHealth = FindFirstObjectByType<PlayerHealth>();
        UpdateBar();
    }

    void Update()
    {
        UpdateBar();
    }

    void UpdateBar()
    {
        if (playerHealth == null || fillImage == null) return;

        float ratio = playerHealth.currentHealth / playerHealth.maxHealth;
        fillImage.fillAmount = ratio;

        // Меняем цвет — красный когда мало HP
        fillImage.color = Color.Lerp(lowColor, fullColor, ratio);

        // Текст (если есть)
        if (hpText != null)
            hpText.text = (int)playerHealth.currentHealth +
                          "/" + (int)playerHealth.maxHealth;
    }
}