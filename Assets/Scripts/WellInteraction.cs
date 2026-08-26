using UnityEngine;
using System.Collections;

public class WellInteraction : MonoBehaviour, IInteractable
{
    [Header("Анимация")]
    public Animator wellAnimator;

    [Header("Спрайты колодца")]
    public Sprite spriteEmpty;
    public Sprite spriteFull;

    [Header("Длительность анимации (сек)")]
    public float animationDuration = 2f;

    private SpriteRenderer sr;
    private bool isAnimating = false;
    private bool isReady = false;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        if (sr != null && spriteEmpty != null)
            sr.sprite = spriteEmpty;
    }

    // ── IInteractable ──────────────────────────────────────────
    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        InventorySlot slot = HotbarManager.Instance?.GetActiveSlot();
        if (slot == null || !slot.IsWateringCan())
        {
            Debug.Log("[Колодец] Нужна лейка в руке!");
            return;
        }

        // Второе нажатие — забрать воду
        if (isReady)
        {
            slot.FillWater();
            isReady = false;

            if (sr != null && spriteEmpty != null)
                sr.sprite = spriteEmpty;

            if (wellAnimator != null)
                wellAnimator.enabled = true;

            Debug.Log("[Колодец] Вода набрана! " + slot.currentWater + "/" + slot.GetMaxWater());
            return;
        }

        // Первое нажатие — анимация
        if (!isAnimating)
        {
            if (slot.currentWater >= slot.GetMaxWater())
            {
                Debug.Log("[Колодец] Лейка уже полная!");
                return;
            }
            StartCoroutine(WellAnimation());
        }
    }
    // ───────────────────────────────────────────────────────────

    IEnumerator WellAnimation()
    {
        isAnimating = true;
        isReady = false;

        if (wellAnimator != null)
        {
            wellAnimator.enabled = true;
            wellAnimator.SetTrigger("Watering");
        }

        yield return new WaitForSeconds(animationDuration);

        if (wellAnimator != null)
            wellAnimator.enabled = false;

        if (sr != null && spriteFull != null)
            sr.sprite = spriteFull;

        isAnimating = false;
        isReady = true;
        Debug.Log("[Колодец] Ведро поднято! Нажми ещё раз чтобы набрать воду.");
    }
}