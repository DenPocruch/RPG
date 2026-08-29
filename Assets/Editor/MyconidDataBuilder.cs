using System.Linq;
using UnityEngine;
using UnityEditor;

/// <summary>
/// Tools → Enemy → Create Myconid EnemyData
/// Собирает EnemyData-ассеты для всех 5 цветов Myconid из нарезанных спрайтов.
/// Листы: ряд 0 = лицом к зрителю (down), ряд 1 = спиной (up),
/// ряд 2 = повёрнут ВПРАВО (sideRight), ряд 3 = ВЛЕВО (side).
/// Кадры в мета нарезаны рядами сверху вниз: ряд r начинается с индекса r * кадровВРяду.
/// Ассеты кладутся в Assets/Resources/Enemies/Myconid<Цвет>.asset (как у слаймов).
/// Идемпотентно: перезапуск пересоздаёт ассеты.
/// </summary>
public static class MyconidDataBuilder
{
    const string Root = "Assets/Art/Enemy/Myconid";
    const string OutFolder = "Assets/Resources/Enemies";

    [MenuItem("Tools/Enemy/Create Myconid EnemyData")]
    static void BuildAll()
    {
        string[] colors = { "Blue", "Green", "Pink", "Purple", "Red" };
        if (!AssetDatabase.IsValidFolder(OutFolder))
            AssetDatabase.CreateFolder("Assets/Resources", "Enemies");

        foreach (var color in colors)
            Build(color);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MyconidDataBuilder] Готово: 5 ассетов в " + OutFolder);
    }

    static void Build(string color)
    {
        string dir = $"{Root}/{color}";

        var data = ScriptableObject.CreateInstance<EnemyData>();
        data.idle = Frames(dir, "Idle", 4);
        data.walk = Frames(dir, "Walk", 6);
        data.attack = Frames(dir, "Attack", 6);
        data.damage = Frames(dir, "Damage", 4);
        data.dead = Frames(dir, "Dead", 5);
        data.sideFacesLeft = true;
        data.animationFPS = 8f;

        string path = $"{OutFolder}/Myconid{color}.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(data, path);
        Debug.Log($"[MyconidDataBuilder] {path}: idle {Count(data.idle)}, walk {Count(data.walk)}, attack {Count(data.attack)}, damage {Count(data.damage)}, dead {Count(data.dead)}");
    }

    static EnemyData.DirectionalFrames Frames(string dir, string sheet, int framesPerRow)
    {
        var df = new EnemyData.DirectionalFrames();
        df.down = LoadRow(dir, sheet, 0, framesPerRow);      // ряд 0 = лицом к зрителю
        df.up = LoadRow(dir, sheet, 1, framesPerRow);        // ряд 1 = спиной к зрителю
        df.sideRight = LoadRow(dir, sheet, 2, framesPerRow); // ряд 2 = повёрнут вправо
        df.side = LoadRow(dir, sheet, 3, framesPerRow);      // ряд 3 = повёрнут влево
        return df;
    }

    static Sprite[] LoadRow(string dir, string sheet, int row, int count)
    {
        string assetPath = $"{dir}/{sheet}.png";
        var all = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().ToArray();
        if (all.Length == 0)
        {
            Debug.LogError($"[MyconidDataBuilder] Нет спрайтов в {assetPath} — проверь нарезку мета");
            return new Sprite[0];
        }

        var result = new Sprite[count];
        for (int i = 0; i < count; i++)
        {
            string spriteName = $"{sheet}_{row * count + i}";
            var s = all.FirstOrDefault(x => x.name == spriteName);
            if (s == null)
            {
                Debug.LogError($"[MyconidDataBuilder] В {assetPath} нет кадра {spriteName}");
                return new Sprite[0];
            }
            result[i] = s;
        }
        return result;
    }

    static int Count(EnemyData.DirectionalFrames df) => df != null ? (df.side != null ? df.side.Length : 0) : 0;
}
