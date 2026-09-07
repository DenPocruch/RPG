using UnityEngine;

public class WeaponController : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private Animator animator;

    [Header("������")]
    public bool weaponEquipped = false;

    [Tooltip("Оверлей-спрайт отключён: оружие рисует слой тела (конструктор персонажа)")]
    public bool overlayDisabled = false;

    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
        spriteRenderer.enabled = false;
    }

    // ����������� ���
    public void ToggleWeapon()
    {
        weaponEquipped = !weaponEquipped;
        spriteRenderer.enabled = weaponEquipped && !overlayDisabled;
    }

    // ������������� ������
    public void ForceUnequip()
    {
        weaponEquipped = false;
        if (spriteRenderer != null)
            spriteRenderer.enabled = false;
    }

    // �������� ����� �����
    public void PlayAttackAnimation(float dirX, float dirY)
    {
        if (!weaponEquipped) return;
        if (animator == null) return;
        animator.SetFloat("LastMoveX", dirX);
        animator.SetFloat("LastMoveY", dirY);
        animator.SetTrigger("SwordAttack");
    }
}