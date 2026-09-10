using UnityEngine;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Массовый фикс импорта портретов (аватары героя/NPC):
/// Point-фильтр, Compression None, без мипмапов,
/// нарезка сеткой 5x3 по 64px (все листы 320x192), пивот Center.
/// Строки сетки = эмоции (наверх row 0), имя кадра BaseName_{row*5+col}.
/// Повторный прогон безопасен. Play-режим должен быть выключен.
/// </summary>
public static class PortraitImportFixer
{
    const string ROOT = "Assets/Art/Character/Portrait/PNG";
    const int CELL = 64;
    const int COLS = 5;
    const int ROWS = 3;
    const int PPU = 16; // как остальные спрайты проекта (для UI не критично, размер даёт RectTransform)

    [MenuItem("Tools/Character/4. Fix Portrait Import (Point, Grid 10x6)")]
    public static void FixAll()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[PortraitImport] Выключи Play-режим перед правкой импорта.");
            return;
        }

        // Кейс-унификация под Android (там пути чувствительны к регистру):
        // Hair/Lyria/blonde.png -> Blonde.png (в конструкторе вариант Lyria/Blonde)
        FixCase("Assets/Art/Character/Portrait/PNG/Hair/Lyria/blonde.png",
                "Assets/Art/Character/Portrait/PNG/Hair/Lyria/Blonde.png");

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { ROOT });
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("[PortraitImport] PNG не найдены в " + ROOT);
            return;
        }

        bool ok = EditorUtility.DisplayDialog("Portrait Import Fix",
            $"Файлов: {guids.Length}\n\nПоставит всем:\n• Filter Point (no filter)\n• Compression None, без мипмапов\n• Нарезка сеткой 5x3 по 64px, пивот Center\n\nПродолжить?",
            "Да, чинить", "Отмена");
        if (!ok) return;

        int done = 0;
        var warnings = new List<string>();
        AssetDatabase.DisallowAutoRefresh();
        try
        {
            for (int g = 0; g < guids.Length; g++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[g]);
                if (EditorUtility.DisplayCancelableProgressBar("Portrait Import Fix", path, (float)g / guids.Length))
                {
                    Debug.LogWarning("[PortraitImport] Прервано пользователем. Готово: " + done + "/" + guids.Length);
                    break;
                }

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                ReadPngSize(path, out int rawW, out int rawH);
                if (rawW != COLS * CELL || rawH != ROWS * CELL)
                {
                    warnings.Add($"{path} — размер {rawW}x{rawH}, ждали {COLS * CELL}x{ROWS * CELL}: пропущен");
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.spritePixelsPerUnit = PPU;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.sRGBTexture = true;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;

                var texSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(texSettings);
                texSettings.spriteMeshType = SpriteMeshType.FullRect;
                texSettings.spriteExtrude = 0;
                importer.SetTextureSettings(texSettings);

                if (IsSlicedRight(path)) { done++; continue; }

                string baseName = Path.GetFileNameWithoutExtension(path);
                var rects = new SpriteRect[COLS * ROWS];
                for (int r = 0; r < ROWS; r++)
                    for (int c = 0; c < COLS; c++)
                    {
                        rects[r * COLS + c] = new SpriteRect
                        {
                            name = baseName + "_" + (r * COLS + c),
                            // Unity Y снизу: row 0 = верхняя строка
                            rect = new Rect(c * CELL, rawH - (r + 1) * CELL, CELL, CELL),
                            alignment = SpriteAlignment.Center,
                            pivot = new Vector2(0.5f, 0.5f),
                            border = Vector4.zero
                        };
                    }

                var factories = new SpriteDataProviderFactories();
                factories.Init();
                bool sliced = false;
                for (int attempt = 0; attempt < 3 && !sliced; attempt++)
                {
                    if (attempt > 0)
                        System.Threading.Thread.Sleep(300);
                    var dataProvider = factories.GetSpriteEditorDataProviderFromObject(importer);
                    if (dataProvider == null) { warnings.Add(path + " — нет SpriteDataProvider"); break; }
                    dataProvider.InitSpriteEditorDataProvider();
                    dataProvider.SetSpriteRects(rects);
                    dataProvider.Apply();
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                    sliced = CountSprites(path) == COLS * ROWS;
                }
                if (!sliced)
                    warnings.Add($"{path} — не нарезался (есть {CountSprites(path)}, надо {COLS * ROWS})");
                else
                    done++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            AssetDatabase.AllowAutoRefresh();
            AssetDatabase.Refresh();
        }

        AssetDatabase.SaveAssets();

        foreach (string warn in warnings)
            Debug.LogWarning("[PortraitImport] " + warn);

        EditorUtility.DisplayDialog("Portrait Import Fix",
            $"Готово: {done}/{guids.Length}\nПредупреждений: {warnings.Count} (см. Console)",
            "OK");
        Debug.Log($"[PortraitImport] Готово: {done}/{guids.Length}, предупреждений: {warnings.Count}");
    }

    /// <summary>Переименование с изменением только регистра (через временное имя).</summary>
    static void FixCase(string from, string to)
    {
        if (from == to) return;
        string tmp = to + ".tmp_rename";
        string err = AssetDatabase.MoveAsset(from, tmp);
        if (!string.IsNullOrEmpty(err))
        {
            Debug.LogWarning("[PortraitImport] Не переименовал " + from + ": " + err);
            return;
        }
        err = AssetDatabase.MoveAsset(tmp, to);
        if (!string.IsNullOrEmpty(err))
            Debug.LogWarning("[PortraitImport] Не переименовал " + tmp + ": " + err);
        else
            Debug.Log("[PortraitImport] Переименовано: " + from + " -> " + to);
    }

    static bool IsSlicedRight(string assetPath)
    {
        try
        {
            var list = new List<Sprite>();
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
                if (o is Sprite s) list.Add(s);
            if (list.Count != COLS * ROWS) return false;
            list.Sort((a, b) => SpriteNameIndex(a.name).CompareTo(SpriteNameIndex(b.name)));
            var s0 = list[0];
            if (Mathf.Abs(s0.rect.width - CELL) > 0.01f || Mathf.Abs(s0.rect.height - CELL) > 0.01f)
                return false;
            if (Mathf.Abs(s0.pivot.x - CELL / 2f) > 0.5f || Mathf.Abs(s0.pivot.y - CELL / 2f) > 0.5f)
                return false;
            return true;
        }
        catch { return false; }
    }

    static int SpriteNameIndex(string spriteName)
    {
        int p = spriteName.LastIndexOf('_');
        if (p >= 0 && int.TryParse(spriteName.Substring(p + 1), out int idx))
            return idx;
        return 0;
    }

    static int CountSprites(string assetPath)
    {
        int n = 0;
        try
        {
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
                if (o is Sprite)
                    n++;
        }
        catch { }
        return n;
    }

    static void ReadPngSize(string assetPath, out int w, out int h)
    {
        w = 0; h = 0;
        try
        {
            using (var fs = new FileStream(assetPath, FileMode.Open, FileAccess.Read))
            {
                if (fs.Length < 24) return;
                var buf = new byte[24];
                fs.Read(buf, 0, 24);
                w = (buf[16] << 24) | (buf[17] << 16) | (buf[18] << 8) | buf[19];
                h = (buf[20] << 24) | (buf[21] << 16) | (buf[22] << 8) | buf[23];
            }
        }
        catch { }
    }
}
