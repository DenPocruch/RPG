using UnityEngine;
using System;
using System.Collections;

/// <summary>
/// Станок-переработчик (бочка брожения, сырный пресс, маслобойка, джем-мейкер и т.д.).
/// Ставится игроком из хотбара (ghost-режим в PlayerMovement), собирается молотком.
///
/// РЕЦЕПТЫ: список рецептов на скрипте префаба (вход → выход, время, соотношение).
/// Удар с подходящим продуктом в руках = загрузка партии (до batchCapacity порций).
/// Удар по готовому станку = продукт выпадает физическим лутом (LootItemPrefab).
///
/// ИКОНКА ГОТОВНОСТИ: дочерний объект префаба (ReadyIcon со SpriteRenderer) —
/// назначается в инспекторе (поле readyIcon) и ПРАВИТСЯ РУКАМИ (размер/позиция/спрайт),
/// скрипт только включает/выключает его. Если не назначен — найдётся по имени "ReadyIcon".
///
/// ПРОИЗВОДСТВО идёт на РЕАЛЬНОМ времени (DateTime.UtcNow.Ticks) — как у растений,
/// поэтому оффлайн-прогресс работает сам (закинул — вышел — зашёл — готово).
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class CraftMachine : MonoBehaviour, IInteractable
{
    [Serializable]
    public class MachineRecipe
    {
        [Tooltip("Какой продукт принимается (точное имя ассета; звёздные версии не считаются)")]
        public ItemData input;
        [Tooltip("Что выдаётся на выходе")]
        public ItemData output;
        [Tooltip("Сколько единиц входа нужно на 1 единицу выхода")]
        public int inputPerOutput = 1;
        [Tooltip("Секунд на одну порцию (партия из N порций = время × N)")]
        public float processSeconds = 600f;
    }

    [Header("Рецепты станка")]
    [Tooltip("Удар с любым из этих продуктов = загрузка партии")]
    public MachineRecipe[] recipes;

    [Header("Спрайты")]
    [Tooltip("Спрайт пустого станка")]
    public Sprite idleSprite;
    [Tooltip("Кадры анимации РАБОТЫ (зацикливаются). Пусто = статичный idleSprite")]
    public Sprite[] workingFrames;
    [Tooltip("FPS анимации работы")]
    public float workingFps = 6f;
    [Tooltip("Спрайт, когда продукт готов и ждёт забора (пусто = остаётся последний рабочий)")]
    public Sprite readySprite;

    [Header("Загрузка")]
    [Tooltip("Сколько порций принимает за раз (порция = inputPerOutput единиц входа)")]
    public int batchCapacity = 5;

    [Header("Разбор молотком")]
    [Tooltip("Имя ItemData-ассета этого станка (для возврата в рюкзак молотком)")]
    public string selfItemName = "";
    [Tooltip("Название для сообщений игроку")]
    public string displayNameRu = "Станок";

    [Header("Иконка готовности")]
    [Tooltip("Дочерний объект префаба (спрайт-индикатор). Правится РУКАМИ в префабе — размер/позиция/спрайт. Если пусто — найдётся по имени ReadyIcon")]
    public GameObject readyIcon;

    // ── Состояние ──
    ItemData inputItem;          // что загружено сейчас
    int inputStored;             // сколько единиц входа лежит
    int outputPending;           // сколько готового ждёт забора
    long finishTicks;            // UtcNow.Ticks момента готовности (0 = не работает)
    Coroutine animRoutine;
    SpriteRenderer sr;

    public bool HasOutput => outputPending > 0;
    public bool IsWorking => finishTicks != 0;
    public string SelfItemName => selfItemName;
    public string DisplayNameRu => displayNameRu;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        var col = GetComponent<BoxCollider2D>();
        if (col != null) col.isTrigger = false;
        if (readyIcon == null)
        {
            Transform t = transform.Find("ReadyIcon");
            if (t != null) readyIcon = t.gameObject;
        }
        ApplyState();
    }

    void Update()
    {
        if (finishTicks != 0 && DateTime.UtcNow.Ticks >= finishTicks)
            FinishBatch();
    }

    void OnDestroy()
    {
        if (animRoutine != null) { StopCoroutine(animRoutine); animRoutine = null; }
    }

    // ═══════════════════════════════════════════════════════════
    // ПРОИЗВОДСТВО
    // ═══════════════════════════════════════════════════════════
    MachineRecipe FindRecipe(ItemData item)
    {
        if (item == null || recipes == null) return null;
        foreach (var r in recipes)
            if (r != null && r.input == item && r.output != null && r.inputPerOutput > 0)
                return r;
        return null;
    }

    /// <summary>Выходной предмет текущей/последней загрузки (для сейва и сбора).</summary>
    ItemData CurrentOutput()
    {
        var r = FindRecipe(inputItem);
        if (r != null) return r.output;
        if (recipes != null && recipes.Length > 0 && recipes[0] != null) return recipes[0].output;
        return null;
    }

    /// <summary>Удар с подходящим продуктом: стак из хотбара → в станок (до batchCapacity порций).</summary>
    void LoadBatch(MachineRecipe recipe)
    {
        InventorySlot slot = HotbarManager.Instance != null ? HotbarManager.Instance.GetActiveSlot() : null;
        if (slot == null || slot.IsEmpty() || slot.quantity <= 0) return;

        int portions = Mathf.Min(slot.quantity / recipe.inputPerOutput, Mathf.Max(1, batchCapacity));
        if (portions <= 0)
        {
            ActionLogUI.Show(displayNameRu + ": нужно минимум " + recipe.inputPerOutput + " × " +
                             recipe.input.itemName);
            return;
        }

        int take = portions * recipe.inputPerOutput;
        slot.quantity -= take;
        if (slot.quantity <= 0) slot.ClearSlot();
        else slot.UpdateUI();
        HotbarManager.Instance.NotifyActiveItemChanged();

        inputItem = recipe.input;
        inputStored = take;
        outputPending = 0;
        float seconds = recipe.processSeconds * portions;
        finishTicks = DateTime.UtcNow.Ticks + (long)(seconds * TimeSpan.TicksPerSecond);

        ApplyState();
        ActionLogUI.Show(displayNameRu + ": загружено " + take + " × " + recipe.input.itemName +
                         " → " + portions + " × " + recipe.output.itemName +
                         " (" + FormatTime(seconds) + ")");
        SaveManager.Instance?.Save();
    }

    void FinishBatch()
    {
        outputPending += Mathf.Max(1, inputStored / Mathf.Max(1, FindRecipe(inputItem)?.inputPerOutput ?? 1));
        inputItem = null;
        inputStored = 0;
        finishTicks = 0;

        ApplyState();
        ActionLogUI.Show(displayNameRu + ": готово! Ударь по нему, чтобы забрать.");
        SaveManager.Instance?.Save();
    }

    /// <summary>Удар по готовому станку: продукт выпадает физическим лутом (как урожай/соты).</summary>
    void Collect()
    {
        ItemData outItem = CurrentOutput();
        if (outItem == null)
        {
            Debug.LogWarning("[CraftMachine] Не найден выходной предмет у " + name);
            return;
        }

        int amount = outputPending;
        var lootPrefab = Resources.Load<GameObject>("LootItemPrefab");
        if (lootPrefab != null)
        {
            Vector2 off = UnityEngine.Random.insideUnitCircle * 0.35f;
            GameObject obj = Instantiate(lootPrefab,
                transform.position + new Vector3(off.x, off.y, 0f), Quaternion.identity);
            LootItem loot = obj.GetComponent<LootItem>();
            if (loot != null)
            {
                loot.itemData = outItem;
                loot.amount = amount;
                loot.despawnOverTime = false; // продукт не пропадает
                loot.craftingXpReward = 0;
                loot.farmingXpReward = 0;
            }
        }
        else
        {
            // Фолбэк: сразу в рюкзак
            InventoryUI.Instance?.AddItem(outItem, amount);
        }

        outputPending = 0;
        ApplyState();
        ActionLogUI.Show("Забрано: " + outItem.itemName + " ×" + amount);
        SaveManager.Instance?.Save();
    }

    // ═══════════════════════════════════════════════════════════
    // ВЗАИМОДЕЙСТВИЕ (удар = атака)
    // ═══════════════════════════════════════════════════════════
    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        ItemData active = HotbarManager.Instance != null ? HotbarManager.Instance.GetActiveItem() : null;
        MachineRecipe recipe = FindRecipe(active);

        // Готовый продукт важнее: забираем даже если в руках подходящий вход
        if (outputPending > 0)
        {
            Collect();
            return;
        }

        if (finishTicks != 0)
        {
            float left = (float)(finishTicks - DateTime.UtcNow.Ticks) / TimeSpan.TicksPerSecond;
            ActionLogUI.Show(displayNameRu + ": работает — осталось " + FormatTime(Mathf.Max(0f, left)));
            return;
        }

        if (inputStored > 0)
        {
            ActionLogUI.Show(displayNameRu + ": внутри " + inputStored + " × " +
                             (inputItem != null ? inputItem.itemName : "?") + " — молоток выгрузит обратно");
            return;
        }

        if (recipe != null)
        {
            LoadBatch(recipe);
            return;
        }

        // Пустой станок, руки пустые или продукт не подходит
        ActionLogUI.Show(AcceptsInfo());
    }

    public string AcceptsInfo()
    {
        if (recipes == null || recipes.Length == 0) return displayNameRu;
        string inputs = "";
        foreach (var r in recipes)
            if (r != null && r.input != null)
                inputs += (inputs.Length > 0 ? ", " : "") + r.input.itemName;
        return displayNameRu + ": принимает " + inputs;
    }

    // ═══════════════════════════════════════════════════════════
    // ВИЗУАЛ
    // ═══════════════════════════════════════════════════════════
    void ApplyState()
    {
        if (animRoutine != null) { StopCoroutine(animRoutine); animRoutine = null; }
        if (sr == null) return;

        if (finishTicks != 0)
        {
            // Работает: анимация кадров или статичный кадр
            if (workingFrames != null && workingFrames.Length > 0)
            {
                animRoutine = StartCoroutine(PlayWorkingAnim());
            }
            else if (idleSprite != null)
            {
                sr.sprite = idleSprite;
            }
        }
        else if (outputPending > 0 && readySprite != null)
        {
            sr.sprite = readySprite;
        }
        else if (idleSprite != null)
        {
            sr.sprite = idleSprite;
        }

        if (readyIcon != null)
            readyIcon.SetActive(outputPending > 0);
    }

    IEnumerator PlayWorkingAnim()
    {
        int i = 0;
        float frameTime = 1f / Mathf.Max(1f, workingFps);
        while (true)
        {
            if (workingFrames[i] != null) sr.sprite = workingFrames[i];
            yield return new WaitForSeconds(frameTime);
            i = i >= workingFrames.Length - 1 ? 0 : i + 1;
        }
    }

    static string FormatTime(float seconds)
    {
        if (seconds < 60f) return Mathf.CeilToInt(seconds) + " сек";
        return Mathf.CeilToInt(seconds / 60f) + " мин";
    }

    // ═══════════════════════════════════════════════════════════
    // РАЗБОР МОЛОТКОМ (тянет PlayerMovement.TryPickupPlaceable)
    // ═══════════════════════════════════════════════════════════
    /// <summary>Готовое — дроп лутом на землю, вход — обратно в рюкзак.
    /// false = рюкзак полон, станок не разбираем.</summary>
    public bool UnloadForPickup()
    {
        // Готовое — физическим лутом (не пропадает)
        if (outputPending > 0)
        {
            ItemData outItem = CurrentOutput();
            int amount = outputPending;
            outputPending = 0;
            var lootPrefab = Resources.Load<GameObject>("LootItemPrefab");
            if (lootPrefab != null && outItem != null)
            {
                Vector2 off = UnityEngine.Random.insideUnitCircle * 0.35f;
                GameObject obj = Instantiate(lootPrefab,
                    transform.position + new Vector3(off.x, off.y, 0f), Quaternion.identity);
                LootItem loot = obj.GetComponent<LootItem>();
                if (loot != null)
                {
                    loot.itemData = outItem;
                    loot.amount = amount;
                    loot.despawnOverTime = false;
                    loot.craftingXpReward = 0;
                    loot.farmingXpReward = 0;
                }
            }
            else if (outItem != null)
            {
                InventoryUI.Instance?.AddItem(outItem, amount);
            }
        }

        // Загруженный вход — в рюкзак (если не влезает — откат, не разбираем)
        if (inputStored > 0 && inputItem != null)
        {
            int left = inputStored;
            while (left > 0 && InventoryUI.Instance != null && InventoryUI.Instance.AddItem(inputItem, 1))
                left--;
            if (left > 0)
            {
                // откат: вернём в станок, готовое уже дропнули — не страшно
                inputStored = left;
                ActionLogUI.Show("Рюкзак полон — продукт не влезает, освободи место!");
                ApplyState();
                return false;
            }
        }

        inputItem = null;
        inputStored = 0;
        finishTicks = 0;
        ApplyState();
        return true;
    }

    // ═══════════════════════════════════════════════════════════
    // СЕЙВ (тянет PlaceablesSaveManager)
    // ═══════════════════════════════════════════════════════════
    public string SaveInputItem() => inputItem != null ? inputItem.name : "";
    public int SaveInputCount() => inputStored;
    public int SaveOutput() => outputPending;
    public long SaveFinishTicks() => finishTicks;

    /// <summary>Восстановление из сейва (PlaceablesSaveManager). Оффлайн-прогресс: если время
    /// готовности уже прошло — партия сразу дозревает.</summary>
    public void ApplySave(string inputItemName, int inputCount, int output, long finish)
    {
        outputPending = Mathf.Max(0, output);
        inputStored = Mathf.Max(0, inputCount);
        if (!string.IsNullOrEmpty(inputItemName))
            inputItem = ItemDatabase.Find(inputItemName);
        if (inputItem == null) inputStored = 0;

        if (inputStored > 0 && finish > 0 && DateTime.UtcNow.Ticks >= finish)
        {
            // Партия успела дойти (в т.ч. за оффлайн) — сразу в готовое
            outputPending += Mathf.Max(1, inputStored / Mathf.Max(1, FindRecipe(inputItem)?.inputPerOutput ?? 1));
            inputItem = null;
            inputStored = 0;
            finishTicks = 0;
        }
        else if (inputStored > 0 && finish > 0)
        {
            finishTicks = finish; // продолжает работать с сохранённого момента
        }

        ApplyState();
    }
}
