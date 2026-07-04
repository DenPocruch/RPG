using UnityEngine;
using System.Collections;

public class ToolController : MonoBehaviour
{
    private Animator animator;
    private SpriteRenderer spriteRenderer;

    void Start()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
            spriteRenderer.enabled = false;
    }

    public void UseTool(ItemType toolType, float dirX, float dirY, float duration)
    {
        StartCoroutine(UseToolCoroutine(toolType, dirX, dirY, duration));
    }

    IEnumerator UseToolCoroutine(ItemType toolType, float dirX, float dirY, float duration)
    {
        if (spriteRenderer != null)
            spriteRenderer.enabled = true;

        if (animator != null)
        {
            animator.SetFloat("LastMoveX", dirX);
            animator.SetFloat("LastMoveY", dirY);

            switch (toolType)
            {
                case ItemType.Hoe:
                    animator.SetTrigger("UseHoe");
                    break;
                case ItemType.Pickaxe:
                    animator.SetTrigger("UsePickaxe");
                    break;
                case ItemType.BugNet:
                    animator.SetTrigger("UseBugNet");
                    break;
                case ItemType.WateringCan:
                    animator.SetTrigger("Watering");
                    break;
                case ItemType.Axe:
                    animator.SetTrigger("UseAxe");
                    break;
                case ItemType.Sickle:
                    animator.SetTrigger("UseSickle");
                    break;
            }
        }

        yield return new WaitForSeconds(duration);

        if (spriteRenderer != null)
            spriteRenderer.enabled = false;
    }

    public void ForceHide()
    {
        StopAllCoroutines();
        if (spriteRenderer != null)
            spriteRenderer.enabled = false;
    }
}