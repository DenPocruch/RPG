using UnityEngine;
using System.Collections;

/// <summary>
/// NPC Кузнец. Реализует IInteractable — открывает панель крафта.
/// Периодически играет анимацию ковки чтобы оживить мир.
/// </summary>
public class BlacksmithInteraction : MonoBehaviour, IInteractable
{
    [Header("Анимация")]
    public Animator blacksmithAnimator;
    public string workAnimationTrigger = "Work"; // триггер анимации ковки

    [Header("Периодическая анимация")]
    public float minIdleTime = 5f;  // минимум секунд между анимациями
    public float maxIdleTime = 12f; // максимум секунд между анимациями

    [Header("Спрайты (опционально)")]
    public Sprite spriteIdle;
    public Sprite spriteWork;

    private SpriteRenderer sr;
    private bool isWorking = false;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        // Запускаем периодическую анимацию
        StartCoroutine(PeriodicWorkAnimation());
    }

    // ── IInteractable ──────────────────────────────────────────
    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        if (CraftingUI.Instance != null)
            CraftingUI.Instance.Open();
    }
    // ───────────────────────────────────────────────────────────

    IEnumerator PeriodicWorkAnimation()
    {
        while (true)
        {
            // Ждём случайное время
            float wait = Random.Range(minIdleTime, maxIdleTime);
            yield return new WaitForSeconds(wait);

            // Не играем если панель крафта открыта
            if (CraftingUI.Instance != null && CraftingUI.Instance.IsOpen())
                continue;

            PlayWorkAnimation();
        }
    }

    void PlayWorkAnimation()
    {
        if (blacksmithAnimator != null)
        {
            blacksmithAnimator.SetTrigger(workAnimationTrigger);
        }
        else if (sr != null && spriteWork != null)
        {
            // Если нет аниматора — просто меняем спрайт
            StartCoroutine(SimpleWorkSprite());
        }
    }

    IEnumerator SimpleWorkSprite()
    {
        if (sr != null && spriteWork != null)
            sr.sprite = spriteWork;

        yield return new WaitForSeconds(1.5f);

        if (sr != null && spriteIdle != null)
            sr.sprite = spriteIdle;
    }
}