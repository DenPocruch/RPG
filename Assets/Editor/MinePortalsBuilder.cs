using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Linq;

/// <summary>
/// Создаёт порталы шахты по образцу существующих:
///  - портал у кузнеца (City → Mine) как PortalToBeginnerForest (SceneTransition + триггер);
///  - двери между зонами внутри Mine как HouseDoor (DoorTeleport + триггер, вход по касанию).
/// Запуск из открытого редактора: Tools → Mine → пункты 1/2/3.
/// Созданные объекты пользователь растаскивает по местам вручную и сохраняет сцену (Ctrl+S).
/// </summary>
public static class MinePortalsBuilder
{
    const string PropsTex = "Assets/Art/Objects/Exterior/Mine and Dungeon/Props Mine.png";
    const string MineScenePath = "Assets/Scenes/Mine.unity";

    // ── 1) Портал в City ─────────────────────────────────────────
    [MenuItem("Tools/Mine/1. City: создать PortalToMine (открой City)")]
    public static void CreateCityPortal()
    {
        if (!RequireScene("City")) return;

        // Вход в шахту у кузнеца (Blacksmith ~ -3.35, -2.02) — потом подвинуть руками
        GameObject portal = new GameObject("PortalToMine");
        portal.transform.position = new Vector3(-2f, -2f, 0f);
        SetSprite(portal, "Props Mine_106"); // тёмный вход, если спрайт найдётся
        var col = portal.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(1.63f, 1f);
        var st = portal.AddComponent<SceneTransition>();
        st.targetScene = "Mine";
        st.targetSpawnId = "FromCity";
        st.triggerOnTouch = true;

        // Точка возврата из шахты (Exit_ToCity в Mine ведёт сюда)
        GameObject back = new GameObject("Spawn_FromMine");
        back.transform.position = new Vector3(-2f, -3f, 0f);
        back.AddComponent<SceneSpawnPoint>().spawnId = "FromMine";

        Undo.RegisterCreatedObjectUndo(portal, "Create PortalToMine");
        Undo.RegisterCreatedObjectUndo(back, "Create Spawn_FromMine");
        Selection.activeGameObject = portal;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Mine] PortalToMine + Spawn_FromMine созданы. Подвинь к кузнецу и сохрани сцену (Ctrl+S).");
    }

    // ── 2) Спавны и двери внутри Mine ────────────────────────────
    [MenuItem("Tools/Mine/2. Mine: создать спавны и двери зон (открой Mine)")]
    public static void CreateMineInternals()
    {
        if (!RequireScene("Mine")) return;

        GameObject root = new GameObject("MinePortals");

        // Прихожая: появление из города
        GameObject entry = new GameObject("Spawn_FromCity");
        entry.transform.SetParent(root.transform);
        entry.transform.position = new Vector3(0f, 0f, 0f);
        entry.AddComponent<SceneSpawnPoint>().spawnId = "FromCity";

        // Выход назад в город
        GameObject exit = new GameObject("Exit_ToCity");
        exit.transform.SetParent(root.transform);
        exit.transform.position = new Vector3(2f, 0f, 0f);
        SetSprite(exit, "Props Mine_180"); // лестница
        var exitCol = exit.AddComponent<BoxCollider2D>();
        exitCol.isTrigger = true;
        exitCol.size = new Vector2(1f, 1f);
        var exitSt = exit.AddComponent<SceneTransition>();
        exitSt.targetScene = "City";
        exitSt.targetSpawnId = "FromMine";
        exitSt.triggerOnTouch = false; // вход по кнопке атаки
        SetAttackMode(exit);

        // 5 зон: спавн + дверь-лестница (как HouseDoor: DoorTeleport, вход по касанию).
        // Дверь Door_ToZoneN заранее нацелена на Spawn_ZoneN — растащи по зонам:
        // дверь, ведущая на уровень N, клади в зону, ОТКУДА спускаются.
        for (int i = 1; i <= 5; i++)
        {
            GameObject spawn = new GameObject("Spawn_Zone" + i);
            spawn.transform.SetParent(root.transform);
            spawn.transform.position = new Vector3(i * 3f, 2f, 0f);

            GameObject door = new GameObject("Door_ToZone" + i);
            door.transform.SetParent(root.transform);
            door.transform.position = new Vector3(i * 3f, 0f, 0f);
            SetSprite(door, "Props Mine_180"); // лестница
            var dc = door.AddComponent<BoxCollider2D>();
            dc.isTrigger = true;
            dc.size = new Vector2(1f, 1f);
            var dt = door.AddComponent<DoorTeleport>();
            dt.targetSpawn = spawn.transform;
            dt.snapCamera = true;
            dt.triggerOnTouch = false; // вход по кнопке атаки
            SetAttackMode(door);

            Undo.RegisterCreatedObjectUndo(spawn, "Create Spawn_Zone" + i);
            Undo.RegisterCreatedObjectUndo(door, "Create Door_ToZone" + i);
        }

        Undo.RegisterCreatedObjectUndo(root, "Create MinePortals root");
        Undo.RegisterCreatedObjectUndo(entry, "Create Spawn_FromCity");
        Undo.RegisterCreatedObjectUndo(exit, "Create Exit_ToCity");
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Mine] Спавны и двери созданы под MinePortals. Растащи по 5 зонам и сохрани сцену (Ctrl+S).");
    }

    // ── 2б) Обратные двери (наверх) ─────────────────────────────
    // Door_BackToZoneN ведёт на Spawn_ZoneN: клади в зону ГЛУБЖЕ,
    // чтобы можно было подняться обратно (цепочка: зона N+1 → зона N → … → зона 1 → Exit_ToCity).
    [MenuItem("Tools/Mine/2b. Mine: создать обратные двери (открой Mine)")]
    public static void CreateMineBackDoors()
    {
        if (!RequireScene("Mine")) return;

        GameObject root = GameObject.Find("MinePortals");
        if (root == null) root = new GameObject("MinePortals");

        for (int i = 1; i <= 4; i++)
        {
            GameObject spawn = GameObject.Find("Spawn_Zone" + i);
            if (spawn == null)
            {
                Debug.LogError("[Mine] Не найден Spawn_Zone" + i + " — сначала выполни пункт 2.");
                continue;
            }
            if (GameObject.Find("Door_BackToZone" + i) != null)
            {
                Debug.Log("[Mine] Door_BackToZone" + i + " уже есть — пропускаю.");
                continue;
            }

            GameObject door = new GameObject("Door_BackToZone" + i);
            door.transform.SetParent(root.transform);
            door.transform.position = spawn.transform.position + new Vector3(1f, -2f, 0f);
            SetSprite(door, "Props Mine_180"); // лестница
            var dc = door.AddComponent<BoxCollider2D>();
            dc.isTrigger = true;
            dc.size = new Vector2(1f, 1f);
            var dt = door.AddComponent<DoorTeleport>();
            dt.targetSpawn = spawn.transform;
            dt.snapCamera = true;
            dt.triggerOnTouch = false; // вход по кнопке атаки
            SetAttackMode(door);

            Undo.RegisterCreatedObjectUndo(door, "Create Door_BackToZone" + i);
        }

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Mine] Обратные двери созданы. Растащи: BackToZone1 в зону 2, BackToZone2 в зону 3 и т.д. Сохрани сцену (Ctrl+S).");
    }

    // ── 2в) Перевести двери на кнопку атаки ──────────────────────
    // Ставит уже расставленным дверям/выходу слой Interactable и выключает
    // вход по касанию: переход срабатывает ударом (как разговор с NPC).
    [MenuItem("Tools/Mine/2c. Mine: двери по кнопке атаки (открой Mine)")]
    public static void ConvertDoorsToAttack()
    {
        if (!RequireScene("Mine")) return;

        int n = 0;
        foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
            n += ConvertRecursive(go);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Mine] Переведено на кнопку атаки: " + n + ". Сохрани сцену (Ctrl+S).");
    }

    static int ConvertRecursive(GameObject go)
    {
        int n = 0;
        if (go.name.StartsWith("Door_ToZone") || go.name.StartsWith("Door_BackToZone") || go.name == "Exit_ToCity")
        {
            Undo.RecordObject(go, "Door attack mode");
            var dt = go.GetComponent<DoorTeleport>();
            if (dt != null) { Undo.RecordObject(dt, "Door attack mode"); dt.triggerOnTouch = false; }
            var st = go.GetComponent<SceneTransition>();
            if (st != null) { Undo.RecordObject(st, "Door attack mode"); st.triggerOnTouch = false; }
            SetAttackMode(go);
            n = 1;
        }
        foreach (Transform child in go.transform)
            n += ConvertRecursive(child.gameObject);
        return n;
    }

    // ── 3) Mine в Build Settings ─────────────────────────────────
    [MenuItem("Tools/Mine/3. Добавить Mine в Build Settings")]
    public static void AddMineToBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == MineScenePath))
        {
            Debug.Log("[Mine] Mine уже в Build Settings.");
            return;
        }
        scenes.Add(new EditorBuildSettingsScene(MineScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log("[Mine] Mine добавлена в Build Settings.");
    }

    // ── helpers ──────────────────────────────────────────────────
    // Слой Interactable (8): детектор атаки находит только этот слой.
    static void SetAttackMode(GameObject go)
    {
        int layer = LayerMask.NameToLayer("Interactable");
        go.layer = layer >= 0 ? layer : 8;
    }

    static bool RequireScene(string name)
    {
        if (SceneManager.GetActiveScene().name == name) return true;
        EditorUtility.DisplayDialog("Mine",
            "Открой сцену '" + name + "' и запусти пункт ещё раз.\nСейчас открыта: " + SceneManager.GetActiveScene().name,
            "Понятно");
        return false;
    }

    static void SetSprite(GameObject go, string spriteName)
    {
        Sprite s = AssetDatabase.LoadAllAssetsAtPath(PropsTex)
            .OfType<Sprite>()
            .FirstOrDefault(x => x.name == spriteName);
        if (s == null)
        {
            Debug.LogWarning("[Mine] Спрайт " + spriteName + " не найден — назначь вручную.");
            return;
        }
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = s;
    }
}
