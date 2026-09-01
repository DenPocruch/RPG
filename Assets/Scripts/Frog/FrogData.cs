using UnityEngine;

/// <summary>
/// Данные жабы. Создаётся через Assets → Create → RPG → Frog.
/// Три направления: ВНИЗ, ВПРАВО (влево = flipX) и ВВЕРХ (спина).
/// Анимация кодом через FrogAI, без Animator-компонента.
/// </summary>
[CreateAssetMenu(fileName = "NewFrog", menuName = "RPG/Frog")]
public class FrogData : ScriptableObject
{
    [System.Serializable]
    public class FrogFrames
    {
        [Tooltip("Кадры лицом к зрителю (движение вниз / отдых / сон)")]
        public Sprite[] down;
        [Tooltip("Кадры спиной к зрителю (движение вверх)")]
        public Sprite[] up;
        [Tooltip("Кадры смотрящей ВПРАВО (движение вправо); влево = flipX")]
        public Sprite[] sideRight;
    }

    [Header("Спрайты (кадры: вниз, вверх, вправо; влево = зеркало)")]
    [Tooltip("Ходьба / прыжки")]
    public FrogFrames walk;
    [Tooltip("Спит (цикл, глаза закрыты)")]
    public FrogFrames sleep;
    [Tooltip("Квакает (цикл, надувает горло)")]
    public FrogFrames croak;

    [Header("Анимация")]
    public float animationFPS = 6f;

    [Header("Ориентация боковых спрайтов")]
    [Tooltip("Если боковые кадры нарисованы смотрящими ВЛЕВО — true. У жабы нарисованы ВПРАВО → false (влево = отзеркалить)")]
    public bool sideFacesLeft = false;

    public static bool Has(FrogFrames f) => f != null && (Has(f.down) || Has(f.up) || Has(f.sideRight));
    public static bool Has(Sprite[] s) => s != null && s.Length > 0;
}
