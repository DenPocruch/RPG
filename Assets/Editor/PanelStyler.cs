using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

/// <summary>
/// Tools → UI → Apply Sell Style To Panels
/// Перекрашивает панели в стиль окна скупщика:
///  - фон панели — тёмное дерево (тинт поверх спрайта)
///  - текст — белый, заголовки — золото #F5C542
///  - шрифт — стандартный TMP
/// Видит НЕАКТИВНЫЕ панели. После применения — сохранить сцену.
/// </summary>
public static class PanelStyler
{
    static readonly Color TitleColor = Hex("#F5C542");
    static readonly Color TextColor = Color.white;
    // Тинт фона панели: тёмное дерево как у скупщика
    static readonly Color PanelBgTint = new Color(0.45f, 0.33f, 0.22f, 1f);

    static readonly string[] panelNames =
    {
        "CookPanel", "ShopPanel", "LumberjackPanel", "MinerPanel",
        "SkillTreePanel", "StatsPanel", "ChestPanel", "DialoguePanel",
        "InventoryPanel", "EquipmentPanel"
    };

    [MenuItem("Tools/UI/Apply Sell Style To Panels")]
    static void Apply()
    {
        int textsChanged = 0, panelsFound = 0, bgsTinted = 0;
        var missing = new System.Text.StringBuilder();

        // Корень поиска: Canvas (рекурсивно видит неактивные)
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) { Debug.LogError("[PanelStyler] Canvas не найден"); return; }

        foreach (var panelName in panelNames)
        {
            Transform panel = FindChildRecursive(canvas.transform, panelName);
            if (panel == null) { missing.Append(panelName).Append(" "); continue; }
            panelsFound++;

            // 1. Фон панели: тинт корневого Image в тёмное дерево
            var rootImg = panel.GetComponent<Image>();
            if (rootImg != null && rootImg.sprite != null)
            {
                Undo.RecordObject(rootImg, "Apply Sell Style");
                rootImg.color = PanelBgTint;
                EditorUtility.SetDirty(rootImg);
                bgsTinted++;
            }

            // 2. Тексты: белый; заголовки — золото
            foreach (var tmp in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                Undo.RecordObject(tmp, "Apply Sell Style");

                var defFont = TMP_Settings.defaultFontAsset;
                if (defFont != null) tmp.font = defFont;

                bool isTitle = tmp.name.Contains("Title") || tmp.name.Contains("Header") ||
                               tmp.name.Contains("Name") || tmp.name.Contains("Label") ||
                               tmp.name.Contains("Recipe");
                tmp.color = isTitle ? TitleColor : TextColor;

                EditorUtility.SetDirty(tmp);
                textsChanged++;
            }
        }

        Debug.Log("[PanelStyler] Панелей: " + panelsFound + ", текстов: " + textsChanged +
                  ", фонов затинтовано: " + bgsTinted +
                  (missing.Length > 0 ? ". Не найдены: " + missing : ""));

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            var r = FindChildRecursive(child, name);
            if (r != null) return r;
        }
        return null;
    }

    static Color Hex(string hex)
    {
        Color c = Color.white;
        ColorUtility.TryParseHtmlString("#" + hex, out c);
        return c;
    }
}
