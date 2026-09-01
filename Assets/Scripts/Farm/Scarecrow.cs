using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Пугало на ферме. Ставится игроком из хотбара (ghost-режим в PlayerMovement),
/// собирается молотком как кормушка/поилка. Защищает грядки в зоне 3×3 тайла
/// вокруг себя: вороны (CrowAI) не выбирают защищённые растения, а если пугало
/// поставили пока ворона клюёт — ворона улетает.
/// При постановке зона защиты подсвечивается (короткая вспышка).
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class Scarecrow : MonoBehaviour, IInteractable
{
    [Header("Зона защиты")]
    [Tooltip("Радиус защиты в тайлах: 1 = 3×3, 2 = 5×5, 3 = 7×7")]
    [Min(1)] public int zoneRadiusTiles = 1;

    [Header("Подсветка зоны")]
    [Tooltip("Секунд показа зоны после постановки")]
    public float zoneFlashTime = 1.6f;

    // Реестр всех пугал в мире (для проверки защиты воронами)
    private static readonly List<Scarecrow> all = new List<Scarecrow>();
    void OnEnable() { all.Add(this); }
    void OnDisable() { all.Remove(this); }

    void Awake()
    {
        // Физический коллайдер (не триггер) — чтобы нельзя было ставить объекты друг на друга
        var col = GetComponent<BoxCollider2D>();
        if (col != null) col.isTrigger = false;
    }

    public Transform GetTransform() => transform;

    public void Interact(GameObject player)
    {
        int size = zoneRadiusTiles * 2 + 1;
        ActionLogUI.Show("Пугало охраняет грядки " + size + "×" + size + " вокруг себя от ворон.");
    }

    // ═══════════════════════════════════════════════════════════
    // ЗОНА ЗАЩИТЫ (квадрат (2×радиус+1) тайлов вокруг пугала)
    // ═══════════════════════════════════════════════════════════

    /// <summary>Защищена ли эта точка хотя бы одним пугалом в мире.</summary>
    public static bool IsProtected(Vector3 worldPos)
    {
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i].Protects(worldPos)) return true;
        return false;
    }

    bool Protects(Vector3 pos)
    {
        var fm = FarmManager.Instance;
        if (fm != null && fm.farmTilemap != null)
        {
            // Защита по клеткам фермы: пугало — центральная клетка зоны
            Vector3Int center = fm.farmTilemap.WorldToCell(transform.position);
            Vector3Int c = fm.farmTilemap.WorldToCell(pos);
            return Mathf.Abs(c.x - center.x) <= zoneRadiusTiles
                && Mathf.Abs(c.y - center.y) <= zoneRadiusTiles;
        }

        // Вне фермы — квадрат (2×радиус+1) метров вокруг пугала
        Vector3 d = pos - transform.position;
        return Mathf.Abs(d.x) <= zoneRadiusTiles + 0.5f
            && Mathf.Abs(d.y) <= zoneRadiusTiles + 0.5f;
    }

    /// <summary>Центр зоны защиты (привязка к клетке фермы, если она есть) — для подсветки.</summary>
    public static Vector3 GetZoneCenter(Vector3 pos)
    {
        var fm = FarmManager.Instance;
        if (fm != null && fm.farmTilemap != null)
            return fm.farmTilemap.GetCellCenterWorld(fm.farmTilemap.WorldToCell(pos));
        return pos;
    }

    /// <summary>Размер зоны в мировых единицах: (2×радиус+1) клеток × размер клетки тайлмапа.</summary>
    public static Vector3 GetZoneSize(int radius)
    {
        var fm = FarmManager.Instance;
        if (fm != null && fm.farmTilemap != null && fm.farmTilemap.layoutGrid != null)
        {
            Vector3 cs = fm.farmTilemap.layoutGrid.cellSize;
            return new Vector3(cs.x * (radius * 2 + 1), cs.y * (radius * 2 + 1), 0.1f);
        }
        return new Vector3(radius * 2 + 1, radius * 2 + 1, 0.1f);
    }

    // ═══════════════════════════════════════════════════════════
    // ПОДСВЕТКА ЗОНЫ (короткая вспышка после постановки)
    // ═══════════════════════════════════════════════════════════
    private Coroutine flashRoutine;

    public void ShowZoneFlash()
    {
        if (zoneFlashTime <= 0f) return;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(ZoneFlash());
    }

    IEnumerator ZoneFlash()
    {
        var go = new GameObject("ZoneFlash");
        go.transform.SetParent(transform, false);
        go.transform.position = GetZoneCenter(transform.position);
        go.transform.localScale = GetZoneSize(zoneRadiusTiles);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetZoneSprite();
        sr.sortingOrder = 59; // над тайлмапом, под ghost'ом (60)

        Color c = new Color(0.55f, 1f, 0.55f, 0.55f);
        sr.color = c;

        float t = 0f;
        while (t < zoneFlashTime)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(0.55f, 0f, t / zoneFlashTime);
            sr.color = c;
            yield return null;
        }
        Destroy(go);
        flashRoutine = null;
    }

    /// <summary>Белая квадратная текстура с рамкой (тонировка зелёным/красным через color).</summary>
    static Sprite _zoneSprite;
    public static Sprite GetZoneSprite()
    {
        if (_zoneSprite != null) return _zoneSprite;

        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.name = "ScarecrowZone";
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                bool border = x < 2 || y < 2 || x >= S - 2 || y >= S - 2;
                px[y * S + x] = new Color32(255, 255, 255, (byte)(border ? 210 : 60));
            }
        }
        tex.SetPixels32(px);
        tex.Apply();

        _zoneSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);
        return _zoneSprite;
    }
}
