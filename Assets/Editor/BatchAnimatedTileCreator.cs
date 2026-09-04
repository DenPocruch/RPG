using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO;

/// <summary>
/// Редакторский инструмент: создаёт Animated Tile по СПИСКУ НОМЕРОВ кадров,
/// которые ты сам указываешь текстом. Нужен когда кадры одной анимации
/// разбросаны по спрайт-листу не подряд (например индексы 1, 12, 23, 34).
///
/// Использование:
/// 1. Перетащи сюда исходную текстуру (спрайт-лист, Sprite Mode = Multiple)
/// 2. В текстовом поле опиши анимации построчно:
///      Water: 1,12,23,34
///      Lava: 2,13,24,35
///    (слева — имя будущего тайла, справа — номера кадров ПО ПОРЯДКУ анимации)
/// 3. Нажми "Создать" — тайлы появятся рядом с текстурой
///
/// Номер кадра = число в конце имени спрайта, которое Unity даёт при нарезке
/// (напр. "Tileset_12" -> номер 12). Наведи на спрайт в Project чтобы увидеть имя.
/// </summary>
public class BatchAnimatedTileCreator : EditorWindow
{
    private Texture2D sourceTexture;
    private string definitions =
        "Water: 1,12,23,34\nLava: 2,13,24,35";
    private float minSpeed = 1f;
    private float maxSpeed = 1f;
    private bool randomizeStart = true;
    private Vector2 scroll;

    // ── Режим B: серии по смещениям ──
    private string seriesPrefix = "BeachWater";
    private int seriesCount = 12;
    private string seriesStarts = "0, 93, 186, 279"; // стартовые номера кадров 1..N через запятую
    private string seriesFixes = ""; // дырки: "99=98, 150=151" — нет спрайта → взять указанный
    private bool seriesCompress = true; // сжать дырки: ряд берёт N СУЩЕСТВУЮЩИХ спрайтов от старта (пустые ячейки пропускаются)

    [MenuItem("Tools/Batch Create Animated Tiles")]
    static void Open()
    {
        GetWindow<BatchAnimatedTileCreator>("Batch Animated Tiles");
    }

    void OnGUI()
    {
        GUILayout.Label("Создание Animated Tile по списку номеров", EditorStyles.boldLabel);
        GUILayout.Space(6);

        sourceTexture = (Texture2D)EditorGUILayout.ObjectField(
            "Спрайт-лист (текстура)", sourceTexture, typeof(Texture2D), false);

        GUILayout.Space(8);

        if (sourceTexture != null && GUILayout.Button("Показать список номеров в этой текстуре"))
        {
            ShowAvailableIndices();
        }

        GUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "Одна строка = одна анимация.\n" +
            "Формат:  Имя: номер1,номер2,номер3,...\n" +
            "Номера — в ТОМ ПОРЯДКЕ в котором идут кадры анимации.\n\n" +
            "Пример:\nWater: 1,12,23,34\nLava: 2,13,24,35",
            MessageType.Info);

        GUILayout.Label("Определения анимаций:");
        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(150));
        definitions = EditorGUILayout.TextArea(definitions, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();

        GUILayout.Space(10);
        minSpeed = EditorGUILayout.FloatField("Min Speed", minSpeed);
        maxSpeed = EditorGUILayout.FloatField("Max Speed", maxSpeed);
        randomizeStart = EditorGUILayout.Toggle("Разный старт (не мигать в такт)", randomizeStart);

        GUILayout.Space(10);
        GUI.enabled = sourceTexture != null;
        if (GUILayout.Button("Создать Animated Tiles (режим А)", GUILayout.Height(32)))
        {
            CreateFromDefinitions();
        }
        GUI.enabled = true;

        GUILayout.Space(16);
        GUILayout.Label("Режим Б: серия тайлов по смещениям", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Для листов где кадры лежат блоками: тайл i берёт спрайты\n" +
            "[старт1+i, старт2+i, ...].\n\n" +
            "Пример: 12 тайлов воды, кадр1 = спрайты 0–11, кадр2 = 93–104,\n" +
            "кадр3 = 186–197, кадр4 = 279–290:\n" +
            "Префикс: BeachWater | Количество: 12 | Старты: 0, 93, 186, 279\n\n" +
            "Получится BeachWater_0 = [0, 93, 186, 279], BeachWater_1 = [1, 94, 187, 280]...",
            MessageType.Info);

        seriesPrefix = EditorGUILayout.TextField("Префикс имени", seriesPrefix);
        seriesCount = EditorGUILayout.IntField("Количество тайлов", seriesCount);
        seriesStarts = EditorGUILayout.TextField("Старты кадров (через запятую)", seriesStarts);
        seriesFixes = EditorGUILayout.TextField("Дырки-подмены (напр. 99=98)", seriesFixes);
        seriesCompress = EditorGUILayout.Toggle("Сжать дырки (пропускать пустые)", seriesCompress);

        GUILayout.Space(6);
        GUI.enabled = sourceTexture != null;
        if (GUILayout.Button("Создать серию (режим Б)", GUILayout.Height(32)))
        {
            CreateFromOffsets();
        }
        GUI.enabled = true;
    }

    // Достаём все под-спрайты текстуры, индексируем по номеру в конце имени
    Dictionary<int, Sprite> GetSpritesByIndex()
    {
        var result = new Dictionary<int, Sprite>();
        string path = AssetDatabase.GetAssetPath(sourceTexture);
        Regex trailingNumber = new Regex(@"(\d+)$");

        foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (sub is Sprite sp)
            {
                Match m = trailingNumber.Match(sp.name);
                if (m.Success)
                {
                    int idx = int.Parse(m.Groups[1].Value);
                    result[idx] = sp;
                }
            }
        }
        return result;
    }

    void ShowAvailableIndices()
    {
        var byIndex = GetSpritesByIndex();
        if (byIndex.Count == 0)
        {
            EditorUtility.DisplayDialog("Нет спрайтов",
                "В этой текстуре не найдено нарезанных спрайтов.\n" +
                "Убедись что Sprite Mode = Multiple и спрайты нарезаны (Sprite Editor → Slice).", "Ок");
            return;
        }

        var sortedNames = byIndex.OrderBy(kv => kv.Key)
            .Select(kv => kv.Key + " (" + kv.Value.name + ")");
        EditorUtility.DisplayDialog("Доступные номера кадров",
            "Всего спрайтов: " + byIndex.Count + "\n\n" +
            string.Join(", ", sortedNames), "Ок");
    }

    void CreateFromDefinitions()
    {
        var byIndex = GetSpritesByIndex();
        if (byIndex.Count == 0)
        {
            EditorUtility.DisplayDialog("Ошибка",
                "В текстуре не найдено нарезанных спрайтов (Sprite Mode должен быть Multiple).", "Ок");
            return;
        }

        string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(sourceTexture));
        int created = 0;
        List<string> errors = new List<string>();

        string[] lines = definitions.Split('\n');
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || !line.Contains(":")) continue;

            string[] parts = line.Split(new[] { ':' }, 2);
            string tileName = parts[0].Trim();
            string[] numberStrs = parts[1].Split(',');

            List<Sprite> frames = new List<Sprite>();
            bool ok = true;

            foreach (string numStr in numberStrs)
            {
                string trimmed = numStr.Trim();
                if (trimmed.Length == 0) continue;

                if (!int.TryParse(trimmed, out int idx))
                {
                    errors.Add(tileName + ": '" + trimmed + "' не число");
                    ok = false;
                    continue;
                }
                if (!byIndex.TryGetValue(idx, out Sprite sp))
                {
                    errors.Add(tileName + ": номер " + idx + " не найден в текстуре");
                    ok = false;
                    continue;
                }
                frames.Add(sp);
            }

            if (!ok || frames.Count == 0) continue;

            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(folder, tileName + "_Animated.asset"));

            AnimatedTile tile = ScriptableObject.CreateInstance<AnimatedTile>();
            tile.m_AnimatedSprites = frames.ToArray();
            tile.m_MinSpeed = minSpeed;
            tile.m_MaxSpeed = maxSpeed;
            tile.m_AnimationStartTime = randomizeStart ? Random.Range(0f, 1f) : 0f;

            AssetDatabase.CreateAsset(tile, assetPath);
            created++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string report = "Создано Animated Tile: " + created + " шт.";
        if (errors.Count > 0)
            report += "\n\nОшибки:\n" + string.Join("\n", errors);

        EditorUtility.DisplayDialog("Готово", report, "Ок");
    }

    // Режим Б: N тайлов, тайл i = [старт1+i, старт2+i, ...]
    void CreateFromOffsets()
    {
        var byIndex = GetSpritesByIndex();
        if (byIndex.Count == 0)
        {
            EditorUtility.DisplayDialog("Ошибка",
                "В текстуре не найдено нарезанных спрайтов (Sprite Mode должен быть Multiple).", "Ок");
            return;
        }

        List<int> starts = new List<int>();
        foreach (string part in seriesStarts.Split(','))
        {
            if (int.TryParse(part.Trim(), out int s)) starts.Add(s);
        }
        // Дырки: "99=98" — спрайта 99 нет, вместо него берём 98
        Dictionary<int, int> fixes = new Dictionary<int, int>();
        foreach (string part in seriesFixes.Split(','))
        {
            string[] kv = part.Split('=');
            if (kv.Length == 2 && int.TryParse(kv[0].Trim(), out int miss) && int.TryParse(kv[1].Trim(), out int sub))
                fixes[miss] = sub;
        }
        if (starts.Count < 2)
        {
            EditorUtility.DisplayDialog("Ошибка", "Нужно минимум 2 старта кадров через запятую.", "Ок");
            return;
        }
        if (seriesCount < 1) seriesCount = 1;
        string prefix = string.IsNullOrWhiteSpace(seriesPrefix) ? "Anim" : seriesPrefix.Trim();

        // Ряды кадров: строгая арифметика [старт+i] или сжатие
        // (первые N СУЩЕСТВУЮЩИХ спрайтов от старта — дырки пропускаются)
        List<List<Sprite>> rows = new List<List<Sprite>>();
        List<string> errors = new List<string>();
        foreach (int st in starts)
        {
            var row = new List<Sprite>();
            if (seriesCompress)
            {
                int idx = st;
                int guard = 0;
                while (row.Count < seriesCount && guard < 10000)
                {
                    if (byIndex.TryGetValue(idx, out Sprite sp)) row.Add(sp);
                    idx++;
                    guard++;
                }
                if (row.Count < seriesCount)
                    errors.Add("Ряд от " + st + ": хватило только " + row.Count + " из " + seriesCount);
            }
            else
            {
                for (int i = 0; i < seriesCount; i++)
                {
                    if (byIndex.TryGetValue(st + i, out Sprite sp)) row.Add(sp);
                    else row.Add(null);
                }
            }
            rows.Add(row);
        }
        if (errors.Count > 0)
        {
            EditorUtility.DisplayDialog("Ошибка", string.Join("\n", errors), "Ок");
            return;
        }

        string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(sourceTexture));
        int created = 0;

        for (int i = 0; i < seriesCount; i++)
        {
            List<Sprite> frames = new List<Sprite>();
            bool ok = true;
            for (int f = 0; f < rows.Count; f++)
            {
                Sprite sp = rows[f][i];
                if (sp == null && seriesCompress) { ok = false; break; } // не должно случиться
                if (sp == null)
                {
                    // Строгий режим: пробуем подмену, иначе пропуск тайла
                    int idx = starts[f] + i;
                    if (fixes.TryGetValue(idx, out int sub))
                        byIndex.TryGetValue(sub, out sp);
                }
                if (sp == null)
                {
                    errors.Add(prefix + "_" + i + ": спрайт " + (starts[f] + i) + " не найден");
                    ok = false;
                    break;
                }
                frames.Add(sp);
            }
            if (!ok) continue;

            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(folder, prefix + "_" + i + "_Animated.asset"));

            AnimatedTile tile = ScriptableObject.CreateInstance<AnimatedTile>();
            tile.m_AnimatedSprites = frames.ToArray();
            tile.m_MinSpeed = minSpeed;
            tile.m_MaxSpeed = maxSpeed;
            tile.m_AnimationStartTime = randomizeStart ? Random.Range(0f, 1f) : 0f;

            AssetDatabase.CreateAsset(tile, assetPath);
            created++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string report = "Создано Animated Tile: " + created + " шт.";
        if (errors.Count > 0)
            report += "\n\nОшибки:\n" + string.Join("\n", errors.Take(20));
        EditorUtility.DisplayDialog("Готово", report, "Ок");
    }
}