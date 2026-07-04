using UnityEngine;
using System.Collections;

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

        // Универсальная проверка интерактивных объектов через детектор активной стороны
        InteractionDetector activeDetector = GetActiveDetector();
        if (activeDetector != null && activeDetector.TryInteract())
            return; // объект сработал — не делаем атаку

        if (farmInteraction != null)
            farmInteraction.CheckHarvest();

        ItemData activeItem = HotbarManager.Instance?.GetActiveItem();
        float cooldown = activeItem != null ? activeItem.attackSpeed : 0.6f;

        if (Time.time - lastAttackTime < cooldown) return;
        lastAttackTime = Time.time;

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
            case ItemType.Consumable:
                EatFood(activeItem);
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
            Debug.Log("Нет воды! Подойди к колодцу с лейкой.");
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
                    Debug.Log("Плоды успешно собраны!");
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
            Debug.Log("Здесь уже есть дерево!");
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

        Debug.Log("Посажен саженец: " + saplingData.itemName);
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
            Debug.Log("[Еда] HP полное — " + food.itemName + " сейчас не нужна");
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