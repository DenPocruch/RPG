using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Сборка базы конструктора из PNG (действие/категория/вариант) + создание
/// тестового бота и сцены. Диапазоны направлений — из тегов .ase (дамп 06.09.2026:
/// блоки Down→Up→Right→Left; особые случаи помечены ниже).
/// Повторный прогон пункта 2 безопасен, fps действий не затирает.
/// </summary>
public static class CharacterDatabaseBuilder
{
    const string PNG_ROOT = "Assets/Art/Character/Character/PNG";
    const string DB_DIR = "Assets/Resources/Character";
    const string DB_PATH = "Assets/Resources/Character/CharacterDatabase.asset";
    const string BOT_PREFAB = "Assets/Prefab/ConstructorBot.prefab";
    const string TEST_SCENE = "Assets/Scenes/ConstructorTest.unity";

    // Папка действия → кадров (Down, Up, Right, Left). 0 = направления нет.
    static readonly Dictionary<string, int[]> DIRS = new Dictionary<string, int[]>
    {
        { "1. Idle", new[] { 4, 4, 4, 4 } },
        { "2. Walk", new[] { 6, 6, 6, 6 } },
        { "3. Run", new[] { 8, 8, 8, 8 } },
        { "4. Pickaxe, Hoe and Catching insects", new[] { 6, 6, 6, 6 } },
        { "5. Axe and Sickle", new[] { 6, 6, 6, 6 } },
        { "6. Shovel", new[] { 5, 5, 5, 5 } },
        { "7. Watering", new[] { 8, 8, 8, 8 } },
        { "8. SwordAttack", new[] { 10, 10, 10, 10 } },
        { "9. Archer", new[] { 7, 7, 7, 7 } },
        { "10. Damage", new[] { 4, 4, 4, 4 } },
        { "11. Death", new[] { 4, 4, 4, 4 } },
        { "12. Fishing - Cast", new[] { 15, 15, 15, 15 } },
        { "12.1. Fishing - Wait", new[] { 4, 4, 4, 4 } },
        { "12.2. Fishing - Bite", new[] { 8, 8, 8, 8 } },
        { "12.3. Fishing - Reel", new[] { 4, 4, 4, 4 } },
        { "12.4. Fishing - Catch", new[] { 4, 4, 4, 4 } },
        { "13. Carrying - Idle", new[] { 4, 4, 4, 4 } },
        { "13.1 Carrying - Walk", new[] { 6, 6, 6, 6 } },
        { "13.2 Carrying - Run", new[] { 8, 8, 8, 8 } }, // PNG — чистая версия 32 кадра (в .ase сборка 88)
        { "13.3 Carrying - Pick Up", new[] { 4, 4, 4, 4 } },
        { "13.4 Carrying - Throwing items", new[] { 5, 5, 5, 5 } },
        { "14. Horse - Idle", new[] { 2, 2, 2, 2 } },
        { "14.1 Horse - Walk", new[] { 4, 4, 4, 4 } },
        { "14.2 Horse - Run", new[] { 6, 6, 6, 6 } },
        { "14.3 Horse - Lower", new[] { 4, 4, 4, 4 } },
        { "14.4 Horse - Eating", new[] { 4, 4, 4, 4 } },
        { "15. Bicycle - Idle", new[] { 2, 2, 2, 2 } },
        { "15.1 Bicycle - Run", new[] { 4, 4, 4, 4 } },
        { "16. Bear - Idle", new[] { 2, 2, 2, 2 } },
        { "16.1 Bear - Walk", new[] { 4, 4, 4, 4 } },
        { "16.2 Bear - Run", new[] { 6, 6, 6, 6 } },
        { "16.3 Bear - Attack", new[] { 4, 4, 4, 4 } },
        { "16.4 Bear - Hit", new[] { 3, 3, 3, 3 } },
        { "16.5 Bear - Dead", new[] { 4, 4, 4, 4 } },
        { "17 Umbrela - Idle", new[] { 4, 4, 4, 4 } },
        { "17.1 Umbrela - Walk", new[] { 6, 6, 6, 6 } },
        { "17.2 Umbrela - Run", new[] { 8, 8, 8, 8 } },
        { "18. Setting", new[] { 1, 1, 1, 1 } },
        { "19. Sleep", new[] { 2, 0, 2, 2 } }, // без Up
        { "20. Petting", new[] { 0, 0, 6, 6 } }, // только Right/Left
        { "21. Climbing", new[] { 0, 5, 0, 0 } }, // одно направление
        { "22. Flute", new[] { 6, 0, 6, 6 } }, // без Up
        { "23. Mage", new[] { 6, 6, 6, 6 } },
        { "24. Swim - Idle", new[] { 4, 4, 4, 4 } },
        { "24.1 Swim - Outwater", new[] { 3, 3, 3, 3 } },
        { "24.2 Swim - Submerged", new[] { 4, 4, 4, 0 } }, // теги без имён 3×4, проверить глазами
        { "24.3 Swim - Swim", new[] { 4, 4, 4, 4 } }, // теги с опечаткой Swin*
        { "25. Broomstick", new[] { 4, 4, 4, 4 } },
    };

    // Порядок отрисовки (дальше → ближе). Неизвестные категории — вперёд.
    static readonly string[] RENDER_ORDER = new string[]
    {
        "Horse", "Bicycle", "Bear", "Bed",
        "Skins", "Elf", "Clothers", "Eyes", "Beard", "Hair", "Hair's",
        "Acc", "Butterfly", "Weapons", "Box", "FX"
    };

    static int RenderOrderOf(string category)
    {
        for (int i = 0; i < RENDER_ORDER.Length; i++)
            if (RENDER_ORDER[i] == category)
                return i;
        return 1000;
    }

    [MenuItem("Tools/Character/2. Build Character Database")]
    public static void BuildDatabase()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[CharacterDB] Выключи Play-режим.");
            return;
        }

        var db = AssetDatabase.LoadAssetAtPath<CharacterDatabase>(DB_PATH);
        var oldFps = new Dictionary<string, float>();
        if (db != null)
            foreach (var a in db.actions)
                oldFps[a.actionName] = a.fps;
        else
        {
            if (!AssetDatabase.IsValidFolder(DB_DIR))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "Character");
            }
            db = ScriptableObject.CreateInstance<CharacterDatabase>();
            AssetDatabase.CreateAsset(db, DB_PATH);
        }

        int warn = 0;
        db.actions.Clear();
        string[] actionDirs = Directory.GetDirectories(PNG_ROOT);
        System.Array.Sort(actionDirs);
        foreach (string actionDir in actionDirs)
        {
            string actionName = Path.GetFileName(actionDir);
            if (!DIRS.TryGetValue(actionName, out int[] counts))
            {
                Debug.LogWarning("[CharacterDB] Нет диапазонов для " + actionName + " — пропущено");
                warn++;
                continue;
            }
            var action = new CharacterAction
            {
                actionName = actionName,
                downFrames = counts[0],
                upFrames = counts[1],
                rightFrames = counts[2],
                leftFrames = counts[3],
                fps = oldFps.ContainsKey(actionName) ? oldFps[actionName] : 8f
            };
            int total = action.TotalFrames;
            string actionDirFwd = actionDir.Replace('\\', '/');

            var catByName = new Dictionary<string, CharacterCategory>(System.StringComparer.OrdinalIgnoreCase);
            CharacterCategory GetCat(string name)
            {
                if (!catByName.TryGetValue(name, out CharacterCategory cc))
                {
                    cc = new CharacterCategory { categoryName = name, renderOrder = RenderOrderOf(name) };
                    catByName[name] = cc;
                }
                return cc;
            }
            void AddFile(string catName, string baseDirFwd, string png)
            {
                string assetPath = png.Replace('\\', '/');
                string variantName = assetPath.Substring(baseDirFwd.Length + 1);
                variantName = variantName.Substring(0, variantName.Length - 4); // без .png
                variantName = variantName.Trim();
                // Мусор слоёв-тайлмапов из Aseprite — не тянем
                if (variantName.StartsWith("Bloco de mapa")) return;
                // Очепятки автора: тот же предмет ("Leprechaun " с хвостовым пробелом,
                // "Pirate eyepatch" одним словом) — сводим к каноническому имени
                if (variantName == "Pirate eyepatch") variantName = "pirate eye patch";
                Object[] reps = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath);
                var sprites = new List<Sprite>();
                foreach (var o in reps)
                    if (o is Sprite s) sprites.Add(s);
                sprites.Sort((a, b) => SpriteIndex(a.name).CompareTo(SpriteIndex(b.name)));
                bool dbl = false;
                if (sprites.Count == total * 2)
                {
                    dbl = true;
                    Debug.Log($"[CharacterDB] {actionName}/{variantName}: двойные кадры ({sprites.Count}) — беру каждый 2-й");
                }
                else if (sprites.Count != total)
                {
                    Debug.LogWarning($"[CharacterDB] {actionName}/{variantName}: кадров {sprites.Count}, надо {total} — пропущен");
                    warn++;
                    return;
                }
                GetCat(catName).variants.Add(new CharacterVariant { variantName = variantName, frames = sprites.ToArray(), doubleFrames = dbl });
            }
            string NormCat(string raw)
            {
                if (raw == "Hair") return "Hair's";       // в части действий без апострофа
                if (raw == "Acessories") return "Acc";    // опечатка автора, набор шляп как в Acc
                return raw;
            }
            bool IsFx(string variantName) => variantName.IndexOf("fx", System.StringComparison.OrdinalIgnoreCase) >= 0;

            string[] catDirs = Directory.GetDirectories(actionDir);
            System.Array.Sort(catDirs);
            foreach (string catDir in catDirs)
            {
                string catDirFwd = catDir.Replace('\\', '/');
                string rawCat = NormCat(Path.GetFileName(catDir));
                string[] pngs = Directory.GetFiles(catDir, "*.png", SearchOption.AllDirectories);
                System.Array.Sort(pngs);
                foreach (string png in pngs)
                {
                    // FX из Weapons (Fish FX, Arrow Fx) — отдельный слой, иначе рыба/стрела
                    // взаимоисключают удочку/лук
                    string rel = png.Replace('\\', '/').Substring(catDirFwd.Length + 1);
                    string catName = (rawCat == "Weapons" && IsFx(rel)) ? "FX" : rawCat;
                    string baseFwd = catDirFwd;
                    // Acc делим по подпапкам: Beard/Elf/Butterfly — отдельные комбинируемые слои,
                    // иначе борода XOR шапка XOR уши (а в арте они независимы)
                    if (rawCat == "Acc")
                    {
                        int slash = rel.IndexOf('/');
                        if (slash > 0)
                        {
                            string sub = rel.Substring(0, slash);
                            catName = sub;
                            baseFwd = catDirFwd + "/" + sub;
                        }
                    }
                    AddFile(catName, baseFwd, png);
                }
            }
            // PNG прямо в папке действия: FX → слой FX, пропс (Flute, Healer Staff) →
            // свой слой (показывается сам), остальное → Acc
            string[] loose = Directory.GetFiles(actionDir, "*.png", SearchOption.TopDirectoryOnly);
            System.Array.Sort(loose);
            foreach (string png in loose)
            {
                string rel = Path.GetFileNameWithoutExtension(png);
                if (IsFx(rel)) AddFile("FX", actionDirFwd, png);
                else AddFile(rel, actionDirFwd, png);
            }
            var orderedCats = new List<CharacterCategory>(catByName.Values);
            orderedCats.Sort((a, b) =>
            {
                int r = a.renderOrder.CompareTo(b.renderOrder);
                return r != 0 ? r : string.Compare(a.categoryName, b.categoryName, System.StringComparison.Ordinal);
            });
            foreach (var c in orderedCats)
                if (c.variants.Count > 0)
                    action.categories.Add(c);
            db.actions.Add(action);
        }

        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
        Debug.Log($"[CharacterDB] Готово: действий {db.actions.Count}, предупреждений: {warn}");
        EditorUtility.DisplayDialog("Character Database", $"Действий: {db.actions.Count}\nПредупреждений: {warn} (см. Console)", "OK");
    }

    static int SpriteIndex(string spriteName)
    {
        int p = spriteName.LastIndexOf('_');
        if (p >= 0 && int.TryParse(spriteName.Substring(p + 1), out int idx))
            return idx;
        return 0;
    }

    [MenuItem("Tools/Character/3. Create Test Bot + Scene")]
    public static void CreateTestBot()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[CharacterDB] Выключи Play-режим.");
            return;
        }
        if (!EditorUtility.DisplayDialog("Test Bot",
            "Создаст префаб ConstructorBot и сцену ConstructorTest (НЕ в Build Settings).\nТекущая открытая сцена будет переключена — несохранённые изменения потеряешь. Продолжить?",
            "Да", "Отмена")) return;

        var db = AssetDatabase.LoadAssetAtPath<CharacterDatabase>(DB_PATH);
        if (db == null)
        {
            Debug.LogError("[CharacterDB] Сначала прогони пункт 2 (база не найдена).");
            return;
        }

        var go = new GameObject("ConstructorBot");
        var vis = go.AddComponent<CharacterVisual>();
        vis.baseSortingOrder = 10;
        vis.choices = new List<CharacterVisual.CategoryChoice>
        {
            new CharacterVisual.CategoryChoice { category = "Skins", variant = "1" },
            new CharacterVisual.CategoryChoice { category = "Eyes", variant = "Male/Green" },
            new CharacterVisual.CategoryChoice { category = "Clothers", variant = "Farm/Blue" },
            new CharacterVisual.CategoryChoice { category = "Hair's", variant = "Standard/Ginger" },
            new CharacterVisual.CategoryChoice { category = "Acc", variant = "" },
        };
        go.AddComponent<ConstructorBotDriver>();
        PrefabUtility.SaveAsPrefabAsset(go, BOT_PREFAB);
        GameObject.DestroyImmediate(go);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 4f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.09f, 0.1f, 0.12f);
        cam.transform.position = new Vector3(0, 0, -10);
        camGo.AddComponent<AudioListener>();
        camGo.tag = "MainCamera";

        var lightGo = new GameObject("Global Light 2D");
        var light = lightGo.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        light.intensity = 1f;

        var botPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BOT_PREFAB);
        PrefabUtility.InstantiatePrefab(botPrefab);

        EditorSceneManager.SaveScene(scene, TEST_SCENE);
        Debug.Log("[CharacterDB] Готово: префаб + сцена ConstructorTest. Открой её и жми Play. Управление — в Console при старте.");
    }
}
