using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Collections.Generic;

/// <summary>
/// Сохранение животных (AnimalController) — работает в ЛЮБОЙ сцене.
/// Менеджер создаётся автоматически при загрузке каждой сцены
/// (вызывается из SaveManager.ProcessScene), вручную добавлять не нужно.
///
/// Ключ сохранения — "ИмяСцены/animals": у каждой сцены свои животные.
///
/// ОФФЛАЙН-ПРОГРЕСС: в сейве хранится реальное время (UTC). При загрузке
/// вычисляется сколько секунд прошло с момента сохранения — животные
/// подрастают и производят продукт "за время отсутствия игрока".
/// Животное держит оффлайн-продукт при себе и отдаёт когда игрок подошёл.
/// </summary>
public class AnimalSaveManager : MonoBehaviour, ISaveable
{
    // ═══════════════════════════════════════════════════════════
    // АВТОСОЗДАНИЕ В КАЖДОЙ СЦЕНЕ
    // ═══════════════════════════════════════════════════════════
    public static void EnsureInScene(Scene scene)
    {
        if (!scene.IsValid()) return;

        // Ищем менеджер именно в этой сцене (в момент загрузки в памяти
        // ещё может находиться умирающий менеджер предыдущей сцены)
        foreach (AnimalSaveManager m in FindObjectsByType<AnimalSaveManager>(FindObjectsSortMode.None))
            if (m != null && m.gameObject.scene == scene) return;

        GameObject go = new GameObject("AnimalSaveManager");
        SceneManager.MoveGameObjectToScene(go, scene); // привязываем к сцене
        var manager = go.AddComponent<AnimalSaveManager>();

        // Восстанавливаем СРАЗУ (а не в Start) — иначе животные успеют
        // отрисоваться на спавне и через кадр телепортироваться
        SaveManager.Instance?.LoadInto(manager);
    }

    // ═══════════════════════════════════════════════════════════
    // ЖИЗНЕННЫЙ ЦИКЛ
    // ═══════════════════════════════════════════════════════════
    void Awake()
    {
        SaveManager.Instance?.Register(this);
    }

    void OnDestroy()
    {
        SaveManager.Instance?.Unregister(this);
    }

    // ═══════════════════════════════════════════════════════════
    // ISaveable
    // ═══════════════════════════════════════════════════════════
    [System.Serializable]
    private class AnimalSave
    {
        public string id;            // имя объекта в сцене (стабильно между запусками)
        public string dataName;      // AnimalData (курица, корова...)
        public int stage;            // 0=Baby, 1=Teen, 2=Adult
        public float growTimer;      // остаток до следующей стадии
        public bool isFed;
        public float productionTimer;
        public bool pendingProduct;  // продукт произведён, но ещё не отдан игроку
        public float x, y, z;
    }

    [System.Serializable]
    private class AnimalsSave
    {
        public long savedAtTicks;    // реальное время сохранения (UTC) — для оффлайн-прогресса
        public List<AnimalSave> animals = new List<AnimalSave>();
    }

    public string SaveKey => "animals";

    public string CaptureState()
    {
        AnimalsSave save = new AnimalsSave
        {
            savedAtTicks = DateTime.UtcNow.Ticks
        };

        foreach (AnimalController animal in FindObjectsByType<AnimalController>(FindObjectsSortMode.None))
        {
            if (animal == null || animal.data == null) continue;

            Vector3 pos = animal.transform.position;
            save.animals.Add(new AnimalSave
            {
                id = animal.gameObject.name,
                dataName = animal.data.name,
                stage = (int)animal.CurrentStage,
                growTimer = animal.GrowTimerRemaining,
                isFed = animal.IsFed,
                productionTimer = animal.ProductionTimerRemaining,
                pendingProduct = animal.HasPendingProduct,
                x = pos.x,
                y = pos.y,
                z = pos.z
            });
        }

        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        AnimalsSave save = JsonUtility.FromJson<AnimalsSave>(json);
        if (save == null || save.animals == null) return;

        // Сколько реальных секунд прошло с момента сохранения
        double elapsedSeconds = (DateTime.UtcNow.Ticks - save.savedAtTicks) / (double)TimeSpan.TicksPerSecond;
        if (elapsedSeconds < 0) elapsedSeconds = 0; // часы на устройстве перевели назад

        AnimalController[] live = FindObjectsByType<AnimalController>(FindObjectsSortMode.None);

        foreach (AnimalController animal in live)
        {
            if (animal == null || animal.data == null) continue;

            // Матчинг по имени объекта + виду (имена расставлены в сцене и стабильны)
            AnimalSave saved = save.animals.Find(a => a.id == animal.gameObject.name && a.dataName == animal.data.name);
            if (saved == null) continue; // новое животное — стартует с нуля

            animal.ApplyRestoredState(
                saved.stage,
                saved.growTimer,
                saved.isFed,
                saved.productionTimer,
                saved.pendingProduct,
                new Vector3(saved.x, saved.y, saved.z));

            // Оффлайн-прогресс: рост и созревание продукта за время отсутствия
            animal.ApplyOfflineTime(elapsedSeconds);
        }

        Debug.Log("[Save] Животные восстановлены (" + SaveKey + "): " + save.animals.Count +
                  " шт., оффлайн прошло " + (int)elapsedSeconds + " сек.");
    }
}
