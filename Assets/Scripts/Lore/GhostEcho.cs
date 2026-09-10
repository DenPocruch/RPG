using UnityEngine;

/// <summary>
/// Часть 3 лора: привидение-«эхо» (слепок умершего в реале пациента).
/// НЕ враг: не атакует, удар = одна случайная фраза в лог.
/// Самонастройка: слой Interactable(8), триггер-коллайдер, полупрозрачный
/// голубой тинт, лёгкое парение + мерцание. Спрайт — любой заглушечный
/// (нормальный призрак дорисуется в ч.5), тинт делает его «призрачным» сам.
/// Ночной режим (realNightOnly): виден 21:00–05:00 реального времени.
/// </summary>
public class GhostEcho : MonoBehaviour, IInteractable
{
    [Header("Вид")]
    public Color ghostTint = new Color(0.65f, 0.85f, 1f, 0.45f);
    public float bobAmount = 0.15f;
    public float bobSpeed = 1.6f;
    public float flickerSpeed = 5f;

    [Header("Режим")]
    [Tooltip("Если true — виден только ночью (21:00–05:00 реального времени)")]
    public bool realNightOnly;

    [Header("Фразы (эхо Act 3 + квест часовни ч.4)")]
    public string[] lines =
    {
        "...передай маме... я успел...",
        "...дострой... часовню... темно...",
        "...счётчик врёт... считай сам...",
        "...море... помнит... всех...",
        "...не пей... синюю воду...",
    };

    SpriteRenderer sr;
    Vector3 basePos;
    float seed;

    void Awake()
    {
        gameObject.layer = 8; // Interactable
        var col = GetComponent<BoxCollider2D>();
        if (col == null) col = gameObject.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(1f, 1.5f);

        sr = GetComponent<SpriteRenderer>();
        if (sr == null) sr = GetComponentInChildren<SpriteRenderer>();
        if (sr != null) sr.color = ghostTint;

        basePos = transform.position;
        seed = Random.value * 10f;
        ApplyNightVisibility();
    }

    void Update()
    {
        ApplyNightVisibility();
        if (sr == null || !sr.enabled) return;
        // Парение + мерцание
        transform.position = basePos + Vector3.up * Mathf.Sin((Time.time + seed) * bobSpeed) * bobAmount;
        Color c = ghostTint;
        c.a = ghostTint.a * (0.75f + 0.25f * Mathf.Sin((Time.time + seed) * flickerSpeed));
        sr.color = c;
    }

    void ApplyNightVisibility()
    {
        if (!realNightOnly) return;
        int h = System.DateTime.Now.Hour;
        bool night = (h >= 21 || h < 5);
        if (sr != null) sr.enabled = night;
        var col = GetComponent<Collider2D>();
        if (col != null) col.enabled = night;
    }

    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        if (sr != null && !sr.enabled) return;
        if (lines == null || lines.Length == 0) return;
        ActionLogUI.Show("[Эхо] " + lines[Random.Range(0, lines.Length)]);
    }
}
