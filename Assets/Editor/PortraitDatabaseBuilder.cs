using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Сборка базы портретов из нарезанных PNG (после пункта 4):
/// Resources/Portrait/PortraitDatabase.asset.
/// Маппинг файловой структуры в имена конструктора:
/// Skins/Male|Female/N, Skins/Ears/Elf/N -> Elf/N, Eyes/цвет, Hair (Standart->Standard),
/// Clothers/пол/цвет, Acc + нормализация (pirate eye patch, Santa hat), Beard/Butterfly.
/// Ears/Human и FX.png не тянутся (в конструкторе им нет пары).
/// Повторный прогон пересобирает (guid ассета стабилен).
/// </summary>
public static class PortraitDatabaseBuilder
{
    const string PNG_ROOT = "Assets/Art/Character/Portrait/PNG";
    const string DB_PATH = "Assets/Resources/Portrait/PortraitDatabase.asset";
    const int CELLS = 15;

    [MenuItem("Tools/Character/5. Build Portrait Database")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[PortraitDB] Выключи Play-режим.");
            return;
        }

        var db = AssetDatabase.LoadAssetAtPath<PortraitDatabase>(DB_PATH);
        if (db == null)
        {
            db = ScriptableObject.CreateInstance<PortraitDatabase>();
            Directory.CreateDirectory(Path.GetDirectoryName(DB_PATH));
            AssetDatabase.CreateAsset(db, DB_PATH);
        }
        db.categories.Clear();

        var catByName = new Dictionary<string, PortraitCategory>(System.StringComparer.OrdinalIgnoreCase);
        PortraitCategory GetCat(string name)
        {
            if (!catByName.TryGetValue(name, out PortraitCategory c))
            {
                c = new PortraitCategory { categoryName = name };
                catByName[name] = c;
                db.categories.Add(c);
            }
            return c;
        }

        int files = 0, warn = 0;
        // (категория портрета, префикс варианта, маска файлов)
        AddDir(db, GetCat, "Skins", "Skins/Male", "Male/", ref files, ref warn);
        AddDir(db, GetCat, "Skins", "Skins/Female", "Female/", ref files, ref warn);
        AddDir(db, GetCat, "Elf", "Skins/Ears/Elf", "", ref files, ref warn);
        AddDir(db, GetCat, "Eyes", "Eyes", "", ref files, ref warn);
        AddDir(db, GetCat, "Hair", "Hair", "", ref files, ref warn, NormalizeHair);
        AddDir(db, GetCat, "Clothers", "Clothers", "", ref files, ref warn, NormalizeClothes);
        AddDir(db, GetCat, "Acc", "Acc", "", ref files, ref warn, NormalizeAcc, topOnly: true);
        AddDir(db, GetCat, "Beard", "Acc/Beard", "", ref files, ref warn);
        AddDir(db, GetCat, "Butterfly", "Acc/Butterfly", "", ref files, ref warn);

        EditorUtility.SetDirty(db);
        AssetDatabase.SaveAssets();
        Debug.Log($"[PortraitDB] Готово: файлов {files}, категорий {db.categories.Count}, предупреждений {warn}");
        if (warn > 0)
            EditorUtility.DisplayDialog("Portrait Database", $"Готово, но предупреждений: {warn} (см. Console)", "OK");
    }

    delegate string Namer(string rel);

    static void AddDir(PortraitDatabase db, System.Func<string, PortraitCategory> GetCat,
        string catName, string subDir, string prefix,
        ref int files, ref int warn, Namer namer = null, bool topOnly = false)
    {
        string dir = Path.Combine(PNG_ROOT, subDir).Replace('\\', '/');
        if (!Directory.Exists(dir)) { Debug.LogWarning("[PortraitDB] Нет папки " + dir); warn++; return; }
        string[] pngs = Directory.GetFiles(dir, "*.png",
            topOnly ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories);
        System.Array.Sort(pngs);
        foreach (string png in pngs)
        {
            string assetPath = png.Replace('\\', '/');
            string rel = assetPath.Substring(dir.Length + 1);
            rel = rel.Substring(0, rel.Length - 4); // без .png
            string variant = (namer != null ? namer(rel) : prefix + rel) ?? "";
            variant = variant.Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(variant)) continue;

            var sprites = new List<Sprite>();
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
                if (o is Sprite s) sprites.Add(s);
            sprites.Sort((a, b) => SpriteNameIndex(a.name).CompareTo(SpriteNameIndex(b.name)));
            if (sprites.Count != CELLS)
            {
                Debug.LogWarning($"[PortraitDB] {assetPath}: кадров {sprites.Count}, надо {CELLS} — прогони пункт 4");
                warn++;
                continue;
            }
            GetCat(catName).variants.Add(new PortraitVariant { variantName = variant, frames = sprites.ToArray() });
            files++;
        }
    }

    // Hair/Fawn/Black -> Fawn/Black; Hair/Standart/X -> Standard/X (опечатка автора)
    static string NormalizeHair(string rel)
    {
        if (rel.StartsWith("Standart/")) return "Standard/" + rel.Substring("Standart/".Length);
        return rel;
    }

    // Clothers/Male/Blue -> Male/Blue (в конструкторе пол одежды не выбирается —
    // цвет совпадёт, пол берём Male; если захочешь Female — поменяй здесь)
    static string NormalizeClothes(string rel) => rel;

    // Та же нормализация, что в CharacterDatabaseBuilder
    static string NormalizeAcc(string rel)
    {
        if (rel == "Pirate eyepatch") return "pirate eye patch";
        if (rel == "Pirate eye patch") return "pirate eye patch";
        if (rel == "Santa Hat") return "Santa hat";
        return rel;
    }

    static int SpriteNameIndex(string spriteName)
    {
        int p = spriteName.LastIndexOf('_');
        if (p >= 0 && int.TryParse(spriteName.Substring(p + 1), out int idx))
            return idx;
        return 0;
    }
}
