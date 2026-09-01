using UnityEngine;

/// <summary>
/// Данные вороны. Создаётся через Assets → Create → RPG → Crow.
/// Два набора кадров: клюёт и летит (кадры полёта нарисованы ВЛЕВО,
/// вправо = flipX). Анимация кодом через CrowAI, без Animator-компонента.
/// </summary>
[CreateAssetMenu(fileName = "CrowData", menuName = "RPG/Crow")]
public class CrowData : ScriptableObject
{
    [Header("Спрайты (кадры состояний)")]
    [Tooltip("Клюёт растение (цикл: наклон-удар-подъём); в паузах клёва замирает на первом кадре")]
    public Sprite[] peck;
    [Tooltip("Летит ВЛЕВО (цикл: взмахи крыльев). Вправо = flipX")]
    public Sprite[] fly;

    [Header("Анимация")]
    public float animationFPS = 8f;
    [Tooltip("Скорость взмахов крыльев в полёте (если 0 — берётся animationFPS)")]
    public float flyFPS = 0f;

    [Header("Ориентация спрайтов полёта")]
    [Tooltip("Кадры полёта нарисованы смотрящими ВЛЕВО — true (право = отзеркалить)")]
    public bool flyFacesLeft = true;

    public static bool Has(Sprite[] s) => s != null && s.Length > 0;
}
