using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Editor-инструмент для дерева прокачки.
/// Tools → Skill Tree → Rebuild UI From Manager:
/// очищает контейнеры вкладок и создаёт SkillNodeUI заново из
/// SkillTreeManager.allNodes (по веткам), клонируя внешний вид
/// существующего узла как шаблон. После перестроения позиции
/// можно свободно двигать вручную.
/// </summary>
public static class SkillTreeRebuilder
{
    [MenuItem("Tools/Skill Tree/Rebuild UI From Manager")]
    static void Rebuild()
    {
        var ui = Object.FindFirstObjectByType<SkillTreeUI>();
        if (ui == null) { Debug.LogError("[SkillTreeRebuild] SkillTreeUI не найден в сцене"); return; }

        var manager = Object.FindFirstObjectByType<SkillTreeManager>();
        if (manager == null || manager.allNodes == null || manager.allNodes.Length == 0)
        { Debug.LogError("[SkillTreeRebuild] SkillTreeManager.allNodes пуст"); return; }

        // Шаблон — первый найденный SkillNodeUI (со всем его оформлением).
        // Клонируем его в корень сцены ДО удаления контейнеров.
        SkillNodeUI template = Object.FindFirstObjectByType<SkillNodeUI>();
        if (template == null) { Debug.LogError("[SkillTreeRebuild] Ни одного SkillNodeUI не найдено (шаблон)"); return; }

        GameObject templateClone = (GameObject)Object.Instantiate(template.gameObject);
        templateClone.name = "SKILL_NODE_TEMPLATE_TEMP";
        templateClone.transform.SetParent(null, false);
        templateClone.SetActive(false);

        Undo.RecordObject(ui, "Rebuild Skill Tree UI");

        RebuildContainer(ui.combatContainer, manager, PlayerLevel.SkillBranch.Combat, templateClone);
        RebuildContainer(ui.farmingContainer, manager, PlayerLevel.SkillBranch.Farming, templateClone);
        RebuildContainer(ui.craftingContainer, manager, PlayerLevel.SkillBranch.Crafting, templateClone);

        Object.DestroyImmediate(templateClone);

        EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
        Debug.Log("[SkillTreeRebuild] Готово: дерево перестроено из allNodes (" + manager.allNodes.Length + " узлов). Позиции можно двигать вручную.");
    }

    static void RebuildContainer(GameObject container, SkillTreeManager manager, PlayerLevel.SkillBranch branch, GameObject templateClone)
    {
        if (container == null) return;

        Undo.RecordObject(container, "Rebuild Skill Tree UI");

        // Удаляем старые узлы контейнера
        for (int i = container.transform.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(container.transform.GetChild(i).gameObject);

        Vector2 cell = new Vector2(150f, 170f); // шаг сетки
        int col = 0, row = 0, count = 0;

        foreach (SkillNode node in manager.allNodes)
        {
            if (node == null || node.branch != branch) continue;

            GameObject go = (GameObject)Object.Instantiate(templateClone, container.transform, false);
            go.name = "Node_" + node.name;
            go.SetActive(true);

            var nodeUI = go.GetComponent<SkillNodeUI>();
            nodeUI.node = node;

            // Простая сетка 4 в ряд; позиции можно двигать вручную после
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition = new Vector2(120f + col * cell.x, -120f - row * cell.y);

            col++;
            if (col >= 4) { col = 0; row++; }
            count++;
        }

        Debug.Log("[SkillTreeRebuild] " + container.name + ": " + count + " узлов");
    }
}
