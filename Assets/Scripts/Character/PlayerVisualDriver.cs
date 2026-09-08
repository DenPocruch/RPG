using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Визуал игрока на код-аниматоре (конструктор персонажа).
/// Заменяет вывод Animator'а: тело + слои + оружие из арта.
/// Старый Animator/SpriteRenderer гасятся (откат: включить обратно + снять драйвер).
/// Триггеры геймплея (атаки/инструменты/рыбалка/смерть) зовут методы Play* —
/// старые animator-вызовы рядом оставлены, на выключенном Animator'е они no-op.
/// </summary>
[RequireComponent(typeof(CharacterVisual))]
public class PlayerVisualDriver : MonoBehaviour
{
    static readonly string[] TIER_PREFIX = { "Wood", "Copper", "Iron", "Gold", "Platinum", "Obsidian" };

    const string A_IDLE = "1. Idle";
    const string A_WALK = "2. Walk";
    const string A_RUN = "3. Run";
    const string A_SWORD = "8. SwordAttack";
    const string A_ARCHER = "9. Archer";
    const string A_MAGE = "23. Mage";
    const string A_PICKAXE = "4. Pickaxe, Hoe and Catching insects";
    const string A_AXE = "5. Axe and Sickle";
    const string A_SHOVEL = "6. Shovel";
    const string A_WATER = "7. Watering";
    const string A_DAMAGE = "10. Damage";
    const string A_DEATH = "11. Death";
    const string A_CAST = "12. Fishing - Cast";
    const string A_WAIT = "12.1. Fishing - Wait";
    const string A_BITE = "12.2. Fishing - Bite";
    const string A_REEL = "12.3. Fishing - Reel";
    const string A_CATCH = "12.4. Fishing - Catch";

    CharacterVisual visual;
    PlayerMovement movement;
    Rigidbody2D rb;
    [Header("Сдвиг визуала (подгонка под старый спрайт; подобрано: -0.03,-0.36)")]
    public Vector2 visualOffset = new Vector2(-0.03f, -0.36f);
    [Header("Играть бег вместо ходьбы (движение нормализовано — длина всегда 1)")]
    public bool useRunAnim = false;
    bool busy;      // идёт one-shot (кроме смерти)
    bool dead;
    bool ysortDynamic;
    string locoAction = "";

    void Awake()
    {
        visual = GetComponent<CharacterVisual>();
        movement = GetComponent<PlayerMovement>();
        rb = GetComponent<Rigidbody2D>();
    }

    void Start()
    {
        if (visual == null) visual = gameObject.AddComponent<CharacterVisual>();

        // Старый визуал: снять порядок/слой, погасить (откат — включить обратно)
        var oldSr = GetComponent<SpriteRenderer>();
        var oldAnim = GetComponent<Animator>();
        string layerName = "Default";
        int baseOrder = 0;
        if (oldSr != null)
        {
            layerName = oldSr.sortingLayerName;
            baseOrder = oldSr.sortingOrder;
            oldSr.enabled = false;
        }
        if (oldAnim != null) oldAnim.enabled = false;

        // YSort: забираем на себя (иначе поведёт один слой)
        var ysort = GetComponent<YSort>();
        if (ysort != null)
        {
            ysort.enabled = false;
            ysortDynamic = true;
        }
        visual.baseSortingOrder = baseOrder;
        visual.SetSortingLayer(layerName);
        visual.baseOffset = visualOffset;

        // Оверлеи инструментов/оружия/лука — прячем (рисует слой тела)
        var tool = GetComponentInChildren<ToolController>();
        if (tool != null)
        {
            tool.overlayDisabled = true;
            var sr = tool.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
            var an = tool.GetComponent<Animator>();
            if (an != null) an.enabled = false;
        }
        var weapon = GetComponentInChildren<WeaponController>();
        if (weapon != null)
        {
            weapon.overlayDisabled = true;
            var sr = weapon.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
            var an = weapon.GetComponent<Animator>();
            if (an != null) an.enabled = false;
        }
        var bow = movement != null ? movement.bowController : GetComponentInChildren<BowController>();
        if (bow != null)
        {
            bow.overlayDisabled = true;
            var sr = bow.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = false;
        }

        ApplySavedAppearance();
        CharacterConstructorUI.OnAppearanceSaved += ApplySavedAppearance;
        visual.Play(A_IDLE);
    }

    void OnDestroy()
    {
        CharacterConstructorUI.OnAppearanceSaved -= ApplySavedAppearance;
    }

    void Update()
    {
        if (visual == null || movement == null) return;
        if (!movement.enabled || dead) return;           // смерть/лок UI/мини-игра: поза замирает
        if (busy) return;                               // one-shot доигрывает
        if (movement.isAttacking || movement.isFishing) return;

        Vector2 v = rb != null ? rb.linearVelocity : Vector2.zero;
        if (v.sqrMagnitude > 0.01f)
        {
            visual.SetDirectionFromVector(v);
            PlayLocomotion(useRunAnim ? A_RUN : A_WALK);
        }
        else
        {
            PlayLocomotion(A_IDLE);
        }
    }

    void LateUpdate()
    {
        if (ysortDynamic && visual != null)
        {
            // Сортируем по НОГАМ (трансформ выше ступней на visualOffset.y):
            // иначе игрок прячется за объект раньше, чем ноги реально зашли за него
            Vector3 p = transform.position;
            p.y += visualOffset.y;
            visual.SetDynamicOrder(YSort.GetOrder(p));
        }
    }

    void PlayLocomotion(string action)
    {
        if (locoAction == action) return;
        if (visual.Play(action))
            locoAction = action;
    }

    void PlayOnceBusy(string action, System.Action done, float fitDuration)
    {
        busy = true;
        locoAction = "";
        visual.SetDirectionFromVector(movement != null ? movement.FacingDirection : Vector2.down);
        visual.PlayOnce(action, () => { busy = false; if (done != null) done.Invoke(); }, fitDuration);
    }

    // ── Внешность ─────────────────────────────────────────────────
    public void ApplySavedAppearance()
    {
        if (visual == null) return;
        var ui = FindFirstObjectByType<CharacterConstructorUI>();
        if (ui == null) return;
        foreach (var kv in ui.GetSavedSelection())
            visual.SetVariant(kv.Key, kv.Value);
    }

    public void SetTint(Color c)
    {
        if (visual != null) visual.SetTint(c);
    }

    // ── Бой/инструменты ───────────────────────────────────────────
    public void PlayMelee(float duration)
    {
        if (dead) return;
        ItemData active = HotbarManager.Instance != null ? HotbarManager.Instance.GetActiveItem() : null;
        if (active != null && active.itemType == ItemType.Weapon)
            visual.SetVariant("Weapons", "Sword/" + TierFromName(active));
        else
            visual.SetVariant("Weapons", ""); // кулаки/молоток: пустые руки
        PlayOnceBusy(A_SWORD, null, duration);
    }

    public void PlayBow(ItemData bow, float duration)
    {
        if (dead) return;
        // Посоху отдельного арта нет — показывает лук того же тира.
        // Тир берём из выстрелившего предмета, а не из активного слота (слот мог смениться)
        visual.SetVariant("Weapons", "Bow and Arrow/" + TierFromName(bow));
        PlayOnceBusy(A_ARCHER, null, duration);
    }

    public void PlayStaff(float duration)
    {
        if (dead) return;
        // Посох и магия — свой слой действия 23. Mage (Healer Staff + FX рисуются сами,
        // тиров у посоха в арте нет). Слой Weapons в Mage отсутствует — лук не вылезет
        PlayOnceBusy(A_MAGE, null, duration);
    }

    public void PlayTool(ItemType type, int tier, float duration)
    {
        if (dead) return;
        string action = A_PICKAXE;
        string variant = "";
        int t = Mathf.Clamp(tier, 1, 10);
        switch (type)
        {
            case ItemType.Pickaxe: action = A_PICKAXE; variant = "Pickaxe/" + t; break;
            case ItemType.Hoe: action = A_PICKAXE; variant = "Hoe/" + t; break;
            case ItemType.BugNet: action = A_PICKAXE; variant = "Bug net"; break;
            case ItemType.WateringCan: action = A_WATER; variant = "Watering/" + t; break;
            case ItemType.Axe: action = A_AXE; variant = "Axe/" + t; break;
            case ItemType.Sickle: action = A_AXE; variant = "Sickle/" + t; break;
            // Лопаты предметом в игре нет (только арт 6. Shovel) — упадёт в default
            default: action = A_PICKAXE; variant = ""; break; // молоток/кулаки/прочее: пустые руки
        }
        visual.SetVariant("Weapons", variant);
        PlayOnceBusy(action, null, duration);
    }

    // ── Рыбалка ──────────────────────────────────────────────────
    public void PlayFish(string trigger)
    {
        if (dead) return;
        ItemData rod = HotbarManager.Instance != null ? HotbarManager.Instance.GetActiveItem() : null;
        if (rod != null && rod.itemType == ItemType.FishingRod)
            visual.SetVariant("Weapons", TierFromName(rod).ToString());
        switch (trigger)
        {
            case "FishCast":
                busy = true; locoAction = "";
                visual.SetDirectionFromVector(movement != null ? movement.FacingDirection : Vector2.down);
                visual.PlayOnce(A_CAST, () => { busy = false; visual.Play(A_WAIT); }, 0f);
                break;
            case "FishBite":
                busy = true; locoAction = "";
                visual.PlayOnce(A_BITE); // держит позу до подсечки/срыва
                break;
            case "FishReel":
                busy = false; locoAction = "";
                visual.Play(A_REEL);
                break;
            case "FishCatch":
                busy = true; locoAction = "";
                visual.PlayOnce(A_CATCH, () => { busy = false; visual.Play(A_IDLE); }, 0.9f);
                break;
            default: // FishCancel и прочие — в покой
                busy = false;
                visual.Play(A_IDLE);
                break;
        }
    }

    // ── Урон/смерть ───────────────────────────────────────────────
    public void PlayDamage()
    {
        if (dead) return;
        PlayOnceBusy(A_DAMAGE, null, 0f);
    }

    public void PlayDeath()
    {
        dead = true;
        busy = false;
        locoAction = "";
        visual.SetDirectionFromVector(movement != null ? movement.FacingDirection : Vector2.down);
        visual.PlayOnce(A_DEATH);
    }

    public void Revive()
    {
        dead = false;
        busy = false;
        visual.Play(A_IDLE);
    }

    static int TierFromName(ItemData item)
    {
        if (item == null) return 1;
        // Тир — из ИМЕНИ АССЕТА (CopperRod_Common → 2). itemName — русское
        // отображаемое ("Медная удочка"), по нему тир не вытащить.
        string n = item.name;
        for (int i = 0; i < TIER_PREFIX.Length; i++)
            if (!string.IsNullOrEmpty(n) && n.StartsWith(TIER_PREFIX[i]))
                return i + 1;
        if (item.toolTier >= 1 && item.toolTier <= 10) return item.toolTier;
        return 1;
    }
}
