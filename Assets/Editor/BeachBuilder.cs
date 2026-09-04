using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Пляж (рыбалка): создаёт пустую сцену Beach, портал City↔Beach,
/// спавны и 3 точки ловли. Карту (песок/вода/пирс/домик Морека) юзер
/// рисует руками. В конце Ctrl+S!
/// </summary>
public static class BeachBuilder
{
    const string BeachScenePath = "Assets/Scenes/Beach.unity";

    // ── 1) Создать сцену Beach ─────────────────────────────────
    [MenuItem("Tools/Fish/1. Создать сцену Beach")]
    public static void CreateBeachScene()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!System.IO.File.Exists(BeachScenePath))
        {
            Scene s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(s, BeachScenePath);
            Debug.Log("[Fish] Сцена Beach создана. Нарисуй карту и жми Ctrl+S.");
        }
        else Debug.Log("[Fish] Beach уже существует.");

        if (!scenes.Any(x => x.path == BeachScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(BeachScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Fish] Beach добавлена в Build Settings.");
        }
    }

    // ── 2) Портал в City ───────────────────────────────────────
    [MenuItem("Tools/Fish/2. City: портал на пляж (открой City)")]
    public static void CreateCityPortal()
    {
        if (SceneManager.GetActiveScene().name != "City")
        {
            EditorUtility.DisplayDialog("Fish", "Открой сцену 'City' и запусти пункт ещё раз.", "Понятно");
            return;
        }

        GameObject portal = new GameObject("PortalToBeach");
        portal.transform.position = new Vector3(8f, -2f, 0f); // подвинуть к краю/дороге руками
        var col = portal.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(1.5f, 1.5f);
        var st = portal.AddComponent<SceneTransition>();
        st.targetScene = "Beach";
        st.targetSpawnId = "FromCity";
        st.triggerOnTouch = true;

        GameObject back = new GameObject("Spawn_FromBeach");
        back.transform.position = new Vector3(8f, -3f, 0f);
        back.AddComponent<SceneSpawnPoint>().spawnId = "FromBeach";

        Undo.RegisterCreatedObjectUndo(portal, "Create PortalToBeach");
        Undo.RegisterCreatedObjectUndo(back, "Create Spawn_FromBeach");
        Selection.activeGameObject = portal;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Fish] PortalToBeach + Spawn_FromBeach созданы. Подвинь и сохрани (Ctrl+S).");
    }

    // ── 3) Внутренности Beach: спавн, выход, точки ловли ───────
    [MenuItem("Tools/Fish/3. Beach: спавны и точки ловли (открой Beach)")]
    public static void CreateBeachInternals()
    {
        if (SceneManager.GetActiveScene().name != "Beach")
        {
            EditorUtility.DisplayDialog("Fish", "Открой сцену 'Beach' и запусти пункт ещё раз.", "Понятно");
            return;
        }

        GameObject root = new GameObject("FishSpots");

        GameObject entry = new GameObject("Spawn_FromCity");
        entry.transform.SetParent(root.transform);
        entry.AddComponent<SceneSpawnPoint>().spawnId = "FromCity";

        GameObject exit = new GameObject("Exit_ToCity");
        exit.transform.SetParent(root.transform);
        exit.transform.position = new Vector3(2f, 0f, 0f);
        var exitCol = exit.AddComponent<BoxCollider2D>();
        exitCol.isTrigger = true;
        exitCol.size = new Vector2(1f, 1f);
        var exitSt = exit.AddComponent<SceneTransition>();
        exitSt.targetScene = "City";
        exitSt.targetSpawnId = "FromBeach";
        exitSt.triggerOnTouch = true; // выход с пляжа — просто заходом
        int layer = LayerMask.NameToLayer("Interactable");
        exit.layer = layer >= 0 ? layer : 8;

        // 3 точки ловли — обвести воду полигоном руками (Edit Collider),
        // таблицу рыбы заполнить в инспекторе FishingSpot
        for (int i = 1; i <= 3; i++)
        {
            GameObject spot = new GameObject("FishingSpot_" + i);
            spot.transform.SetParent(root.transform);
            spot.transform.position = new Vector3(i * 4f, -4f, 0f);
            var sc = spot.AddComponent<PolygonCollider2D>();
            sc.isTrigger = true;
            sc.SetPath(0, new Vector2[] {
                new Vector2(-3f, -2f), new Vector2(3f, -2f),
                new Vector2(3f, 2f), new Vector2(-3f, 2f) });
            var fs = spot.AddComponent<FishingSpot>();
            fs.spotName = "Точка " + i;
            Undo.RegisterCreatedObjectUndo(spot, "Create FishingSpot_" + i);
        }

        Undo.RegisterCreatedObjectUndo(root, "Create FishSpots");
        Undo.RegisterCreatedObjectUndo(entry, "Create Spawn_FromCity");
        Undo.RegisterCreatedObjectUndo(exit, "Create Exit_ToCity");
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Fish] Спавны и 3 точки ловли созданы. Расставь по карте и сохрани (Ctrl+S). Таблицы рыбы — в инспекторе точек.");
    }

    // ── 3b) Починить существующий выход (был «по удару») ───────
    [MenuItem("Tools/Fish/3b. Fix: выход с пляжа по касанию (открой Beach)")]
    public static void FixBeachExit()
    {
        if (SceneManager.GetActiveScene().name != "Beach")
        {
            EditorUtility.DisplayDialog("Fish", "Открой сцену 'Beach' и запусти пункт ещё раз.", "Понятно");
            return;
        }
        GameObject exit = FindObjectInScene("Exit_ToCity");
        if (exit == null) { Debug.LogError("[Fish] Exit_ToCity не найден!"); return; }
        var st = exit.GetComponent<SceneTransition>();
        if (st == null) { Debug.LogError("[Fish] На выходе нет SceneTransition!"); return; }
        Undo.RecordObject(st, "Fix beach exit");
        st.triggerOnTouch = true;
        EditorUtility.SetDirty(st);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Fish] Выход теперь по касанию. Ctrl+S!");
    }

    // ── 4) Рыба (3 вида): предмет + FishData ───────────────────
    [MenuItem("Tools/Fish/4. Create Fish (3 вида)")]
    public static void CreateFish()
    {
        EnsureFolder("Assets/Resources/Items/Fish");
        EnsureFolder("Assets/Resources/Fish");

        MakeFish("Fish_Sardine", "Сардина", "Обычная морская рыбка.",
            "Assets/Art/Icons/Fish/Sea/Sardine.png", "Sardine_0",
            0, 15, 10, 5);
        MakeFish("Fish_Carp", "Карп", "Крепкий речной боец.",
            "Assets/Art/Icons/Fish/River/Carp.png", "Carp_0",
            1, 40, 30, 10);
        MakeFish("Fish_Gold", "Золотая рыбка", "Легенда пресных вод. Говорят, исполняет желания. Не исполняет.",
            "Assets/Art/Icons/Fish/River/Golden Fish.png", "Golden Fish_0",
            2, 120, 100, 25);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Fish] 3 вида готовы. Таблицы точек ловли заполни в инспекторе FishingSpot.");
    }

    static void MakeFish(string asset, string ruName, string desc, string png, string sprite,
        int difficulty, int price, int firstBonus, int heal)
    {
        Sprite icon = AssetDatabase.LoadAllAssetsAtPath(png)
            .OfType<Sprite>().FirstOrDefault(s => s.name == sprite);
        if (icon == null) { Debug.LogError("[Fish] Нет спрайта " + sprite); return; }

        string itemPath = "Assets/Resources/Items/Fish/" + asset + ".asset";
        ItemData item = AssetDatabase.LoadAssetAtPath<ItemData>(itemPath);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(item, itemPath);
        }
        item.itemName = ruName;
        item.description = desc;
        item.icon = icon;
        item.worldSprite = icon;
        item.itemType = ItemType.Consumable;
        item.rarity = ItemRarity.Common;
        item.isStackable = true;
        item.maxStack = 99;
        item.healAmount = heal;
        EditorUtility.SetDirty(item);

        string dataPath = "Assets/Resources/Fish/" + asset + ".asset";
        FishData data = AssetDatabase.LoadAssetAtPath<FishData>(dataPath);
        if (data == null)
        {
            data = ScriptableObject.CreateInstance<FishData>();
            AssetDatabase.CreateAsset(data, dataPath);
        }
        data.fishName = ruName;
        data.icon = icon;
        data.description = desc;
        data.fishItem = item;
        data.difficulty = difficulty;
        data.price = price;
        data.firstCatchBonus = firstBonus;
        EditorUtility.SetDirty(data);
    }

    // ── 5) Нарезка иконок рыбы 16×16 ────────────────────────────
    // Все PNG под Icons/Fish (рекурсивно): сетка 16px, pivot center, PPU 16.
    // Ссылок на эти спрайты нигде нет (проверено) — internalID перегенерируются.
    [MenuItem("Tools/Fish/5. Slice Fish Icons (сетка 16x16)")]
    public static void SliceFishIcons()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Art/Icons/Fish" });
        int files = 0, sprites = 0;
        foreach (string g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (!path.EndsWith(".png")) continue;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            // Размер из байтов файла (не зависит от readable-флага)
            Texture2D tmp = new Texture2D(2, 2);
            tmp.LoadImage(System.IO.File.ReadAllBytes(path));
            int cols = Mathf.Max(1, tmp.width / 16);
            int rows = Mathf.Max(1, tmp.height / 16);
            Object.DestroyImmediate(tmp);

            string baseName = System.IO.Path.GetFileNameWithoutExtension(path);
            var sheet = new List<SpriteMetaData>();
            int n = 0;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    // y сверху вниз — как показывает Sprite Editor
                    sheet.Add(new SpriteMetaData
                    {
                        name = baseName + "_" + n++,
                        rect = new Rect(c * 16, r * 16, 16, 16),
                        alignment = (int)SpriteAlignment.Center,
                        pivot = new Vector2(0.5f, 0.5f),
                        border = Vector4.zero
                    });
                }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 16;
            importer.filterMode = FilterMode.Point;
            var texSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(texSettings);
            texSettings.spriteMeshType = SpriteMeshType.Tight;
            texSettings.spriteExtrude = 1;
            importer.SetTextureSettings(texSettings);
            importer.spritesheet = sheet.ToArray();
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            files++;
            sprites += sheet.Count;
        }
        AssetDatabase.Refresh();
        Debug.Log("[Fish] Нарезано файлов: " + files + ", спрайтов: " + sprites + " (сетка 16x16).");
    }

    // ── 6) Морек (рыбак): объект + зона ─────────────────────────
    [MenuItem("Tools/Fish/6. Beach: создать Морека (открой Beach)")]
    public static void CreateMorek()
    {
        if (SceneManager.GetActiveScene().name != "Beach")
        {
            EditorUtility.DisplayDialog("Fish", "Открой сцену 'Beach' и запусти пункт ещё раз.", "Понятно");
            return;
        }

        DialogueData dialogue = EnsureMorekDialogue();

        GameObject morek = FindObjectInScene("Morek");
        if (morek == null)
        {
            morek = new GameObject("Morek");
            morek.transform.position = new Vector3(0f, 2f, 0f);
            Undo.RegisterCreatedObjectUndo(morek, "Create Morek");
        }

        // Старый прямой интеракт заменяем нормальным NPC (диалог как у всех)
        var old = morek.GetComponent<MorekInteraction>();
        if (old != null) Undo.DestroyObjectImmediate(old);

        var inter = morek.GetComponent<NPCInteractable>();
        if (inter == null) inter = morek.AddComponent<NPCInteractable>();
        inter.dialogue = dialogue;
        inter.detectRadius = 3f;
        inter.talkRadius = 2f;

        if (morek.GetComponent<MorekNPC>() == null)
            morek.AddComponent<MorekNPC>();

        // Триггер-зона на самом объекте (слой Interactable — иначе удар не найдёт!)
        var col = morek.GetComponent<Collider2D>();
        if (col == null)
        {
            var box = morek.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(1.2f, 1.2f);
        }
        else col.isTrigger = true;
        int layer = LayerMask.NameToLayer("Interactable");
        morek.layer = layer >= 0 ? layer : 8;

        if (morek.GetComponent<SpriteRenderer>() == null)
            Debug.LogWarning("[Fish] У Морека нет спрайта — назначь SpriteRenderer вручную.");

        Selection.activeGameObject = morek;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Fish] Морек готов: диалог + панели. Позицию и спрайт поправь руками, Ctrl+S.");
    }

    // Диалог Морека (кодогенерация — руками YAML не править, там хвостовые пробелы!)
    static DialogueData EnsureMorekDialogue()
    {
        EnsureFolder("Assets/Resources/Dialogue");
        const string path = "Assets/Resources/Dialogue/Morek.asset";
        DialogueData d = AssetDatabase.LoadAssetAtPath<DialogueData>(path);
        if (d == null)
        {
            d = ScriptableObject.CreateInstance<DialogueData>();
            AssetDatabase.CreateAsset(d, path);
        }
        d.npcName = "Морек";
        d.startNodeId = 0;
        d.nodes = new DialogueNode[]
        {
            new DialogueNode
            {
                id = 0,
                text = "Йо-хо! Заходи, рыбак. Море сегодня щедрое — проверь сам.",
                options = new DialogueOption[]
                {
                    new DialogueOption { text = "Взять удочку", nextNodeId = -1, action = DialogueActionType.Custom, actionParam = "GiveRod", conditionTag = "morek_norod" },
                    new DialogueOption { text = "Продать рыбу (+50%)", nextNodeId = -1, action = DialogueActionType.Custom, actionParam = "SellFish" },
                    new DialogueOption { text = "Коллекция", nextNodeId = -1, action = DialogueActionType.Custom, actionParam = "Collection" },
                    new DialogueOption { text = "Пока!", nextNodeId = -1, action = DialogueActionType.None },
                }
            }
        };
        EditorUtility.SetDirty(d);
        AssetDatabase.SaveAssets();
        return d;
    }

    // Поиск по сцене ВКЛЮЧАЯ выключенные объекты (GameObject.Find их не видит)
    static GameObject FindObjectInScene(string name)
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            GameObject found = FindRecursive(root.transform, name);
            if (found != null) return found;
        }
        return null;
    }

    static GameObject FindRecursive(Transform t, string name)
    {
        if (t.name == name) return t.gameObject;
        foreach (Transform child in t)
        {
            GameObject found = FindRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static void EnsureFolder(string path)    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
