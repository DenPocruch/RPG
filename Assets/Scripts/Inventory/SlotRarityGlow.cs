using UnityEngine;
using UnityEngine.UI;

public class SlotRarityGlow : MonoBehaviour
{
    [Header("Ссылки")]
    public RawImage glowImage;

    [Header("Угол направления (градусы)")]
    [Range(0f, 360f)]
    [Tooltip("45 = ↗ снизу-слева вверх-вправо | 90 = снизу вверх | 135 = ↖")]
    public float beamAngle = 45f;

    [Header("Скорость движения")]
    public float scrollSpeed = 0.4f;

    [Header("Ширина/длина луча")]
    [Range(0.05f, 1f)]
    public float beamWidth = 0.5f;

    [Header("Цвета редкостей")]
    public Color colorUncommon = new Color(0.2f, 1f, 0.2f, 0.6f);
    public Color colorRare = new Color(0.2f, 0.5f, 1f, 0.65f);
    public Color colorEpic = new Color(0.8f, 0.2f, 1f, 0.7f);
    public Color colorLegendary = new Color(1f, 0.6f, 0f, 0.8f);

    private const int TEX_SIZE = 32; // небольшая текстура для производительности
    private Texture2D tex;
    private Color[] pixels = new Color[TEX_SIZE * TEX_SIZE];
    private float beamPos = 0f;   // текущая позиция луча [−width … 1+width]
    private bool isActive = false;

    // Кэш направления
    private float dirX, dirY, dMin, dRange;
    private float lastAngle = -999f;

    void Awake()
    {
        tex = new Texture2D(TEX_SIZE, TEX_SIZE, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp; // CLAMP — не повторяем!
        tex.filterMode = FilterMode.Bilinear;

        if (glowImage != null)
        {
            glowImage.texture = tex;
            glowImage.enabled = false;
            glowImage.uvRect = new Rect(0, 0, 1, 1);
        }

        RebuildDirection();
    }

    void Update()
    {
        if (!isActive || glowImage == null) return;

        // Пересчитываем направление если угол изменился
        if (!Mathf.Approximately(beamAngle, lastAngle))
            RebuildDirection();

        // Луч движется от -beamWidth до 1+beamWidth (входит и выходит плавно)
        float totalRange = 1f + 2f * beamWidth;
        beamPos += scrollSpeed * Time.deltaTime;
        if (beamPos > 1f + beamWidth) beamPos = -beamWidth; // сброс после выхода

        UpdateTexture();
    }

    void UpdateTexture()
    {
        float sigma = beamWidth * 0.35f;
        float sigma2 = 2f * sigma * sigma;

        for (int y = 0; y < TEX_SIZE; y++)
        {
            for (int x = 0; x < TEX_SIZE; x++)
            {
                float nx = (float)x / TEX_SIZE;
                float ny = (float)y / TEX_SIZE;

                // Проекция на направление (нормализовано 0→1)
                float d = (nx * dirX + ny * dirY - dMin) / dRange;

                // Расстояние от текущей позиции луча — БЕЗ ПОВТОРЕНИЙ
                float dist = Mathf.Abs(d - beamPos);
                float alpha = Mathf.Exp(-(dist * dist) / (sigma2 + 0.0001f));

                pixels[y * TEX_SIZE + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(false); // false = не пересоздавать мипмапы
    }

    public void SetItem(ItemData item)
    {
        if (glowImage == null) return;

        if (item == null || item.rarity == ItemRarity.Common)
        {
            glowImage.enabled = false;
            isActive = false;
            return;
        }

        glowImage.color = GetRarityColor(item.rarity);
        glowImage.enabled = true;
        isActive = true;
        beamPos = -beamWidth; // стартуем с нижнего-левого угла
    }

    public void Clear()
    {
        if (glowImage != null) glowImage.enabled = false;
        isActive = false;
    }

    void RebuildDirection()
    {
        lastAngle = beamAngle;
        float rad = beamAngle * Mathf.Deg2Rad;
        dirX = Mathf.Cos(rad);
        dirY = Mathf.Sin(rad);
        dMin = Mathf.Min(0f, dirX) + Mathf.Min(0f, dirY);
        dRange = (Mathf.Max(0f, dirX) + Mathf.Max(0f, dirY)) - dMin;
        if (dRange < 0.001f) dRange = 1f;
    }

    Color GetRarityColor(ItemRarity rarity)
    {
        switch (rarity)
        {
            case ItemRarity.Uncommon: return colorUncommon;
            case ItemRarity.Rare: return colorRare;
            case ItemRarity.Epic: return colorEpic;
            case ItemRarity.Legendary: return colorLegendary;
        }
        return Color.clear;
    }
}