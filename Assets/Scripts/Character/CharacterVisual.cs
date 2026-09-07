using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Послойный персонаж конструктора: один общий индекс кадра на все слои.
/// Код-аниматор (как EnemyAnimator): действие + направление + FPS.
/// Слои, которых нет в текущем действии (напр. Weapons в Idle), прячутся сами.
/// </summary>
public class CharacterVisual : MonoBehaviour
{
    const string DB_RESOURCE_PATH = "Character/CharacterDatabase";
    const string IDLE_ACTION = "1. Idle";
    const float DIR_DEADZONE = 0.3f;

    [Serializable]
    public class CategoryChoice
    {
        public string category;
        public string variant;
    }

    [Header("База данных (пусто = грузится из Resources)")]
    public CharacterDatabase database;
    [Header("Сортировка")]
    public int baseSortingOrder = 0;
    [Header("Категории с фолбэком: нет выбранного варианта в действии — первый попавшийся")]
    public List<string> fallbackCategories = new List<string> { "Weapons", "Box", "FX" };
    [Header("Сдвиг слоя оружия (удочки нарисованы на холсте 64px против тела 32px)")]
    public Vector2 weaponsLayerOffset = Vector2.zero;
    [System.Serializable]
    public class MountOffset
    {
        public string prefix;
        public Vector2 offset;
    }
    [Header("Маунты: сдвиг всадника по типам (лошадь/велик/медведь)")]
    public List<MountOffset> mountOffsets = new List<MountOffset>
    {
        new MountOffset { prefix = "14.", offset = new Vector2(0f, 1f) },
        new MountOffset { prefix = "15.", offset = Vector2.zero },
        new MountOffset { prefix = "16.", offset = new Vector2(0f, 1f) },
    };
    public List<string> mountCategories = new List<string> { "Horse", "Bicycle", "Bear", "Bed" };
    [Header("Множитель скорости анимации (0 = стоп-кадр)")]
    public float playbackSpeed = 1f;
    [Header("Выбор внешности")]
    public List<CategoryChoice> choices = new List<CategoryChoice>();

    CharacterAction currentAction;
    CharDir dir = CharDir.Down;
    int frame;
    float timer;
    bool loop = true;
    Action onDone;

    readonly Dictionary<string, SpriteRenderer> layers = new Dictionary<string, SpriteRenderer>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> selection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    readonly List<string> orderedCategories = new List<string>();

    public string CurrentActionName => currentAction != null ? currentAction.actionName : "?";
    public CharDir Direction => dir;

    void Awake()
    {
        if (database == null)
        {
            database = Resources.Load<CharacterDatabase>(DB_RESOURCE_PATH);
            if (database == null)
            {
                Debug.LogError("[CharacterVisual] Нет базы " + DB_RESOURCE_PATH + " — прогони Tools → Character → 2.");
                enabled = false;
                return;
            }
        }
        foreach (var c in choices)
            if (!string.IsNullOrEmpty(c.category))
                selection[c.category] = c.variant ?? "";
        EnsureDefaultChoices();
        RebuildLayers();
        if (!Play(IDLE_ACTION) && database.actions.Count > 0)
            Play(database.actions[0].actionName);
    }

    void Update()
    {
        if (currentAction == null) return;
        int count = currentAction.DirCount((int)dir);
        if (count <= 0) return;
        timer += Time.deltaTime * playbackSpeed;
        float interval = 1f / Mathf.Max(1f, currentAction.fps);
        while (timer >= interval)
        {
            timer -= interval;
            if (loop)
            {
                frame = (frame + 1) % count;
            }
            else if (frame + 1 < count)
            {
                frame++;
            }
            else
            {
                var cb = onDone;
                onDone = null;
                if (cb != null) cb.Invoke();
                return;
            }
        }
        Refresh();
    }

    // ── Управление ──────────────────────────────────────────────
    public bool Play(string actionName, bool loopAction = true)
    {
        var a = database.FindAction(actionName);
        if (a == null)
        {
            Debug.LogWarning("[CharacterVisual] Нет действия: " + actionName);
            return false;
        }
        currentAction = a;
        loop = loopAction;
        onDone = null;
        frame = 0;
        timer = 0f;
        Refresh();
        return true;
    }

    public bool PlayOnce(string actionName, Action done = null)
    {
        if (!Play(actionName, false)) return false;
        onDone = done;
        return true;
    }

    public void SetDirection(CharDir d)
    {
        if (dir == d) return;
        dir = d;
        Refresh();
    }

    public void SetDirectionFromVector(Vector2 v)
    {
        if (v.sqrMagnitude < 0.0001f) return;
        if (Mathf.Abs(v.x) > DIR_DEADZONE)
            SetDirection(v.x > 0 ? CharDir.Right : CharDir.Left);
        else
            SetDirection(v.y > 0 ? CharDir.Up : CharDir.Down);
    }

    public void SetVariant(string category, string variant)
    {
        if (string.IsNullOrEmpty(category)) return;
        selection[category] = variant ?? "";
        bool found = false;
        foreach (var c in choices)
            if (string.Equals(c.category, category, StringComparison.OrdinalIgnoreCase))
            {
                c.variant = variant;
                found = true;
                break;
            }
        if (!found)
            choices.Add(new CategoryChoice { category = category, variant = variant });
        Refresh();
    }

    /// <summary>Следующий вариант по кругу (сначала внутри текущего действия). Возвращает имя или null.</summary>
    public string CycleVariant(string category)
    {
        var names = new List<string>();
        if (currentAction != null)
        {
            var cat = currentAction.FindCategory(category);
            if (cat != null)
                foreach (var v in cat.variants)
                    if (!names.Contains(v.variantName))
                        names.Add(v.variantName);
            names.Sort(StringComparer.Ordinal);
        }
        if (names.Count == 0)
            names = CollectVariantNames(category);
        if (names.Count == 0) return null;
        string cur = selection.ContainsKey(category) ? selection[category] : "";
        int idx = names.FindIndex(n => string.Equals(n, cur, StringComparison.OrdinalIgnoreCase));
        string next = names[(idx + 1) % names.Count];
        SetVariant(category, next);
        return next;
    }

    /// <summary>Шаг на +-кадров (для покадрового просмотра). Работает и на паузе.</summary>
    public void StepFrame(int delta)
    {
        if (currentAction == null) return;
        int count = currentAction.DirCount((int)dir);
        if (count <= 0) return;
        frame = (frame + delta % count + count) % count;
        timer = 0f;
        Refresh();
    }

    public string GetStatus()
    {
        if (currentAction == null) return "нет действия";
        int count = currentAction.DirCount((int)dir);
        float fps = Mathf.Max(1f, currentAction.fps) * playbackSpeed;
        return $"{currentAction.actionName} {dir} кадр {frame + 1}/{count} ({fps:0.##} fps)";
    }

    public string GetDebugInfo()
    {
        var sb = new System.Text.StringBuilder(GetStatus());
        foreach (var catName in orderedCategories)
        {
            var sr = layers[catName];
            string sel = selection.ContainsKey(catName) ? selection[catName] : "(нет)";
            sb.Append($" | {catName}={sel}:{(sr.enabled && sr.sprite != null ? sr.sprite.name : "скрыт")}");
        }
        return sb.ToString();
    }

    public List<string> CollectVariantNames(string category)
    {
        var names = new List<string>();
        if (database == null) return names;
        foreach (var a in database.actions)
        {
            var cat = a.FindCategory(category);
            if (cat == null) continue;
            foreach (var v in cat.variants)
                if (!names.Contains(v.variantName))
                    names.Add(v.variantName);
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    // ── Построение ──────────────────────────────────────────────
    bool IsFallback(string category)
    {
        foreach (var f in fallbackCategories)
            if (string.Equals(f, category, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    void EnsureDefaultChoices()
    {
        // Все категории базы (порядок отрисовки), без выбора = первый вариант
        var all = new SortedDictionary<int, List<string>>();
        foreach (var a in database.actions)
            foreach (var cat in a.categories)
            {
                if (!all.ContainsKey(cat.renderOrder))
                    all[cat.renderOrder] = new List<string>();
                if (!all[cat.renderOrder].Contains(cat.categoryName))
                    all[cat.renderOrder].Add(cat.categoryName);
            }
        foreach (var kv in all)
            foreach (var catName in kv.Value)
                if (!selection.ContainsKey(catName) || string.IsNullOrEmpty(selection[catName]))
                {
                    string first = FirstVariantAnywhere(catName);
                    selection[catName] = first ?? "";
                    choices.Add(new CategoryChoice { category = catName, variant = first ?? "" });
                }
    }

    string FirstVariantAnywhere(string category)
    {
        foreach (var a in database.actions)
        {
            var cat = a.FindCategory(category);
            if (cat != null && cat.variants.Count > 0)
                return cat.variants[0].variantName;
        }
        return null;
    }

    void RebuildLayers()
    {
        foreach (Transform child in transform)
            if (child.name.StartsWith("L_"))
                Destroy(child.gameObject);
        layers.Clear();
        orderedCategories.Clear();

        var order = new SortedDictionary<int, List<string>>();
        foreach (var a in database.actions)
            foreach (var cat in a.categories)
            {
                if (!order.ContainsKey(cat.renderOrder))
                    order[cat.renderOrder] = new List<string>();
                bool has = false;
                foreach (var n in order[cat.renderOrder])
                    if (string.Equals(n, cat.categoryName, StringComparison.OrdinalIgnoreCase)) { has = true; break; }
                if (!has) order[cat.renderOrder].Add(cat.categoryName);
            }
        int i = 0;
        foreach (var kv in order)
            foreach (var catName in kv.Value)
            {
                var go = new GameObject("L_" + catName);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sortingOrder = baseSortingOrder + i;
                layers[catName] = sr;
                orderedCategories.Add(catName);
                i++;
            }
    }

    void Refresh()
    {
        if (currentAction == null) return;
        int d = (int)dir;
        int count = currentAction.DirCount(d);
        if (count <= 0) return;
        int offset = currentAction.DirOffset(d);
        int idx = offset + Mathf.Min(frame, count - 1);

        foreach (var catName in orderedCategories)
        {
            var sr = layers[catName];
            var cat = currentAction.FindCategory(catName);
            Sprite s = null;
            if (cat != null && selection.ContainsKey(catName))
            {
                var v = cat.FindVariant(selection[catName]);
                if (v == null && IsFallback(catName) && cat.variants.Count > 0)
                    v = cat.variants[0]; // оружия: нет такого варианта — берём первое
                if (v != null && v.frames != null)
                {
                    int vi = v.doubleFrames ? idx * 2 : idx;
                    if (vi < v.frames.Length)
                        s = v.frames[vi];
                }
            }
            sr.sprite = s;
            sr.enabled = s != null;
            Vector2 off = Vector2.zero;
            if (string.Equals(catName, "Weapons", StringComparison.OrdinalIgnoreCase))
                off += weaponsLayerOffset;
            Vector2 mOff = MountOffsetFor();
            if (mOff != Vector2.zero && !IsMountCategory(catName))
                off += mOff;
            sr.transform.localPosition = off;
        }
    }

    bool IsMountAction()
    {
        return MountOffsetFor() != Vector2.zero;
    }

    Vector2 MountOffsetFor()
    {
        if (currentAction == null) return Vector2.zero;
        foreach (var m in mountOffsets)
            if (!string.IsNullOrEmpty(m.prefix) && currentAction.actionName.StartsWith(m.prefix))
                return m.offset;
        return Vector2.zero;
    }

    bool IsMountCategory(string category)
    {
        foreach (var m in mountCategories)
            if (string.Equals(m, category, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
