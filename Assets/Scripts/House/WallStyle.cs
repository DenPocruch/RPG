using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;

/// <summary>
/// Стиль стен дома (конструктор дома): полный набор тайлов одного цвета.
/// Лежит в Resources/House/WallStyle_*.asset. Билдер Tools → House →
/// Build WallStyle Brick заполняет кирпичный набор по именам ассетов
/// (Assets/Art/Tileset Grass Spring/Tileset House/) — руками таскать не надо.
/// Будущие цвета = дубликат ассета + замена ссылок (фаза 3).
/// Роли подтверждены аудитом фазы 2 (раздел 10 концепта).
/// </summary>
[CreateAssetMenu(fileName = "WallStyle_Brick", menuName = "RPG/House/Wall Style")]
public class WallStyle : ScriptableObject
{
    [Header("Фон и пол (информативно для фаз 3–4)")]
    [Tooltip("Заливка всего вокруг (Tileset House_76)")]
    public TileBase bgFill;
    [Tooltip("Плитка пола этого стиля (Tileset House_12)")]
    public TileBase floorTile;

    [Header("Выпуклые углы")]
    public TileBase cornerTL; // 1
    public TileBase cornerTR; // 3
    public TileBase cornerBL; // 68
    public TileBase cornerBR; // 70

    [Header("Прямые")]
    [Tooltip("Верх/низ (117)")]
    public TileBase straightH;
    [Tooltip("Бока (34)")]
    public TileBase straightV;

    [Header("Вилки 3x3 (Т-стыки и крест)")]
    [Tooltip("Верхний край (кирпич: 2) — Т-стык открытием вверх")]
    public TileBase forkTop; // 2
    [Tooltip("Нижний край (кирпич: 69) — Т-стык открытием вниз")]
    public TileBase forkBottom; // 69
    [Tooltip("Левый край (кирпич: 35) — Т-стык открытием влево")]
    public TileBase forkLeft; // 35
    [Tooltip("Правый край (кирпич: 37) — Т-стык открытием вправо")]
    public TileBase forkRight; // 37
    [Tooltip("Центр (кирпич: 36) — крест 4 стороны")]
    public TileBase forkCenter; // 36

    [Header("Задняя лента (2 ряда поверх пола у севера)")]
    public TileBase bandTopL; // 197
    public TileBase bandTopM; // 198
    public TileBase bandTopR; // 199
    public TileBase bandBotL; // 217
    public TileBase bandBotM; // 218
    public TileBase bandBotR; // 219
    [Tooltip("Боковые крышки ленты слева (кирпич: 196 / 216)")]
    public TileBase bandTopEdge; // 196
    public TileBase bandBotEdge; // 216
    [Tooltip("Западная колонна поворота (кирпич: 67 — низ, 37 — верх). Для сарая не размечены — null.")]
    public TileBase westTurnA; // 67
    public TileBase westTurnB; // 37
    [Tooltip("Середина L-колонки под полом (кирпич: Tileset House_0). Для сарая не размечена — null.")]
    public TileBase columnMid0; // 0

    [Header("Вогнутые углы (основной набор = как собрано сейчас)")]
    public TileBase concaveNW; // 1
    public TileBase concaveNE; // 3
    public TileBase concaveSW; // 68
    public TileBase concaveSE; // 70

    [Header("Вогнутые углы (альтернативы 77/75/40/38 — если глазами не те)")]
    public TileBase concaveAltNW; // 77
    public TileBase concaveAltNE; // 75
    public TileBase concaveAltSW; // 40
    public TileBase concaveAltSE; // 38
    [Tooltip("true = использовать альтернативный набор вогнутых")]
    public bool useConcaveAlts = false;

    [Header("Дверь (locked, автотайлер не ставит и не трогает)")]
    public TileBase doorJambL; // 118
    public TileBase doorJambR; // 116
    public TileBase doorThreshold; // 12

    [Header("Замок двери: тайлы, которые никогда не перестраиваются")]
    public List<string> lockTileNames = new List<string>
        { "Tileset House_118", "Tileset House_116", "Tileset House_12", "Tileset House_65" };

    const string RESOURCE_PREFIX = "House/WallStyle_";

    /// <param name="styleId">Brick, Barn... (суффикс ассета WallStyle_<id>)</param>
    public static WallStyle Load(string styleId)
    {
        if (string.IsNullOrEmpty(styleId)) return null;
        return Resources.Load<WallStyle>(RESOURCE_PREFIX + styleId);
    }

    public TileBase ConcaveNW => useConcaveAlts && concaveAltNW != null ? concaveAltNW : concaveNW;
    public TileBase ConcaveNE => useConcaveAlts && concaveAltNE != null ? concaveAltNE : concaveNE;
    public TileBase ConcaveSW => useConcaveAlts && concaveAltSW != null ? concaveAltSW : concaveSW;
    public TileBase ConcaveSE => useConcaveAlts && concaveAltSE != null ? concaveAltSE : concaveSE;

    /// <summary>Тайл относится к замку двери (не трогать при перестройке)?</summary>
    public bool IsLockedTile(TileBase tile)
    {
        if (tile == null || lockTileNames == null) return false;
        return lockTileNames.Contains(tile.name);
    }

    /// <summary>Тайл входит в набор стен стиля (кольцо+лента+дверь, без фона/пола)?</summary>
    public bool ContainsWallTile(TileBase tile)
    {
        if (tile == null) return false;
        return tile == cornerTL || tile == cornerTR || tile == cornerBL || tile == cornerBR
            || tile == straightH || tile == straightV
            || tile == forkTop || tile == forkBottom || tile == forkLeft || tile == forkRight || tile == forkCenter
            || tile == bandTopL || tile == bandTopM || tile == bandTopR || tile == bandTopEdge
            || tile == bandBotL || tile == bandBotM || tile == bandBotR || tile == bandBotEdge
            || tile == westTurnA || tile == westTurnB || tile == columnMid0
            || tile == ConcaveNW || tile == ConcaveNE || tile == ConcaveSW || tile == ConcaveSE
            || tile == doorJambL || tile == doorJambR || tile == doorThreshold;
    }
}
