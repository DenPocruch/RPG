using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// База портретов (аватар героя/NPC): категория → вариант → 60 кадров (10x6, строки=эмоции).
/// Имена вариантов повторяют конструктор (Skins Male/1, Eyes Brown, Hair Standard/Blonde...)
/// чтобы маппинг из сейва внешности был тривиальным. Строится пунктом
/// Tools → Character → 5. Build Portrait Database.
/// </summary>
[CreateAssetMenu(menuName = "RPG/Portrait Database", fileName = "PortraitDatabase")]
public class PortraitDatabase : ScriptableObject
{
    public List<PortraitCategory> categories = new List<PortraitCategory>();

    public PortraitCategory FindCategory(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        for (int i = 0; i < categories.Count; i++)
            if (string.Equals(categories[i].categoryName, name, StringComparison.OrdinalIgnoreCase))
                return categories[i];
        return null;
    }
}

[Serializable]
public class PortraitCategory
{
    public string categoryName;
    public List<PortraitVariant> variants = new List<PortraitVariant>();

    public PortraitVariant FindVariant(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        for (int i = 0; i < variants.Count; i++)
            if (string.Equals(variants[i].variantName, name, StringComparison.OrdinalIgnoreCase))
                return variants[i];
        return null;
    }
}

[Serializable]
public class PortraitVariant
{
    [Tooltip("Имя как в конструкторе: Male/1, Brown, Standard/Blonde, pirate eye patch")]
    public string variantName;
    [Tooltip("15 кадров: индекс = emotionRow*5 + frameCol")]
    public Sprite[] frames = new Sprite[0];
}
