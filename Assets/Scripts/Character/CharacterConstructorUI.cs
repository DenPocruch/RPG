using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using TMPro;
using System;
using System.Collections.Generic;

/// <summary>
/// UI конструктора персонажа. Панель собрана руками в SampleScene,
/// ВСЕ ссылки — перетаскиванием в инспекторе (как ShopUI):
/// скрипт висит где угодно (хоть на самой панели), поля:
/// panel, previewImage, openButton, btnRandom, btnDone, btnClose,
/// botPrefab, rows[7] (в каждом: category, allowNone, row, btnLeft, btnRight, value).
/// Превью: отдельный бот через свою камеру в RenderTexture (мир не трогается).
/// Сейв: ISaveable "appearance" (применяется к превью; на игрока — шагом переноса визуала).
/// </summary>
public class CharacterConstructorUI : MonoBehaviour, ISaveable
{
    const int PREVIEW_LAYER = 9;
    const int RT_SIZE = 256;
    static readonly Vector3 RIG_POS = new Vector3(1000f, 1000f, 0f);

    [Header("Корень панели (перетащить ConstructorPanel)")]
    public GameObject panel;
    [Header("Превью")]
    public RawImage previewImage;
    [Header("Кнопки")]
    public Button openButton;
    public Button btnRandom;
    public Button btnDone;
    public Button btnClose;
    [Header("Префаб бота для превью (Assets/Prefab/ConstructorBot)")]
    public GameObject botPrefab;

    [Serializable]
    public class RowRefs
    {
        public string category = "Skins";
        public bool allowNone = true;
        public Transform row;
        public Button btnLeft;
        public Button btnRight;
        public TMP_Text value;
    }

    [Header("Ряды (категории уже вписаны — перетащить ссылки)")]
    public RowRefs[] rowsConfig = new RowRefs[]
    {
        new RowRefs { category = "Skins", allowNone = false },
        new RowRefs { category = "Eyes", allowNone = true },
        new RowRefs { category = "Clothers", allowNone = true },
        new RowRefs { category = "Hair's", allowNone = true },
        new RowRefs { category = "Beard", allowNone = true },
        new RowRefs { category = "Elf", allowNone = true },
        new RowRefs { category = "Acc", allowNone = true },
    };

    class RowUI
    {
        public RowRefs cfg;
        public List<string> options = new List<string>();
        public List<string> display = new List<string>();
        public int index;
    }

    // ISaveable
    public string SaveKey => "appearance";
    [Serializable] class Entry { public string key; public string value; }
    [Serializable] class Blob { public List<Entry> entries = new List<Entry>(); }

    /// <summary>Сохранённый выбор внешности (копия). Смена выбора — событие OnAppearanceSaved.</summary>
    public Dictionary<string, string> GetSavedSelection()
    {
        return new Dictionary<string, string>(savedSelection, StringComparer.OrdinalIgnoreCase);
    }
    public static event Action OnAppearanceSaved;

    readonly List<RowUI> rows = new List<RowUI>();
    readonly Dictionary<string, string> savedSelection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    static GameObject botObj;
    static CharacterVisual botVisual;
    // Риг общий (static): переживает сцены и защищает от дублей,
    // если скриптов конструктора вдруг окажется два
    static GameObject rigRoot;
    static Camera previewCam;
    static RenderTexture rt;
    PlayerMovement lockedMovement;

    public string CaptureState()
    {
        var b = new Blob();
        foreach (var kv in savedSelection)
            b.entries.Add(new Entry { key = kv.Key, value = kv.Value });
        return JsonUtility.ToJson(b);
    }

    public void RestoreState(string json)
    {
        savedSelection.Clear();
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var b = JsonUtility.FromJson<Blob>(json);
            if (b != null && b.entries != null)
                foreach (var e in b.entries)
                    savedSelection[e.key] = e.value ?? "";
        }
        catch (Exception e) { Debug.LogWarning("[ConstructorUI] Битый сейв внешности: " + e.Message); }
    }

    void Awake()
    {
        if (panel == null) panel = gameObject;
        if (SaveManager.Instance != null)
            SaveManager.Instance.Register(this);
        else
            Debug.LogWarning("[ConstructorUI] Нет SaveManager — сейв внешности не будет работать");

        if (openButton != null) openButton.onClick.AddListener(Open);
        else Debug.LogWarning("[ConstructorUI] Не назначена кнопка открытия (openButton)");
        if (btnRandom != null) btnRandom.onClick.AddListener(RandomizeAll);
        if (btnDone != null) btnDone.onClick.AddListener(() => Close(true));
        if (btnClose != null) btnClose.onClick.AddListener(() => Close(false));
        if (previewImage == null) Debug.LogWarning("[ConstructorUI] Не назначен previewImage");
        if (botPrefab == null) Debug.LogWarning("[ConstructorUI] Не назначен botPrefab");

        foreach (var cfg in rowsConfig)
        {
            var r = new RowUI { cfg = cfg };
            if (cfg == null || cfg.row == null)
            {
                Debug.LogWarning("[ConstructorUI] Пустой ряд в rowsConfig");
                rows.Add(r);
                continue;
            }
            if (cfg.btnLeft != null) cfg.btnLeft.onClick.AddListener(() => StepRow(r, -1));
            if (cfg.btnRight != null) cfg.btnRight.onClick.AddListener(() => StepRow(r, 1));
            if (cfg.value == null) Debug.LogWarning("[ConstructorUI] Нет value в ряде " + cfg.category);
            rows.Add(r);
        }
        // Прогрев заранее (панель в сцене АКТИВНА — Awake отрабатывает на загрузке игры):
        // риг, бот и база грузятся сразу, первое открытие панели — мгновенное
        EnsureRig();
        SpawnBot();
        panel.SetActive(false);
    }

    void Start()
    {
        if (SaveManager.Instance != null)
            SaveManager.Instance.LoadInto(this);
    }

    // ── Открытие/закрытие ─────────────────────────────────────────
    public void Open()
    {
        if (botPrefab == null)
        {
            Debug.LogError("[ConstructorUI] Не назначен Bot Prefab");
            return;
        }
        EnsureRig();
        if (botVisual == null) SpawnBot();
        if (botVisual == null) return;
        botVisual.Play("1. Idle");
        ApplySavedToBot();
        RebuildRows();
        Debug.Log("[ConstructorPreview] " + botVisual.GetDebugInfo());
        if (previewImage != null) previewImage.texture = rt;

        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            lockedMovement = player.GetComponent<PlayerMovement>();
            if (lockedMovement != null) lockedMovement.enabled = false;
        }
        panel.SetActive(true);
    }

    public void Close(bool save)
    {
        if (save && botVisual != null)
        {
            SaveBotSelection();
            if (SaveManager.Instance != null) SaveManager.Instance.Save();
            if (OnAppearanceSaved != null) OnAppearanceSaved.Invoke();
        }
        // Бота НЕ удаляем — следующее открытие мгновенное
        if (lockedMovement != null) { lockedMovement.enabled = true; lockedMovement = null; }
        panel.SetActive(false);
    }

    void SaveBotSelection()
    {
        savedSelection.Clear();
        if (botVisual == null) return;
        foreach (var r in rows)
            if (!string.IsNullOrEmpty(r.cfg.category))
                savedSelection[r.cfg.category] = CurrentValue(r);
    }

    // ── Ряды ──────────────────────────────────────────────────────
    CharacterDatabase Database =>
        (botVisual != null && botVisual.database != null) ? botVisual.database : null;

    void RebuildRows()
    {
        foreach (var r in rows)
        {
            r.options.Clear();
            if (r.cfg.allowNone) r.options.Add("");
            var db = Database;
            if (db != null && !string.IsNullOrEmpty(r.cfg.category))
            {
                // Дубли-очепятки ("Santa Hat"/"santa hat") сливаем без учёта регистра
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var a in db.actions)
                {
                    var cat = a.FindCategory(r.cfg.category);
                    if (cat == null) continue;
                    foreach (var v in cat.variants)
                        if (names.Add(v.variantName)) r.options.Add(v.variantName);
                }
                r.options.Sort(StringComparer.Ordinal);
                if (r.cfg.allowNone)
                {
                    r.options.Remove("");
                    r.options.Insert(0, "");
                }
            }
            string cur = botVisual != null ? botVisual.GetVariant(r.cfg.category) : "";
            r.index = Math.Max(0, r.options.FindIndex(o => string.Equals(o, cur, StringComparison.OrdinalIgnoreCase)));
            // Короткие имена collisions (Male/Brown vs Female/Brown) — таким показываем полные
            r.display.Clear();
            var prettyCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var o in r.options)
            {
                string p = Pretty(o);
                prettyCount[p] = prettyCount.ContainsKey(p) ? prettyCount[p] + 1 : 1;
            }
            foreach (var o in r.options)
                r.display.Add(prettyCount[Pretty(o)] > 1 ? o : Pretty(o));
            RefreshRow(r);
        }
    }

    void StepRow(RowUI r, int delta)
    {
        if (r.options.Count == 0 || botVisual == null) return;
        r.index = (r.index + delta % r.options.Count + r.options.Count) % r.options.Count;
        ApplyRow(r);
    }

    void ApplyRow(RowUI r)
    {
        string v = CurrentValue(r);
        botVisual.SetVariant(r.cfg.category, v);
        RefreshRow(r);
    }

    string CurrentValue(RowUI r) => r.options.Count > 0 ? r.options[r.index] : "";

    void RefreshRow(RowUI r)
    {
        if (r.cfg.value != null)
            r.cfg.value.text = (r.display.Count > r.index) ? r.display[r.index] : Pretty(CurrentValue(r));
    }

    static string Pretty(string variant)
    {
        if (string.IsNullOrEmpty(variant)) return "—";
        int p = variant.LastIndexOf('/');
        return p >= 0 ? variant.Substring(p + 1) : variant;
    }

    void RandomizeAll()
    {
        if (botVisual == null) return;
        foreach (var r in rows)
        {
            if (r.options.Count == 0) continue;
            int from = (r.cfg.allowNone || r.options[0] != "") ? 0 : 1;
            if (from >= r.options.Count) continue;
            r.index = UnityEngine.Random.Range(from, r.options.Count);
            ApplyRow(r);
        }
    }

    void ApplySavedToBot()
    {
        if (botVisual == null) return;
        foreach (var kv in savedSelection)
            botVisual.SetVariant(kv.Key, kv.Value);
    }

    // ── Превью-риг ────────────────────────────────────────────────
    void EnsureRig()
    {
        if (rigRoot != null) { FixPreviewRig(); return; }
        rigRoot = new GameObject("ConstructorPreviewRig");
        rigRoot.transform.position = RIG_POS;
        DontDestroyOnLoad(rigRoot);

        // Свой свет: сценовый Global Light умирает при смене сцены,
        // без него превью — чёрный силуэт. Point (не Global!): URP 2D
        // разрешает только один Global Light на слой и роняет ошибку
        var lightGo = new GameObject("PreviewLight");
        lightGo.transform.SetParent(rigRoot.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1f, 0f);
        var pl = lightGo.AddComponent<Light2D>();
        pl.lightType = Light2D.LightType.Point;
        pl.pointLightOuterRadius = 10f;
        pl.pointLightInnerRadius = 10f;
        pl.intensity = 1f;

        var camGo = new GameObject("PreviewCamera");
        camGo.transform.SetParent(rigRoot.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 1.1f, -10f);
        camGo.layer = PREVIEW_LAYER;
        previewCam = camGo.AddComponent<Camera>();
        previewCam.orthographic = true;
        previewCam.orthographicSize = 1.4f;
        previewCam.clearFlags = CameraClearFlags.SolidColor;
        previewCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        previewCam.cullingMask = 1 << PREVIEW_LAYER;
        rt = new RenderTexture(RT_SIZE, RT_SIZE, 24);
        previewCam.targetTexture = rt;
        FixPreviewRig();
    }

    /// <summary>
    /// Чинит уже созданный риг (в т.ч. старый, из прошлой версии кода):
    /// свет обязан быть на слое превью и Additive, иначе камера его не видит и бот чёрный.
    /// </summary>
    void FixPreviewRig()
    {
        if (rigRoot == null) return;
        var lightTr = rigRoot.transform.Find("PreviewLight");
        if (lightTr != null)
        {
            // Камера превью видит ТОЛЬКО слой 9 — свет на Default она игнорирует,
            // спрайты Lit остаются без света = чёрный силуэт
            lightTr.gameObject.layer = PREVIEW_LAYER;
            var pl = lightTr.GetComponent<Light2D>();
            if (pl != null)
            {
                pl.enabled = true;
                pl.lightType = Light2D.LightType.Point;
                pl.pointLightInnerRadius = 10f;
                pl.pointLightOuterRadius = 10f;
                pl.intensity = 1f;
                pl.color = Color.white;
                pl.blendStyleIndex = 1; // 0=Multiply, 1=Additive (Renderer2D.asset)
            }
        }
        var camTr = rigRoot.transform.Find("PreviewCamera");
        if (camTr != null)
        {
            var cam = camTr.GetComponent<Camera>();
            if (cam != null)
            {
                cam.enabled = true;
                cam.cullingMask = 1 << PREVIEW_LAYER;
                if (previewCam == null) previewCam = cam;
            }
            if (rt == null)
            {
                rt = new RenderTexture(RT_SIZE, RT_SIZE, 24);
                if (previewCam != null) previewCam.targetTexture = rt;
            }
        }
    }

    void SpawnBot()
    {
        ClearBot();
        if (botPrefab == null || rigRoot == null) return;
        botObj = Instantiate(botPrefab, RIG_POS, Quaternion.identity, rigRoot.transform);
        SetLayerRecursive(botObj, PREVIEW_LAYER);
        ApplyPreviewUnlit(botObj);
        var driver = botObj.GetComponent<ConstructorBotDriver>();
        if (driver != null) Destroy(driver);
        botVisual = botObj.GetComponent<CharacterVisual>();
        if (botVisual == null)
            Debug.LogError("[ConstructorUI] В префабе нет CharacterVisual");
    }

    // Превью не зависит от света: unlit-материал показывает истинные цвета спрайтов.
    // С Lit-материалом было то чёрное (свет не доставал), то белое (Additive-пересвет).
    static Material previewUnlitMat;

    static void ApplyPreviewUnlit(GameObject root)
    {
        if (previewUnlitMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (sh == null)
            {
                Debug.LogWarning("[ConstructorUI] Нет unlit-шейдера для превью");
                return;
            }
            previewUnlitMat = new Material(sh);
        }
        foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
            sr.sharedMaterial = previewUnlitMat;
    }

    void ClearBot()
    {
        if (botObj != null) { Destroy(botObj); botObj = null; botVisual = null; }
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursive(t.gameObject, layer);
    }
}
