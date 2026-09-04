using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkillTreeUI : MonoBehaviour
{
    public static SkillTreeUI Instance;

    [Header("Панель")]
    public GameObject skillTreePanel;

    [Header("Вкладки")]
    public Button tabCombat;
    public Button tabFarming;
    public Button tabCrafting;
    public Button tabEquipment; // привяжи новую кнопку ветки экипировки (опц.)
    public Image tabCombatBg;
    public Image tabFarmingBg;
    public Image tabCraftingBg;
    public Image tabEquipmentBg;
    public Color tabActiveColor = new Color(0.9f, 0.7f, 0.2f);
    public Color tabInactiveColor = new Color(0.3f, 0.3f, 0.3f);

    [Header("Контейнеры узлов")]
    public GameObject combatContainer;
    public GameObject farmingContainer;
    public GameObject craftingContainer;
    public GameObject equipmentContainer; // контейнер кнопок ветки (узлы добавит юзер)

    [Header("Автокнопки ветки экипировки")]
    [Tooltip("Префаб кнопки узла (SkillNodeButton). Перетащи раз — 45 кнопок построятся сами сеткой 9 видов × 5 тиров")]
    public GameObject nodeButtonPrefab;

    [Header("Панель деталей")]
    public GameObject detailPanel;
    public Image detailIcon;
    public TMP_Text detailName;
    public TMP_Text detailRank;          // "Ранг: 2/5"
    public TMP_Text detailDescription;
    public TMP_Text detailEffect;        // текущий суммарный бонус
    public TMP_Text detailNextEffect;    // бонус следующего ранга
    public TMP_Text detailCost;          // стоимость следующего ранга
    public TMP_Text detailRequirements;
    public Button unlockButton;
    public TMP_Text unlockButtonText;

    [Header("Инфобар")]
    public TMP_Text levelText;
    public TMP_Text skillPointsText;
    public TMP_Text goldText;

    [Header("Кнопка сброса")]
    public Button resetButton;
    public TMP_Text resetButtonText;

    [Header("Пагинация (перелистывание)")]
    public Button pagePrevButton;
    public Button pageNextButton;
    [Min(1)] public int nodesPerPage = 12;

    private PlayerLevel.SkillBranch currentBranch = PlayerLevel.SkillBranch.Combat;
    private SkillNodeUI selectedNode = null;
    private bool isOpen = false;
    private bool justShown = false;
    private RectTransform panelRect;
    private readonly Dictionary<PlayerLevel.SkillBranch, int> pageByBranch = new();

    void Awake()
    {
        // Защита от дубликата: копия PersistentRoot при возврате в сцену
        // создаёт второй экземпляр — копию уничтожаем, оригинал живёт
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (skillTreePanel != null)
        {
            skillTreePanel.SetActive(false);
            panelRect = skillTreePanel.GetComponent<RectTransform>();
        }
    }

    void Start()
    {
        if (SkillTreeManager.Instance != null)
            SkillTreeManager.Instance.onSkillTreeChanged += RefreshAll;

        if (PlayerLevel.Instance != null)
        {
            PlayerLevel.Instance.onLevelUp += _ => UpdateInfoBar();
            PlayerLevel.Instance.onSkillPointsChanged += _ => UpdateInfoBar();
        }

        if (CurrencyManager.Instance != null)
            CurrencyManager.Instance.onGoldChanged += _ => UpdateInfoBar();

        tabCombat?.onClick.AddListener(() => SwitchTab(PlayerLevel.SkillBranch.Combat));
        tabFarming?.onClick.AddListener(() => SwitchTab(PlayerLevel.SkillBranch.Farming));
        tabCrafting?.onClick.AddListener(() => SwitchTab(PlayerLevel.SkillBranch.Crafting));
        tabEquipment?.onClick.AddListener(() => SwitchTab(PlayerLevel.SkillBranch.Equipment));

        unlockButton?.onClick.AddListener(OnUnlockClick);
        resetButton?.onClick.AddListener(OnResetClick);

        pagePrevButton?.onClick.AddListener(PrevPage);
        pageNextButton?.onClick.AddListener(NextPage);

        if (resetButtonText != null && SkillTreeManager.Instance != null)
            resetButtonText.text = "Сброс (" + SkillTreeManager.Instance.resetCost + "g)";

        if (detailPanel != null) detailPanel.SetActive(false);
        EnsureEquipmentButtons();
        SwitchTab(PlayerLevel.SkillBranch.Combat);
    }

    // ═══════════════════════════════════════════════════════════
    // АВТОКНОПКИ ВЕТКИ ЭКИПИРОВКИ (45 узлов руками — не вариант)
    // Сетка: колонки = виды (меч/лук/.../топор), строки = тиры (медь→обсидиан).
    // Только для Equipment — остальные ветки ручные, их не трогаем.
    // ═══════════════════════════════════════════════════════════
    void EnsureEquipmentButtons()
    {
        if (nodeButtonPrefab == null || equipmentContainer == null) return;
        if (SkillTreeManager.Instance == null || SkillTreeManager.Instance.allNodes == null) return;

        // Чистим чужое: контейнер могли склонировать из другой ветки —
        // кнопки не-Equipment удаляем (только в рантайме, файл сцены не трогаем)
        foreach (SkillNodeUI ui in equipmentContainer.GetComponentsInChildren<SkillNodeUI>(true))
        {
            if (ui == null) continue;
            if (ui.node == null || ui.node.branch != PlayerLevel.SkillBranch.Equipment)
                Destroy(ui.gameObject);
        }

        // Какие узлы уже имеют кнопки
        var hasButton = new System.Collections.Generic.HashSet<SkillNode>();
        foreach (SkillNodeUI ui in equipmentContainer.GetComponentsInChildren<SkillNodeUI>(true))
            if (ui != null && ui.node != null) hasButton.Add(ui.node);

        // Узлы ветки по порядку: уровень (тир), затем имя (вид)
        var equipNodes = new System.Collections.Generic.List<SkillNode>();
        foreach (SkillNode n in SkillTreeManager.Instance.allNodes)
        {
            if (n == null || n.branch != PlayerLevel.SkillBranch.Equipment) continue;
            if (hasButton.Contains(n)) continue;
            equipNodes.Add(n);
        }
        if (equipNodes.Count == 0) return;

        equipNodes.Sort((a, b) =>
        {
            int d = a.requiredLevel.CompareTo(b.requiredLevel);
            return d != 0 ? d : string.CompareOrdinal(a.nodeName, b.nodeName);
        });

        // Существующие СВОИ кнопки задают смещение (чужие выше уже помечены на удаление)
        int startIndex = 0;
        foreach (SkillNodeUI ui in equipmentContainer.GetComponentsInChildren<SkillNodeUI>(true))
            if (ui != null && ui.node != null && ui.node.branch == PlayerLevel.SkillBranch.Equipment)
                startIndex++;

        // Если на контейнере висит LayoutGroup (как Grid Layout Group) —
        // раскладкой занимается он, позиции руками не ставим
        bool autoLayout = equipmentContainer.GetComponent<UnityEngine.UI.LayoutGroup>() != null;

        const float cellX = 115f, cellY = 130f;
        const int cols = 9;
        for (int i = 0; i < equipNodes.Count; i++)
        {
            int slot = startIndex + i;
            GameObject go = Instantiate(nodeButtonPrefab, equipmentContainer.transform);
            go.name = "EquipNodeBtn_" + equipNodes[i].name;
            var ui = go.GetComponent<SkillNodeUI>();
            if (ui == null) { Destroy(go); continue; }
            ui.node = equipNodes[i];
            var btn = go.GetComponentInChildren<UnityEngine.UI.Button>();
            if (btn != null) btn.onClick.AddListener(ui.OnClick);
            if (!autoLayout)
            {
                var rt = go.GetComponent<RectTransform>();
                if (rt != null)
                    rt.anchoredPosition = new Vector2((slot % cols) * cellX, -(slot / cols) * cellY);
            }
            ui.Refresh();
        }
        Debug.Log("[SkillTree] Автокнопки экипировки: " + equipNodes.Count);
    }

    void Update()
    {
        if (!isOpen) return;
        if (justShown) { justShown = false; return; }

        bool clicked = Input.GetMouseButtonDown(0) ||
            (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began);
        if (!clicked) return;

        Vector2 pos = Input.touchCount > 0
            ? Input.GetTouch(0).position
            : (Vector2)Input.mousePosition;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (panelRect != null && !RectTransformUtility.RectangleContainsScreenPoint(
                panelRect, pos, canvas != null ? canvas.worldCamera : null))
            Close();
    }

    // ═══════════════════════════════════════════════════════════
    // ОТКРЫТИЕ / ЗАКРЫТИЕ
    // ═══════════════════════════════════════════════════════════
    public void Toggle() { if (isOpen) Close(); else Open(); }

    public void Open()
    {
        skillTreePanel.SetActive(true);
        isOpen = true;
        justShown = true;

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;

        UpdateInfoBar();
        EnsureEquipmentButtons(); // на случай узлов, подъехавших позже Start
        RefreshAll();
    }

    public void Close()
    {
        skillTreePanel.SetActive(false);
        isOpen = false;
        selectedNode = null;
        if (detailPanel != null) detailPanel.SetActive(false);

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;
    }

    // ═══════════════════════════════════════════════════════════
    // ВКЛАДКИ
    // ═══════════════════════════════════════════════════════════
    void SwitchTab(PlayerLevel.SkillBranch branch)
    {
        currentBranch = branch;

        if (combatContainer != null) combatContainer.SetActive(branch == PlayerLevel.SkillBranch.Combat);
        if (farmingContainer != null) farmingContainer.SetActive(branch == PlayerLevel.SkillBranch.Farming);
        if (craftingContainer != null) craftingContainer.SetActive(branch == PlayerLevel.SkillBranch.Crafting);
        if (equipmentContainer != null) equipmentContainer.SetActive(branch == PlayerLevel.SkillBranch.Equipment);

        if (tabCombatBg != null) tabCombatBg.color = branch == PlayerLevel.SkillBranch.Combat ? tabActiveColor : tabInactiveColor;
        if (tabFarmingBg != null) tabFarmingBg.color = branch == PlayerLevel.SkillBranch.Farming ? tabActiveColor : tabInactiveColor;
        if (tabCraftingBg != null) tabCraftingBg.color = branch == PlayerLevel.SkillBranch.Crafting ? tabActiveColor : tabInactiveColor;
        if (tabEquipmentBg != null) tabEquipmentBg.color = branch == PlayerLevel.SkillBranch.Equipment ? tabActiveColor : tabInactiveColor;

        selectedNode = null;
        if (detailPanel != null) detailPanel.SetActive(false);
        ApplyPage();
        RefreshAll();
    }

    /// <summary>Открыть вкладку экипировки извне (повесь на новую кнопку ветки).</summary>
    public void ShowEquipmentTab()
    {
        if (!isOpen) Open();
        SwitchTab(PlayerLevel.SkillBranch.Equipment);
    }

    // ═══════════════════════════════════════════════════════════
    // ПАГИНАЦИЯ
    // ═══════════════════════════════════════════════════════════
    public void NextPage() { pageByBranch[currentBranch] = GetCurrentPage() + 1; ApplyPage(); }
    public void PrevPage() { pageByBranch[currentBranch] = GetCurrentPage() - 1; ApplyPage(); }

    int GetCurrentPage()
    {
        pageByBranch.TryGetValue(currentBranch, out int page);
        return page;
    }

    GameObject CurrentContainer()
    {
        return currentBranch switch
        {
            PlayerLevel.SkillBranch.Farming => farmingContainer,
            PlayerLevel.SkillBranch.Crafting => craftingContainer,
            PlayerLevel.SkillBranch.Equipment => equipmentContainer != null ? equipmentContainer : combatContainer,
            _ => combatContainer,
        };
    }

    void ApplyPage()
    {
        GameObject container = CurrentContainer();
        if (container == null) return;

        int perPage = Mathf.Max(1, nodesPerPage);
        var nodes = container.GetComponentsInChildren<SkillNodeUI>(true);
        int pageCount = Mathf.Max(1, Mathf.CeilToInt(nodes.Length / (float)perPage));
        int page = Mathf.Clamp(GetCurrentPage(), 0, pageCount - 1);
        pageByBranch[currentBranch] = page;

        for (int i = 0; i < nodes.Length; i++)
            nodes[i].gameObject.SetActive(i >= page * perPage && i < (page + 1) * perPage);

        if (pagePrevButton != null) pagePrevButton.interactable = page > 0;
        if (pageNextButton != null) pageNextButton.interactable = page < pageCount - 1;
    }

    // ═══════════════════════════════════════════════════════════
    // ВЫБОР УЗЛА — показывает детали с рангами
    // ═══════════════════════════════════════════════════════════
    public void SelectNode(SkillNodeUI nodeUI)
    {
        selectedNode = nodeUI;
        if (nodeUI == null || nodeUI.node == null)
        {
            if (detailPanel != null) detailPanel.SetActive(false);
            return;
        }

        if (detailPanel != null) detailPanel.SetActive(true);

        SkillNode node = nodeUI.node;
        int currentRank = SkillTreeManager.Instance.GetRank(node);
        bool isMax = SkillTreeManager.Instance.IsMaxRank(node);
        bool available = SkillTreeManager.Instance.IsAvailable(node);

        // Иконка и название
        if (detailIcon != null) detailIcon.sprite = node.icon;
        if (detailName != null) detailName.text = node.nodeName;
        if (detailDescription != null) detailDescription.text = node.description;

        // Ранг "Ранг: 2/5"
        if (detailRank != null)
        {
            detailRank.gameObject.SetActive(node.maxRanks > 1);
            if (node.maxRanks > 1)
                detailRank.text = "Ранг: " + currentRank + "/" + node.maxRanks;
        }

        // Текущий суммарный эффект
        if (detailEffect != null)
            detailEffect.text = currentRank > 0
                ? "Сейчас: " + node.GetEffectDescription(currentRank)
                : "";

        // Эффект следующего ранга
        if (detailNextEffect != null)
            detailNextEffect.text = !isMax
                ? "Следующий ранг: " + node.GetEffectDescription(1)
                : "✓ Максимальный ранг";

        // Стоимость следующего ранга (растёт с каждым рангом)
        if (detailCost != null)
        {
            if (!isMax)
            {
                var (pts, gold) = SkillTreeManager.Instance.GetNextRankCost(node);
                string ptsStr = pts > 0 ? pts + " очко(а)" : "";
                string goldStr = gold > 0 ? gold + " золота" : "";
                string sep = (ptsStr.Length > 0 && goldStr.Length > 0) ? " + " : "";
                detailCost.text = "Стоимость: " + ptsStr + sep + goldStr;
            }
            else
            {
                detailCost.text = "";
            }
        }

        // Требования
        if (detailRequirements != null)
        {
            string reqs = "Требует: уровень " + node.requiredLevel;
            if (node.requiredNodes != null && node.requiredNodes.Length > 0)
            {
                reqs += "\nПредыдущие: ";
                foreach (SkillNode req in node.requiredNodes)
                    if (req != null) reqs += req.nodeName + " ";
            }
            detailRequirements.text = reqs;
        }

        // Кнопка с текстом ранга
        if (unlockButton != null)
        {
            unlockButton.interactable = available;
            if (unlockButtonText != null)
            {
                if (isMax)
                    unlockButtonText.text = "✓ МАКСИМУМ";
                else if (currentRank > 0)
                    unlockButtonText.text = "Улучшить (" + (currentRank + 1) + "/" + node.maxRanks + ")";
                else if (available)
                    unlockButtonText.text = "Изучить";
                else
                    unlockButtonText.text = "Заблокировано";
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ПРОКАЧКА
    // ═══════════════════════════════════════════════════════════
    void OnUnlockClick()
    {
        if (selectedNode == null || selectedNode.node == null) return;

        bool success = SkillTreeManager.Instance.TryUnlock(selectedNode.node);
        if (success)
        {
            UpdateInfoBar();   // сразу обновляем золото и очки в инфобаре
            RefreshAll();
            SelectNode(selectedNode); // обновляем панель деталей
        }
    }

    void OnResetClick()
    {
        bool success = SkillTreeManager.Instance.TryReset();
        if (success)
        {
            UpdateInfoBar();
            RefreshAll();
            if (detailPanel != null) detailPanel.SetActive(false);
            selectedNode = null;
        }
    }

    // ═══════════════════════════════════════════════════════════
    // ОБНОВЛЕНИЕ
    // ═══════════════════════════════════════════════════════════
    public void RefreshAll()
    {
        if (!isOpen) return;
        ApplyPage();
        RefreshContainer(combatContainer);
        RefreshContainer(farmingContainer);
        RefreshContainer(craftingContainer);
        RefreshContainer(equipmentContainer);
    }

    void RefreshContainer(GameObject container)
    {
        if (container == null) return;
        foreach (SkillNodeUI nodeUI in container.GetComponentsInChildren<SkillNodeUI>())
            nodeUI.Refresh();
    }

    void UpdateInfoBar()
    {
        if (PlayerLevel.Instance != null)
        {
            if (levelText != null) levelText.text = "Уровень " + PlayerLevel.Instance.TotalLevel;
            if (skillPointsText != null) skillPointsText.text = "Очки: " + PlayerLevel.Instance.AvailableSkillPoints;
        }
        if (goldText != null && CurrencyManager.Instance != null)
            goldText.text = "Золото: " + CurrencyManager.Instance.Gold;
    }
}