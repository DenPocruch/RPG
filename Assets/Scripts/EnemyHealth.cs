using UnityEngine;
using System.Collections;

public class EnemyHealth : MonoBehaviour
{
    [Header("Здоровье")]
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Спрайт")]
    public SpriteRenderer spriteRenderer;

    [Header("Позиция попапа")]
    public Vector2 popupOffset = new Vector2(0f, 1f); // настрой под высоту персонажа

    [Header("Опыт за убийство")]
    public int xpReward = 20; // сколько Combat XP даёт убийство

    void Start()
    {
        currentHealth = maxHealth;
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    /// <summary>Получить урон с информацией о крите для попапа.</summary>
    public void TakeDamage(float damage, bool isCrit = false)
    {
        if (currentHealth <= 0) return;

        currentHealth -= damage;

        // Всплывающий урон над врагом
        if (DamagePopupManager.Instance != null)
        {
            DamagePopup.PopupType type = isCrit
                ? DamagePopup.PopupType.Crit
                : DamagePopup.PopupType.Normal;
            DamagePopupManager.Instance.Spawn(
                (Vector2)transform.position + popupOffset, damage, type);
        }

        SimpleEnemyAI ai = GetComponent<SimpleEnemyAI>();
        if (ai != null) ai.OnDamage();

        StartCoroutine(FlashRed());

        if (currentHealth <= 0) Die();
    }

    IEnumerator FlashRed()
    {
        if (spriteRenderer != null)
            spriteRenderer.color = Color.red;
        yield return new WaitForSeconds(0.15f);
        if (spriteRenderer != null)
            spriteRenderer.color = Color.white;
    }

    void Die()
    {
        currentHealth = 0;

        // Опыт за убийство
        if (PlayerLevel.Instance != null && xpReward > 0)
            PlayerLevel.Instance.AddXp(PlayerLevel.SkillBranch.Combat, xpReward);

        LootDrop lootDrop = GetComponent<LootDrop>();
        if (lootDrop != null) lootDrop.DropLoot();

        SimpleEnemyAI ai = GetComponent<SimpleEnemyAI>();
        if (ai != null)
        {
            ai.enabled = false;
            ai.OnDeath();
        }
        else Destroy(gameObject);
    }
}