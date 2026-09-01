using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class PlayerMovement : MonoBehaviour
{
    [Header("Движение")]
    public float moveSpeed = 3f;
    public FixedJoystick joystick;

    [Header("Флаги")]
    public bool isAttacking = false;

    [Header("Хитбоксы (меч + взаимодействие)")]
    public AttackHitbox attackDown;
    public AttackHitbox attackUp;
    public AttackHitbox attackRight;
    public AttackHitbox attackLeft;

    [Header("Детекторы взаимодействия (на тех же AttackArea)")]
    public InteractionDetector detectorDown;
    public InteractionDetector detectorUp;
    public InteractionDetector detectorRight;
    public InteractionDetector detectorLeft;

    [Header("Лук")]
    public BowController bowController;

    [Header("Инструменты")]
    public ToolController toolController;

    [Header("Длительность анимаций (сек)")]
    public float meleeAttackDuration = 0.6f;
    public float bowAttackDuration = 1.0f;
    public float toolUseDuration = 0.8f;

    [Header("Дальность инструментов")]
    public float axeRange = 1.2f;
    public float pickaxeRange = 1.2f;
    public float sickleRange = 1.0f;

    [Header("Размещение объектов (кормушка/поилка/пугало)")]
    [Tooltip("Дистанция призрака перед игроком")]
    public float placeDistance = 1.1f;
    [Tooltip("Радиус проверки занятости места")]
    public float placeCheckRadius = 0.45f;
    private GameObject placementGhost;
    private SpriteRenderer ghostSr;
    private ItemData ghostItem;
    private float placeRotation = 0f;
    private GameObject zonePreview;      // подсветка зоны пугала (3×3) при постановке
    private SpriteRenderer zoneSr;
    private int ghostZoneRadius = 1;     // радиус зоны пугала (из поля zoneRadiusTiles его префаба)

    [Header("Молоток (сбор кормушки/поилки в рюкзак)")]
    [Tooltip("Дальность разбора размещённых объектов молотком")]
    public float hammerRange = 1.5f;
    private Component highlightedPlaceable; // FeederStorage/WaterTrough под подсветкой
    private readonly Dictionary<SpriteRenderer, Color> savedHighlightColors = new Dictionary<SpriteRenderer, Color>();

    /// <summary>Повторный тап по АКТИВНОМУ слоту хотбара (HotbarManager).</summary>
    public static event System.Action onSlotRetapped;

    private Rigidbody2D rb;
    private Animator animator;
    private FarmInteraction farmInteraction;
    private Vector2 movement;

    private float lastMoveX = 0f;
    private float lastMoveY = -1f;
    private float lastAttackTime = -99f;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        farmInteraction = GetComponent<FarmInteraction>();
        animator.SetFloat("LastMoveX", lastMoveX);
        animator.SetFloat("LastMoveY", lastMoveY);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            Attack();

        if (isAttacking) return;

        float h = joystick != null && joystick.Horizontal != 0
                    ? joystick.Horizontal
                    : Input.GetAxisRaw("Horizontal");
        float v = joystick != null && joystick.Vertical != 0
                    ? joystick.Vertical
                    : Input.GetAxisRaw("Vertical");

        movement = new Vector2(h, v).normalized;

        if (animator != null)
        {
            animator.SetFloat("Speed", movement.magnitude);
            if (movement.magnitude > 0.1f)
            {
                animator.SetFloat("MoveX", h);
                animator.SetFloat("MoveY", v);
                lastMoveX = h;
                lastMoveY = v;
            }
            animator.SetFloat("LastMoveX", lastMoveX);
            animator.SetFloat("LastMoveY", lastMoveY);
        }

        UpdatePlacementGhost();
        UpdateHammerHighlight();
    }

    /// <summary>Вызывается из HotbarManager при повторном тапе по активному слоту.</summary>
    public static void NotifySlotRetapped() => onSlotRetapped?.Invoke();

    void OnEnable()
    {
        onSlotRetapped += RotatePlacementGhost;
    }

    void OnDisable()
    {
        onSlotRetapped -= RotatePlacementGhost;
        RestoreHammerHighlight();
        DestroyGhost();
    }

    /// <summary>Поворот ghost-объекта на 90° перед постановкой.</summary>
    void RotatePlacementGhost()
    {
        if (placementGhost == null) return;
        placeRotation += 90f;
        placementGhost.transform.localEulerAngles = new Vector3(0f, 0f, placeRotation);
    }

    void FixedUpdate()
    {
        if (isAttacking) { rb.linearVelocity = Vector2.zero; return; }
        rb.linearVelocity = movement * moveSpeed;
    }

    public void ToggleEquip()
    {
        ItemData activeItem = HotbarManager.Instance?.GetActiveItem();
        if (activeItem == null) return;

        if (activeItem.itemType == ItemType.Weapon)
        {
            WeaponController weapon = GetComponentInChildren<WeaponController>();
            if (weapon != null) weapon.ToggleWeapon();
            if (bowController != null) bowController.ForceUnequip();
            if (toolController != null) toolController.ForceHide();
        }
        else if (activeItem.itemType == ItemType.RangedWeapon)
        {
            if (bowController != null) bowController.ToggleBow();
            WeaponController weapon = GetComponentInChildren<WeaponController>();
            if (weapon != null) weapon.ForceUnequip();
            if (toolController != null) toolController.ForceHide();
        }
    }

    public void Attack()
    {
        if (isAttacking) return;

        ItemData activeItem = HotbarManager.Instance?.GetActiveItem();

        // Молоток: удар = разобрать кормушку/поилку в рюкзак.
        // Проверяем ДО детектора — иначе удар по кормушке откроет FeedUI
        if (activeItem != null && activeItem.itemType == ItemType.Hammer)
        {
            StartHammerUse(activeItem);
            return;
        }

        // Универсальная проверка интерактивных объектов через детектор активной стороны
        InteractionDetector activeDetector = GetActiveDetector();
        if (activeDetector != null && activeDetector.TryInteract())
            return; // объект сработал — не делаем атаку

        if (farmInteraction != null)
            farmInteraction.CheckHarvest();

        float cooldown = activeItem != null ? activeItem.attackSpeed : 0.6f;

        if (Time.time - lastAttackTime < cooldown) return;
        lastAttackTime = Time.time;

        // Ghost-режим размещения (кормушка/поилка в руках) — атака ставит объект
        if (placementGhost != null && IsPlaceable(activeItem))
        {
            TryPlaceAtGhost(activeItem);
            return;
        }

        if (activeItem == null)
        {
            StartMeleeAttack(0.6f);
            return;
        }

        switch (activeItem.itemType)
        {
            case ItemType.Weapon:
                StartMeleeAttack(activeItem.attackSpeed);
                break;
            case ItemType.RangedWeapon:
                StartBowAttack(activeItem);
                break;
            case ItemType.Hoe:
            case ItemType.BugNet:
                StartToolUse(activeItem, "Tools");
                break;
            case ItemType.Pickaxe:
                StartPickaxeUse(activeItem);
                break;
            case ItemType.WateringCan:
                StartWateringCanUse(activeItem);
                break;
            case ItemType.Axe:
                StartAxeUse(activeItem);
                break;
            case ItemType.Sickle:
                StartSickleUse(activeItem);
                break;
            case ItemType.Seed:
                if (farmInteraction != null)
                    farmInteraction.TryPlantOrHarvest();
                break;
            case ItemType.Sapling:
                PlantSapling(activeItem);
                break;
            case ItemType.AnimalBaby:
                SpawnAnimal(activeItem);
                break;
            case ItemType.Consumable:
                EatFood(activeItem);
                break;
            default:
                // Удобрение: активным предметом по грядке с растением
                if (activeItem != null && activeItem.isFertilizer && farmInteraction != null)
                    farmInteraction.TryPlantOrHarvest();
                break;
        }
    }

    // Выбираем детектор по направлению взгляда персонажа
    InteractionDetector GetActiveDetector()
    {
        if (Mathf.Abs(lastMoveX) > Mathf.Abs(lastMoveY))
            return lastMoveX > 0 ? detectorRight : detectorLeft;
        else
            return lastMoveY > 0 ? detectorUp : detectorDown;
    }

    void StartWateringCanUse(ItemData toolData)
    {
        InventorySlot slot = HotbarManager.Instance?.GetActiveSlot();

        if (slot == null || !slot.HasWater())
        {
            ActionLogUI.Show("Нет воды! Подойди к колодцу с лейкой.");
            WaterBar waterBar = FindFirstObjectByType<WaterBar>();
            if (waterBar != null) waterBar.PlayEmptyEffect();
            return;
        }

        StartToolUse(toolData, "Watering");
    }

    void StartMeleeAttack(float duration)
    {
        isAttacking = true;
        animator.SetTrigger("Attack");

        WeaponController weapon = GetComponentInChildren<WeaponController>();
        if (weapon != null)
            weapon.PlayAttackAnimation(lastMoveX, lastMoveY);

        AttackHitbox hitbox = GetActiveHitbox();
        if (hitbox != null)
            hitbox.PerformAttack(new Vector2(lastMoveX, lastMoveY));

        StartCoroutine(WaitAndReset(duration > 0 ? duration : meleeAttackDuration));
    }

    void StartBowAttack(ItemData bowData)
    {
        if (bowController == null || !bowController.bowEquipped) return;

        isAttacking = true;
        animator.SetTrigger("BowShoot");
        bowController.Shoot(lastMoveX, lastMoveY, bowData);

        float duration = bowData.attackSpeed > 0 ? bowData.attackSpeed : bowAttackDuration;
        StartCoroutine(WaitAndReset(duration));
    }

    void StartToolUse(ItemData toolData, string animTrigger)
    {
        isAttacking = true;

        animator.SetTrigger(animTrigger);
        animator.SetFloat("LastMoveX", lastMoveX);
        animator.SetFloat("LastMoveY", lastMoveY);

        if (toolController != null)
            toolController.UseTool(toolData.itemType, lastMoveX, lastMoveY,
                toolData.attackSpeed > 0 ? toolData.attackSpeed : toolUseDuration);

        if (farmInteraction != null)
            farmInteraction.UseFarmTool(toolData.itemType, lastMoveX, lastMoveY);

        StartCoroutine(WaitAndReset(
            toolData.attackSpeed > 0 ? toolData.attackSpeed : toolUseDuration));
    }

    void StartAxeUse(ItemData axeData)
    {
        isAttacking = true;

        animator.SetTrigger("AxeSickle");
        animator.SetFloat("LastMoveX", lastMoveX);
        animator.SetFloat("LastMoveY", lastMoveY);

        if (toolController != null)
            toolController.UseTool(axeData.itemType, lastMoveX, lastMoveY,
                axeData.attackSpeed > 0 ? axeData.attackSpeed : toolUseDuration);

        ChopTree();

        StartCoroutine(WaitAndReset(
            axeData.attackSpeed > 0 ? axeData.attackSpeed : toolUseDuration));
    }

    void StartPickaxeUse(ItemData pickaxeData)
    {
        isAttacking = true;

        animator.SetTrigger("Tools");
        animator.SetFloat("LastMoveX", lastMoveX);
        animator.SetFloat("LastMoveY", lastMoveY);

        if (toolController != null)
            toolController.UseTool(pickaxeData.itemType, lastMoveX, lastMoveY,
                pickaxeData.attackSpeed > 0 ? pickaxeData.attackSpeed : toolUseDuration);

        MineOre();

        StartCoroutine(WaitAndReset(
            pickaxeData.attackSpeed > 0 ? pickaxeData.attackSpeed : toolUseDuration));
    }

    void StartSickleUse(ItemData sickleData)
    {
        isAttacking = true;

        animator.SetTrigger("AxeSickle");
        animator.SetFloat("LastMoveX", lastMoveX);
        animator.SetFloat("LastMoveY", lastMoveY);

        if (toolController != null)
            toolController.UseTool(sickleData.itemType, lastMoveX, lastMoveY,
                sickleData.attackSpeed > 0 ? sickleData.attackSpeed : toolUseDuration);

        bool harvestedTree = TryHarvestFruitTree();

        if (!harvestedTree && farmInteraction != null)
            farmInteraction.TryPlantOrHarvest();

        StartCoroutine(WaitAndReset(
            sickleData.attackSpeed > 0 ? sickleData.attackSpeed : toolUseDuration));
    }

    bool TryHarvestFruitTree()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, sickleRange);
        foreach (Collider2D hit in hits)
        {
            TreeComponent tree = hit.GetComponentInParent<TreeComponent>();
            if (tree != null && tree.HasFruit())
            {
                bool harvested = tree.TryHarvestFruit();
                if (harvested)
                {
                    ActionLogUI.Show("Плоды успешно собраны!");
                    return true;
                }
            }
        }
        return false;
    }

    void PlantSapling(ItemData saplingData)
    {
        if (saplingData.treePrefab == null)
        {
            Debug.LogWarning("Tree Prefab не задан в ItemData саженца!");
            return;
        }

        Vector3 plantPos = transform.position;

        if (!PlantedTree.CanPlantHere(plantPos))
        {
            ActionLogUI.Show("Здесь уже есть дерево!");
            return;
        }

        GameObject treeObj = Instantiate(saplingData.treePrefab, plantPos, Quaternion.identity);
        PlantedTree planted = treeObj.GetComponent<PlantedTree>();
        if (planted != null)
            planted.saplingData = saplingData;

        InventorySlot activeSlot = HotbarManager.Instance?.GetActiveSlot();
        if (activeSlot != null)
        {
            if (activeSlot.quantity > 1)
            {
                activeSlot.quantity--;
                activeSlot.UpdateUI();
            }
            else
                activeSlot.ClearSlot();
        }

        ActionLogUI.Show("Посажен саженец: " + saplingData.itemName);
    }

    // ═══════════════════════════════════════════════════════════
    // РАЗМЕЩЕНИЕ ОБЪЕКТОВ (кормушка/поилка/пугало): ghost перед игроком,
    // ходьба двигает призрак, атака ставит, смена слота отменяет
    // ═══════════════════════════════════════════════════════════
    public static bool IsPlaceable(ItemData item)
        => item != null && (item.itemType == ItemType.Feeder || item.itemType == ItemType.WaterTrough
            || item.itemType == ItemType.Scarecrow);

    void UpdatePlacementGhost()
    {
        ItemData active = HotbarManager.Instance != null ? HotbarManager.Instance.GetActiveItem() : null;
        bool want = IsPlaceable(active) && active.placeablePrefab != null;
        if (!want) { DestroyGhost(); return; }

        if (placementGhost == null || ghostItem != active)
            CreateGhost(active);

        Vector2 dir = new Vector2(lastMoveX, lastMoveY);
        if (dir.sqrMagnitude < 0.01f) dir = Vector2.down;
        Vector3 pos = transform.position + (Vector3)(dir.normalized * placeDistance);
        placementGhost.transform.position = pos;

        bool canPlace = CanPlaceAt(pos);

        // Пугало: подсвечиваем зону защиты вокруг призрака (размер — из префаба)
        if (active.itemType == ItemType.Scarecrow)
        {
            EnsureZonePreview();
            zonePreview.transform.position = Scarecrow.GetZoneCenter(pos);
            zonePreview.transform.localScale = Scarecrow.GetZoneSize(ghostZoneRadius);
            zoneSr.color = canPlace
                ? new Color(0.55f, 1f, 0.55f, 0.5f)
                : new Color(1f, 0.35f, 0.35f, 0.5f);
        }
        else DestroyZonePreview();

        if (ghostSr != null)
            ghostSr.color = canPlace
                ? new Color(0.55f, 1f, 0.55f, 0.6f)
                : new Color(1f, 0.35f, 0.35f, 0.6f);
    }

    // ── Квадрат зоны пугала (создаётся только пока пугало в руках) ──
    void EnsureZonePreview()
    {
        if (zonePreview != null) return;
        zonePreview = new GameObject("ScarecrowZonePreview");
        zoneSr = zonePreview.AddComponent<SpriteRenderer>();
        zoneSr.sprite = Scarecrow.GetZoneSprite();
        zoneSr.sortingOrder = 59; // над тайлмапом, под призраком (60)
    }

    void DestroyZonePreview()
    {
        if (zonePreview != null) Destroy(zonePreview);
        zonePreview = null;
        zoneSr = null;
    }

    void CreateGhost(ItemData item)
    {
        DestroyGhost();
        ghostItem = item;
        placeRotation = 0f;

        // Радиус зоны пугала берём из скрипта на префабе (1 = 3×3, 2 = 5×5, ...)
        ghostZoneRadius = 1;
        if (item.placeablePrefab != null)
        {
            Scarecrow sc = item.placeablePrefab.GetComponent<Scarecrow>();
            if (sc != null) ghostZoneRadius = sc.zoneRadiusTiles;
        }

        placementGhost = new GameObject("PlacementGhost");
        placementGhost.transform.localEulerAngles = Vector3.zero;
        ghostSr = placementGhost.AddComponent<SpriteRenderer>();
        ghostSr.sprite = item.icon != null ? item.icon : item.worldSprite;
        ghostSr.sortingOrder = 60;
        var c = ghostSr.color;
        c.a = 0.6f;
        ghostSr.color = c;
    }

    void DestroyGhost()
    {
        if (placementGhost != null) Destroy(placementGhost);
        placementGhost = null;
        ghostSr = null;
        ghostItem = null;
        DestroyZonePreview();
    }

    /// <summary>Можно ли поставить объект: нет стен/воды/других предметов в точке.
    /// Игрок и животные не мешают (игрок отойдёт, животные обойдут).</summary>
    bool CanPlaceAt(Vector3 pos)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, placeCheckRadius);
        foreach (Collider2D h in hits)
        {
            if (h.isTrigger) continue;
            if (h.transform == transform || h.CompareTag("Player")) continue;
            if (h.GetComponentInParent<AnimalController>() != null) continue;
            return false;
        }
        return true;
    }

    void TryPlaceAtGhost(ItemData item)
    {
        if (placementGhost == null || item.placeablePrefab == null) return;

        Vector3 pos = placementGhost.transform.position;
        if (!CanPlaceAt(pos))
        {
            ActionLogUI.Show("Здесь нельзя поставить — место занято!");
            return;
        }

        GameObject placed = Instantiate(item.placeablePrefab, pos, Quaternion.Euler(0f, 0f, placeRotation));

        // Пугало: короткая вспышка зоны защиты после постановки
        if (item.itemType == ItemType.Scarecrow && placed != null)
        {
            Scarecrow sc = placed.GetComponent<Scarecrow>();
            if (sc != null) sc.ShowZoneFlash();
        }

        InventorySlot slot = HotbarManager.Instance != null ? HotbarManager.Instance.GetActiveSlot() : null;
        if (slot != null)
        {
            if (slot.quantity > 1) { slot.quantity--; slot.UpdateUI(); }
            else slot.ClearSlot();
            HotbarManager.Instance.NotifyActiveItemChanged();
        }

        ActionLogUI.Show("Поставлено: " + item.itemName + ". Удар по нему — открыть.");

        // Сейв сразу: иначе при выходе раньше автосейва кормушка потеряется
        SaveManager.Instance?.Save();
    }

    // ═══════════════════════════════════════════════════════════
    // МОЛОТОК: удар по кормушке/поилке = разобрать в рюкзак.
    // Объект перед игроком подсвечивается зелёным (UpdateHammerHighlight)
    // ═══════════════════════════════════════════════════════════
    void StartHammerUse(ItemData hammer)
    {
        float cooldown = hammer.attackSpeed > 0 ? hammer.attackSpeed : toolUseDuration;
        if (Time.time - lastAttackTime < cooldown) return;
        lastAttackTime = Time.time;

        isAttacking = true;
        animator.SetTrigger("Tools");
        animator.SetFloat("LastMoveX", lastMoveX);
        animator.SetFloat("LastMoveY", lastMoveY);

        TryPickupPlaceable();

        StartCoroutine(WaitAndReset(cooldown));
    }

    /// <summary>Ближайшая кормушка/поилка/пугало перед игроком (null — нет в зоне молотка).</summary>
    Component FindPlaceableInFront()
    {
        Vector2 dir = new Vector2(lastMoveX, lastMoveY);
        if (dir.sqrMagnitude < 0.01f) dir = Vector2.down;
        Vector2 checkPos = (Vector2)transform.position + dir.normalized * (hammerRange * 0.55f);

        Collider2D[] hits = Physics2D.OverlapCircleAll(checkPos, hammerRange * 0.75f);
        foreach (Collider2D h in hits)
        {
            FeederStorage feeder = h.GetComponentInParent<FeederStorage>();
            if (feeder != null) return feeder;
            WaterTrough trough = h.GetComponentInParent<WaterTrough>();
            if (trough != null) return trough;
            Scarecrow scarecrow = h.GetComponentInParent<Scarecrow>();
            if (scarecrow != null) return scarecrow;
        }
        return null;
    }

    void TryPickupPlaceable()
    {
        Component target = FindPlaceableInFront();
        if (target == null) return; // замах вхолостую

        ItemData pickupItem;
        string nameRu;

        if (target is FeederStorage feeder)
        {
            // Сначала выгружаем корм в рюкзак — иначе кормушка не собирается
            if (feeder.TotalStock > 0)
            {
                feeder.TakeAllBack();
                if (feeder.TotalStock > 0)
                {
                    ActionLogUI.Show("Рюкзак полон — корм не влезает, освободи место!");
                    return;
                }
            }
            pickupItem = ItemDatabase.Find("Feeder");
            nameRu = "Кормушка";
        }
        else if (target is WaterTrough trough)
        {
            if (trough.water > 0)
                ActionLogUI.Show("Вода из поилки вылилась (" + trough.water + " ед.)");
            pickupItem = ItemDatabase.Find("WaterTrough");
            nameRu = "Поилка";
        }
        else if (target is Scarecrow)
        {
            pickupItem = ItemDatabase.Find("Scarecrow");
            nameRu = "Пугало";
        }
        else return;

        if (pickupItem == null)
        {
            Debug.LogWarning("[Молоток] Не найден предмет в ItemDatabase: " + nameRu);
            return;
        }

        if (InventoryUI.Instance == null || !InventoryUI.Instance.AddItem(pickupItem, 1))
        {
            ActionLogUI.Show("Рюкзак полон — " + nameRu + " не влезает!");
            return;
        }

        RestoreHammerHighlight();
        Destroy(target.gameObject);

        ActionLogUI.Show(nameRu + " убрана в рюкзак");

        // Сейв сразу: иначе при выходе раньше автосейва объект «воскреснет»
        SaveManager.Instance?.Save();
    }

    // ── Зелёная подсветка объекта, который молоток сейчас разберёт ──
    void UpdateHammerHighlight()
    {
        if (isAttacking) return;

        ItemData active = HotbarManager.Instance != null ? HotbarManager.Instance.GetActiveItem() : null;
        bool hammerMode = active != null && active.itemType == ItemType.Hammer;

        Component target = hammerMode ? FindPlaceableInFront() : null;

        if (target == highlightedPlaceable) return;

        RestoreHammerHighlight();
        highlightedPlaceable = target;

        if (target == null) return;

        Color tint = new Color(0.45f, 1f, 0.45f);
        foreach (SpriteRenderer sr in target.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr == null) continue;
            savedHighlightColors[sr] = sr.color;
            sr.color = Color.Lerp(sr.color, tint, 0.75f);
        }
    }

    void RestoreHammerHighlight()
    {
        foreach (var kvp in savedHighlightColors)
        {
            if (kvp.Key == null) continue; // объект разобран — рендерер уничтожен
            kvp.Key.color = kvp.Value;
        }
        savedHighlightColors.Clear();
        highlightedPlaceable = null;
    }

    // Выпустить детёныша животного из активного слота хотбара
    void SpawnAnimal(ItemData babyItem)
    {
        if (babyItem.animalPrefab == null)
        {
            Debug.LogWarning("[Животные] Не назначен animalPrefab в ItemData: " + babyItem.itemName);
            return;
        }

        Vector3 spawnPos = transform.position + new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(-0.6f, 0.6f), 0);
        Instantiate(babyItem.animalPrefab, spawnPos, Quaternion.identity);

        InventorySlot activeSlot = HotbarManager.Instance?.GetActiveSlot();
        if (activeSlot != null)
        {
            if (activeSlot.quantity > 1) { activeSlot.quantity--; activeSlot.UpdateUI(); }
            else activeSlot.ClearSlot();
            HotbarManager.Instance.NotifyActiveItemChanged();
        }

        ActionLogUI.Show("[Животные] Выпущен детёныш: " + babyItem.itemName);
    }
void EatFood(ItemData food)
    {
        if (food == null) return;

        bool consumed = false;

        // Мгновенное восстановление HP
        if (food.healAmount > 0)
        {
            PlayerHealth ph = GetComponent<PlayerHealth>();
            if (ph != null && ph.currentHealth < ph.maxHealth)
            {
                ph.currentHealth = Mathf.Min(ph.currentHealth + food.healAmount, ph.maxHealth);
                consumed = true;

                if (DamagePopupManager.Instance != null)
                    DamagePopupManager.Instance.Spawn(
                        transform.position + Vector3.up, food.healAmount, DamagePopup.PopupType.Heal);
            }
        }

        // Временный бафф
        if (food.foodBuffType != FoodBuffType.None && food.foodBuffDuration > 0f)
        {
            PlayerBuffs.Instance?.ApplyFoodBuff(food);
            consumed = true;
        }

        // Если еда только лечит, а HP полное — не тратим её впустую
        if (!consumed)
        {
            ActionLogUI.Show("[Еда] HP полное — " + food.itemName + " сейчас не нужна");
            return;
        }

        // Съедаем 1 штуку из активного слота
        InventorySlot slot = HotbarManager.Instance?.GetActiveSlot();
        if (slot != null)
        {
            if (slot.quantity > 1) { slot.quantity--; slot.UpdateUI(); }
            else slot.ClearSlot();
            HotbarManager.Instance.NotifyActiveItemChanged();
        }
    }

    void ChopTree()
    {
        Vector2 dir = new Vector2(lastMoveX, lastMoveY).normalized;
        Vector2 checkPos = (Vector2)transform.position + dir * axeRange;

        Collider2D[] hits = Physics2D.OverlapCircleAll(checkPos, axeRange * 0.8f);
        foreach (Collider2D hit in hits)
        {
            TreeComponent tree = hit.GetComponentInParent<TreeComponent>();
            if (tree != null)
            {
                tree.Chop();
                break;
            }
        }
    }

    void MineOre()
    {
        Vector2 dir = new Vector2(lastMoveX, lastMoveY).normalized;
        Vector2 checkPos = (Vector2)transform.position + dir * pickaxeRange;

        Collider2D[] hits = Physics2D.OverlapCircleAll(checkPos, pickaxeRange * 0.8f);
        foreach (Collider2D hit in hits)
        {
            OreVeinComponent vein = hit.GetComponentInParent<OreVeinComponent>();
            if (vein != null)
            {
                vein.Mine();
                break;
            }
        }
    }

    IEnumerator WaitAndReset(float duration)
    {
        yield return new WaitForSeconds(duration);
        isAttacking = false;
    }

    AttackHitbox GetActiveHitbox()
    {
        if (Mathf.Abs(lastMoveX) > Mathf.Abs(lastMoveY))
            return lastMoveX > 0 ? attackRight : attackLeft;
        else
            return lastMoveY > 0 ? attackUp : attackDown;
    }

    void ResetAttack()
    {
        isAttacking = false;
    }
}