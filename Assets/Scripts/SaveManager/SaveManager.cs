using UnityEngine;
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
    private string FilePath => Path.Combine(Application.persistentDataPath, FILE_NAME);

    private readonly List<ISaveable> saveables = new List<ISaveable>();
    private readonly Dictionary<string, string> loadedBlobs = new Dictionary<string, string>();

    [System.Serializable] private class Entry { public string key; public string json; }
    [System.Serializable] private class SaveFile { public List<Entry> entries = new List<Entry>(); }

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
    }

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

    /// <summary>Система зовёт это в своём Start после инициализации.</summary>
    public void LoadInto(ISaveable s)
    {
        if (s == null) return;
        if (loadedBlobs.TryGetValue(s.SaveKey, out string json) && !string.IsNullOrEmpty(json))
        {
            try { s.RestoreState(json); }
            catch (System.Exception e) { Debug.LogWarning("[Save] Ошибка загрузки " + s.SaveKey + ": " + e.Message); }
        }
    }

    // ═══════════════════════════════════════════════════════════
    // СОХРАНЕНИЕ
    // ═══════════════════════════════════════════════════════════
    public void Save()
    {
        SaveFile file = new SaveFile();
        foreach (ISaveable s in saveables)
        {
            if (s == null) continue;

            // ВАЖНО: ISaveable — интерфейс, обычная проверка на null не ловит
            // уничтоженный MonoBehaviour (смена сцены). Проверяем через Object.
            UnityEngine.Object obj = s as UnityEngine.Object;
            if (obj == null) continue; // объект уничтожен — пропускаем

            string json = s.CaptureState();
            file.entries.Add(new Entry { key = s.SaveKey, json = json });
            loadedBlobs[s.SaveKey] = json; // держим память в актуальном виде
        }

        try
        {
            string data = JsonUtility.ToJson(file);
            File.WriteAllText(FilePath, data);
            Debug.Log("[Save] Сохранено (" + file.entries.Count + " систем) → " + FilePath);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Save] Ошибка записи: " + e.Message);
        }
    }

    void ReadFile()
    {
        loadedBlobs.Clear();
        if (!File.Exists(FilePath))
        {
            Debug.Log("[Save] Файл сохранения не найден — новая игра");
            return;
        }

        try
        {
            string data = File.ReadAllText(FilePath);
            SaveFile file = JsonUtility.FromJson<SaveFile>(data);
            if (file != null && file.entries != null)
                foreach (Entry e in file.entries)
                    loadedBlobs[e.key] = e.json;
            Debug.Log("[Save] Загружено сохранение (" + loadedBlobs.Count + " систем)");
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Save] Ошибка чтения: " + e.Message);
        }
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
        if (File.Exists(FilePath)) File.Delete(FilePath);
        loadedBlobs.Clear();
        Debug.Log("[Save] Сохранение удалено");
    }
}