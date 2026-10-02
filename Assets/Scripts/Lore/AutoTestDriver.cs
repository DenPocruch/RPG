#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;

/// <summary>
/// Автотест-водитель: даёт агенту «руки» через MCP update_component.
/// Протокол: агент пишет команду в поле command, драйвер выполняет и кладёт
/// итог в lastResult (читается через get_gameobject с properties).
/// Команды (аргументы через |):
///   step | attack | pos | open_inv | hotbar|i
///   joy|x|y|sec — подержать джойстик (реальный путь ввода!)
///   talk|MayorNPC — подойти и поговорить (дистанция + Interact)
///   dlg_list | dlg_choose|i | dlg_skip
///   click|ИмяКнопки — клик по активной UI-кнопке
///   where|AssetName — где лежит предмет (pack/hotbar)
///   drag_to_hotbar|AssetName — перетащить из рюкзака в хотбар (как драг)
/// Только для редактора, в сборку на телефон не попадает.
/// </summary>
public class AutoTestDriver : MonoBehaviour
{
    public string command = "";
    public string lastResult = "ready";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<AutoTestDriver>() != null) return;
        var go = new GameObject("AutoTestDriver");
        DontDestroyOnLoad(go);
        go.AddComponent<AutoTestDriver>();
    }

    void Update()
    {
        if (string.IsNullOrEmpty(command)) return;
        TakeAndExecute();
    }

    // update_component пишет поле через сериализацию — в редакторе это дёргает
    // OnValidate сразу, не дожидаясь Update. Выполняем тут же (Update — запасной путь).
    bool validating;
    void OnValidate()
    {
        if (!Application.isPlaying) return;
        if (validating || string.IsNullOrEmpty(command)) return;
        validating = true;
        Debug.Log("[AutoTestDriver] cmd: " + command);
        TakeAndExecute();
        validating = false;
    }

    void TakeAndExecute()
    {
        string cmd = command;
        command = "";
        try { Execute(cmd); }
        catch (System.Exception e) { lastResult = "ERR: " + e.Message; }
        Debug.Log("[AutoTestDriver] => " + lastResult);
    }

    GameObject Player() => GameObject.FindWithTag("Player");

    void Execute(string cmd)
    {
        string[] p = cmd.Split('|');
        switch (p[0])
        {
            case "step":
                lastResult = "idx=" + TutorialManager.ProgressIndex()
                    + " id=" + TutorialManager.CurrentStep()
                    + " done=" + (TutorialManager.Instance != null && TutorialManager.Instance.IsDone);
                break;
            case "pos":
                var pl0 = Player();
                lastResult = pl0 != null ? Vec(pl0.transform.position)
                    + " scene=" + pl0.scene.name : "ERR: нет игрока";
                break;
            case "attack":
                var pm = Player()?.GetComponent<PlayerMovement>();
                if (pm == null) { lastResult = "ERR: нет PlayerMovement"; break; }
                pm.Attack();
                lastResult = "OK attack";
                break;
            case "open_inv":
                if (InventoryUI.Instance == null) { lastResult = "ERR: нет InventoryUI"; break; }
                InventoryUI.Instance.ToggleInventory();
                lastResult = "OK inv";
                break;
            case "hotbar":
                HotbarManager.Instance?.SetActiveSlot(int.Parse(p[1]));
                lastResult = "OK hotbar " + p[1];
                break;
            case "joy":
                StartCoroutine(JoyHold(float.Parse(p[1]), float.Parse(p[2]), float.Parse(p[3])));
                lastResult = "RUN joy";
                break;
            case "talk":
                TalkTo(p[1]);
                break;
            case "dlg_list":
                lastResult = ListOptions();
                break;
            case "dlg_choose":
                ChooseOption(int.Parse(p[1]));
                break;
            case "dlg_skip":
                var dm0 = DialogueManager.Instance;
                if (dm0 != null && dm0.advanceButton != null) dm0.advanceButton.onClick.Invoke();
                lastResult = "OK skip";
                break;
            case "click":
                ClickButton(p[1]);
                break;
            case "where":
                lastResult = Where(p[1]);
                break;
            case "drag_to_hotbar":
                DragToHotbar(p[1]);
                break;
            case "notify":
                TutorialManager.Notify(p[1], p.Length > 2 ? int.Parse(p[2]) : 0);
                lastResult = "OK notify " + p[1];
                break;
            case "fastdlg":
                if (DialogueManager.Instance != null) DialogueManager.Instance.charsPerSecond = 1500f;
                lastResult = "OK fastdlg";
                break;
            case "batch":
                StartCoroutine(RunBatch(p.Length > 1 ? p[1] : ""));
                lastResult = "RUN batch";
                break;
            case "close_all":
                CloseAll();
                break;
            case "farm_all":
                StartCoroutine(FarmAll());
                lastResult = "RUN farm_all";
                break;
            case "ripen":
                Ripen();
                break;
            case "harvest_all":
                StartCoroutine(HarvestAll());
                lastResult = "RUN harvest_all";
                break;
            case "hunt":
                StartCoroutine(Hunt(false));
                lastResult = "RUN hunt";
                break;
            case "hunt_any":
                StartCoroutine(Hunt(true));
                lastResult = "RUN hunt_any";
                break;
            default:
                lastResult = "ERR: unknown " + p[0];
                break;
        }
    }

    static string Vec(Vector3 v) => "(" + v.x.ToString("F2") + "," + v.y.ToString("F2") + ")";

    // ── Джойстик по-настоящему: фейк-драг через публичный OnDrag ──
    IEnumerator JoyHold(float x, float y, float sec)
    {
        var pl = Player();
        var pm = pl != null ? pl.GetComponent<PlayerMovement>() : null;
        Joystick joy = pm != null ? pm.joystick : null;
        if (joy == null || pl == null) { lastResult = "ERR: нет джойстика"; yield break; }
        Vector3 before = pl.transform.position;
        var ed = new PointerEventData(EventSystem.current);
        Vector2 center = RectTransformUtility.WorldToScreenPoint(null, joy.transform.position);
        ed.position = center + new Vector2(x, y).normalized * 120f;
        ed.button = PointerEventData.InputButton.Left;
        ((IPointerDownHandler)joy).OnPointerDown(ed);
        float t = 0f;
        while (t < sec)
        {
            t += Time.deltaTime;
            ((IDragHandler)joy).OnDrag(ed);
            yield return null;
        }
        ((IPointerUpHandler)joy).OnPointerUp(ed);
        Vector3 after = pl.transform.position;
        lastResult = "OK joy dir=" + joy.Direction
            + " from=" + Vec(before) + " to=" + Vec(after);
    }

    // ── Макросы фермы: всё настоящими механиками, без пауз между ударами ──
    // Точка мотыги фиксирована: игрок встаёт на cell-(0,0.46), hoePoint бьёт в cell.
    readonly System.Collections.Generic.List<Vector3> lastCells =
        new System.Collections.Generic.List<Vector3>();
    static readonly Vector2[] CellGrid =
        { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1),
          new Vector2(1, 1), new Vector2(-1, 0), new Vector2(-1, 1) };

    bool SelectHotbar(string asset)
    {
        var hb = HotbarManager.Instance;
        if (hb == null || hb.slots == null) return false;
        foreach (var s in hb.slots)
            if (s != null && !s.IsEmpty() && s.currentItem != null && s.currentItem.name == asset)
            { hb.SetActiveSlot(s.slotIndex); return true; }
        return false;
    }

    void Face(Vector2 dir)
    {
        var pm = Player()?.GetComponent<PlayerMovement>();
        if (pm == null) return;
        var t = typeof(PlayerMovement);
        var fx = t.GetField("lastMoveX", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var fy = t.GetField("lastMoveY", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (fx != null) { fx.SetValue(pm, dir.x); fy.SetValue(pm, dir.y); }
    }

    void CloseAll()
    {
        var sb = new System.Text.StringBuilder();
        var dm = DialogueManager.Instance;
        if (dm != null && dm.IsOpen) { dm.EndDialogue(); sb.Append("dlg;"); }
        if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen()) { InventoryUI.Instance.CloseInventory(); sb.Append("inv;"); }
        if (ShopUI.Instance != null && ShopUI.Instance.IsOpen()) { ShopUI.Instance.Close(); sb.Append("shop;"); }
        if (SellUI.Instance != null && SellUI.Instance.IsOpen()) { SellUI.Instance.Close(); sb.Append("sell;"); }
        if (CookUI.Instance != null && CookUI.Instance.IsOpen()) { CookUI.Instance.Close(); sb.Append("cook;"); }
        lastResult = sb.Length > 0 ? "OK closed " + sb : "OK nothing open";
    }

    IEnumerator FarmAll()
    {
        var spot = GameObject.Find("TutorialDigSpot");
        var pl = Player();
        var pm = pl != null ? pl.GetComponent<PlayerMovement>() : null;
        if (spot == null || pl == null || pm == null) { lastResult = "ERR: нет точки/игрока"; yield break; }
        lastCells.Clear();
        foreach (var g in CellGrid) lastCells.Add(spot.transform.position + (Vector3)g);

        if (!SelectHotbar("Hoe")) { lastResult = "ERR: нет мотыги в хотбаре"; yield break; }
        foreach (var c in lastCells)
        {
            pl.transform.position = c - new Vector3(0f, 0.46f, 0f);
            Face(Vector2.up);
            pm.Attack();
            yield return new WaitForSeconds(1f);
        }
        if (!SelectHotbar("Wheat Seeds")) { lastResult = "ERR: нет семян; step=" + TutorialManager.CurrentStep(); yield break; }
        foreach (var c in lastCells)
        {
            pl.transform.position = c - new Vector3(0f, 0.46f, 0f);
            Face(Vector2.up);
            pm.Attack();
            yield return new WaitForSeconds(0.8f);
        }
        if (!SelectHotbar("WateringCan")) { lastResult = "ERR: нет лейки; step=" + TutorialManager.CurrentStep(); yield break; }
        var well = FindFirstObjectByType<WellInteraction>();
        if (well == null) { lastResult = "ERR: нет колодца"; yield break; }
        pl.transform.position = well.transform.position + new Vector3(0.8f, 0f, 0f);
        Face(Vector2.left);
        well.Interact(pl);
        yield return new WaitForSeconds(2.6f);
        well.Interact(pl);
        yield return new WaitForSeconds(0.5f);
        var slot = HotbarManager.Instance?.GetActiveSlot();
        if (slot == null || !slot.HasWater())
        {
            well.Interact(pl);
            yield return new WaitForSeconds(2.6f);
            well.Interact(pl);
            yield return new WaitForSeconds(0.5f);
        }
        foreach (var c in lastCells)
        {
            pl.transform.position = c - new Vector3(0f, 0.46f, 0f);
            Face(Vector2.up);
            pm.Attack();
            yield return new WaitForSeconds(1.4f);
        }
        lastResult = "OK farm_all step=" + TutorialManager.CurrentStep();
        Debug.Log("[AutoTestDriver] => " + lastResult);
    }

    // Чит дозревания: стадии — максимум, механика срезания/подбора настоящая
    void Ripen()
    {
        int n = 0;
        foreach (var c in FindObjectsByType<CropTile>(FindObjectsSortMode.None))
        {
            if (c == null) continue;
            var stages = c.cropData != null ? c.cropData.growthStages : null;
            c.currentStage = stages != null && stages.Length > 0 ? stages.Length - 1 : 3;
            c.isReady = true;
            c.isWatered = true;
            n++;
        }
        lastResult = "OK ripen " + n;
    }

    IEnumerator HarvestAll()
    {
        var pl = Player();
        var pm = pl != null ? pl.GetComponent<PlayerMovement>() : null;
        if (pl == null || pm == null) { lastResult = "ERR: нет игрока"; yield break; }
        if (!SelectHotbar("Sickle")) { lastResult = "ERR: нет серпа"; yield break; }
        if (lastCells.Count == 0) { lastResult = "ERR: нет клеток (сначала farm_all)"; yield break; }
        foreach (var c in lastCells)
        {
            pl.transform.position = c - new Vector3(0f, 0.46f, 0f);
            Face(Vector2.up);
            pm.Attack();
            yield return new WaitForSeconds(1f);
        }
        // Проход по клеткам — подобрать упавшее (магнит+подбор вплотную)
        foreach (var c in lastCells)
        {
            pl.transform.position = c;
            yield return new WaitForSeconds(0.6f);
        }
        lastResult = "OK harvest_all step=" + TutorialManager.CurrentStep();
        Debug.Log("[AutoTestDriver] => " + lastResult);
    }
    // choose — выбрать опцию и дождаться новых кнопок/закрытия (до 6с)
    // ── Пакет команд за один запрос: "choose#0;wait#1;list;step"
    // choose — выбрать опцию и дождаться новых кнопок/закрытия (до 6с)
    IEnumerator RunBatch(string arg)
    {
        var sb = new System.Text.StringBuilder();
        foreach (string raw in arg.Split(';'))
        {
            if (string.IsNullOrEmpty(raw)) continue;
            string[] s = raw.Split('#');
            string c = s[0].Trim();
            string a = s.Length > 1 ? s[1].Trim() : "";
            if (c == "wait") { yield return new WaitForSeconds(float.Parse(a)); sb.Append("waited;"); }
            else if (c == "choose")
            {
                ChooseOption(int.Parse(a));
                sb.Append("chose=").Append(lastResult).Append(";");
                yield return WaitSettled();
                var dm = DialogueManager.Instance;
                bool open = dm != null && dm.IsOpen;
                sb.Append(open ? "open:" + ListOptions() : "closed").Append(";");
            }
            else if (c == "skip") { var d = DialogueManager.Instance; if (d != null && d.advanceButton != null) d.advanceButton.onClick.Invoke(); sb.Append("skip;"); }
            else if (c == "list") { sb.Append(ListOptions()).Append(";"); }
            else if (c == "step")
            {
                sb.Append("idx=").Append(TutorialManager.ProgressIndex())
                  .Append(" id=").Append(TutorialManager.CurrentStep()).Append(";");
            }
            else if (c == "attack")
            {
                var pmm = Player()?.GetComponent<PlayerMovement>();
                if (pmm != null) pmm.Attack();
                sb.Append("attack;");
                yield return new WaitForSeconds(0.5f);
            }
            else sb.Append("?").Append(c).Append(";");
        }
        lastResult = sb.ToString();
        Debug.Log("[AutoTestDriver] => " + lastResult);
    }

    // Ждать пока диалог закроется или появятся кнопки (кадры идут сами)
    IEnumerator WaitSettled()
    {
        float t = 0f;
        while (t < 6f)
        {
            yield return new WaitForSeconds(0.3f);
            t += 0.3f;
            var dm = DialogueManager.Instance;
            if (dm == null || !dm.IsOpen) yield break;
            if (dm.optionsContainer != null
                && dm.optionsContainer.GetComponentsInChildren<Button>(false).Length > 0)
                yield break;
        }
    }
    // ── Охота: подойти к ближайшему слайму/врагу, встать лицом, добить ──
    // Направление атаки задаём через приватные lastMoveX/Y рефлексией (только тесты).
    IEnumerator Hunt(bool anyEnemy)
    {
        var pl = Player();
        var pm = pl != null ? pl.GetComponent<PlayerMovement>() : null;
        if (pl == null || pm == null) { lastResult = "ERR: нет игрока"; yield break; }
        var fx = typeof(PlayerMovement).GetField("lastMoveX",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var fy = typeof(PlayerMovement).GetField("lastMoveY",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        float t = 0f;
        int swings = 0;
        while (t < 60f)
        {
            t += 0.2f;
            EnemyHealth best = null;
            float bestD = float.MaxValue;
            foreach (var e in FindObjectsByType<EnemyHealth>(FindObjectsSortMode.None))
            {
                if (e == null || e.currentHealth <= 0) continue;
                if (!anyEnemy && e.GetComponent<TutorialSlime>() == null) continue;
                float d = Vector2.Distance(pl.transform.position, e.transform.position);
                if (d < bestD) { bestD = d; best = e; }
            }
            if (best == null) { lastResult = "OK hunt done swings=" + swings; yield break; }
            Vector2 dir = ((Vector2)best.transform.position - (Vector2)pl.transform.position).normalized;
            if (fx != null) { fx.SetValue(pm, dir.x); fy.SetValue(pm, dir.y); }
            if (bestD > 1.2f)
                pl.transform.position += (Vector3)(dir * 2.5f * 0.2f);
            else
            {
                pm.Attack();
                swings++;
                yield return new WaitForSeconds(0.7f);
                continue;
            }
            yield return new WaitForSeconds(0.2f);
        }
        lastResult = "WARN hunt timeout swings=" + swings;
    }

    // ── Разговор: подойти вплотную + тот же Interact, что по кнопке атаки ──
    void TalkTo(string typeName)
    {
        var pl = Player();
        if (pl == null) { lastResult = "ERR: нет игрока"; return; }
        var all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        NPCInteractable inter = null;
        foreach (var m in all)
        {
            if (m == null || m.GetType().Name != typeName) continue;
            inter = m.GetComponent<NPCInteractable>();
            if (inter != null) break;
        }
        if (inter == null) { lastResult = "ERR: нет " + typeName; return; }
        pl.transform.position = inter.transform.position + new Vector3(0.8f, 0f, 0f);
        inter.Interact(pl);
        bool open = DialogueManager.Instance != null && DialogueManager.Instance.IsOpen;
        lastResult = open ? "OK talk->dialog open" : "WARN talk: диалог не открылся";
    }

    string ListOptions()
    {
        var dm = DialogueManager.Instance;
        if (dm == null || !dm.IsOpen) return "ERR: диалог закрыт";
        if (dm.optionsContainer == null) return "ERR: нет контейнера";
        var btns = dm.optionsContainer.GetComponentsInChildren<Button>(false);
        if (btns.Length == 0) return "EMPTY";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < btns.Length; i++)
        {
            var t = btns[i].GetComponentInChildren<TMP_Text>();
            sb.Append(i).Append(": ").Append(t != null ? t.text : "?").Append(" | ");
        }
        return sb.ToString();
    }

    void ChooseOption(int i)
    {
        var dm = DialogueManager.Instance;
        if (dm == null || !dm.IsOpen) { lastResult = "ERR: диалог закрыт"; return; }
        var btns = dm.optionsContainer.GetComponentsInChildren<Button>(false);
        if (i < 0 || i >= btns.Length) { lastResult = "ERR: опций " + btns.Length; return; }
        var t = btns[i].GetComponentInChildren<TMP_Text>();
        btns[i].onClick.Invoke();
        lastResult = "OK chose[" + i + "]=" + (t != null ? t.text : "?");
    }

    void ClickButton(string name)
    {
        var btns = FindObjectsByType<Button>(FindObjectsSortMode.None);
        foreach (var b in btns)
        {
            if (b == null || !b.gameObject.activeInHierarchy) continue;
            if (b.name.Trim() != name.Trim()) continue;
            b.onClick.Invoke();
            lastResult = "OK click " + b.name;
            return;
        }
        lastResult = "ERR: кнопка не найдена: " + name;
    }

    string Where(string asset)
    {
        var sb = new System.Text.StringBuilder();
        var inv = InventoryUI.Instance;
        if (inv != null && inv.slots != null)
            foreach (var s in inv.slots)
                if (s != null && !s.IsEmpty() && s.currentItem != null && s.currentItem.name == asset)
                    sb.Append("pack:").Append(s.slotIndex).Append("x").Append(s.quantity).Append(" ");
        var hb = HotbarManager.Instance;
        if (hb != null && hb.slots != null)
            foreach (var s in hb.slots)
                if (s != null && !s.IsEmpty() && s.currentItem != null && s.currentItem.name == asset)
                    sb.Append("hotbar:").Append(s.slotIndex).Append("x").Append(s.quantity).Append(" ");
        return sb.Length > 0 ? sb.ToString() : "NONE";
    }

    // Эмуляция drag pack→hotbar через те же публичные методы, что жмёт игрок
    void DragToHotbar(string asset)
    {
        var inv = InventoryUI.Instance;
        var hb = HotbarManager.Instance;
        if (inv == null || hb == null) { lastResult = "ERR: нет UI"; return; }
        InventorySlot src = null;
        foreach (var s in inv.slots)
            if (s != null && !s.IsEmpty() && s.currentItem != null && s.currentItem.name == asset) { src = s; break; }
        if (src == null) { lastResult = "ERR: нет в рюкзаке"; return; }
        foreach (var s in hb.slots)
        {
            if (s == null || !s.IsEmpty()) continue;
            hb.SetItemInSlot(s.slotIndex, src.currentItem, src.quantity);
            src.ClearSlot();
            lastResult = "OK drag->hotbar " + s.slotIndex;
            return;
        }
        lastResult = "ERR: хотбар полон";
    }
}
#endif
