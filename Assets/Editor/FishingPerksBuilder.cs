using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Узлы ветки Fishing (дерево навыков):
/// Tools → Fish → 9. Build Fishing Perk Nodes.
/// 5 разблокировок удочек + 5 перков веса (ОДИН перк на удочку, 5 рангов × +20%)
/// + 18 крючков = 28 узлов.
/// Теги fish_rod_&lt;Тир&gt; / fish_rod_&lt;Тир&gt;_w1..w5 / fish_hook_&lt;Имя&gt; — единый формат
/// с ShopInteraction (магазин) и SkillTreeManager.GetRodWeightMult (бонус веса).
/// Цепочки (requiredNodes): удочка N требует удочку N-1; усиление Wi требует W(i-1)
/// и разблокировку своей удочки; крючок N требует крючок N-1 — без базы дальше нельзя.
/// Повторный запуск ОБНОВЛЯЕТ in place (имена ассетов стабильны — сейвы рангов не ломаются).
/// UI-кнопки ставит пункт 10 (контейнер fishingContainer, сортировка по уровням).
/// </summary>
public static class FishingPerksBuilder
{
    const string NODE_OUT = "Assets/Resources/SkillNodes/Tree/Fishing/";
    const string ROD_DIR = "Assets/Resources/Items/Equipment/";
    const string HOOK_DIR = "Assets/Resources/Items/Hooks/";

    private class RodTier
    {
        public string id; // Copper
        public string adjF; // "Медная" (удочка — ж.р.)
        public int reqLevel;
        public int cost;
    }

    // Медь 5/3, дальше +5 ур. и +2 очка. Дерево свободно (подарок Морека).
    static readonly RodTier[] RODS = {
        new RodTier { id = "Copper", adjF = "Медная", reqLevel = 5, cost = 3 },
        new RodTier { id = "Iron", adjF = "Железная", reqLevel = 10, cost = 5 },
        new RodTier { id = "Gold", adjF = "Золотая", reqLevel = 15, cost = 7 },
        new RodTier { id = "Platinum", adjF = "Платиновая", reqLevel = 20, cost = 9 },
        new RodTier { id = "Obsidian", adjF = "Обсидиановая", reqLevel = 25, cost = 11 },
    };

    const int WEIGHT_RANKS = 5; // рангов перка веса (+20% ×5 = +100% на максе)
    const float WEIGHT_STEP = 0.2f;
    static readonly int[] WEIGHT_COSTS = { 1, 2, 3, 4, 5 }; // цена по рангам (rankPointCosts)

    // Крючки по порядку силы (как в магазине): уровень 5 + 2 за шаг, цена 1 очко.
    static readonly string[] HOOKS = {
        "Copper_I", "Copper_II",
        "Silver_I", "Silver_II",
        "Gold_I", "Gold_II",
        "Iron_I", "Iron_II",
        "Ruby_I", "Ruby_II",
        "Sapphire_I", "Sapphire_II",
        "Amethyst_I", "Amethyst_II",
        "Rose_I", "Rose_II",
        "Obsidian_I", "Obsidian_II",
    };
    const int HOOK_BASE_LEVEL = 5;
    const int HOOK_LEVEL_STEP = 2;
    const int HOOK_COST = 1;

    [MenuItem("Tools/Fish/9. Build Fishing Perk Nodes (28 узлов)")]
    public static void BuildPerkNodes()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/SkillNodes/Tree/Fishing"))
            AssetDatabase.CreateFolder("Assets/Resources/SkillNodes/Tree", "Fishing");

        // Миграция со старой схемы (5 отдельных W1..W5-нод на удочку):
        // удаляем устаревшие ассеты — теперь ОДИН перк веса на 5 рангов
        foreach (RodTier t0 in RODS)
            for (int k = 1; k <= WEIGHT_RANKS; k++)
            {
                string oldPath = NODE_OUT + "FishNode_Rod_" + t0.id + "_W" + k + ".asset";
                if (AssetDatabase.LoadAssetAtPath<SkillNode>(oldPath) != null)
                {
                    AssetDatabase.DeleteAsset(oldPath);
                    Debug.Log("[Fish] Удалён старый узел: " + oldPath);
                }
            }

        int made = 0, updated = 0;
        var byKey = new Dictionary<string, SkillNode>();

        // ── Разблокировки удочек + усиления веса ──
        SkillNode prevUnlock = null;
        foreach (RodTier t in RODS)
        {
            string rodAsset = ROD_DIR + t.id + "/" + t.id + "Rod_Common.asset";
            ItemData rod = AssetDatabase.LoadAssetAtPath<ItemData>(rodAsset);
            if (rod == null) Debug.LogWarning("[Fish] Нет удочки: " + rodAsset + " (сначала Equipment → 1!)");

            string uKey = "Rod_" + t.id;
            string uPath = NODE_OUT + "FishNode_" + uKey + ".asset";
            bool uExists = AssetDatabase.LoadAssetAtPath<SkillNode>(uPath) != null;
            SkillNode unlock = GetOrCreate(uPath);
            unlock.nodeName = t.adjF + " удочка";
            unlock.description = "Открывает в магазине: " + (rod != null ? rod.itemName : t.adjF + " удочка")
                + ". Без перка у торговца только деревянная.";
            unlock.icon = rod != null ? rod.icon : null;
            unlock.branch = PlayerLevel.SkillBranch.Fishing;
            unlock.requiredLevel = t.reqLevel;
            unlock.skillPointsCost = t.cost;
            unlock.goldCost = 0;
            unlock.maxRanks = 1;
            unlock.rankCostMultiplier = 1f;
            unlock.effectType = SkillEffectType.UnlockItem;
            unlock.effectValue = 0f;
            unlock.unlocksItem = rod;
            unlock.unlocksFeature = "fish_rod_" + t.id;
            unlock.requiredNodes = prevUnlock != null ? new SkillNode[] { prevUnlock } : new SkillNode[0];
            EditorUtility.SetDirty(unlock);
            byKey[uKey] = unlock;
            if (uExists) updated++; else made++;
            prevUnlock = unlock;

            // Усиление веса: ОДИН перк на 5 рангов (+20% за ранг, итого +100%).
            // Цены 1,2,3,4,5 — через rankPointCosts (формула множителя так не умеет).
            // Качается в той же ноде после разблокировки удочки (requiredNodes = unlock).
            string wKey = "Rod_" + t.id + "_W";
            string wPath = NODE_OUT + "FishNode_" + wKey + ".asset";
            bool wExists = AssetDatabase.LoadAssetAtPath<SkillNode>(wPath) != null;
            SkillNode w = GetOrCreate(wPath);
            w.nodeName = t.adjF + " удочка: улов +20%/ранг";
            w.description = "Каждый ранг: +" + (WEIGHT_STEP * 100f).ToString("0")
                + "% к макс. весу улова " + t.adjF.ToLower() + " удочки (макс. +100% на 5 ранге).";
            w.icon = rod != null ? rod.icon : null;
            w.branch = PlayerLevel.SkillBranch.Fishing;
            w.requiredLevel = t.reqLevel;
            w.skillPointsCost = WEIGHT_COSTS[0];
            w.rankPointCosts = (int[])WEIGHT_COSTS.Clone();
            w.goldCost = 0;
            w.maxRanks = WEIGHT_RANKS;
            w.rankCostMultiplier = 1f;
            w.effectType = SkillEffectType.FishingRodWeight;
            w.effectValue = WEIGHT_STEP;
            w.unlocksItem = null;
            w.unlocksFeature = "fish_rod_" + t.id + "_w";
            w.requiredNodes = new SkillNode[] { unlock };
            EditorUtility.SetDirty(w);
            byKey[wKey] = w;
            if (wExists) updated++; else made++;
        }

        // ── Крючки (линейная цепочка) ──
        SkillNode prevHook = null;
        for (int i = 0; i < HOOKS.Length; i++)
        {
            string hKey = "Hook_" + HOOKS[i];
            string hPath = NODE_OUT + "FishNode_" + hKey + ".asset";
            bool hExists = AssetDatabase.LoadAssetAtPath<SkillNode>(hPath) != null;
            ItemData hook = AssetDatabase.LoadAssetAtPath<ItemData>(HOOK_DIR + "Hook_" + HOOKS[i] + ".asset");
            if (hook == null) Debug.LogWarning("[Fish] Нет крючка: Hook_" + HOOKS[i] + " (сначала Fish → 8!)");

            SkillNode h = GetOrCreate(hPath);
            h.nodeName = hook != null ? hook.itemName : ("Крючок " + HOOKS[i].Replace("_", " "));
            h.description = "Открывает в магазине: " + h.nodeName
                + (hook != null ? " (" + hook.hookMinKg + "–" + hook.hookMaxKg + " кг)." : ".");
            h.icon = hook != null ? hook.icon : null;
            h.branch = PlayerLevel.SkillBranch.Fishing;
            h.requiredLevel = HOOK_BASE_LEVEL + HOOK_LEVEL_STEP * i;
            h.skillPointsCost = HOOK_COST;
            h.goldCost = 0;
            h.maxRanks = 1;
            h.rankCostMultiplier = 1f;
            h.effectType = SkillEffectType.UnlockItem;
            h.effectValue = 0f;
            h.unlocksItem = hook;
            h.unlocksFeature = "fish_hook_" + HOOKS[i];
            h.requiredNodes = prevHook != null ? new SkillNode[] { prevHook } : new SkillNode[0];
            EditorUtility.SetDirty(h);
            byKey[hKey] = h;
            if (hExists) updated++; else made++;
            prevHook = h;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Fish] Перки рыбалки: новых " + made + ", обновлено " + updated + " (28). Кнопки — пункт 10.");
    }

    // ═══════════════════════════════════════════════════════════
    // 10) ФИЗИЧЕСКАЯ РАССТАНОВКА В СЦЕНУ:
    // - дописывает 28 узлов в SkillTreeManager.allNodes в инспекторе
    //   (заодно чистит битые ссылки и старые W1..W5-ноды);
    // - чистит fishingContainer от чужих/устаревших кнопок и кладёт кнопки,
    //   отсортированные ПО УРОВНЯМ (младшие сверху), сетка 6 колонок
    //   (дальше двигаешь руками как хочешь).
    // Открой SampleScene (там PersistentRoot) и запусти пункт. В конце Ctrl+S!
    // ═══════════════════════════════════════════════════════════
    [MenuItem("Tools/Fish/10. Place Fishing Nodes into Scene (открой SampleScene)")]
    public static void PlaceNodesIntoScene()
    {
        if (SceneManager.GetActiveScene().name != "SampleScene")
        {
            EditorUtility.DisplayDialog("Fishing",
                "Открой сцену 'SampleScene' (там живёт PersistentRoot) и запусти пункт ещё раз.\nСейчас открыта: "
                + SceneManager.GetActiveScene().name, "Понятно");
            return;
        }

        const string BTN_PREFAB = "Assets/Prefab/SkillNodeButton.prefab";
        GameObject btnPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BTN_PREFAB);
        if (btnPrefab == null) { Debug.LogError("[Fish] Нет префаба: " + BTN_PREFAB); return; }
        if (btnPrefab.GetComponent<SkillNodeUI>() == null && btnPrefab.GetComponentInChildren<SkillNodeUI>() == null)
        {
            Debug.LogError("[Fish] В " + BTN_PREFAB + " нет SkillNodeUI!");
            return;
        }

        // ── 1) SkillTreeManager.allNodes (порядок = по уровням) ──
        SkillTreeManager mgr = FindInScene<SkillTreeManager>();
        if (mgr == null) { Debug.LogError("[Fish] SkillTreeManager не найден в сцене!"); return; }

        // Имена старых W1..W5-нод (теперь один перк _W на 5 рангов) — вычищаем
        var obsolete = new HashSet<string>();
        foreach (RodTier t in RODS)
            for (int i = 1; i <= WEIGHT_RANKS; i++)
                obsolete.Add("FishNode_Rod_" + t.id + "_W" + i);

        var ordered = new List<SkillNode>();
        foreach (RodTier t in RODS)
        {
            AddNode(ordered, "FishNode_Rod_" + t.id);
            AddNode(ordered, "FishNode_Rod_" + t.id + "_W");
        }
        foreach (string hk in HOOKS)
            AddNode(ordered, "FishNode_Hook_" + hk);

        ordered.Sort((a, b) =>
        {
            int d = a.requiredLevel.CompareTo(b.requiredLevel);
            return d != 0 ? d : string.CompareOrdinal(a.nodeName, b.nodeName);
        });

        var allList = new List<SkillNode>(mgr.allNodes ?? new SkillNode[0]);
        int removed = allList.RemoveAll(n => n == null || obsolete.Contains(n.name));
        int mgrAdded = 0;
        foreach (SkillNode n in ordered)
            if (!allList.Contains(n)) { allList.Add(n); mgrAdded++; }
        if (removed > 0 || mgrAdded > 0)
        {
            Undo.RecordObject(mgr, "Add fishing nodes");
            mgr.allNodes = allList.ToArray();
            EditorUtility.SetDirty(mgr);
        }

        // ── 2) fishingContainer: берём из SkillTreeUI (привяжи поле в инспекторе!) ──
        SkillTreeUI ui = FindInScene<SkillTreeUI>();
        if (ui == null) { Debug.LogError("[Fish] SkillTreeUI не найден в сцене!"); return; }
        if (ui.fishingContainer == null)
        {
            Debug.LogError("[Fish] В SkillTreeUI не привязан fishingContainer! Привяжи контейнер в инспекторе и повтори.");
            return;
        }
        GameObject container = ui.fishingContainer;

        var present = new HashSet<string>();
        var trash = new List<GameObject>();
        foreach (SkillNodeUI sui in container.GetComponentsInChildren<SkillNodeUI>(true))
        {
            if (sui == null || sui.node == null) { if (sui != null) trash.Add(sui.gameObject); continue; }
            if (obsolete.Contains(sui.node.name)) { trash.Add(sui.gameObject); continue; } // старый W-ранг
            if (sui.node.branch == PlayerLevel.SkillBranch.Fishing)
                present.Add(sui.node.name);
            else
                trash.Add(sui.gameObject); // чужая ветка (клон) — убираем
        }
        foreach (GameObject go in trash)
            Undo.DestroyObjectImmediate(go);

        const float cellX = 115f, cellY = 130f;
        const int cols = 6;
        int placed = 0;
        foreach (SkillNode n in ordered)
        {
            if (present.Contains(n.name)) continue;
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(btnPrefab, container.transform);
            go.name = "FishNodeBtn_" + n.name;
            SkillNodeUI sui = go.GetComponent<SkillNodeUI>();
            if (sui == null) sui = go.GetComponentInChildren<SkillNodeUI>();
            sui.node = n;
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
                rt.anchoredPosition = new Vector2((placed % cols) * cellX, -(placed / cols) * cellY);
            Undo.RegisterCreatedObjectUndo(go, "Place fishing node button");
            placed++;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = container;
        Debug.Log("[Fish] В сцену: убрано устаревших " + removed + ", узлов в менеджер +" + mgrAdded
            + ", кнопок +" + placed + " (по уровням). Дальше двигай кнопки руками и жми Ctrl+S!");
    }

    static void AddNode(List<SkillNode> ordered, string assetName)
    {
        string path = NODE_OUT + assetName + ".asset";
        SkillNode node = AssetDatabase.LoadAssetAtPath<SkillNode>(path);
        if (node == null) { Debug.LogWarning("[Fish] Нет узла: " + path + " (сначала пункт 9!)"); return; }
        ordered.Add(node);
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
}
