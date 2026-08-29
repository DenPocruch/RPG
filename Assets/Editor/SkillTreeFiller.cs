using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Tools → Skill Tree → Fill And Sort Nodes
/// Заполняет контейнеры вкладок SkillNodeUI из префаба SkillNodeButton
/// для всех SkillNode (Assets/Resources/SkillNodes/Tree) и сортирует
/// по requiredLevel. Идемпотентно: существующие ноды не дублируются.
/// Видимость страниц уже рулит SkillTreeUI (пагинация).
/// </summary>
public static class SkillTreeFiller
{
    const string NodePrefabPath = "Assets/Prefab/SkillNodeButton.prefab";
    const string NodesFolder = "Assets/Resources/SkillNodes/Tree";

    [MenuItem("Tools/Skill Tree/Fill And Sort Nodes")]
    static void FillAndSort()
    {
        var ui = Object.FindFirstObjectByType<SkillTreeUI>(FindObjectsInactive.Include);
        if (ui == null) { Debug.LogError("[SkillTreeFiller] SkillTreeUI не найден в сцене"); return; }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NodePrefabPath);
        if (prefab == null) { Debug.LogError("[SkillTreeFiller] Не найден префаб " + NodePrefabPath); return; }

        var allNodes = LoadNodes();
        if (allNodes.Count == 0) { Debug.LogError("[SkillTreeFiller] Не найдено ни одного SkillNode в " + NodesFolder); return; }

        int totalAdded = 0;
        totalAdded += FillContainer(ui.combatContainer, PlayerLevel.SkillBranch.Combat, allNodes, prefab);
        totalAdded += FillContainer(ui.farmingContainer, PlayerLevel.SkillBranch.Farming, allNodes, prefab);
        totalAdded += FillContainer(ui.craftingContainer, PlayerLevel.SkillBranch.Crafting, allNodes, prefab);

        SortContainer(ui.combatContainer);
        SortContainer(ui.farmingContainer);
        SortContainer(ui.craftingContainer);

        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        Debug.Log("[SkillTreeFiller] Готово. Добавлено нод: " + totalAdded +
                  ". Сохрани сцену (Ctrl+S).");
    }

    static List<SkillNode> LoadNodes()
    {
        var result = new List<SkillNode>();
        foreach (var guid in AssetDatabase.FindAssets("t:SkillNode", new[] { NodesFolder }))
        {
            var node = AssetDatabase.LoadAssetAtPath<SkillNode>(AssetDatabase.GUIDToAssetPath(guid));
            if (node != null) result.Add(node);
        }
        return result.OrderBy(n => (int)n.branch).ThenBy(n => n.requiredLevel).ThenBy(n => n.name).ToList();
    }

    static int FillContainer(GameObject container, PlayerLevel.SkillBranch branch, List<SkillNode> allNodes, GameObject prefab)
    {
        if (container == null) { Debug.LogError("[SkillTreeFiller] Контейнер " + branch + " не привязан в SkillTreeUI"); return 0; }

        var existing = new HashSet<string>();
        foreach (var nodeUI in container.GetComponentsInChildren<SkillNodeUI>(true))
            if (nodeUI.node != null)
                existing.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(nodeUI.node)));

        int added = 0;
        foreach (var node in allNodes)
        {
            if (node.branch != branch) continue;
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(node));
            if (existing.Contains(guid)) continue;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.SetParent(container.transform, false);
            go.name = node.name;

            var nodeUI = go.GetComponent<SkillNodeUI>();
            nodeUI.node = node;

            Undo.RegisterCreatedObjectUndo(go, "Add Skill Node UI");
            added++;
        }
        Debug.Log("[SkillTreeFiller] " + container.name + ": +" + added + " (было " + existing.Count + ")");
        return added;
    }

    static void SortContainer(GameObject container)
    {
        if (container == null) return;
        var sorted = container.GetComponentsInChildren<SkillNodeUI>(true)
            .Where(n => n.node != null)
            .OrderBy(n => n.node.requiredLevel)
            .ThenBy(n => n.node.nodeName)
            .ToList();
        for (int i = 0; i < sorted.Count; i++)
            sorted[i].transform.SetSiblingIndex(i);
        Debug.Log("[SkillTreeFiller] " + container.name + ": отсортировано " + sorted.Count + " нод по requiredLevel");
    }
}