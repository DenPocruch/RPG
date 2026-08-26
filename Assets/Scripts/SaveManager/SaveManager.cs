using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Любая система, которая хочет сохраняться, реализует этот интерфейс.
/// Добавить новую систему в сохранение = просто реализовать ISaveable и
/// зарегистрироваться. SaveManager трогать не нужно.
/// </summary>
public interface ISaveable
{
    string SaveKey { get; }          // уникальный ключ ("gold", "level"...)
    string CaptureState();           // вернуть своё состояние как JSON
    void RestoreState(string json); // восстановить из JSON
}

/// <summary>
/// Центральный менеджер сохранения. Одно сохранение, автосейв при сворачивании/
/// выходе + периодически. Файл: Application.persistentDataPath/save.json
///
/// Порядок (гарантирован через DefaultExecutionOrder):
///   1. SaveManager.Awake — читает файл в память (loadedBlobs)
///   2. Системы Awake — регистрируются для сохранения
///   3. Системы Start — инициализируются, затем зовут LoadInto(this) → восстановление
/// </summary>
[DefaultExecutionOrder(-1000)] // Awake раньше всех остальных систем
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [Header("Автосохранение")]
    public bool autosaveEnabled = true;
    public float autosaveInterval = 60f; // секунд

    private const string FILE_NAME = "save.json";
    private const string BACKUP_NAME = "save_backup.json";
    private const string TEMP_NAME = "save_temp.json";
    private const int SAVE_VERSION = 2;

    private string FilePath => Path.Combine(Application.persistentDataPath, FILE_NAME);
    private string BackupPath => Path.Combine(Application.persistentDataPath, BACKUP_NAME);
    private string TempPath => Path.Combine(Application.persistentDataPath, TEMP_NAME);

    private readonly List<ISaveable> saveables = new List<ISaveable>();
    private readonly Dictionary<string, string> loadedBlobs = new Dictionary<string, string>();

    [System.Serializable] private class Entry { public string key; public string json; }
    [System.Serializable] private class SaveFile { public int version = SAVE_VERSION; public List<Entry> entries = new List<Entry>(); }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        ReadFile();
    }

    void Start()
    {
        if (autosaveEnabled)
            InvokeRepeating(nameof(Save), autosaveInterval, autosaveInterval);

        // Страховка: обрабатываем стартовую сцену вручную — на случай если
        // sceneLoaded для первой сцены отработал до подписки или не сработал.
        // Повторный вызов безопасен (защита от двойной обработки внутри).
        ProcessScene(SceneManager.GetActiveScene());
    }

    // ═══════════════════════════════════════════════════════════
    // АВТОУСТАНОВКА СОХРАНЯЕМЫХ КОМПОНЕНТОВ ПРИ ЗАГРУЗКЕ СЦЕНЫ
    // (вызывается до Start'ов объектов сцены — LoadInto успевает отработать)
    // ═══════════════════════════════════════════════════════════
    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoadedEvent;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoadedEvent;
    }

    void OnSceneLoadedEvent(Scene scene, LoadSceneMode mode)
    {
        if (mode != LoadSceneMode.Single) return;
        ProcessScene(scene);
    }

    void ProcessScene(Scene scene)
    {
        if (!scene.IsValid()) return;

        // Стартовую сцену обрабатываем один раз (Start + sceneLoaded могут
        // оба вызвать этот метод для неё)
        if (initialSceneResolved && scene.name == initialSceneName) return;

        Debug.Log("[Save] Обработка сцены: " + scene.name +
                  (SceneTransition.PortalTransitionActive ? " (переход через портал)" : " (старт/загрузка)"));

        // Менеджер деревьев — в каждой сцене свой
        TreeSaveManager.EnsureInScene(scene);

        // Менеджер животных — в каждой сцене свой
        AnimalSaveManager.EnsureInScene(scene);

        // Позиция игрока — компонент вешаем автоматически, если его нет
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null && player.GetComponent<PlayerPositionSaver>() == null)
            player.AddComponent<PlayerPositionSaver>();

        // Переход через портал — точку появления ставит SceneTransition
        if (SceneTransition.PortalTransitionActive) return;

        // Первый запуск игры: если игрок сохранился в другой сцене — грузим её
        if (!initialSceneResolved)
        {
            initialSceneResolved = true;
            initialSceneName = scene.name;

            string savedScene = PlayerPositionSaver.SavedSceneName();
            Debug.Log("[Save] Сохранённая сцена игрока: " + (savedScene ?? "<нет>") + ", текущая: " + scene.name);

            if (!string.IsNullOrEmpty(savedScene)
                && savedScene != scene.name
                && Application.CanStreamedLevelBeLoaded(savedScene))
            {
                Debug.Log("[Save] Старт в сохранённой сцене: " + savedScene);

                // Мгновенно чёрный экран — стартовая сцена не успеет отрисоваться
                initialSwitchFadeInPending = true;
                if (ScreenFader.Instance != null)
                    ScreenFader.Instance.SetBlackInstant();

                SceneManager.LoadScene(savedScene);
                return;
            }
        }

        // Восстанавливаем позицию если эта сцена — та, где игрок сохранялся
        if (player != null)
        {
            PlayerPositionSaver saver = player.GetComponent<PlayerPositionSaver>();
            if (saver != null) saver.RestoreFromSaveManager();
        }

        // Стартовая сцена сменилась на сохранённую — осветляем экран
        if (initialSwitchFadeInPending)
        {
            initialSwitchFadeInPending = false;
            if (ScreenFader.Instance != null) ScreenFader.Instance.StartFadeIn();
        }
    }

    private bool initialSwitchFadeInPending = false;

    private bool initialSceneResolved = false;
    private string initialSceneName;

    // ═══════════════════════════════════════════════════════════
    // РЕГИСТРАЦИЯ И ЗАГРУЗКА (системы зовут это сами)
    // ═══════════════════════════════════════════════════════════
    public void Register(ISaveable s)
    {
        if (s != null && !saveables.Contains(s)) saveables.Add(s);
    }

    /// <summary>Отписка — объекты сцены зовут это в OnDestroy при смене сцены,
    /// чтобы SaveManager не держал ссылку на уничтоженный объект.</summary>
    public void Unregister(ISaveable s)
    {
        if (s != null) saveables.Remove(s);
    }

    /// <summary>Прочитать блоб сохранения по ключу (null если нет).</summary>
    public string GetBlob(string key)
    {
        return loadedBlobs.TryGetValue(key, out string json) ? json : null;
    }

    /// <summary>Система зовёт это в своём Start после инициализации.</summary>
    public void LoadInto(ISaveable s)
    {
        if (s == null) return;

        // Сперва ключ своей сцены ("Farm/farm"), затем — старый плоский
        // ("farm") для совместимости с сохранениями до разделения по сценам.
        string json = null;
        if (!loadedBlobs.TryGetValue(FullKey(s), out json) || string.IsNullOrEmpty(json))
            loadedBlobs.TryGetValue(s.SaveKey, out json);

        if (!string.IsNullOrEmpty(json))
        {
            try { s.RestoreState(json); }
            catch (System.Exception e) { Debug.LogWarning("[Save] Ошибка загрузки " + s.SaveKey + ": " + e.Message); }
        }
    }

    /// <summary>
    /// Полный ключ сохранения с учётом сцены.
    /// Объекты конкретной сцены (ферма, сундуки, мастерские) получают ключ
    /// "ИмяСцены/ключ" — у каждой сцены своё независимое состояние, переход
    /// между сценами больше не перезаписывает данные друг друга.
    /// Глобальные системы из PersistentRoot (золото, инвентарь, уровень...)
    /// сохраняются под простым ключом — они общие для всех сцен.
    /// </summary>
    public static string FullKey(ISaveable s)
    {
        Component c = s as Component;
        if (c == null) return s.SaveKey;

        // Глобальный объект: сам или кто-то из родителей имеет GamePersistence
        Transform t = c.transform;
        while (t != null)
        {
            if (t.GetComponent<GamePersistence>() != null) return s.SaveKey;
            t = t.parent;
        }

        // Подстраховка: объект уже живёт в DontDestroyOnLoad-сцене
        if (!c.gameObject.scene.IsValid() || c.gameObject.scene.name == "DontDestroyOnLoad")
            return s.SaveKey;

        return c.gameObject.scene.name + "/" + s.SaveKey;
    }

    // ═══════════════════════════════════════════════════════════
    // СОХРАНЕНИЕ (атомарная запись + бэкап + dirty-проверка)
    // ═══════════════════════════════════════════════════════════
    public void Save()
    {
        SaveFile file = new SaveFile();
        var currentKeys = new HashSet<string>();
        bool changed = false; // dirty flag: писать файл нужно только если данные менялись

        foreach (ISaveable s in saveables)
        {
            if (s == null) continue;

            // ВАЖНО: ISaveable — интерфейс, обычная проверка на null не ловит
            // уничтоженный MonoBehaviour (смена сцены). Проверяем через Object.
            UnityEngine.Object obj = s as UnityEngine.Object;
            if (obj == null) continue; // объект уничтожен — пропускаем

            try
            {
                string key = FullKey(s);
                string json = s.CaptureState();

                // Если хоть одна система отдала данные, отличные от прошлых — пишем
                if (!changed &&
                    (!loadedBlobs.TryGetValue(key, out string old) || old != json))
                    changed = true;

                file.entries.Add(new Entry { key = key, json = json });
                loadedBlobs[key] = json; // держим память в актуальном виде
                currentKeys.Add(key);
            }
            catch (System.Exception e)
            {
                // Одна сломанная система не должна убивать всё сохранение
                Debug.LogError("[Save] Ошибка сохранения " + s.SaveKey + ": " + e.Message);
            }
        }

        // ВАЖНО: в файл добавляем и блобы других сцен (они остаются в памяти,
        // но их системы уже отписались после выгрузки сцены). Без этого
        // сохранение в текущей сцене стирало бы из файла данные всех остальных.
        foreach (var kvp in loadedBlobs)
        {
            if (!currentKeys.Contains(kvp.Key))
                file.entries.Add(new Entry { key = kvp.Key, json = kvp.Value });
        }

        // Dirty flag: данные не менялись с прошлой записи — файл не трогаем
        // (экономит флеш-память и батарею телефона)
        if (!changed) return;

        WriteFile(file);
    }

    /// <summary>
    /// Атомарная запись: данные пишутся во временный файл, затем заменяют
    /// основной. Если процесс умрёт во время записи — основной файл не пострадает.
    /// Предыдущий успешный save.json сохраняется как бэкап.
    /// </summary>
    void WriteFile(SaveFile file)
    {
        try
        {
            string data = JsonUtility.ToJson(file);
            File.WriteAllText(TempPath, data);

            try
            {
                if (File.Exists(FilePath))
                {
                    // Атомарная замена: основной ← новый, бэкап ← старый основной
                    File.Replace(TempPath, FilePath, BackupPath, false);
                }
                else
                {
                    if (File.Exists(BackupPath)) File.Delete(BackupPath);
                    File.Move(TempPath, FilePath);
                }
            }
            catch (System.Exception)
            {
                // Запасной вариант если File.Replace не поддерживается платформой:
                // старый → бэкап, новый → основной (не атомарно, но надёжно в целом)
                if (File.Exists(FilePath)) File.Copy(FilePath, BackupPath, true);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(TempPath, FilePath);
            }

            Debug.Log("[Save] Сохранено (" + file.entries.Count + " систем) → " + FilePath);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Save] Ошибка записи: " + e.Message);
            if (File.Exists(TempPath))
                try { File.Delete(TempPath); } catch { }
        }
    }

    void ReadFile()
    {
        loadedBlobs.Clear();

        // Основной файл, при повреждении — бэкап предыдущего сохранения
        if (TryReadFile(FilePath, out SaveFile file))
        {
            FillFrom(file);
            Debug.Log("[Save] Загружено сохранение (" + loadedBlobs.Count + " систем)");
            return;
        }

        if (File.Exists(BackupPath) && TryReadFile(BackupPath, out file))
        {
            FillFrom(file);
            Debug.Log("[Save] Основной файл повреждён — восстановлено из БЭКАПА (" + loadedBlobs.Count + " систем)");
            return;
        }

        Debug.Log("[Save] Файл сохранения не найден — новая игра");
    }

    bool TryReadFile(string path, out SaveFile file)
    {
        file = null;
        if (!File.Exists(path)) return false;

        try
        {
            file = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path));
            return file != null && file.entries != null;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Save] Ошибка чтения " + Path.GetFileName(path) + ": " + e.Message);
            return false;
        }
    }

    void FillFrom(SaveFile file)
    {
        foreach (Entry e in file.entries)
            loadedBlobs[e.key] = e.json;
    }

    // ═══════════════════════════════════════════════════════════
    // АВТОСОХРАНЕНИЕ ПРИ СВОРАЧИВАНИИ / ВЫХОДЕ
    // ═══════════════════════════════════════════════════════════
    void OnApplicationPause(bool paused)
    {
        if (paused && autosaveEnabled) Save(); // сворачивание на мобильном
    }

    void OnApplicationQuit()
    {
        if (autosaveEnabled) Save();
    }

    // Для отладки — стереть сохранение
    public void DeleteSave()
    {
        foreach (string path in new string[] { FilePath, BackupPath, TempPath })
            if (File.Exists(path)) File.Delete(path);
        loadedBlobs.Clear();
        Debug.Log("[Save] Сохранение удалено");
    }
}