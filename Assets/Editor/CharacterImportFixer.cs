using UnityEngine;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Массовый фикс импорта спрайтов персонажа (конструктор):
/// 16 PPU, Point-фильтр, FullRect, пивот BottomCenter, Compression None,
/// нарезка сеткой 32xH (все листы — однорядные стрипы).
/// ВАЖНО: режет FullRect, НЕ Trim — слои конструктора (кожа/одежда/волосы)
/// обязаны иметь одинаковые rect покадрово, иначе будет джиттер слоёв.
/// Повторный прогон безопасен (значения абсолютные, имена кадров детерминированы).
/// </summary>
public static class CharacterImportFixer
{
    const string ROOT = "Assets/Art/Character/Character/PNG";
    const int CELL = 32;
    const int PPU = 16;

    [MenuItem("Tools/Character/1. Fix Import (16 PPU, Point, FullRect)")]
    public static void FixAll()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[CharacterImport] Выключи Play-режим перед правкой импорта.");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { ROOT });
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("[CharacterImport] PNG не найдены в " + ROOT);
            return;
        }

        bool ok = EditorUtility.DisplayDialog("Character Import Fix",
            $"Файлов: {guids.Length}\n\nПоставит всем:\n• 16 PPU\n• Filter Point (no filter)\n• FullRect + пивот BottomCenter\n• Compression None, без мипмапов\n• Нарезка сеткой 32xH (FullRect, НЕ Trim)\n\nЗаймёт несколько минут. Продолжить?",
            "Да, чинить", "Отмена");
        if (!ok) return;

        int done = 0;
        var warnings = new List<string>();
        // Глушим авто-рефреш на время пачки: иначе Directory Monitoring +
        // параллельные воркеры импорта сталкиваются с нашим ImportAsset
        // на записи .meta («Cannot open file ... for write» на случайных файлах)
        AssetDatabase.DisallowAutoRefresh();
        try
        {
            for (int g = 0; g < guids.Length; g++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[g]);
                if (EditorUtility.DisplayCancelableProgressBar("Character Import Fix", path, (float)g / guids.Length))
                {
                    Debug.LogWarning("[CharacterImport] Прервано пользователем. Готово: " + done + "/" + guids.Length);
                    break;
                }

                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                // Сырой размер PNG (импортированный может быть ужатым лимитом Max Size)
                ReadPngSize(path, out int rawW, out int rawH);
                if (rawW <= 0 || rawH <= 0) { warnings.Add(path + " — не прочли размер PNG"); continue; }

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
                // Стрипы удочек — 3840px: лимит 2048 ужал бы их в мыло и порвал сетку
                if (rawW > importer.maxTextureSize)
                    importer.maxTextureSize = 4096;

                // spriteMeshType/spriteExtrude — только через TextureImporterSettings
                // (прямого API на TextureImporter нет, проверено по докам Unity 6)
                var texSettings = new TextureImporterSettings();
                importer.ReadTextureSettings(texSettings);
                texSettings.spriteMeshType = SpriteMeshType.FullRect;
                texSettings.spriteExtrude = 0;
                importer.SetTextureSettings(texSettings);

                // Нарезка по СЫРОМУ размеру PNG (с импортированным совпадает:
                // даунскейла нет, npotScale=None). Один проход — без повторного
                // ImportAsset, иначе параллельные воркеры импорта дерутся за .meta
                // («Cannot open file ... for write»).
                int w = rawW, h = rawH;
                // Ячейка: обычно 32, но оружие рыбалки нарисовано на холсте 64x64
                // (3840/64 = 60 кадров = норма, никакие не двойные)
                int cellW = IsWideCell(path) ? 64 : CELL;
                if (w % cellW != 0)
                    warnings.Add($"{path} — ширина {w} не кратна {cellW}, последний кадр обрезан");

                int cols = Mathf.Max(1, w / cellW);
                // Уже нарезано верно — пропускаем (повторные прогоны чинят только битые)
                if (IsSlicedRight(path, cols, cellW, h)) { done++; continue; }

                string baseName = Path.GetFileNameWithoutExtension(path);
                // Unity 6: нарезка только через ISpriteEditorDataProvider (старый spritesheet obsolete)
                // Удочки: пивот (0.5, 0.25) = 16px от низа (уровень рук), alignment ОБЯЗАН быть
                // Custom — иначе Unity перезапишет пивот пресетом
                bool wideCell = IsWideCell(path);
                var cellAlign = wideCell ? SpriteAlignment.Custom : SpriteAlignment.BottomCenter;
                var cellPivot = wideCell ? new Vector2(0.5f, 0.25f) : new Vector2(0.5f, 0f);
                var rects = new SpriteRect[cols];
                for (int i = 0; i < cols; i++)
                {
                    rects[i] = new SpriteRect
                    {
                        name = baseName + "_" + i,
                        rect = new Rect(i * cellW, 0, cellW, h),
                        alignment = cellAlign,
                        pivot = cellPivot,
                        border = Vector4.zero
                    };
                }
                var factories = new SpriteDataProviderFactories();
                factories.Init();
                bool sliced = false;
                // До 3 попыток: файл может быть transient-залочен (воркеры Unity,
                // антивирус, git-опросы IDE) — повтор лечит
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
                    sliced = CountSprites(path) == cols;
                }
                if (!sliced)
                    warnings.Add($"{path} — не нарезался (есть {CountSprites(path)}, надо {cols})");
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
            Debug.LogWarning("[CharacterImport] " + warn);

        EditorUtility.DisplayDialog("Character Import Fix",
            $"Готово: {done}/{guids.Length}\nПредупреждений: {warnings.Count} (см. Console)",
            "OK");
        Debug.Log($"[CharacterImport] Готово: {done}/{guids.Length}, предупреждений: {warnings.Count}");
    }

    /// <summary>Широкая ячейка 64px: оружие рыбалки (действия 12*) нарисовано 64x64.</summary>
    static bool IsWideCell(string assetPath)
    {
        string p = assetPath.Replace('\\', '/');
        int w = p.IndexOf("/Weapons/", System.StringComparison.OrdinalIgnoreCase);
        if (w < 0) return false;
        int a = p.LastIndexOf('/', w - 1);
        string action = a >= 0 ? p.Substring(a + 1, w - a - 1) : "";
        return action.StartsWith("12");
    }

    /// <summary>Проверка: файл уже нарезан нашей сеткой (число + первый кадр + пивот).</summary>
    static bool IsSlicedRight(string assetPath, int cols, int cellW, int h)
    {
        try
        {
            var list = new List<Sprite>();
            foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath))
                if (o is Sprite s) list.Add(s);
            if (list.Count != cols) return false;
            list.Sort((a, b) => SpriteNameIndex(a.name).CompareTo(SpriteNameIndex(b.name)));
            var s0 = list[0];
            if (Mathf.Abs(s0.rect.width - cellW) > 0.01f || Mathf.Abs(s0.rect.height - h) > 0.01f)
                return false;
            // Пивот в пикселях: тело BottomCenter (cellW/2, 0), удочки Custom (cellW/2, h*0.25)
            bool wide = IsWideCell(assetPath);
            float expX = cellW / 2f, expY = wide ? h * 0.25f : 0f;
            if (Mathf.Abs(s0.pivot.x - expX) > 0.5f || Mathf.Abs(s0.pivot.y - expY) > 0.5f)
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

    /// <summary>Сколько спрайтов реально нарезано в спрайте (проверка результата).</summary>
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

    /// <summary>Сырые габариты PNG из заголовка IHDR (без импорта Unity).</summary>
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
