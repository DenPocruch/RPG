using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

/// <summary>
/// Портал перехода в другую сцену (ферма ↔ город).
/// Затемняет экран → сохраняет текущее состояние → грузит новую сцену →
/// ставит игрока на нужную точку появления → осветляет.
///
/// Может срабатывать по касанию (triggerOnTouch) или по кнопке атаки
/// (IInteractable). Игрок и менеджеры переживают переход через PersistentRoot.
/// </summary>
public class SceneTransition : MonoBehaviour, IInteractable
{
    [Header("Куда ведёт портал")]
    public string targetScene = "City";     // имя сцены (должна быть в Build Settings)
    public string targetSpawnId = "Default";  // id точки появления в той сцене

    [Header("Как срабатывает")]
    [Tooltip("Автоматически при входе в триггер (наступил на дверь)")]
    public bool triggerOnTouch = true;

    private static string pendingSpawnId;
    private bool started = false;

    // ── IInteractable (кнопка атаки) ───────────────────────────
    public Transform GetTransform() => transform;
    public void Interact(GameObject player) => Begin();
    // ───────────────────────────────────────────────────────────

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!triggerOnTouch) return;
        if (!other.CompareTag("Player")) return;
        Begin();
    }

    void Begin()
    {
        if (started) return;
        started = true;
        pendingSpawnId = targetSpawnId;
        StartCoroutine(TransitionRoutine());
    }

    IEnumerator TransitionRoutine()
    {
        // Сохраняем состояние текущей сцены (ферма/сундуки и т.д.) перед уходом
        SaveManager.Instance?.Save();

        // Затемнение
        if (ScreenFader.Instance != null)
            yield return ScreenFader.Instance.FadeOut();

        // Грузим новую сцену (одиночный режим — старая выгружается,
        // PersistentRoot переживает благодаря DontDestroyOnLoad)
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.LoadScene(targetScene);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        // Ставим игрока на точку появления
        GameObject player = GameObject.FindWithTag("Player");
        SceneSpawnPoint spawn = SceneSpawnPoint.Find(pendingSpawnId);

        if (player != null && spawn != null)
        {
            Vector3 pos = spawn.transform.position;
            pos.z = player.transform.position.z;
            player.transform.position = pos;

            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) rb.linearVelocity = Vector2.zero;

            // Мгновенно ставим камеру на игрока
            if (Camera.main != null)
            {
                Vector3 cam = Camera.main.transform.position;
                cam.x = pos.x; cam.y = pos.y;
                Camera.main.transform.position = cam;
            }
        }

        // Осветление
        if (ScreenFader.Instance != null)
            ScreenFader.Instance.StartFadeIn();
    }
}