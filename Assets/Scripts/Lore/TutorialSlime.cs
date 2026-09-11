using UnityEngine;

/// <summary>
/// Путеводитель: учебный слайм участка №9. Маркер + ослабление
/// (новичок с деревянным мечом должен справляться, но получать по носу).
/// Без респауна (см. SimpleEnemyAI.RespawnCoroutine), смерть считается
/// паком через Notify("farmkill"). Вешается паком автоматически.
/// </summary>
public class TutorialSlime : MonoBehaviour
{
    [Header("Ослабление для обучения")]
    public float hp = 30f;
    public float damage = 3f;

    void Awake()
    {
        var health = GetComponent<EnemyHealth>();
        if (health != null)
        {
            health.maxHealth = hp;
            health.currentHealth = hp;
        }
        var ai = GetComponent<SimpleEnemyAI>();
        if (ai != null) ai.damageToPlayer = damage;
    }
}
