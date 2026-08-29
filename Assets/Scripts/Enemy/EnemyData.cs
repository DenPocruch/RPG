using UnityEngine;

/// <summary>
/// Данные вида врага (слизь, гриб...). Создаётся через Assets → Create → RPG → Enemy.
/// Схема как у животных (AnimalData): направления up/down/side, «право» = отзеркаленный side.
/// Листы нарезаны рядами: верхний ряд = side (влево), средний = down, нижний = up.
/// Анимация кодом через EnemyAnimator, без Animator-компонента.
/// </summary>
[CreateAssetMenu(fileName = "NewEnemy", menuName = "RPG/Enemy")]
public class EnemyData : ScriptableObject
{
    [System.Serializable]
    public class DirectionalFrames
    {
        public Sprite[] up;
        public Sprite[] down;
        public Sprite[] side;      // используется и для лево, и для право (право = flipX)
        [Tooltip("Отдельно нарисованные кадры «вправо». Если пусто — право = отзеркаленный side")]
        public Sprite[] sideRight;
    }

    [Header("Спрайты (кадры по направлениям)")]
    public DirectionalFrames idle;
    public DirectionalFrames walk;
    [Tooltip("Ближняя атака (удар оружием). Если заполнена — враг Атакует с анимацией, а не уроном касания")]
    public DirectionalFrames attack;
    [Tooltip("Один раз при получении урона, потом возврат к циклу")]
    public DirectionalFrames damage;
    [Tooltip("Один раз при смерти, замирает на последнем кадре")]
    public DirectionalFrames dead;

    [Header("Анимация")]
    public float animationFPS = 8f;

    [Header("Ориентация боковых спрайтов")]
    [Tooltip("Если боковые спрайты нарисованы смотрящими ВЛЕВО — оставь true (право = отзеркалить)")]
    public bool sideFacesLeft = true;

    public static bool Has(DirectionalFrames df)
    {
        if (df == null) return false;
        return Has(df.up) || Has(df.down) || Has(df.side) || Has(df.sideRight);
    }

    public static bool Has(Sprite[] s) => s != null && s.Length > 0;
}
