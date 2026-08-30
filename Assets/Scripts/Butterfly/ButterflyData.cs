using UnityEngine;

/// <summary>
/// Данные бабочки. Создаётся через Assets → Create → RPG → Butterfly.
/// Бабочка нарисована одним рядом кадров (7 кадров полёта, летящей «вперёд»),
/// поэтому вместо DirectionalFrames как у EnemyData — один массив спрайтов
/// и один флаг: отзеркаливать ли спрайт при полёте влево.
/// </summary>
[CreateAssetMenu(fileName = "NewButterfly", menuName = "RPG/Butterfly")]
public class ButterflyData : ScriptableObject
{
    [Header("Спрайты (кадры взмаха крыльев, обычно 7)")]
    public Sprite[] frames;

    [Header("Анимация")]
    public float animationFPS = 12f;

    [Tooltip("Отзеркаливать основной спрайт при полёте ВЛЕВО (спрайты нарисованы летящими вправо/вперёд)")]
    public bool mirrorSprite = true;

    public bool HasFrames => frames != null && frames.Length > 0;
}
