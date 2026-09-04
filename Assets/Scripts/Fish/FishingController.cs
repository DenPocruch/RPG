using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Рыбалка: заброс → ожидание → поклёвка (тап) → мини-игра (шкала).
/// Ленивый DontDestroyOnLoad-синглтон (создаётся при первом забросе).
/// Хранит коллекцию (пойманные виды) и флаг подаренной удочки — ключ "fish".
/// </summary>
public class FishingController : MonoBehaviour, ISaveable
{
    public static FishingController Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("FishingController");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<FishingController>();
            }
            return _instance;
        }
    }
    private static FishingController _instance;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        SaveManager.Instance?.Register(this);
    }

    void OnDestroy() { SaveManager.Instance?.Unregister(this); }

    public enum FishState { Idle, Waiting, Bite, Minigame }

    [Header("Поклёвка")]
    public float waitMin = 2f;
    public float waitMax = 8f;
    public float biteWindow = 1.5f;

    [Header("Мини-игра (база, удочка добавляет сверху)")]
    public float baseZoneWidth = 0.22f; // доля шкалы
    public float baseGain = 0.45f;      // прогресс/сек в зоне
    public float baseDecay = 0.3f;      // убыль/сек вне зоны

    private FishState state = FishState.Idle;
    private ItemData rod;
    private FishingSpot spot;
    private FishData hooked;
    private float timer;
    private GameObject bobber;
    private Vector3 bobberBase;
    private float animT;

    // ═══════════════════════════════════════════════════════════
    // ВХОД — зовёт PlayerMovement при ударе с удочкой
    // ═══════════════════════════════════════════════════════════
    public void OnAttackPress()
    {
        switch (state)
        {
            case FishState.Idle:
                TryCast();
                break;
            case FishState.Waiting:
                CancelCast(); // повторный тап — смотать
                break;
            case FishState.Bite:
                Hook();
                break;
            case FishState.Minigame:
                break; // удержание читает сам UI
        }
    }

    public bool IsBusy() => state != FishState.Idle;

    void TryCast()
    {
        if (HotbarManager.Instance == null) return;
        ItemData active = HotbarManager.Instance.GetActiveItem();
        if (active == null || active.itemType != ItemType.FishingRod)
        {
            ActionLogUI.Show("[Рыбалка] Возьми удочку в руки!");
            return;
        }

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;

        FishingSpot s = FishingSpot.SpotAt(player.transform.position);
        if (s == null)
        {
            ActionLogUI.Show("[Рыбалка] Здесь не клюёт — встань ближе к воде!");
            return;
        }

        rod = active;
        spot = s;
        SpawnBobber(player.transform.position);
        timer = Random.Range(waitMin, waitMax);
        state = FishState.Waiting;
        ActionLogUI.Show("[Рыбалка] Жди поклёвки… (повторный тап — смотать)");
    }

    void SpawnBobber(Vector3 from)
    {
        ClearBobber();
        // Точка заброса — случайная рядом в пределах зоны
        Vector2 p = from;
        Collider2D col = spot.GetComponent<Collider2D>();
        if (col != null)
        {
            Bounds b = col.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector2 cand = new Vector2(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.max.y));
                if (Vector2.Distance(cand, from) <= 3.5f && col.OverlapPoint(cand)) { p = cand; break; }
            }
        }
        bobberBase = new Vector3(p.x, p.y, 0);
        bobber = new GameObject("Bobber");
        bobber.transform.position = bobberBase;
        var sr = bobber.AddComponent<SpriteRenderer>();
        sr.sprite = rod != null && rod.worldSprite != null ? rod.worldSprite : rod?.icon;
        sr.color = new Color(1f, 0.5f, 0.5f);
        bobber.transform.localScale = Vector3.one * 0.6f;
    }

    void CancelCast()
    {
        ClearBobber();
        state = FishState.Idle;
        ActionLogUI.Show("[Рыбалка] Смотано.");
    }

    void ClearBobber()
    {
        if (bobber != null) Destroy(bobber);
        bobber = null;
    }

    void Update()
    {
        animT += Time.deltaTime;
        if (state == FishState.Waiting)
        {
            // Поплавок качается на волнах
            if (bobber != null)
                bobber.transform.position = bobberBase + new Vector3(0, Mathf.Sin(animT * 3f) * 0.05f, 0);
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                hooked = spot != null ? spot.RollFish() : null;
                if (hooked == null) { CancelCast(); return; }
                state = FishState.Bite;
                timer = biteWindow;
                ActionLogUI.Show("[Рыбалка] КЛЮЁТ! Жми!");
            }
        }
        else if (state == FishState.Bite)
        {
            // Поплавок дёргается
            if (bobber != null)
                bobber.transform.position = bobberBase + new Vector3(0, -0.12f + Mathf.Sin(animT * 25f) * 0.06f, 0);
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                ClearBobber();
                state = FishState.Idle;
                ActionLogUI.Show("[Рыбалка] Сорвалась…");
            }
        }
    }

    void Hook()
    {
        if (hooked == null) { state = FishState.Idle; return; }
        state = FishState.Minigame;
        ClearBobber();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        float zone = Mathf.Clamp01(baseZoneWidth + (rod != null ? rod.fishingZoneBonus : 0f));
        float gain = baseGain + (rod != null ? rod.fishingSpeedBonus : 0f);
        FishingUI.Instance?.Open(hooked, zone, gain, baseDecay);
    }

    /// <summary>Финал мини-игры (зовёт FishingUI).</summary>
    public void FinishMinigame(bool win)
    {
        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;

        if (win && hooked != null)
        {
            if (InventoryUI.Instance != null && hooked.fishItem != null)
                InventoryUI.Instance.AddItem(hooked.fishItem, 1);

            string id = hooked.name;
            bool first = !caughtIds.Contains(id);
            if (first)
            {
                caughtIds.Add(id);
                int bonus = hooked.price + hooked.firstCatchBonus;
                if (CurrencyManager.Instance != null)
                    CurrencyManager.Instance.AddGold(bonus);
                ActionLogUI.Show("[Рыбалка] " + hooked.fishName + "! Новый вид (+" + bonus + "g)");
            }
            else
            {
                ActionLogUI.Show("[Рыбалка] Поймано: " + hooked.fishName);
            }
            if (SaveManager.Instance != null) SaveManager.Instance.Save();
        }
        else if (!win)
        {
            ActionLogUI.Show("[Рыбалка] Сорвалась…");
        }

        hooked = null;
        state = FishState.Idle;
    }

    // ═══════════════════════════════════════════════════════════
    // КОЛЛЕКЦИЯ + ПОДАРОК (сейв)
    // ═══════════════════════════════════════════════════════════
    private List<string> caughtIds = new List<string>();
    private bool rodGifted = false;

    public bool IsCaught(FishData f) => f != null && caughtIds.Contains(f.name);
    public bool HasRodGift() => rodGifted;

    public void MarkRodGifted()
    {
        rodGifted = true;
        SaveManager.Instance?.Save();
    }

    [System.Serializable] private class FishSave { public List<string> caught = new List<string>(); public bool rodGift; }

    public string SaveKey => "fish";

    public string CaptureState()
    {
        return JsonUtility.ToJson(new FishSave { caught = caughtIds, rodGift = rodGifted });
    }

    public void RestoreState(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        FishSave s = JsonUtility.FromJson<FishSave>(json);
        if (s == null) return;
        caughtIds = s.caught ?? new List<string>();
        rodGifted = s.rodGift;
    }
}
