using UnityEngine;
using TMPro;

/// <summary>
/// Надпись над объектом мира: [иконка] текст (TMP 3D + чёрная обводка).
/// Все параметры (смещения, ширина, отступы, шрифт, обводка) настраиваются
/// в инспекторе. Дети создаются кнопкой-контекст-меню или автоматически.
/// Префаб: Assets/Resources/WorldLabel.prefab — кормушки/поилки подхватывают его сами.
/// </summary>
public class WorldLabel : MonoBehaviour
{
    [Header("Позиция лейбла (localPosition)")]
    public Vector3 offset = new Vector3(0f, 1.45f, 0f);

    [Header("Иконка")]
    public Vector3 iconOffset = new Vector3(-0.45f, 0f, 0f);
    public float iconScale = 0.4f;
    public int iconSortingOrder = 31;

    [Header("Текст")]
    public float fontSize = 2.6f;
    public Vector2 textSize = new Vector2(1.6f, 0.5f);
    public TextAlignmentOptions alignment = TextAlignmentOptions.Left;
    public int textSortingOrder = 32;

    [Header("Обводка")]
    public float outlineWidth = 0.15f;
    public Color outlineColor = new Color(0f, 0f, 0f, 1f);

    private SpriteRenderer icon;
    private TextMeshPro label;

    void Awake()
    {
        EnsureBuilt();
    }

    /// <summary>Создаёт/чинит детей и применяет все настройки. Работает и в редакторе.</summary>
    public void EnsureBuilt()
    {
        transform.localPosition = offset;

        if (icon == null)
        {
            var t = transform.Find("Icon");
            if (t != null) icon = t.GetComponent<SpriteRenderer>();
            if (icon == null)
            {
                var go = new GameObject("Icon");
                go.transform.SetParent(transform, false);
                icon = go.AddComponent<SpriteRenderer>();
            }
        }
        icon.transform.localPosition = iconOffset;
        icon.transform.localScale = Vector3.one * iconScale;
        icon.sortingOrder = iconSortingOrder;

        if (label == null)
        {
            var t = transform.Find("Text");
            if (t != null) label = t.GetComponent<TextMeshPro>();
            if (label == null)
            {
                var go = new GameObject("Text");
                go.transform.SetParent(transform, false);
                label = go.AddComponent<TextMeshPro>();
            }
        }
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.fontStyle = FontStyles.Bold;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.sortingOrder = textSortingOrder;
        label.rectTransform.sizeDelta = textSize;

        ApplyOutline();
        UnityEditor.EditorUtility.SetDirty(this);
    }

    /// <summary>Обводка через материал. TMP назначает дефолтный шрифт не сразу
    /// после AddComponent, поэтому вызывается лениво (каждый Set).</summary>
    void ApplyOutline()
    {
        // Шрифт мог не успеть назначиться — подставим дефолтный из TMP Settings
        if (label.font == null && TMP_Settings.defaultFontAsset != null)
            label.font = TMP_Settings.defaultFontAsset;
        if (label.font == null || label.fontSharedMaterial == null) return; // ещё рано

        var mat = label.fontMaterial;
        mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
        mat.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
    }

    /// <summary>Обновить текст и цвет.</summary>
    public void Set(string text, Color color)
    {
        if (label == null) EnsureBuilt();
        label.text = text;
        label.color = color;
        if (label.fontSharedMaterial == null || !label.fontSharedMaterial.IsKeywordEnabled(ShaderUtilities.Keyword_Outline))
            ApplyOutline(); // шрифт/материал только что назначились — наводим обводку
    }

    /// <summary>Обновить иконку (null = скрыть).</summary>
    public void SetIcon(Sprite sprite)
    {
        if (icon == null) EnsureBuilt();
        icon.sprite = sprite;
        icon.enabled = sprite != null;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // В редакторе применяем изменения полей только когда дети уже созданы
        if (!Application.isPlaying && transform.Find("Text") != null)
            UnityEditor.EditorApplication.delayCall += () => { if (this != null) EnsureBuilt(); };
    }

    [ContextMenu("Создать/обновить детей")]
    private void CtxBuild() => EnsureBuilt();
#endif
}
