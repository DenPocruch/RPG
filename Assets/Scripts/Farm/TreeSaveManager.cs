using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Сохранение посаженных деревьев (PlantedTree) — работает в ЛЮБОЙ сцене.
/// Менеджер создаётся автоматически при загрузке каждой сцены (см. Init ниже),
/// вручную добавлять его на сцену не нужно.
///
/// Ключ сохранения — "ИмяСцены/trees": у каждой сцены свои деревья.
/// </summary>
public class TreeSaveManager : MonoBehaviour, ISaveable
{
    // ═══════════════════════════════════════════════════════════
    // АВТОСОЗДАНИЕ В КАЖДОЙ СЦЕНЕ
    // EnsureInScene вызывает SaveManager при загрузке сцены.
    // ═══════════════════════════════════════════════════════════
    public static void EnsureInScene(Scene scene)
    {
        if (!scene.IsValid()) return;

        // Ищем менеджер именно в этой сцене (в момент загрузки в памяти
        // ещё может находиться умирающий менеджер предыдущей сцены)
        foreach (TreeSaveManager m in FindObjectsByType<TreeSaveManager>(FindObjectsSortMode.None))
            if (m != null && m.gameObject.scene == scene) return;

        GameObject go = new GameObject("TreeSaveManager");
        SceneManager.MoveGameObjectToScene(go, scene); // привязываем к сцене
        var manager = go.AddComponent<TreeSaveManager>();

        // Восстанавливаем СРАЗУ (а не в Start) — до первого рендера сцены
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
    private class TreeSave
    {
        public float x;
        public float y;
        public float z;
        public string itemName;      // ItemData саженца
        public int stage;            // стадия роста (если ещё растёт)
        public float growthTimer;    // накопленное время текущей стадии
        public bool grown;           // полностью выросло
        public bool dried;           // высохло (плодовое)
        public bool hasFruit;
        public int fruitHarvestCount;
        public float fruitTimer;
        public bool fruitTimerActive;
    }

    [System.Serializable]
    private class TreesSave
    {
        public List<TreeSave> trees = new List<TreeSave>();
    }

    public string SaveKey => "trees";

    public string CaptureState()
    {
        TreesSave save = new TreesSave();

        foreach (PlantedTree tree in FindObjectsByType<PlantedTree>(FindObjectsSortMode.None))
        {
            if (tree == null || tree.saplingData == null) continue;

            // Падающее (срубленное) дерево не сохраняем — оно исчезает из мира
            TreeComponent fallingCheck = tree.GetComponent<TreeComponent>();
            if (fallingCheck != null && fallingCheck.IsFalling) continue;

            TreeSave ts = new TreeSave
            {
                x = tree.transform.position.x,
                y = tree.transform.position.y,
                z = tree.transform.position.z,
                itemName = tree.saplingData.name,
                stage = tree.CurrentStage,
                growthTimer = tree.GrowthTimer,
                grown = tree.IsFullyGrown
            };

            // У выросшего дерева сохраняем состояние плодоношения
            if (tree.IsFullyGrown)
            {
                TreeComponent tc = tree.GetComponent<TreeComponent>();
                if (tc != null)
                {
                    ts.dried = tc.IsDried;
                    ts.hasFruit = tc.HasFruit();
                    ts.fruitHarvestCount = tc.FruitHarvestCount;
                    ts.fruitTimer = tc.FruitTimer;
                    ts.fruitTimerActive = tc.FruitTimerActive;
                }
            }

            save.trees.Add(ts);
        }

        return JsonUtility.ToJson(save);
    }

    public void RestoreState(string json)
    {
        TreesSave save = JsonUtility.FromJson<TreesSave>(json);
        if (save == null || save.trees == null) return;

        // Убираем существующие деревья (на случай повторной загрузки в этой же сессии)
        foreach (PlantedTree oldTree in FindObjectsByType<PlantedTree>(FindObjectsSortMode.None))
            if (oldTree != null) Destroy(oldTree.gameObject);

        foreach (TreeSave ts in save.trees)
        {
            ItemData saplingData = ItemDatabase.Find(ts.itemName);
            if (saplingData == null || saplingData.treePrefab == null)
            {
                Debug.LogWarning("[Save] Саженец/префаб дерева не найден: " + ts.itemName);
                continue;
            }

            Vector3 pos = new Vector3(ts.x, ts.y, ts.z);
            GameObject treeObj = Instantiate(saplingData.treePrefab, pos, Quaternion.identity);
            PlantedTree planted = treeObj.GetComponent<PlantedTree>();
            if (planted == null) continue;

            // ВАЖНО: saplingData устанавливаем ДО того как отработает Start()
            // (Instantiate не вызывает Start синхронно)
            planted.saplingData = saplingData;

            if (ts.grown)
            {
                planted.RestoreGrown();

                TreeComponent tc = treeObj.GetComponent<TreeComponent>();
                if (tc != null)
                    tc.ApplyRestoredState(ts.dried, ts.hasFruit, ts.fruitHarvestCount, ts.fruitTimer, ts.fruitTimerActive);
            }
            else
            {
                planted.RestoreGrowth(ts.stage, ts.growthTimer);
            }
        }

        Debug.Log("[Save] Деревья восстановлены (" + SaveKey + "): " + save.trees.Count + " шт.");
    }
}
