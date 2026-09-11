using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
/// Tools → Lore → 3. Doc Dialogues: автодок деревьев из данных.
/// Читает ВСЕ DialogueData в Resources/Dialogue и перегенерирует
/// Docs/Lore/Dialogues/Auto/*.md — доки не расходятся с игрой.
/// Ручные доки (Mayor.md и др.) — про ЗАМЫСЕЛ, их не трогает.
/// Прогоняй после каждой сборки диалогов.
/// </summary>
public static class LoreDialogueDocs
{
    [MenuItem("Tools/Lore/3. Doc Dialogues")]
    public static void GenerateAll()
    {
        string docsDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Lore", "Dialogues", "Auto"));
        Directory.CreateDirectory(docsDir);
        // Чистим старые (иначе удалённые ассеты оставят мусор)
        foreach (string f in Directory.GetFiles(docsDir, "*.md"))
        {
            try { File.Delete(f); } catch { }
        }

        int count = 0;
        var guids = AssetDatabase.FindAssets("t:DialogueData");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var d = AssetDatabase.LoadAssetAtPath<DialogueData>(path);
            if (d == null) continue;
            File.WriteAllText(Path.Combine(docsDir, d.name + ".md"), RenderTree(d),
                new UTF8Encoding(false));
            count++;
        }
        Debug.Log("[Lore-doc] Деревья обновлены: " + count + " шт. → Docs/Lore/Dialogues/Auto/");
    }

    static string RenderTree(DialogueData d)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Автодерево: " + d.name);
        sb.AppendLine();
        sb.AppendLine("> НЕ ПРАВИТЬ РУКАМИ — генерируется Tools → Lore → 3. Doc Dialogues.");
        sb.AppendLine("> Замысел — в соседних ручных доках (`Mayor.md` и др.).");
        sb.AppendLine();
        sb.AppendLine("- NPC: " + d.npcName + ", старт: " + d.startNodeId);

        var tags = new HashSet<string>();
        if (d.nodes != null)
            foreach (var n in d.nodes)
            {
                if (n == null || n.options == null) continue;
                foreach (var o in n.options)
                    if (o != null && !string.IsNullOrEmpty(o.conditionTag)) tags.Add(o.conditionTag);
            }
        sb.AppendLine("- Условия: " + (tags.Count > 0 ? string.Join(", ", tags) : "нет"));
        sb.AppendLine();

        if (d.nodes != null)
        {
            var ordered = new List<DialogueNode>(d.nodes);
            ordered.RemoveAll(n => n == null);
            ordered.Sort((a, b) => a.id.CompareTo(b.id));
            foreach (var n in ordered)
            {
                sb.AppendLine("## [" + n.id + "] " + Short(n.text));
                if (n.options == null || n.options.Length == 0)
                {
                    sb.AppendLine("- (конец: кнопка «Закрыть»)");
                }
                else
                {
                    foreach (var o in n.options)
                    {
                        if (o == null) continue;
                        var line = new StringBuilder();
                        line.Append("- «" + Short(o.text, 60) + "» → ");
                        line.Append(o.nextNodeId < 0 ? "выход" : o.nextNodeId.ToString());
                        if (!string.IsNullOrEmpty(o.conditionTag)) line.Append(" [if " + o.conditionTag + "]");
                        if (o.action != DialogueActionType.None)
                        {
                            line.Append(" [act " + o.action);
                            if (!string.IsNullOrEmpty(o.actionParam)) line.Append("(" + o.actionParam + ")");
                            line.Append("]");
                        }
                        sb.AppendLine(line.ToString());
                    }
                }
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    static string Short(string s, int max = 80)
    {
        if (string.IsNullOrEmpty(s)) return "(пусто)";
        s = s.Replace("\r", " ").Replace("\n", " ").Replace("  ", " ").Trim();
        return s.Length > max ? s.Substring(0, max) + "…" : s;
    }
}
