using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class WaterBar : MonoBehaviour
{
    [Header("UI")]
    public Image fillImage;          // заливка шкалы воды
    public Image backgroundImage;    // фон шкалы
    public Text waterText;           // текст "10/10" (опционально)
    public GameObject barVisual;     // визуальная часть (показ/скрытие)
                                     // сам WaterBar объект всегда активен!

    [Header("Цвета заливки")]
    public Color fullColor = new Color(0.2f, 0.6f, 1f);
    public Color emptyColor = new Color(0.1f, 0.3f, 0.6f);

    [Header("Эффект пустой лейки")]
    public float shakeDuration = 0.4f;
    public float shakeMagnitude = 8f;

    private RectTransform shakeRect;  // трясём backgroundImage
    private bool isShaking = false;
    private Vector2 originalPos;
    private Color originalBgColor;

    void Start()
    {
        // Трясём background
        if (backgroundImage != null)
        {
            shakeRect = backgroundImage.GetComponent<RectTransform>();
            if (shakeRect != null)
                originalPos = shakeRect.anchoredPosition;
            originalBgColor = backgroundImage.color;
        }

        // Скрываем визуал по умолчанию
        if (barVisual != null)
            barVisual.SetActive(false);
    }

    void Update()
    {
        InventorySlot activeSlot = HotbarManager.Instance?.GetActiveSlot();
        bool showBar = activeSlot != null && activeSlot.IsWateringCan();

        // Показываем/скрываем только визуал — сам скрипт всегда работает
        if (barVisual != null)
            barVisual.SetActive(showBar);

        if (!showBar || fillImage == null) return;

        // Обновляем заливку
        float ratio = activeSlot.GetMaxWater() > 0
            ? (float)activeSlot.currentWater / activeSlot.GetMaxWater()
            : 0f;

        fillImage.fillAmount = ratio;
        fillImage.color = Color.Lerp(emptyColor, fullColor, ratio);

        if (waterText != null)
            waterText.text = activeSlot.currentWater + "/" + activeSlot.GetMaxWater();
    }

    // Вызывается когда лейка пустая
    public void PlayEmptyEffect()
    {
        if (!isShaking)
            StartCoroutine(ShakeAndFlash());
    }

    IEnumerator ShakeAndFlash()
    {
        isShaking = true;

        // Показываем визуал на время эффекта даже если лейка не активна
        if (barVisual != null) barVisual.SetActive(true);

        // Красный фон
        if (backgroundImage != null)
            backgroundImage.color = Color.red;
        if (fillImage != null)
            fillImage.color = Color.red;

        // Дрожание backgroundImage
        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            if (shakeRect != null)
            {
                float x = originalPos.x + Random.Range(-shakeMagnitude, shakeMagnitude);
                float y = originalPos.y + Random.Range(-shakeMagnitude * 0.3f, shakeMagnitude * 0.3f);
                shakeRect.anchoredPosition = new Vector2(x, y);
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Возврат на место
        if (shakeRect != null)
            shakeRect.anchoredPosition = originalPos;

        // Возврат цвета
        if (backgroundImage != null)
            backgroundImage.color = originalBgColor;

        isShaking = false;
    }
}