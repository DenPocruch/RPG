using UnityEngine;

/// <summary>
/// ѕомечает объект как "посто€нный" Ч он переживает смену сцен (DontDestroyOnLoad)
/// и не дублируетс€ при возврате в сцену где он был.
///
/// ¬ешаетс€ на PersistentRoot Ч родительский объект, под которым лежат:
/// Player, все менеджеры (Save, Currency, Level, SkillTree, Inventory, Equipment,
/// Hotbar, Dialogue...) и Canvas со всем UI.
///
/// »гровые сцены (ферма, город) содержат “ќЋ№ ќ мир: тайлмапы, декор, NPC,
/// сундуки, камеру, точки сети, точки по€влени€. ћенеджеров/игрока там нет Ч
/// они приход€т из PersistentRoot.
/// </summary>
public class GamePersistence : MonoBehaviour
{
    private static GamePersistence instance;

    void Awake()
    {
        // ”же есть посто€нный корень (вернулись в стартовую сцену) Ч этот лишний
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }
}