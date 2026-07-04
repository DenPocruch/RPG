using UnityEngine;

public class WeaponController : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Animator animator;

    [Header("Оружие")]
    public bool weaponEquipped = false;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        spriteRenderer.enabled = false;
    }

    // Переключить меч
    public void ToggleWeapon()
    {
        weaponEquipped = !weaponEquipped;
        spriteRenderer.enabled = weaponEquipped;
    }

    // Принудительно убрать
    public void ForceUnequip()
    {
        weaponEquipped = false;
        if (spriteRenderer != null)
            spriteRenderer.enabled = false;
    }

    // Анимация атаки мечом
    public void PlayAttackAnimation(float dirX, float dirY)
    {
        if (!weaponEquipped) return;
        if (animator == null) return;
        animator.SetFloat("LastMoveX", dirX);
        animator.SetFloat("LastMoveY", dirY);
        animator.SetTrigger("SwordAttack");
    }
}