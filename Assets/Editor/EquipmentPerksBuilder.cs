using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Узлы разблокировки экипировки (ветка Equipment):
/// Tools → Equipment → 3. Build Equipment Perk Nodes.
/// 5 тиров (Copper/Iron/Gold/Platinum/Obsidian) × 9 видов
/// (Sword/Bow/Staff/Helmet/Chestplate/Leggings/Boots/Pickaxe/Axe) = 45 узлов.
/// Теги equip_&lt;тир&gt;_&lt;вид&gt; — единый формат из EquipmentLocks.TagFor.
/// Цепочка: железо требует медь того же вида и т.д. (requiredNodes).
/// Цены: 3/5/7/10/15 очков, уровни 1/5/10/15/20. Повторный запуск ОБНОВЛЯЕТ
/// in place (имена ассетов стабильны — сейвы рангов не ломаются).
/// UI-кнопки в дереве юзер расставляет ВРУЧНУЮ (контейнер equipmentContainer).
/// </summary>
public static class EquipmentPerksBuilder
{
    const string ART = "Assets/Art/Icons/RPG icons/Weapons and Armor/";
    const string EQUIP_OUT = "Assets/Resources/Items/Equipment/";
    const string NODE_OUT = "Assets/Resources/SkillNodes/Tree/Equipment/";

    private class PerkTier
    {
        public string id; // Copper
        public string artFolder; // "2. Cooper"
        public string adjM, adjPl, adjF;
        public int cost; // очков навыков
        public int reqLevel;
    }

    static readonly PerkTier[] TIERS = {
        new PerkTier { id = "Copper", artFolder = "2. Cooper", adjM = "Медный", adjPl = "Медные", adjF = "Медная", cost = 3, reqLevel = 1 },
        new PerkTier { id = "Iron", artFolder = "3. Iron", adjM = "Железный", adjPl = "Железные", adjF = "Железная", cost = 5, reqLevel = 5 },
        new PerkTier { id = "Gold", artFolder = "4. Gold", adjM = "Золотой", adjPl = "Золотые", adjF = "Золотая", cost = 7, reqLevel = 10 },
        new PerkTier { id = "Platinum", artFolder = "5. Platinum", adjM = "Платиновый", adjPl = "Платиновые", adjF = "Платиновая", cost = 10, reqLevel = 15 },
        new PerkTier { id = "Obsidian", artFolder = "9. Obsidian", adjM = "Обсидиановый", adjPl = "Обсидиановые", adjF = "Обсидиановая", cost = 15, reqLevel = 20 },
    };

    private class PerkKind
    {
        public string id; // Sword (инфикс ассета + имя PNG/спрайта)
        public string noun; // "меч"
        public int gender; // 0 м.р. (adjM), 1 мн.ч. (adjPl), 2 ж.р. (adjF)
    }

    static readonly PerkKind[] KINDS = {
        new PerkKind { id = "Sword", noun = "меч", gender = 0 },
        new PerkKind { id = "Bow", noun = "лук", gender = 0 },
        new PerkKind { id = "Staff", noun = "посох", gender = 0 },
        new PerkKind { id = "Helmet", noun = "шлем", gender = 0 },
        new PerkKind { id = "Chestplate", noun = "нагрудник", gender = 0 },
        new PerkKind { id = "Leggings", noun = "поножи", gender = 1 },
        new PerkKind { id = "Boots", noun = "сапоги", gender = 1 },
        new PerkKind { id = "Pickaxe", noun = "кирка", gender = 2 },
        new PerkKind { id = "Axe", noun = "топор", gender = 0 },
    };

    [MenuItem("Tools/Equipment/3. Build Equipment Perk Nodes (45 узлов)")]
    public static void BuildPerkNodes()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/SkillNodes/Tree/Equipment"))
            AssetDatabase.CreateFolder("Assets/Resources/SkillNodes/Tree", "Equipment");

        int made = 0, updated = 0;
        var byKey = new Dictionary<string, SkillNode>();

        for (int ti = 0; ti < TIERS.Length; ti++)
        {
            PerkTier t = TIERS[ti];
            foreach (PerkKind k in KINDS)
            {
                string assetName = "EquipNode_" + t.id + "_" + k.id;
                string path = NODE_OUT + assetName + ".asset";
                bool exists = AssetDatabase.LoadAssetAtPath<SkillNode>(path) != null;

                SkillNode node = GetOrCreate(path);
                FillNode(node, t, k);
                byKey[t.id + k.id] = node;
                if (exists) updated++; else made++;
            }
        }

        // Цепочки: тир N требует тир N-1 того же вида
        foreach (var kvp in byKey)
        {
            string key = kvp.Key;
            SkillNode node = kvp.Value;
            SkillNode prev = null;
            for (int ti = 1; ti < TIERS.Length; ti++)
            {
                foreach (PerkKind k in KINDS)
                {
                    if (TIERS[ti].id + k.id == key)
                        prev = byKey[TIERS[ti - 1].id + k.id];
                }
            }
            node.requiredNodes = prev != null ? new SkillNode[] { prev } : new SkillNode[0];
            EditorUtility.SetDirty(node);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Equip] Перки экипировки: новых " + made + ", обновлено " + updated + " (45). Кнопки в дереве — вручную в equipmentContainer.");
    }

    // ═══════════════════════════════════════════════════════════
    // 4) ФИЗИЧЕСКАЯ РАССТАНОВКА В СЦЕНУ (как раньше через тул):
    // - дописывает 45 узлов в SkillTreeManager.allNodes в инспекторе;
    // - чистит CraftingEquipment от чужих кнопок и кладёт 45 кнопок сеткой
    //   (дальше двигаешь руками как хочешь).
    // Открой SampleScene (там PersistentRoot) и запусти пункт. В конце Ctrl+S!
    // ═══════════════════════════════════════════════════════════
    [MenuItem("Tools/Equipment/4. Place Nodes into Scene (открой SampleScene)")]
    public static void PlaceNodesIntoScene()
    {
        if (SceneManager.GetActiveScene().name != "SampleScene")
        {
            EditorUtility.DisplayDialog("Equipment",
                "Открой сцену 'SampleScene' (там живёт PersistentRoot) и запусти пункт ещё раз.\nСейчас открыта: "
                + SceneManager.GetActiveScene().name, "Понятно");
            return;
        }

        const string BTN_PREFAB = "Assets/Prefab/SkillNodeButton.prefab";
        GameObject btnPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BTN_PREFAB);
        if (btnPrefab == null) { Debug.LogError("[Equip] Нет префаба: " + BTN_PREFAB); return; }
        if (btnPrefab.GetComponent<SkillNodeUI>() == null && btnPrefab.GetComponentInChildren<SkillNodeUI>() == null)
        {
            Debug.LogError("[Equip] В " + BTN_PREFAB + " нет SkillNodeUI!");
            return;
        }

        // ── 1) SkillTreeManager.allNodes ──
        // Ищем вручную по корням сцены: GameObject.Find / FindFirstObjectByType
        // НЕ видят выключенные объекты, а панель дерева скрыта
        SkillTreeManager mgr = FindInScene<SkillTreeManager>();
        if (mgr == null) { Debug.LogError("[Equip] SkillTreeManager не найден в сцене!"); return; }

        var allList = new List<SkillNode>(mgr.allNodes ?? new SkillNode[0]);
        int mgrAdded = 0;
        var ordered = new List<SkillNode>();
        for (int ti = 0; ti < TIERS.Length; ti++)
        {
            foreach (PerkKind k in KINDS)
            {
                string path = NODE_OUT + "EquipNode_" + TIERS[ti].id + "_" + k.id + ".asset";
                SkillNode node = AssetDatabase.LoadAssetAtPath<SkillNode>(path);
                if (node == null) { Debug.LogWarning("[Equip] Нет узла: " + path + " (сначала пункт 3!)"); continue; }
                ordered.Add(node);
                if (!allList.Contains(node)) { allList.Add(node); mgrAdded++; }
            }
        }
        if (mgrAdded > 0)
        {
            Undo.RecordObject(mgr, "Add equipment nodes");
            mgr.allNodes = allList.ToArray();
            EditorUtility.SetDirty(mgr);
        }

        // ── 2) CraftingEquipment: чистим чужие кнопки, кладём свои ──
        GameObject container = FindObjectInScene("CraftingEquipment");
        if (container == null) { Debug.LogError("[Equip] Объект 'CraftingEquipment' не найден в сцене!"); return; }

        // Уже лежащие наши кнопки — не дублируем
        var present = new HashSet<string>();
        var trash = new List<GameObject>();
        foreach (SkillNodeUI ui in container.GetComponentsInChildren<SkillNodeUI>(true))
        {
            if (ui == null || ui.node == null) { if (ui != null) trash.Add(ui.gameObject); continue; }
            if (ui.node.branch == PlayerLevel.SkillBranch.Equipment)
                present.Add(ui.node.name);
            else
                trash.Add(ui.gameObject); // чужая ветка (клон) — убираем
        }
        foreach (GameObject go in trash)
            Undo.DestroyObjectImmediate(go);

        const float cellX = 115f, cellY = 130f;
        const int cols = 9;
        int placed = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            if (present.Contains(ordered[i].name)) continue;
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(btnPrefab, container.transform);
            go.name = "EquipNodeBtn_" + ordered[i].name;
            SkillNodeUI ui = go.GetComponent<SkillNodeUI>();
            if (ui == null) ui = go.GetComponentInChildren<SkillNodeUI>();
            ui.node = ordered[i];
            // Клик уже заведён в префабе (Button.onClick → SkillNodeUI.OnClick) — ничего не трогаем
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition = new Vector2((placed % cols) * cellX, -(placed / cols) * cellY);
            Undo.RegisterCreatedObjectUndo(go, "Place equipment node button");
            placed++;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = container;
        Debug.Log("[Equip] В сцену: узлов в менеджер +" + mgrAdded + ", кнопок +" + placed
            + ". Дальше двигай кнопки руками и жми Ctrl+S!");
    }
    // ── поиск по сцене ВКЛЮЧАЯ выключенные объекты ──
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

    static T FindInScene<T>() where T : Component
    {
        foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            T c = root.GetComponentInChildren<T>(true);
            if (c != null) return c;
        }
        return null;
    }

    static SkillNode GetOrCreate(string path)
    {
        SkillNode n = AssetDatabase.LoadAssetAtPath<SkillNode>(path);
        if (n != null) return n;
        n = ScriptableObject.CreateInstance<SkillNode>();
        AssetDatabase.CreateAsset(n, path);
        return n;
    }

    static void FillNode(SkillNode node, PerkTier t, PerkKind k)
    {
        string adj = k.gender == 0 ? t.adjM : (k.gender == 1 ? t.adjPl : t.adjF);
        string ruName = adj + " " + k.noun;
        // "Медный меч" — с большой буквы для дерева
        node.nodeName = char.ToUpper(ruName[0]) + ruName.Substring(1);
        node.description = "Позволяет носить и использовать: " + ruName + " (все редкости). Открывает вид в магазине.";
        node.branch = PlayerLevel.SkillBranch.Equipment;
        node.requiredLevel = t.reqLevel;
        node.skillPointsCost = t.cost;
        node.goldCost = 0;
        node.maxRanks = 1;
        node.rankCostMultiplier = 1f;
        node.effectType = SkillEffectType.UnlockItem;
        node.effectValue = 0f;

        // Иконка = иконка предмета тира
        Sprite icon = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ART + t.artFolder + "/" + k.id + ".png"))
        {
            Sprite sp = o as Sprite;
            if (sp != null && sp.name == k.id + "_0") { icon = sp; break; }
        }
        if (icon == null) Debug.LogWarning("[Equip] Нет иконки перка: " + t.id + "/" + k.id);
        node.icon = icon;

        // unlocksItem = Common-предмет вида (для текста "Открыть: ..."),
        // unlocksFeature = тег для проверок кода и магазина
        ItemData common = AssetDatabase.LoadAssetAtPath<ItemData>(
            EQUIP_OUT + t.id + "/" + t.id + k.id + "_Common.asset");
        if (common == null) Debug.LogWarning("[Equip] Нет Common-предмета: " + t.id + k.id + " (сначала пункт 1!)");
        node.unlocksItem = common;
        node.unlocksFeature = EquipmentLocks.TagFor(t.id, k.id);

        EditorUtility.SetDirty(node);
    }
}
