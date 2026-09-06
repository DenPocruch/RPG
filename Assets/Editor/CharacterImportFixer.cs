using UnityEngine;
using UnityEditor;
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

                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null) { warnings.Add(path + " — не загрузилась текстура"); continue; }
                int w = tex.width, h = tex.height;
                if (w % CELL != 0)
                    warnings.Add($"{path} — ширина {w} не кратна {CELL}, последний кадр обрезан");

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Multiple;
                importer.spritePixelsToUnits = PPU;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.spriteMeshType = SpriteMeshType.FullRect;
                importer.spriteExtrude = 0;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = false;
                importer.sRGBTexture = true;
                importer.alphaIsTransparency = true;

                int cols = Mathf.Max(1, w / CELL);
                string baseName = Path.GetFileNameWithoutExtension(path);
                var sheet = new SpriteMetaData[cols];
                for (int i = 0; i < cols; i++)
                {
                    sheet[i] = new SpriteMetaData
                    {
                        name = baseName + "_" + i,
                        rect = new Rect(i * CELL, 0, CELL, h),
                        alignment = (int)SpriteAlignment.BottomCenter,
                        border = Vector4.zero
                    };
                }
                importer.spritesheet = sheet;
                importer.SaveAndReimport();
                done++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();

        foreach (string warn in warnings)
            Debug.LogWarning("[CharacterImport] " + warn);

        EditorUtility.DisplayDialog("Character Import Fix",
            $"Готово: {done}/{guids.Length}\nПредупреждений: {warnings.Count} (см. Console)",
            "OK");
        Debug.Log($"[CharacterImport] Готово: {done}/{guids.Length}, предупреждений: {warnings.Count}");
    }
}
