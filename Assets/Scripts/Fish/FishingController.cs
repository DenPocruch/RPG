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

    [Header("Анимация (триггеры в Frame_0.controller)")]
    [Tooltip("Пауза с удочкой в позе Catch после поимки, потом разблокировка движения")]
    public float catchDuration = 0.9f;

    [Header("Темп ловли")]
    [Tooltip("Пауза перед следующим забросом после поимки (сек)")]
    public float catchCooldown = 1f;

    private FishState state = FishState.Idle;
    private ItemData rod;
    private FishingSpot spot;
    private FishData hooked;
    private float timer;
    private PlayerMovement cachedPM;
    private float hookedWeight; // вес текущей рыбы (ролл на старте боя)
    private float nextCastAllowedAt;
    private GameObject lootItemPrefab; // кэш префаба дропа (как в FarmInteraction)
    // Крючок на момент заброса (слот заблокирован на весь цикл — снапшот надёжен)
    private float castHookMin = 0f;
    private float castHookMax = float.MaxValue;
    private bool castHasHook = false;

    // ── Аниматор игрока (триггеры FishCast/FishWait/FishBite/FishReel/FishCatch/FishCancel) ──
    private static readonly string[] FishTriggers = { "FishCast", "FishBite", "FishReel", "FishCatch", "FishCancel" };

    private Animator PlayerAnim()
    {
        GameObject player = GameObject.FindWithTag("Player");
        return player != null ? player.GetComponent<Animator>() : null;
    }

    private void PlayFish(string trigger)
    {
        Animator a = PlayerAnim();
        if (a == null) return;
        foreach (string t in FishTriggers) a.ResetTrigger(t);
        a.SetTrigger(trigger);
        GameObject player = GameObject.FindWithTag("Player");
        PlayerVisualDriver driver = player != null ? player.GetComponent<PlayerVisualDriver>() : null;
        if (driver != null) driver.PlayFish(trigger);
    }

    private void LockFishing(bool v)
    {
        if (cachedPM == null) cachedPM = FindFirstObjectByType<PlayerMovement>();
        if (cachedPM != null) cachedPM.SetFishingLock(v);
        if (!v) cachedPM = null;
    }

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
        if (Time.time < nextCastAllowedAt) return; // кулдаун после поимки (молча)
        ItemData active = HotbarManager.Instance.GetActiveItem();
        if (active == null || active.itemType != ItemType.FishingRod)
        {
            ActionLogUI.Show("[Рыбалка] Возьми удочку в руки!");
            return;
        }

        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) return;
        PlayerMovement pm = player.GetComponent<PlayerMovement>();

        // Заброс С БЕРЕГА: ищем воду перед игроком (по взгляду), а не под ногами.
        // 1) точки вдоль взгляда, 2) площадь детектора удара, 3) под ногами (старое).
        Vector2 origin = player.transform.position;
        Vector2 face = pm != null ? pm.FacingDirection : Vector2.down;
        FishingSpot s = null;
        float[] dists = { 1.6f, 1.2f, 2.0f, 0.8f, 2.4f };
        foreach (float d in dists)
        {
            s = FishingSpot.SpotAt(origin + face * d);
            if (s != null) break;
        }
        if (s == null && pm != null && pm.ActiveDetector != null)
        {
            InteractionDetector det = pm.ActiveDetector;
            Vector2 center = (Vector2)det.transform.position + det.boxOffset;
            for (int i = 0; i < 10 && s == null; i++)
            {
                Vector2 cand = center + new Vector2(
                    Random.Range(-det.boxSize.x, det.boxSize.x) * 0.5f,
                    Random.Range(-det.boxSize.y, det.boxSize.y) * 0.5f);
                s = FishingSpot.SpotAt(cand);
            }
        }
        if (s == null)
            s = FishingSpot.SpotAt(origin);
        if (s == null)
        {
            ActionLogUI.Show("[Рыбалка] Встань лицом к воде — заброс идёт вперёд!");
            return;
        }

        rod = active;
        spot = s;

        // Крючок: снапшот диапазона на заброс + upfront-проверка точки.
        // Вне диапазона — не клюёт вовсе (поклёвки таких рыб не будет).
        castHookMin = 0f;
        castHookMax = float.MaxValue;
        castHasHook = false;
        if (HookSocketUI.Instance != null)
            castHasHook = HookSocketUI.Instance.GetHookRange(out castHookMin, out castHookMax);
        if (castHasHook && !spot.HasOverlap(castHookMin, castHookMax))
        {
            ActionLogUI.Show("[Рыбалка] На этот крючок здесь не клюёт — нужен другой!");
            return;
        }
        // Прочность: −1 за заброс (0 = сломался, ловим дальше без крючка)
        HookSocketUI.Instance?.UseCast();

        timer = Random.Range(waitMin, waitMax);
        state = FishState.Waiting;
        cachedPM = pm;
        LockFishing(true);
        PlayFish("FishCast");
        ActionLogUI.Show("[Рыбалка] Жди поклёвки… (повторный тап — смотать)");
    }

    void CancelCast()
    {
        state = FishState.Idle;
        PlayFish("FishCancel");
        LockFishing(false);
        ActionLogUI.Show("[Рыбалка] Смотано.");
    }

    void Update()
    {
        // Удочку убрали посреди цикла — сматываем (иначе лок движения зависнет)
        if ((state == FishState.Waiting || state == FishState.Bite) && !HasRodInHands())
        {
            CancelCast();
            return;
        }
        if (state == FishState.Waiting)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                hooked = spot != null ? spot.RollFish(castHookMin, castHookMax) : null;
                if (hooked == null) { CancelCast(); return; }
                state = FishState.Bite;
                timer = biteWindow;
                PlayFish("FishBite");
                BiteFeedback();
                ActionLogUI.Show("[Рыбалка] КЛЮЁТ! Жми!");
            }
        }
        else if (state == FishState.Bite)
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                state = FishState.Idle;
                PlayFish("FishCancel");
                LockFishing(false);
                ActionLogUI.Show("[Рыбалка] Сорвалась…");
            }
        }
    }

    void Hook()
    {
        if (hooked == null) { state = FishState.Idle; PlayFish("FishCancel"); LockFishing(false); return; }

        // Сначала панель: если не открылась — мягкий откат, иначе зависнем
        // в Minigame с замороженным игроком. Вес роллится на старте боя —
        // он влияет и на силу рыбы, и на дроп. С крючком вес всегда внутри
        // его диапазона (пересечение с диапазоном вида).
        hookedWeight = castHasHook
            ? hooked.RollWeightInRange(castHookMin, castHookMax)
            : hooked.RollWeight();
        FishingUI ui = FishingUI.Instance;
        if (ui != null) ui.Open(hooked, rod, hookedWeight);
        if (ui == null || !ui.IsOpen())
        {
            state = FishState.Idle;
            PlayFish("FishCancel");
            LockFishing(false);
            ActionLogUI.Show("[Рыбалка] Сорвалась…");
            return;
        }

        state = FishState.Minigame;
        PlayFish("FishReel"); // поза вываживания на весь бой (состояние FishReel)
        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;
    }

    /// <summary>Сигнал поклёвки: вибрация + звук (клип задаётся в FishingTuning, может быть пусто).</summary>
    private void BiteFeedback()
    {
        FishingTuning t = FishingTuning.Instance;
        if (t != null && t.vibrateOnBite)
        {
            try { Handheld.Vibrate(); } catch { }
        }
        if (t != null && t.biteClip != null)
        {
            Camera cam = Camera.main;
            Vector3 pos = cam != null ? cam.transform.position : Vector3.zero;
            AudioSource.PlayClipAtPoint(t.biteClip, pos);
        }
    }

    /// <summary>Финал мини-игры (зовёт FishingUI). loseText — причина поражения.</summary>
    public void FinishMinigame(bool win, string loseText = null)
    {
        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;

        // Во время мини-игры игрок стоял в позе Bite (движение заморожено локом).
        // Победа — one-shot Catch с задержкой разблокировки, поражение — сразу в Idle.
        if (win) PlayFish("FishCatch");
        else { PlayFish("FishCancel"); LockFishing(false); }

        if (win)
            nextCastAllowedAt = Time.time + catchCooldown;

        if (win && hooked != null)
        {
            // Вес зароллен на старте боя — та же рыба падает рядом лутом.
            // Фолбэк (нет префаба) — сразу в рюкзак с тем же весом.
            float w = hookedWeight > 0f ? hookedWeight : hooked.RollWeight();
            if (!TryDropFish(hooked.fishItem, w)
                && InventoryUI.Instance != null && hooked.fishItem != null)
                InventoryUI.Instance.AddItem(hooked.fishItem, 1, w);

            // Бонус редкости удочки (как bonusYield кирок): доп. рыбы рядом
            int extra = RollBonusFish(rod);
            for (int i = 0; i < extra; i++)
            {
                float ew = castHasHook
                    ? hooked.RollWeightInRange(castHookMin, castHookMax)
                    : hooked.RollWeight();
                if (!TryDropFish(hooked.fishItem, ew)
                    && InventoryUI.Instance != null && hooked.fishItem != null)
                    InventoryUI.Instance.AddItem(hooked.fishItem, 1, ew);
            }

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
            ActionLogUI.Show("[Рыбалка] " + (string.IsNullOrEmpty(loseText) ? "Сорвалась…" : loseText));
        }

        hooked = null;
        hookedWeight = 0f;
        state = FishState.Idle;
        if (win) StartCoroutine(CatchUnlock());
    }

    private System.Collections.IEnumerator CatchUnlock()
    {
        yield return new WaitForSeconds(catchDuration > 0f ? catchDuration : 0.9f);
        LockFishing(false);
    }

    private bool HasRodInHands()
    {
        ItemData active = HotbarManager.Instance != null ? HotbarManager.Instance.GetActiveItem() : null;
        return active != null && active.itemType == ItemType.FishingRod;
    }

    /// <summary>Доп. рыбы за редкость удочки. Семантика как RollBonusDrops кирок:
    /// сотни = гарант, остаток = шанс (150 → +1 и 50% на ещё +1).</summary>
    private int RollBonusFish(ItemData rodData)
    {
        if (rodData == null) return 0;
        int pct = 0;
        FishingTuning t = FishingTuning.Instance;
        int r = (int)rodData.rarity;
        if (t != null && t.bonusFishByRarity != null && r >= 0 && r < t.bonusFishByRarity.Length)
            pct = t.bonusFishByRarity[r];
        if (pct <= 0) pct = rodData.bonusYield; // фолбэк, если проставят как киркам
        if (pct <= 0) return 0;
        int n = pct / 100;
        if (Random.Range(0, 100) < pct % 100) n++;
        return n;
    }

    /// <summary>Дроп пойманной рыбы рядом с игроком (паттерн урожая).
    /// Дистанция 0.8м — за радиусом подбора (0.5), чтобы рыбу было видно.</summary>
    private bool TryDropFish(ItemData fish, float weightKg)
    {
        if (fish == null) return false;
        if (lootItemPrefab == null)
            lootItemPrefab = Resources.Load<GameObject>("LootItemPrefab");
        if (lootItemPrefab == null) return false;

        GameObject player = GameObject.FindWithTag("Player");
        Vector3 basePos = player != null ? player.transform.position : transform.position;
        Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(0.7f, 0.9f);
        if (offset.sqrMagnitude < 0.01f) offset = Vector2.right * 0.8f;

        GameObject obj = Instantiate(lootItemPrefab,
            basePos + new Vector3(offset.x, offset.y, 0), Quaternion.identity);
        LootItem loot = obj.GetComponent<LootItem>();
        if (loot != null)
        {
            loot.itemData = fish;
            loot.amount = 1;
            loot.despawnOverTime = false; // рыба не пропадает
            loot.craftingXpReward = 0;
            loot.farmingXpReward = 0;
            loot.fishWeightKg = weightKg;
        }
        return true;
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
