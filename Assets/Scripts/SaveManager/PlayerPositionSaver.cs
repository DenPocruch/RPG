using UnityEngine;

/// <summary>
/// Сохранение позиции игрока между сессиями.
/// Вешается на Player (в PersistentRoot).
///
/// Позиция — сценарозависимая величина: в блобе храним имя сцены и координаты.
/// Восстанавливаем только если текущая сцена совпадает с сохранённой —
/// тогда игрок при запуске игры появляется там, где вышел.
///
/// При переходе между сценами через портал позицию задаёт SceneSpawnPoint —
/// он срабатывает позже (в sceneLoaded) и перекрывает восстановленное.
/// </summary>
public class PlayerPositionSaver : MonoBehaviour, ISaveable
{
    void Awake()
    {
        SaveManager.Instance?.Register(this);
    }

    void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    void Start()
    {
        SaveManager.Instance?.LoadInto(this);
    }

    /// <summary>Повторное восстановление (зывает SaveManager после загрузки нужной сцены).</summary>
    public void RestoreFromSaveManager()
    {
        SaveManager.Instance?.LoadInto(this);
    }

    /// <summary>Имя сцены, в которой игрок сохранился (null если сохранения нет).</summary>
    public static string SavedSceneName()
    {
        string json = SaveManager.Instance != null ? SaveManager.Instance.GetBlob(SaveKeyStatic) : null;
        if (string.IsNullOrEmpty(json)) return null;

        PositionSave save = JsonUtility.FromJson<PositionSave>(json);
        return save != null ? save.scene : null;
    }

    private const string SaveKeyStatic = "playerPos";

    // ─── ISaveable ─────────────────────────────────────────────
    [System.Serializable]
    private class PositionSave
    {
        public string scene;
        public float x;
        public float y;
        public float z;
    }

    public string SaveKey => SaveKeyStatic;

    public string CaptureState()
    {
        PositionSave save = new PositionSave
        {
            scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            x = transform.position.x,
            y = transform.position.y,
            z = transform.position.z
        };
        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        PositionSave save = JsonUtility.FromJson<PositionSave>(json);
        if (save == null) return;

        // Восстанавливаем только в той же сцене где игрок был сохранён
        string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (save.scene != currentScene) return;

        Vector3 pos = new Vector3(save.x, save.y, transform.position.z);
        transform.position = pos;

        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        // Мгновенно ставим камеру на игрока, чтобы не было рывка
        if (Camera.main != null)
        {
            Vector3 cam = Camera.main.transform.position;
            cam.x = pos.x;
            cam.y = pos.y;
            Camera.main.transform.position = cam;
        }

        Debug.Log("[Save] Позиция игрока восстановлена: " + pos);
    }
}
