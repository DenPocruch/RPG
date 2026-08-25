using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Плавное затемнение экрана в чёрный и обратно.
/// Используется для переходов улица↔дом.
/// Метод Transition(onBlack) затемняет → вызывает действие в самый тёмный момент
/// (там телепортируем игрока) → осветляет обратно.
/// </summary>
public class ScreenFader : MonoBehaviour
{
    public static ScreenFader Instance { get; private set; }

    [Header("Чёрная картинка на весь экран")]
    public Image fadeImage;   // полноэкранный чёрный Image
    public CanvasGroup canvasGroup; // на том же объекте — для блокировки кликов

    [Header("Длительность")]
    public float fadeDuration = 0.4f;
    public float blackHold = 0.1f; // сколько держать чёрный экран

    private bool isTransitioning = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // Старт — прозрачный, клики пропускаем
        if (fadeImage != null)
        {
            Color c = fadeImage.color; c.a = 0f; fadeImage.color = c;
        }
        if (canvasGroup != null) canvasGroup.blocksRaycasts = false;
    }

    /// <summary>
    /// Затемнить → выполнить onBlack (телепорт) → осветлить.
    /// Пока идёт переход, управление игроком заблокировано.
    /// </summary>
    public void Transition(System.Action onBlack)
    {
        if (isTransitioning) return;
        StartCoroutine(TransitionRoutine(onBlack));
    }

    IEnumerator TransitionRoutine(System.Action onBlack)
    {
        isTransitioning = true;

        // Блокируем управление игроком
        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        if (canvasGroup != null) canvasGroup.blocksRaycasts = true;

        // Затемнение 0 → 1
        yield return Fade(0f, 1f);

        // В самый тёмный момент — телепорт
        onBlack?.Invoke();

        yield return new WaitForSeconds(blackHold);

        // Осветление 1 → 0
        yield return Fade(1f, 0f);

        if (canvasGroup != null) canvasGroup.blocksRaycasts = false;
        if (pm != null) pm.enabled = true;

        isTransitioning = false;
    }

    IEnumerator Fade(float from, float to)
    {
        if (fadeImage == null) yield break;

        float t = 0f;
        Color c = fadeImage.color;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(from, to, t / fadeDuration);
            fadeImage.color = c;
            yield return null;
        }
        c.a = to;
        fadeImage.color = c;
    }

    public bool IsTransitioning() => isTransitioning;

    // ═══════════════════════════════════════════════════════════
    // ДЛЯ СМЕНЫ СЦЕН — раздельные затемнение/осветление
    // ═══════════════════════════════════════════════════════════
    /// <summary>Затемнить в чёрный (вызывать перед загрузкой сцены).</summary>
    public IEnumerator FadeOut()
    {
        if (canvasGroup != null) canvasGroup.blocksRaycasts = true;
        yield return Fade(0f, 1f);
    }

    /// <summary>Мгновенно сделать экран чёрным, без анимации —
    /// чтобы стартовая сцена не успела отрисоваться при загрузке другой сцены.</summary>
    public void SetBlackInstant()
    {
        if (fadeImage != null)
        {
            Color c = fadeImage.color; c.a = 1f; fadeImage.color = c;
        }
        if (canvasGroup != null) canvasGroup.blocksRaycasts = true;
    }

    /// <summary>Осветлить из чёрного (после загрузки сцены).</summary>
    public void StartFadeIn()
    {
        StartCoroutine(FadeInRoutine());
    }

    IEnumerator FadeInRoutine()
    {
        yield return Fade(1f, 0f);
        if (canvasGroup != null) canvasGroup.blocksRaycasts = false;
    }
}