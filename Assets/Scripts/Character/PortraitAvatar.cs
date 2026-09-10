using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Портрет-аватар (uGUI): стопка Image поверх друг друга, спрайты из PortraitDatabase.
/// Герой: autoFromConstructor=true — берёт выбор из сейва конструктора и сам
/// обновляется по OnAppearanceSaved. NPC: autoFromConstructor=false + ручные
/// варианты в инспекторе (имена как в конструкторе: кожа "1", глаза "Male/Brown"...).
/// Слои: Skins → Clothers → Eyes → Hair → Beard → Elf → Acc.
/// </summary>
public class PortraitAvatar : MonoBehaviour
{
    const string DB_RESOURCE_PATH = "Portrait/PortraitDatabase";

    static readonly string[] LAYERS = { "Skins", "Clothers", "Eyes", "Hair", "Beard", "Elf", "Acc" };

    [Header("Герой: брать внешность из конструктора (иначе ручные поля ниже)")]
    public bool autoFromConstructor = true;

    [Header("Строка эмоций (0..2) и кадр (0..4) сетки 5x3")]
    [Range(0, 2)] public int emotionRow = 0;
    [Range(0, 4)] public int frameCol = 0;

    [Header("Отзеркалить по горизонтали")]
    public bool mirror = false;

    [Header("Ручные варианты (NPC): имена как в конструкторе")]
    public string skin = "1";
    public string eyes = "";
    public string hair = "";
    public string clothes = "";
    public string beard = "";
    public string elf = "";
    public string acc = "";

    public PortraitDatabase database;

    readonly Dictionary<string, Image> layers = new Dictionary<string, Image>(System.StringComparer.OrdinalIgnoreCase);

    void Awake()
    {
        if (database == null)
        {
            database = Resources.Load<PortraitDatabase>(DB_RESOURCE_PATH);
            if (database == null)
                Debug.LogError("[PortraitAvatar] Нет базы " + DB_RESOURCE_PATH + " — прогони Tools → Character → 5.");
        }
        RebuildLayers();
    }

    void OnEnable()
    {
        if (autoFromConstructor)
        {
            ApplyFromConstructor();
            CharacterConstructorUI.OnAppearanceSaved += ApplyFromConstructor;
            // Подстраховка от гонки стартов: конструктор мог ещё не загрузить сейв —
            // одна повторная попытка через полсекунды (только если выбор пуст)
            CancelInvoke(nameof(RetryIfEmpty));
            Invoke(nameof(RetryIfEmpty), 0.5f);
        }
        else ApplyManual();
    }

    void OnDisable()
    {
        if (autoFromConstructor)
            CharacterConstructorUI.OnAppearanceSaved -= ApplyFromConstructor;
        CancelInvoke(nameof(RetryIfEmpty));
    }

    void RetryIfEmpty()
    {
        var ui = FindFirstObjectByType<CharacterConstructorUI>();
        if (ui != null && ui.GetSavedSelection().Count > 0)
            ApplyFromConstructor();
    }

    void RebuildLayers()
    {
        foreach (Transform child in transform)
            if (child.name.StartsWith("P_"))
                Destroy(child.gameObject);
        layers.Clear();
        foreach (string layer in LAYERS)
        {
            var go = new GameObject("P_" + layer);
            go.transform.SetParent(transform, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.preserveAspect = true;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            layers[layer] = img;
        }
        ApplyMirror();
    }

    void OnValidate()
    {
        ApplyMirror();
    }

    void ApplyMirror()
    {
        foreach (var kv in layers)
        {
            if (kv.Value == null) continue;
            var rt = kv.Value.rectTransform;
            var s = rt.localScale;
            s.x = mirror ? -Mathf.Abs(s.x == 0 ? 1 : s.x) : Mathf.Abs(s.x == 0 ? 1 : s.x);
            rt.localScale = s;
        }
    }

    /// <summary>Герой: внешность из сейва конструктора.</summary>
    public void ApplyFromConstructor()
    {
        var ui = FindFirstObjectByType<CharacterConstructorUI>();
        Dictionary<string, string> sel = ui != null ? ui.GetSavedSelection() : null;
        if (sel == null || sel.Count == 0)
        {
            // Сейва ещё нет (первый запуск): показываем дефолт префаба бота
            ApplyManual();
            return;
        }
        SetLayer("Skins", MapSkin(Get(sel, "Skins"), ui.GetGender()));
        SetLayer("Clothers", MapClothes(Get(sel, "Clothers"), ui.GetGender()));
        SetLayer("Eyes", MapEyes(Get(sel, "Eyes")));
        SetLayer("Hair", Get(sel, "Hair's"));
        SetLayer("Beard", Get(sel, "Beard"));
        SetLayer("Elf", Get(sel, "Elf"));
        SetLayer("Acc", Get(sel, "Acc"));
    }

    /// <summary>NPC/дефолт: ручные поля инспектора.</summary>
    public void ApplyManual()
    {
        SetLayer("Skins", MapSkin(skin));
        SetLayer("Clothers", MapClothes(clothes));
        SetLayer("Eyes", MapEyes(eyes));
        SetLayer("Hair", hair);
        SetLayer("Beard", beard);
        SetLayer("Elf", elf);
        SetLayer("Acc", acc);
    }

    static string Get(Dictionary<string, string> sel, string key)
    {
        return sel.TryGetValue(key, out string v) ? v ?? "" : "";
    }

    // Кожа "1" -> "Male/1" или "Female/1" по полу героя
    static string MapSkin(string v, string gender = "Male")
    {
        if (string.IsNullOrEmpty(v)) return "";
        if (gender != "Male" && gender != "Female") gender = "Male";
        return v.Contains("/") ? v : gender + "/" + v;
    }

    // Одежда "Farm/Blue" -> "Male/Blue" или "Female/Blue" по полу героя
    static string MapClothes(string v, string gender = "Male")
    {
        if (string.IsNullOrEmpty(v)) return "";
        if (gender != "Male" && gender != "Female") gender = "Male";
        int p = v.LastIndexOf('/');
        string color = p >= 0 ? v.Substring(p + 1) : v;
        return gender + "/" + color;
    }

    // Глаза "Male/Brown" -> "Brown" (в портретах один набор глаз)
    static string MapEyes(string v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        int p = v.LastIndexOf('/');
        return p >= 0 ? v.Substring(p + 1) : v;
    }

    void SetLayer(string portraitCat, string variant)
    {
        if (!layers.TryGetValue(portraitCat, out Image img) || img == null) return;
        Sprite s = null;
        if (!string.IsNullOrEmpty(variant) && database != null)
        {
            var cat = database.FindCategory(portraitCat);
            var v = cat != null ? cat.FindVariant(variant) : null;
            if (v != null && v.frames != null && v.frames.Length > 0)
            {
                int idx = Mathf.Clamp(emotionRow * 5 + frameCol, 0, v.frames.Length - 1);
                s = v.frames[idx];
            }
        }
        img.sprite = s;
        img.enabled = s != null;
    }
}
