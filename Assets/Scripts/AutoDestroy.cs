using UnityEngine;

// Автоматически уничтожает объект через заданное время
// Вешай на любой эффект — частицы, анимации
public class AutoDestroy : MonoBehaviour
{
    public float lifetime = 1f;

    void Start()
    {
        Destroy(gameObject, lifetime);
    }
}