using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Панель Морека: подарок-удочка, продажа всей рыбы (+50% к цене),
/// коллекция (сетка видов). Строится кодом под Canvas.
/// </summary>
public class MorekUI : MonoBehaviour
{
    public static MorekUI Instance
    {
        get
        {
            if (_instance == null)
            {
                Canvas canvas = GameObject.Find("Canvas") != null
                    ? GameObject.Find("Canvas").GetComponent<Canvas>()
                    : FindFirstObjectByType<Canvas>();
                if (canvas == null) return null;
                var go = new GameObject("MorekUI");
                go.transform.SetParent(canvas.transform, false);
                _instance = go.AddComponent<MorekUI>();
            }
            return _instance;
        }
    }
    private static MorekUI _instance;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        BuildUI();
        root.SetActive(false);
        collectionRoot.SetActive(false);
    }

    private GameObject root;
    private GameObject collectionRoot;
    private Transform collectionGrid;
    private TMP_Text rodButtonText;
    private Button rodButton;
    private bool isOpen;

    public void Open()
    {
        if (root == null) BuildUI();
        root.SetActive(true);
        collectionRoot.SetActive(false);
        isOpen = true;
        RefreshRodButton();

        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = false;
        if (InventoryUI.Instance != null) InventoryUI.Instance.OpenInventory();
    }

    public void Close()
    {
        isOpen = false;
        if (root != null) root.SetActive(false);
        if (collectionRoot != null) collectionRoot.SetActive(false);
        PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
        if (pm != null) pm.enabled = true;
        if (InventoryUI.Instance != null) InventoryUI.Instance.CloseInventory();
    }

    public bool IsOpen() => isOpen;

    void RefreshRodButton()
    {
        bool gifted = FishingController.Instance != null && FishingController.Instance.HasRodGift();
        if (rodButtonText != null)
            rodButtonText.text = gifted ? "Удочка получена ✓" : "Взять удочку";
        if (rodButton != null) rodButton.interactable = !gifted;
    }

    void OnRodClick() => GiveRod();

    /// <summary>Подарок-удочка (зовёт диалог Морека и старая панель).</summary>
    public void GiveRod()
    {
        if (FishingController.Instance != null && FishingController.Instance.HasRodGift()) return;
        ItemData rod = ItemDatabase.Find("WoodRod_Common");
        if (rod == null)
        {
            ActionLogUI.Show("[Морек] Удочки ещё не готовы… (Tools → Equipment → 1)");
            return;
        }
        if (InventoryUI.Instance != null) InventoryUI.Instance.AddItem(rod, 1);
        FishingController.Instance?.MarkRodGifted();
        ActionLogUI.Show("[Морек] Держи удочку, рыбак! Встань у воды и бей.");
        RefreshRodButton();
    }

    void OnSellClick()
    {
        // Продажа — через общую Sell Panel в режиме Морека
        Close();
        if (SellUI.Instance != null) SellUI.Instance.OpenFish();
    }

    void OnCollectionClick() => ToggleCollection();

    /// <summary>Панель коллекции (диалог Морека и старая панель).</summary>
    public void ToggleCollection()
    {
        if (root == null) BuildUI();
        // Панель коллекции живёт отдельно — открываем её поверх всего
        if (!root.activeSelf)
        {
            root.SetActive(true);
            PlayerMovement pm = FindFirstObjectByType<PlayerMovement>();
            if (pm != null) pm.enabled = false;
        }
        bool show = !collectionRoot.activeSelf || !root.activeSelf;
        collectionRoot.SetActive(show);
        if (show) RefreshCollection();
    }

    void RefreshCollection()
    {
        foreach (Transform child in collectionGrid)
            Destroy(child.gameObject);

        FishData[] all = Resources.LoadAll<FishData>("Fish");
        System.Array.Sort(all, (a, b) => a.difficulty.CompareTo(b.difficulty));
        foreach (FishData f in all)
        {
            if (f == null) continue;
            bool caught = FishingController.Instance != null && FishingController.Instance.IsCaught(f);

            GameObject cell = new GameObject("Fish_" + f.name);
            cell.transform.SetParent(collectionGrid, false);
            var rt = cell.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100f, 120f);
            var img = cell.AddComponent<Image>();
            img.sprite = f.icon;
            img.color = caught ? Color.white : Color.black;
            img.preserveAspect = true;

            GameObject label = new GameObject("Label");
            label.transform.SetParent(cell.transform, false);
            var lrt = label.AddComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0, 0);
            lrt.anchorMax = new Vector2(1, 0);
            lrt.offsetMin = new Vector2(0, -30f);
            lrt.offsetMax = new Vector2(0, 0);
            var tmp = label.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 18;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text = caught ? f.fishName : "???";
        }
    }

    // ═══════════════════════════════════════════════════════════
    void BuildUI()
    {
        root = new GameObject("MorekPanel");
        root.transform.SetParent(transform, false);
        var rootRt = root.AddComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.sizeDelta = new Vector2(440f, 420f);
        rootRt.anchoredPosition = Vector2.zero;
        var bg = root.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.1f, 0.15f, 0.95f);

        MakeText(root.transform, "Морек", 36, new Vector2(0, 165f));

        rodButton = MakeButton(root.transform, "Взять удочку", new Vector2(0, 90f));
        rodButton.onClick.AddListener(OnRodClick);
        rodButtonText = rodButton.GetComponentInChildren<TMP_Text>();

        var sell = MakeButton(root.transform, "Продать рыбу (+50%)", new Vector2(0, 10f));
        sell.onClick.AddListener(OnSellClick);

        var coll = MakeButton(root.transform, "Коллекция", new Vector2(0, -70f));
        coll.onClick.AddListener(OnCollectionClick);

        var close = MakeButton(root.transform, "Закрыть", new Vector2(0, -150f));
        close.onClick.AddListener(Close);

        collectionRoot = new GameObject("Collection");
        collectionRoot.transform.SetParent(root.transform, false);
        var cRt = collectionRoot.AddComponent<RectTransform>();
        cRt.anchorMin = new Vector2(0.5f, 0.5f);
        cRt.anchorMax = new Vector2(0.5f, 0.5f);
        cRt.sizeDelta = new Vector2(400f, 160f);
        cRt.anchoredPosition = new Vector2(0, -290f);
        var cBg = collectionRoot.AddComponent<Image>();
        cBg.color = new Color(0f, 0f, 0f, 0.9f);

        GameObject grid = new GameObject("Grid");
        grid.transform.SetParent(collectionRoot.transform, false);
        var gRt = grid.AddComponent<RectTransform>();
        gRt.anchorMin = new Vector2(0.5f, 0.5f);
        gRt.anchorMax = new Vector2(0.5f, 0.5f);
        gRt.sizeDelta = new Vector2(360f, 130f);
        var layout = grid.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 15f;
        collectionGrid = grid.transform;
    }

    Button MakeButton(Transform parent, string text, Vector2 pos)
    {
        GameObject go = new GameObject("Btn_" + text);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(320f, 60f);
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        img.color = new Color(0.15f, 0.3f, 0.45f, 1f);
        var btn = go.AddComponent<Button>();

        GameObject label = new GameObject("Label");
        label.transform.SetParent(go.transform, false);
        var lrt = label.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
        var tmp = label.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = 26;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text = text;
        return btn;
    }

    TMP_Text MakeText(Transform parent, string text, int size, Vector2 pos)
    {
        GameObject go = new GameObject("Title");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(400f, 60f);
        rt.anchoredPosition = pos;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.text = text;
        return tmp;
    }
}
