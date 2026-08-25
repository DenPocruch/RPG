using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// UI повара — книга рецептов. Слева прокручиваемый список рецептов,
/// справа детали выбранного (ингредиенты have/need, время, цена) + кнопка "Заказать".
/// Готовые блюда копятся в выходном слоте, забираются drag&drop.
/// Открывается как лесопилка — рюкзак сдвигается влево.
/// </summary>
public class CookUI : MonoBehaviour
{
    public static CookUI Instance;

    [Header("UI панели")]
    public GameObject cookPanel;
    public RectTransform cookRect;

    [Header("Список рецептов")]
    public GameObject recipeButtonPrefab;
    public Transform recipeListContent; // Content объекта ScrollView

    [Header("Детали выбранного рецепта")]
    public GameObject detailPanel;
    public Image detailIcon;
    public TMP_Text detailName;
    public TMP_Text detailDescription;
    public TMP_Text detailIngredients; // список have/need
    public TMP_Text detailCostTime;    // "Время: 30с | Цена: 5g"
    public Button orderButton;
    public TMP_Text orderButtonText;

    [Header("Прогресс и очередь")]
    public GameObject progressBarRoot;
    public Image progressBarFill;
    public TMP_Text progressText;
    public TMP_Text queueText;

    [Header("Выходной слот блюд")]
    public GameObject slotPrefab;
    public Transform outputSlotParent;
    public TMP_Text storageText;

    [Header("Позиции панели")]
    public float panelY = 47f;
    public float shiftDistance = 325f;
    public float shiftSpeed = 8f;

    private bool isOpen = false;
    private Vector2 targetPos;
    private Vector2 normalPos;

    private RecipeData selectedRecipe;
    private List<RecipeButtonUI> recipeButtons = new List<RecipeButtonUI>();
    private InventorySlot outputUiSlot;

    void Awake()
    {
        // Защита от дубликата: копия PersistentRoot при возврате в сцену
        // создаёт второй экземпляр — копию уничтожаем, оригинал живёт
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (cookPanel != null) cookPanel.SetActive(false);
    }

    void Start()
    {
        normalPos = new Vector2(0, panelY);
        if (cookRect != null)
        {
            cookRect.anchoredPosition = normalPos;
            targetPos = normalPos;
        }

        orderButton?.onClick.AddListener(OnOrderClick);

        if (CookStorage.Instance != null)
            CookStorage.Instance.onStorageChanged += OnStorageChanged;

        if (detailPanel != null) detailPanel.SetActive(false);
    }

    void OnDestroy()
    {
        if (CookStorage.Instance != null)
            CookStorage.Instance.onStorageChanged -= OnStorageChanged;
    }

    void Update()
    {
        if (cookRect != null)
            cookRect.anchoredPosition = Vector2.Lerp(
                cookRect.anchoredPosition, targetPos, Time.deltaTime * shiftSpeed);

        if (!isOpen) return;

        UpdateProgressBar();
        UpdateStorageText();
        if (selectedRecipe != null) RefreshDetail(); // обновляем have/need вживую
    }

    // ═══════════════════════════════════════════════════════════
    // ОТКРЫТИЕ / ЗАКРЫТИЕ
    // ═══════════════════════════════════════════════════════════
    public void Open()
    {
        cookPanel.SetActive(true);
        isOpen = true;

        InventoryPanelMover.Instance?.SetOffsetX(-shiftDistance);
        targetPos = new Vector2(shiftDistance, panelY);
        if (cookRect != null)
            cookRect.anchoredPosition = new Vector2(shiftDistance, panelY);

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.OpenInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        BuildRecipeList();
        BuildOutputSlot();
        OnStorageChanged();
    }

    public void Close()
    {
        cookPanel.SetActive(false);
        isOpen = false;

        InventoryPanelMover.Instance?.ResetPosition();
        targetPos = normalPos;

        if (InventoryUI.Instance != null)
            InventoryUI.Instance.CloseInventory();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;
    }

    public bool IsOpen() => isOpen;

    // ═══════════════════════════════════════════════════════════
    // СПИСОК РЕЦЕПТОВ
    // ═══════════════════════════════════════════════════════════
    void BuildRecipeList()
    {
        if (recipeListContent == null || recipeButtonPrefab == null || CookStorage.Instance == null)
            return;

        foreach (Transform child in recipeListContent) Destroy(child.gameObject);
        recipeButtons.Clear();

        foreach (RecipeData r in CookStorage.Instance.allRecipes)
        {
            if (r == null) continue;
            GameObject obj = Instantiate(recipeButtonPrefab, recipeListContent);
            ResetTransform(obj);
            RecipeButtonUI btn = obj.GetComponent<RecipeButtonUI>();
            btn.Setup(r);
            recipeButtons.Add(btn);
        }
    }

    public void SelectRecipe(RecipeData recipe)
    {
        selectedRecipe = recipe;

        foreach (RecipeButtonUI btn in recipeButtons)
            btn.SetSelected(btn.GetRecipe() == recipe);

        if (detailPanel != null) detailPanel.SetActive(recipe != null);
        RefreshDetail();
    }

    void RefreshDetail()
    {
        if (selectedRecipe == null || CookStorage.Instance == null) return;

        RecipeData r = selectedRecipe;

        if (detailIcon != null)
            detailIcon.sprite = r.outputItem != null ? r.outputItem.icon : null;
        if (detailName != null) detailName.text = r.recipeName;
        if (detailDescription != null) detailDescription.text = r.description;

        // Ингредиенты have/need с цветом
        if (detailIngredients != null)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (RecipeIngredient ing in r.ingredients)
            {
                if (ing.item == null) continue;
                int have = CountInInventory(ing.item);
                bool enough = have >= ing.amount;
                string color = enough ? "#7CFC7C" : "#FF6B6B";
                sb.AppendLine("<color=" + color + ">" + ing.item.itemName +
                    ": " + have + "/" + ing.amount + "</color>");
            }
            detailIngredients.text = sb.ToString().TrimEnd();
        }

        // Время и цена
        if (detailCostTime != null)
        {
            float time = CookStorage.Instance.GetCookTime(r);
            int cost = CookStorage.Instance.GetCookCost(r);
            string s = "Время: " + Mathf.CeilToInt(time) + "с";
            if (cost > 0) s += "  |  Цена: " + cost + "g";
            detailCostTime.text = s;
        }

        // Кнопка заказа
        bool unlocked = r.IsUnlocked();
        bool hasIngr = CookStorage.Instance.HasIngredients(r);
        int cost2 = CookStorage.Instance.GetCookCost(r);
        bool canAfford = CurrencyManager.Instance == null || CurrencyManager.Instance.Gold >= cost2;

        if (orderButton != null)
        {
            orderButton.interactable = unlocked && hasIngr && canAfford;
            if (orderButtonText != null)
            {
                if (!unlocked) orderButtonText.text = "Рецепт закрыт";
                else if (!hasIngr) orderButtonText.text = "Нет ингредиентов";
                else if (!canAfford) orderButtonText.text = "Не хватает золота";
                else orderButtonText.text = "Заказать";
            }
        }
    }

    int CountInInventory(ItemData item)
    {
        if (InventoryUI.Instance == null) return 0;
        int total = 0;
        foreach (InventorySlot s in InventoryUI.Instance.slots)
            if (!s.IsEmpty() && s.currentItem == item) total += s.quantity;
        return total;
    }

    // ═══════════════════════════════════════════════════════════
    // ЗАКАЗ
    // ═══════════════════════════════════════════════════════════
    void OnOrderClick()
    {
        if (selectedRecipe == null || CookStorage.Instance == null) return;

        bool ok = CookStorage.Instance.TryOrder(selectedRecipe);
        if (ok)
        {
            // Обновляем список кнопок (вдруг открылись новые рецепты по прогрессу)
            foreach (RecipeButtonUI btn in recipeButtons) btn.Setup(btn.GetRecipe());
            RefreshDetail();
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ВЫХОДНОЙ СЛОТ БЛЮД
    // ═══════════════════════════════════════════════════════════
    void BuildOutputSlot()
    {
        if (outputSlotParent == null || slotPrefab == null || CookStorage.Instance == null) return;

        foreach (Transform child in outputSlotParent) Destroy(child.gameObject);

        GameObject obj = Instantiate(slotPrefab, outputSlotParent);
        obj.name = "DishOutputUiSlot";
        ResetTransform(obj);

        outputUiSlot = obj.GetComponent<InventorySlot>();
        outputUiSlot.isHotbarSlot = false;
        outputUiSlot.allowOverflow = true;
        outputUiSlot.acceptsManualDeposit = false;
        outputUiSlot.linkedChestSlot = CookStorage.Instance.GetOutputSlot();
        outputUiSlot.overflowCapacity = CookStorage.Instance.GetDishCapacity();

        InventorySlot dataOut = CookStorage.Instance.GetOutputSlot();
        if (!dataOut.IsEmpty())
            outputUiSlot.SetItemWithWater(dataOut.currentItem, dataOut.quantity, 0);
    }

    void OnStorageChanged()
    {
        if (!isOpen) return;

        // Обновляем выходной слот
        if (outputUiSlot != null && CookStorage.Instance != null)
        {
            InventorySlot dataOut = CookStorage.Instance.GetOutputSlot();
            if (dataOut.IsEmpty()) outputUiSlot.ClearSlot();
            else outputUiSlot.SetItemWithWater(dataOut.currentItem, dataOut.quantity, 0);
            outputUiSlot.overflowCapacity = CookStorage.Instance.GetDishCapacity();
        }
    }

    void UpdateProgressBar()
    {
        if (CookStorage.Instance == null) return;

        RecipeData current = CookStorage.Instance.GetCurrentOrder();

        if (queueText != null)
        {
            int q = CookStorage.Instance.GetQueueCount();
            queueText.text = q > 0 ? "В очереди: " + q : "Очередь пуста";
        }

        if (current == null)
        {
            if (progressBarRoot != null) progressBarRoot.SetActive(false);
            return;
        }

        if (progressBarRoot != null) progressBarRoot.SetActive(true);

        float total = CookStorage.Instance.GetTotalTime();
        float remaining = CookStorage.Instance.GetTimeRemaining();
        float progress = total > 0 ? 1f - (remaining / total) : 1f;

        if (progressBarFill != null) progressBarFill.fillAmount = progress;
        if (progressText != null)
            progressText.text = "Готовим " + current.recipeName + "... " +
                Mathf.CeilToInt(remaining) + "с";
    }

    void UpdateStorageText()
    {
        if (storageText == null || CookStorage.Instance == null) return;
        InventorySlot dataOut = CookStorage.Instance.GetOutputSlot();
        storageText.text = "Склад блюд: " + dataOut.quantity + "/" + CookStorage.Instance.GetDishCapacity();
    }

    void ResetTransform(GameObject obj)
    {
        RectTransform rt = obj.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchoredPosition = Vector2.zero; // фикс: слот улетал вниз без этого
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
    }
}