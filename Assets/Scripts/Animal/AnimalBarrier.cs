using UnityEngine;

/// Невидимый барьер: животные не проходят, игрок проходит.
/// Класть на тот же объект, где BoxCollider2D/EdgeCollider2D.
[RequireComponent(typeof(Collider2D))]
public class AnimalBarrier : MonoBehaviour
{
    void Start()
    {
        Collider2D[] mine = GetComponentsInChildren<Collider2D>(false);

        // Матрица слоёв на случай, если объект стоит на слое AnimalBarrier
        // (страховка — настройки матрицы могли не примениться)
        int selfLayer = gameObject.layer;
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0 && playerLayer != selfLayer)
            Physics2D.IgnoreLayerCollision(selfLayer, playerLayer, true);

        // Гарантированное отключение коллизии с коллайдерами игрока по тегу,
        // независимо от того, на каком слое стоит игрок
        GameObject p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            Collider2D[] pcs = p.GetComponentsInChildren<Collider2D>();
            foreach (Collider2D m in mine)
                foreach (Collider2D c in pcs)
                    Physics2D.IgnoreCollision(m, c);
        }
    }
}
