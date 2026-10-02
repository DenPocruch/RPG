using UnityEngine;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Генератор префабов интерьерной мебели для домов NPC.
/// Нарезку НЕ трогает (пользователь режет вручную) — правит только пивоты
/// на BottomCenter, rect'ы не меняет.
/// Пропускает: Beds (уже в сцене), Chairs/Sofa (уже собраны),
/// Doors-windows-curtains (настенное, другим методом позже).
/// Каждый префаб: SpriteRenderer + BoxCollider2D (низ) + YSort.
/// Особые: Candles/Part11 — без коллайдера (настол/напольное); Basketball — пол
/// (без коллайдера и YSort, порядок -10); свечи чуть выше (+15 к порядку).
/// Запуск: Tools → Interior → Build Furniture Prefabs (Play выключен!).
/// Повторный прогон сносит папки целей и собирает начисто.
/// Коврики: коллайдер ставится всем подряд — какие мешают, скажешь имена
/// префабов, сниму точечно.
/// </summary>
public static class InteriorFurnitureBuilder
{
    struct Target
    {
        public string texPath;
        public string folder;
        public float bottomFrac;
        public bool noCollider;
        public int yOffset;
        public bool floor;
        public int minPx;
        public Target(string texPath, string folder, float bottomFrac,
            bool noCollider = false, int yOffset = 0, bool floor = false, int minPx = 10)
        {
            this.texPath = texPath;
            this.folder = folder;
            this.bottomFrac = bottomFrac;
            this.noCollider = noCollider;
            this.yOffset = yOffset;
            this.floor = floor;
            this.minPx = minPx;
        }
    }

    static readonly Target[] TARGETS = new Target[]
    {
        // Пересборка после ручной нарезки
        new Target("Assets/Art/Objects/Interior/Tables and desks.png", "Tables", 0.5f),
        new Target("Assets/Art/Objects/Interior/Closet.png", "Closet", 0.3f),
        new Target("Assets/Art/Objects/Interior/Dressers.png", "Dressers", 0.6f),
        new Target("Assets/Art/Objects/Interior/Fireplace.png", "Fireplace", 0.4f),
        // Новое из папки
        new Target("Assets/Art/Objects/Interior/basketball.png", "Basketball", 0.5f, floor: true, minPx: 8),
        new Target("Assets/Art/Objects/Interior/Blacksmith.png", "Blacksmith", 0.5f),
        new Target("Assets/Art/Objects/Interior/candle.png", "Candles", 0.5f, noCollider: true, yOffset: 15, minPx: 6),
        new Target("Assets/Art/Objects/Interior/Candle 1.png", "Candles", 0.5f, noCollider: true, yOffset: 15, minPx: 6),
        new Target("Assets/Art/Objects/Interior/Candle 2.png", "Candles", 0.5f, noCollider: true, yOffset: 15, minPx: 6),
        new Target("Assets/Art/Objects/Interior/Candle 3.png", "Candles", 0.5f, noCollider: true, yOffset: 15, minPx: 6),
        new Target("Assets/Art/Objects/Interior/candle 4.png", "Candles", 0.5f, noCollider: true, yOffset: 15, minPx: 6),
        new Target("Assets/Art/Objects/Interior/Candle 5.png", "Candles", 0.5f, noCollider: true, yOffset: 15, minPx: 6),
        new Target("Assets/Art/Objects/Interior/Candle 6.png", "Candles", 0.5f, noCollider: true, yOffset: 15, minPx: 6),
        new Target("Assets/Art/Objects/Interior/cats furniture.png", "Cats", 0.5f),
        new Target("Assets/Art/Objects/Interior/hospital wing.png", "Hospital", 0.5f),
        new Target("Assets/Art/Objects/Interior/Others.png", "Others", 0.5f),
        new Target("Assets/Art/Objects/Interior/Part 1 copiar.png", "Part1", 0.5f),
        new Target("Assets/Art/Objects/Interior/Part 2 copiar.png", "Part2", 0.5f),
        new Target("Assets/Art/Objects/Interior/Part 9 copiar.png", "Part9", 0.5f),
        new Target("Assets/Art/Objects/Interior/Part 10 copiar.png", "Part10", 0.5f),
        new Target("Assets/Art/Objects/Interior/Part 11 copiar.png", "Part11", 0.5f, noCollider: true, yOffset: 1),
        new Target("Assets/Art/Objects/Interior/School.png", "School", 0.5f),
        new Target("Assets/Art/Objects/Interior/Temple.png", "Temple", 0.5f, minPx: 8),
        new Target("Assets/Art/Objects/Interior/Xmas.png", "Xmas", 0.5f),
    };

    const float WIDTH_FACTOR = 0.85f;

    [MenuItem("Tools/Interior/Build Furniture Prefabs")]
    public static void Build()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[Interior] Выключи Play-режим перед генерацией префабов.");
            return;
        }

        FixPivotsOnly(); // только пивоты BottomCenter, rect'ы НЕ трогаем (ручная нарезка!)

        int made = 0, skipped = 0;
        var perFolder = new Dictionary<string, int>();
        var doneFolders = new HashSet<string>();

        foreach (var t in TARGETS)
        {
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(t.texPath)
                .OfType<Sprite>()
                .OrderBy(s => s.name, System.StringComparer.Ordinal)
                .ToArray();

            if (sprites.Length == 0)
            {
                Debug.LogWarning("[Interior] Нет спрайтов: " + t.texPath);
                continue;
            }

            string folder = "Assets/Prefab/Interior/" + t.folder;
            // Папку чистим один раз (несколько листов льют в Candles!)
            if (!doneFolders.Contains(folder))
            {
                if (AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
                EnsureFolder(folder);
                doneFolders.Add(folder);
            }

            foreach (var s in sprites)
            {
                if (s.rect.width < t.minPx || s.rect.height < t.minPx)
                {
                    skipped++;
                    continue;
                }

                string prefabPath = folder + "/" + s.name + ".prefab";
                GameObject root = new GameObject(s.name);

                var sr = root.AddComponent<SpriteRenderer>();
                sr.sprite = s;

                if (t.floor)
                {
                    sr.sortingOrder = -10; // пол: всегда под игроком, без YSort
                }
                else
                {
                    sr.sortingOrder = 0;
                    var ys = root.AddComponent<YSort>();
                    ys.sortingOffset = t.yOffset;
                }

                if (!t.noCollider && !t.floor)
                {
                    // Коллайдер только на низ: верх перекрывает игрока (YSort разрулит).
                    // Настенное висит высоко — его коллайдер игроку недоступен, не мешает.
                    Vector2 full = s.bounds.size;
                    Vector2 min = s.bounds.min;
                    float colH = Mathf.Max(0.15f, full.y * t.bottomFrac);
                    float colW = Mathf.Max(0.15f, full.x * WIDTH_FACTOR);

                    var col = root.AddComponent<BoxCollider2D>();
                    col.isTrigger = false;
                    col.size = new Vector2(colW, colH);
                    col.offset = new Vector2(min.x + full.x * 0.5f, min.y + colH * 0.5f);
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Object.DestroyImmediate(root);
                made++;
                if (!perFolder.ContainsKey(t.folder)) perFolder[t.folder] = 0;
                perFolder[t.folder]++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string report = string.Join(", ", perFolder.OrderBy(kv => kv.Key).Select(kv => kv.Key + ":" + kv.Value));
        Debug.Log($"[Interior] Готово: префабов {made}, пропущено мелочи {skipped}. {report}");
        EditorUtility.DisplayDialog("Interior Furniture",
            $"Готово: префабов {made}\nПропущено мелочи: {skipped}\n\n{report}\n\nBeds/Chairs/Sofa/двери-шторы не тронуты.",
            "OK");
    }

    /// <summary>
    /// Только пивоты BottomCenter (0.5, 0). Rect'ы НЕ меняет — нарезка ручная!
    /// </summary>
    static void FixPivotsOnly()
    {
        AssetDatabase.DisallowAutoRefresh();
        try
        {
            foreach (var t in TARGETS)
            {
                var importer = AssetImporter.GetAtPath(t.texPath) as TextureImporter;
                if (importer == null) continue;

                var factories = new SpriteDataProviderFactories();
                factories.Init();
                var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
                if (provider == null) continue;
                provider.InitSpriteEditorDataProvider();

                SpriteRect[] rects = provider.GetSpriteRects();
                bool dirty = false;
                for (int i = 0; i < rects.Length; i++)
                {
                    if (rects[i].alignment != SpriteAlignment.BottomCenter)
                    {
                        rects[i].alignment = SpriteAlignment.BottomCenter;
                        rects[i].pivot = new Vector2(0.5f, 0f);
                        dirty = true;
                    }
                }
                if (dirty)
                {
                    provider.SetSpriteRects(rects);
                    provider.Apply();
                    AssetDatabase.ImportAsset(t.texPath, ImportAssetOptions.ForceUpdate);
                    Debug.Log("[Interior] Пивоты BottomCenter: " + t.texPath);
                }
            }
        }
        finally
        {
            AssetDatabase.AllowAutoRefresh();
        }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
