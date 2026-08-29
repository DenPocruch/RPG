using System.Linq;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Tools → Enemy → Create Goblin EnemyData / Prefabs
/// Листы гоблинов: ряд 0 = лицом к зрителю (down), ряд 1 = спиной (up),
/// ряд 2 = повёрнут ВПРАВО (sideRight). Отдельного ряда «влево» нет —
/// лево = отзеркаленный sideRight (side = sideRight, sideFacesLeft = false).
/// Ассеты: Assets/Resources/Enemies/GoblinSpear|GoblinArcher.asset,
/// префабы: Assets/Prefab/Enemy/Goblins/ (база — Slime.prefab).
/// Идемпотентно: перезапуск пересоздаёт ассеты.
/// </summary>
public static class GoblinDataBuilder
{
    const string SpearDir = "Assets/Art/Enemy/Goblins/Spear Goblin";
    const string ArcherDir = "Assets/Art/Enemy/Goblins/Archer Goblin";
    const string OutFolder = "Assets/Resources/Enemies";
    const string PrefabFolder = "Assets/Prefab/Enemy/Goblins";
    const string BasePrefab = "Assets/Prefab/Enemy/Slimes/Slime.prefab";
    const string ArrowPrefab = "Assets/Prefab/Arrow.prefab";

    [MenuItem("Tools/Enemy/Create Goblin EnemyData")]
    static void BuildData()
    {
        if (!AssetDatabase.IsValidFolder(OutFolder))
            AssetDatabase.CreateFolder("Assets/Resources", "Enemies");

        BuildData("GoblinSpear", SpearDir, "Spear");
        BuildData("GoblinArcher", ArcherDir, "Bow");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[GoblinDataBuilder] EnemyData готов: GoblinSpear, GoblinArcher");
    }

    [MenuItem("Tools/Enemy/Create Goblin Prefabs")]
    static void BuildPrefabs()
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets/Prefab/Enemy", "Goblins");

        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefab);
        var arrowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPrefab);
        if (basePrefab == null) { Debug.LogError("[GoblinDataBuilder] Нет " + BasePrefab); return; }

        BuildPrefab("GoblinSpear", basePrefab, null);
        BuildPrefab("GoblinArcher", basePrefab, arrowPrefab);

        AssetDatabase.SaveAssets();
        Debug.Log("[GoblinDataBuilder] Префабы готовы в " + PrefabFolder);
    }

    static void BuildData(string name, string dir, string attackSheet)
    {
        var data = ScriptableObject.CreateInstance<EnemyData>();
        data.idle = Frames(dir, "Idle", 4);
        data.walk = Frames(dir, "Walk", 6);
        data.attack = Frames(dir, attackSheet, attackSheet == "Spear" ? 6 : 7);
        data.damage = Frames(dir, "Damage", 4);
        data.dead = Frames(dir, "Dead", 4);
        data.sideFacesLeft = false; // боковой ряд нарисован ВПРАВО, лево = зеркало
        data.animationFPS = 8f;

        string path = $"{OutFolder}/{name}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(data, path);
        Debug.Log($"[GoblinDataBuilder] {path}: idle {Count(data.idle)}, walk {Count(data.walk)}, attack {Count(data.attack)}, damage {Count(data.damage)}, dead {Count(data.dead)}");
    }

    // Все ряды одинаковой длины; если кадров меньше ожидания — берём сколько есть
    static EnemyData.DirectionalFrames Frames(string dir, string sheet, int framesPerRow)
    {
        string assetPath = $"{dir}/{sheet}.png";
        var all = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().ToArray();
        var df = new EnemyData.DirectionalFrames();
        if (all.Length == 0)
        {
            Debug.LogError($"[GoblinDataBuilder] Нет спрайтов в {assetPath} — проверь нарезку мета");
            return df;
        }

        df.down = LoadRow(sheet, all, 0, framesPerRow);
        df.up = LoadRow(sheet, all, 1, framesPerRow);
        df.sideRight = LoadRow(sheet, all, 2, framesPerRow);
        df.side = df.sideRight; // лево = отзеркаленный правый ряд
        return df;
    }

    static Sprite[] LoadRow(string sheet, Sprite[] all, int row, int count)
    {
        var result = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            string spriteName = $"{sheet}_{row * count + i}";
            var s = all.FirstOrDefault(x => x.name == spriteName);
            if (s == null)
            {
                Debug.LogError($"[GoblinDataBuilder] Нет кадра {spriteName}");
                return new Sprite[0];
            }
            result[i] = s;
        }
        return result;
    }

    static void BuildPrefab(string name, GameObject basePrefab, GameObject arrowPrefab)
    {
        var data = AssetDatabase.LoadAssetAtPath<EnemyData>($"{OutFolder}/{name}.asset");
        if (data == null) { Debug.LogError($"[GoblinDataBuilder] Нет ассета {name}.asset — сначала Create Goblin EnemyData"); return; }

        var go = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        go.name = name;

        var sr = go.GetComponent<SpriteRenderer>();
        if (sr != null && EnemyData.Has(data.idle) && data.idle.down.Length > 0)
            sr.sprite = data.idle.down[0];

        var ai = go.GetComponent<SimpleEnemyAI>();
        if (ai != null)
        {
            ai.enemyData = data;
            if (arrowPrefab != null)
            {
                ai.arrowPrefab = arrowPrefab;
                var fpGo = new GameObject("FirePoint");
                fpGo.transform.SetParent(go.transform, false);
                fpGo.transform.localPosition = new Vector3(0.2f, 0.5f, 0f);
                ai.firePoint = fpGo.transform;
            }
        }

        string path = $"{PrefabFolder}/{name}.prefab";
        AssetDatabase.DeleteAsset(path);
        PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        Debug.Log($"[GoblinDataBuilder] Префаб: {path}");
    }

    static int Count(EnemyData.DirectionalFrames df) => df != null && df.side != null ? df.side.Length : 0;
}
