using UnityEngine;
using TMPro;

public class DamagePopup : MonoBehaviour
{
    [Header("Текст")]
    public TextMeshPro text;

    [Header("Анимация монетки (только для Gold)")]
    public SpriteRenderer coinSprite;   // дочерний объект CoinSprite
    public Sprite[] coinFrames;   // Money_0 ... Money_5
    public float coinFPS = 12f;
    public Vector2 coinOffset = new Vector2(-0.3f, 0f); // левее текста

    [Header("Параметры полёта")]
    public float lifetime = 1f;
    public float floatSpeed = 1.5f;
    public float horizontalSpread = 0.5f;
    public float scaleStart = 1f;
    public float scaleEnd = 0.6f;

    private float timer;
    private Vector3 velocity;
    private Color startColor;
    private bool isGold = false;
    private float coinTimer = 0f;
    private int coinFrame = 0;

    public enum PopupType { Normal, Crit, Dodge, Block, Heal, Gold }

    public void Setup(float damage, PopupType type)
    {
        if (text == null) text = GetComponent<TextMeshPro>();
        if (text == null) return;

        // Поверх YSort
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null) { mr.sortingLayerName = "Default"; mr.sortingOrder = 10000; }

        // Монетка по умолчанию скрыта
        isGold = false;
        if (coinSprite != null) coinSprite.enabled = false;

        switch (type)
        {
            case PopupType.Normal:
                text.text = damage.ToString("0");
                text.color = Color.white;
                text.fontSize = 4f;
                text.fontStyle = FontStyles.Normal;
                break;

            case PopupType.Crit:
                text.text = damage.ToString("0") + "!";
                text.color = new Color(1f, 0.5f, 0f);
                text.fontSize = 6f;
                text.fontStyle = FontStyles.Bold;
                break;

            case PopupType.Dodge:
                text.text = "Промах!";
                text.color = new Color(0.5f, 0.8f, 1f);
                text.fontSize = 4f;
                text.fontStyle = FontStyles.Normal;
                break;

            case PopupType.Block:
                text.text = "Блок " + damage.ToString("0");
                text.color = new Color(0.7f, 0.7f, 1f);
                text.fontSize = 4f;
                text.fontStyle = FontStyles.Normal;
                break;

            case PopupType.Heal:
                text.text = "+" + damage.ToString("0");
                text.color = new Color(0.3f, 1f, 0.3f);
                text.fontSize = 4f;
                text.fontStyle = FontStyles.Normal;
                break;

            case PopupType.Gold:
                text.text = "+" + ((int)damage).ToString();
                text.color = new Color(1f, 0.85f, 0f);
                text.fontSize = 4.5f;
                text.fontStyle = FontStyles.Bold;
                isGold = true;

                // Включаем и позиционируем монетку
                if (coinSprite != null && coinFrames != null && coinFrames.Length > 0)
                {
                    coinSprite.enabled = true;
                    coinSprite.sortingLayerName = "Default";
                    coinSprite.sortingOrder = 10001; // поверх текста
                    coinSprite.transform.localPosition = coinOffset;
                    coinSprite.sprite = coinFrames[0];
                    coinFrame = 0;
                    coinTimer = 0f;
                }
                break;
        }

        startColor = text.color;

        float xOffset = Random.Range(-horizontalSpread, horizontalSpread);
        velocity = new Vector3(xOffset, floatSpeed, 0);
        transform.localScale = Vector3.one * scaleStart;
        timer = 0f;
    }

    void Update()
    {
        timer += Time.deltaTime;
        float t = timer / lifetime;

        // Движение
        transform.position += velocity * Time.deltaTime;
        velocity *= 0.96f;

        // Масштаб
        transform.localScale = Vector3.one * Mathf.Lerp(scaleStart, scaleEnd, t);

        // Затухание последние 40%
        if (t > 0.6f)
        {
            float alpha = 1f - ((t - 0.6f) / 0.4f);
            if (text != null)
                text.color = new Color(startColor.r, startColor.g, startColor.b, alpha);

            // Затухание монетки синхронно с текстом
            if (isGold && coinSprite != null)
                coinSprite.color = new Color(1f, 1f, 1f, alpha);
        }

        // Анимация монетки
        if (isGold && coinSprite != null && coinFrames != null && coinFrames.Length > 0)
        {
            coinTimer += Time.deltaTime;
            if (coinTimer >= 1f / coinFPS)
            {
                coinTimer = 0f;
                coinFrame = (coinFrame + 1) % coinFrames.Length;
                coinSprite.sprite = coinFrames[coinFrame];
            }
        }

        if (timer >= lifetime)
            Destroy(gameObject);
    }
}