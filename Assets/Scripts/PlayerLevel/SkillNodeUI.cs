using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SkillNodeUI : MonoBehaviour
{
    [Header("Данные узла")]
    public SkillNode node;

    [Header("UI компоненты")]
    public Image iconImage;
    public Image frameBorder;
    public Image lockIcon;
    public TMP_Text nameText;
    public TMP_Text costText;
    public TMP_Text rankText;    // "2/5" — ранги

    [Header("Цвета состояний")]
    public Color colorLocked = new Color(0.3f, 0.3f, 0.3f);
    public Color colorAvailable = new Color(1f, 0.9f, 0.3f);
    public Color colorUnlocked = new Color(0.3f, 1f, 0.4f);

    [Header("Цвета иконки")]
    public Color iconLocked = new Color(1f, 1f, 1f, 0.3f);
    public Color iconAvailable = new Color(1f, 1f, 1f, 1f);
    public Color iconUnlocked = new Color(1f, 1f, 1f, 1f);

    public enum NodeState { Locked, Available, Unlocked }
    private NodeState currentState = NodeState.Locked;

    public void Refresh()
    {
        if (node == null) return;

        // Состояние
        bool isMax = SkillTreeManager.Instance.IsMaxRank(node);
        if (isMax)
            currentState = NodeState.Unlocked;
        else if (SkillTreeManager.Instance.IsUnlocked(node) || SkillTreeManager.Instance.IsAvailable(node))
            currentState = NodeState.Available;
        else
            currentState = NodeState.Locked;

        // Иконка
        if (iconImage != null)
        {
            iconImage.sprite = node.icon;
            iconImage.color = currentState == NodeState.Locked ? iconLocked : iconAvailable;
        }

        // Рамка
        if (frameBorder != null)
        {
            switch (currentState)
            {
                case NodeState.Locked: frameBorder.color = colorLocked; break;
                case NodeState.Available: frameBorder.color = colorAvailable; break;
                case NodeState.Unlocked: frameBorder.color = colorUnlocked; break;
            }
        }

        // Замочек
        if (lockIcon != null)
            lockIcon.enabled = currentState == NodeState.Locked;

        // Название
        if (nameText != null)
            nameText.text = node.nodeName;

        // Ранги
        int currentRank = SkillTreeManager.Instance.GetRank(node);

        if (rankText != null)
        {
            if (node.maxRanks > 1)
            {
                rankText.gameObject.SetActive(true);
                rankText.text = currentRank + "/" + node.maxRanks;
                rankText.color = isMax ? colorUnlocked : Color.white;
            }
            else
            {
                rankText.gameObject.SetActive(false);
            }
        }

        // Стоимость
        if (costText != null)
        {
            if (isMax && node.maxRanks == 1)
            {
                costText.text = "✓";
                costText.color = colorUnlocked;
            }
            else if (isMax)
            {
                costText.text = "МАКС";
                costText.color = colorUnlocked;
            }
            else
            {
                var (pts, gold) = SkillTreeManager.Instance.GetNextRankCost(node);
                string ptsStr = pts > 0 ? pts + "✦" : "";
                string goldStr = gold > 0 ? gold + "g" : "";
                string sep = (ptsStr.Length > 0 && goldStr.Length > 0) ? "/" : "";
                costText.text = ptsStr + sep + goldStr;
                costText.color = currentState == NodeState.Available
                    ? Color.white
                    : new Color(1f, 1f, 1f, 0.5f);
            }
        }
    }

    public NodeState GetState() => currentState;

    public void OnClick()
    {
        if (SkillTreeUI.Instance != null)
            SkillTreeUI.Instance.SelectNode(this);
    }
}