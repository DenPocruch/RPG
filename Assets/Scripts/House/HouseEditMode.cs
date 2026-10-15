using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Режим планировки дома (конструктор дома, фаза 7): вход ударом с чертежом
/// в хотбаре, выход крестиком панели. На входе игрок замирает
/// (паттерн CharacterConstructorUI), панель открывается; на выходе игрок
/// оживает + сейв. Действия применяются сразу с сейвом (стека Готово/Отмены нет).
/// Панель верстает владелец (HouseEditBinder); без биндера вход отклоняется.
/// </summary>
public class HouseEditMode : MonoBehaviour
{
    public static HouseEditMode Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("HouseEditMode");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<HouseEditMode>();
            }
            return _instance;
        }
    }
    private static HouseEditMode _instance;

    public static bool IsOpen { get; private set; }

    PlayerMovement lockedMovement;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance == this) _instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Панель сценовая: при смене сцены выходим молча (игрок не должен зависнуть).
        if (IsOpen)
            Exit(silent: true);
    }

    public static void Toggle()
    {
        if (IsOpen) Instance.Exit();
        else Instance.Enter();
    }

    public void Enter()
    {
        if (IsOpen) return;
        if (HouseEditUI.Instance == null || !HouseEditUI.Instance.IsReady)
        {
            Debug.LogWarning("[House] Нет панели планировки (собери Canvas + HouseEditBinder).");
            return;
        }
        var player = GameObject.FindWithTag("Player");
        if (player != null)
            lockedMovement = player.GetComponent<PlayerMovement>();
        if (lockedMovement != null)
            lockedMovement.enabled = false;
        IsOpen = true;
        HouseEditUI.Instance.Open();
        Debug.Log("[House] Режим планировки: открыт.");
    }

    public void Exit(bool silent = false)
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (HouseEditUI.Instance != null)
            HouseEditUI.Instance.Close();
        if (lockedMovement != null)
        {
            lockedMovement.enabled = true;
            lockedMovement = null;
        }
        SaveManager.Instance?.Save();
        if (!silent)
            Debug.Log("[House] Режим планировки: закрыт.");
    }
}
