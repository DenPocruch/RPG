using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>Направления в стрипах: блоки идут строго Down → Up → Right → Left.</summary>
public enum CharDir { Down = 0, Up = 1, Right = 2, Left = 3 }

/// <summary>
/// База данных конструктора персонажа: действие → категории → варианты → кадры.
/// Строится пунктом Tools → Character → 2. Build Character Database.
/// </summary>
[CreateAssetMenu(menuName = "RPG/Character Database", fileName = "CharacterDatabase")]
public class CharacterDatabase : ScriptableObject
{
    public List<CharacterAction> actions = new List<CharacterAction>();

    public CharacterAction FindAction(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        for (int i = 0; i < actions.Count; i++)
            if (string.Equals(actions[i].actionName, name, StringComparison.OrdinalIgnoreCase))
                return actions[i];
        return null;
    }
}

[Serializable]
public class CharacterAction
{
    public string actionName;
    [Tooltip("Кадров по направлениям: 0 = направления нет (напр. Climbing только Up)")]
    public int downFrames;
    public int upFrames;
    public int rightFrames;
    public int leftFrames;
    public float fps = 8f;
    public List<CharacterCategory> categories = new List<CharacterCategory>();

    public int TotalFrames => downFrames + upFrames + rightFrames + leftFrames;

    public int DirCount(int dir)
    {
        switch (dir)
        {
            case 0: return downFrames;
            case 1: return upFrames;
            case 2: return rightFrames;
            default: return leftFrames;
        }
    }

    public int DirOffset(int dir)
    {
        switch (dir)
        {
            case 0: return 0;
            case 1: return downFrames;
            case 2: return downFrames + upFrames;
            default: return downFrames + upFrames + rightFrames;
        }
    }

    public CharacterCategory FindCategory(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        for (int i = 0; i < categories.Count; i++)
            if (string.Equals(categories[i].categoryName, name, StringComparison.OrdinalIgnoreCase))
                return categories[i];
        return null;
    }
}

[Serializable]
public class CharacterCategory
{
    public string categoryName;
    [Tooltip("Порядок отрисовки: меньше = дальше от камеры")]
    public int renderOrder;
    public List<CharacterVariant> variants = new List<CharacterVariant>();

    public CharacterVariant FindVariant(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        for (int i = 0; i < variants.Count; i++)
            if (string.Equals(variants[i].variantName, name, StringComparison.OrdinalIgnoreCase))
                return variants[i];
        return null;
    }
}

[Serializable]
public class CharacterVariant
{
    [Tooltip("Относительный путь без расширения, напр. Fawn/Ginger, Farm/Blue, 1")]
    public string variantName;
    [Tooltip("Кадров в 2 раза больше нормы (оружие рыбалки, Fish FX): берётся каждый 2-й")]
    public bool doubleFrames;
    public Sprite[] frames = new Sprite[0];
}
