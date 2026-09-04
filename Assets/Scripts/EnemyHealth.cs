using UnityEngine;
using System.Collections;

public class EnemyHealth : MonoBehaviour
{
    [Header("��������")]
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("������� ������� (���������� �� ���������)")]
    [Tooltip("������� ����: ������� ���� ������ (������� ���������� ���������)")]
    public float defense = 0f;
    [Tooltip("����� ������ %: �������� �������� ������� 1�1, ������� 0. ����� ���� >100 (����� ���������������)")]
    public float dodgeChance = 0f;
    [Tooltip("�������� ���������: ���������� ������ ������")]
    public float penetration = 0f;
    [Tooltip("�������� ��������� %: ������ ������ ������")]
    public float accuracy = 0f;

    [Header("������")]
    public SpriteRenderer spriteRenderer;

    [Header("������� ������")]
    public Vector2 popupOffset = new Vector2(0f, 1f); // ������� ��� ������ ���������

    [Header("���� �� ��������")]
    public int xpReward = 20; // ������� Combat XP ��� ��������

    void Start()
    {
        currentHealth = maxHealth;
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    /// <summary>�������� ���� � ����������� � ����� ��� ������.
    /// �������� ������������ ��������� ����� ������, ��������� — ����� ������.</summary>
    public void TakeDamage(float damage, bool isCrit = false, float attackerAccuracy = 0f, float attackerPenetration = 0f)
    {
        if (currentHealth <= 0) return;

        // ������: �������� �������� ������� ������ �������
        float effectiveDodge = Mathf.Max(0f, dodgeChance - attackerAccuracy);
        if (effectiveDodge > 0f && Random.Range(0f, 100f) < effectiveDodge)
        {
            if (DamagePopupManager.Instance != null)
                DamagePopupManager.Instance.Spawn(
                    (Vector2)transform.position + popupOffset, 0, DamagePopup.PopupType.Dodge);
            // ���� �������� (��������), �� ����� ���
            SimpleEnemyAI alertAi = GetComponent<SimpleEnemyAI>();
            if (alertAi != null) alertAi.OnDamage();
            return;
        }

        // ������: ��������� ���������� ����� ������
        float effectiveDefense = Mathf.Max(0f, defense - attackerPenetration);
        float finalDamage = Mathf.Max(damage - effectiveDefense, 1f);

        currentHealth -= finalDamage;

        // ����������� ���� ��� ������
        if (DamagePopupManager.Instance != null)
        {
            DamagePopup.PopupType type = isCrit
                ? DamagePopup.PopupType.Crit
                : DamagePopup.PopupType.Normal;
            DamagePopupManager.Instance.Spawn(
                (Vector2)transform.position + popupOffset, finalDamage, type);
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

        // ���� �� ��������
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