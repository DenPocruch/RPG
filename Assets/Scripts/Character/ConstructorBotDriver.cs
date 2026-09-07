using UnityEngine;

/// <summary>
/// Тестовый драйвер бота конструктора (только сцена ConstructorTest):
/// WASD/стрелки — ходить, Space — тестовая анимация, Z X C V B G — смена слоёв.
/// </summary>
[RequireComponent(typeof(CharacterVisual))]
public class ConstructorBotDriver : MonoBehaviour
{
    const string WALK = "2. Walk";
    const string IDLE = "1. Idle";

    public float speed = 3f;

    CharacterVisual visual;
    string lastTest = "8. SwordAttack";
    float lastSpeed = 1f;
    readonly System.Collections.Generic.List<string> testActions = new System.Collections.Generic.List<string>();
    int testIdx;

    void Start()
    {
        visual = GetComponent<CharacterVisual>();
        // Все действия из базы — T листает их по кругу для проверки
        if (visual != null && visual.database != null)
            foreach (var a in visual.database.actions)
                testActions.Add(a.actionName);
        if (testActions.Count == 0)
            testActions.Add(lastTest);
        Debug.Log("[Bot] WASD ходить | Space повторить | T след. анимация (" + testActions.Count + ") | Z/X/C/V/B/G слои | N борода | M уши | J эффекты | I инфо | P пауза | -/+ скорость | ,/. кадр");
    }

    void Update()
    {
        if (visual == null) return;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector2 mv = new Vector2(h, v);
        if (mv.sqrMagnitude > 0.01f)
        {
            mv.Normalize();
            transform.position += (Vector3)(mv * speed * Time.deltaTime);
            visual.SetDirectionFromVector(mv);
            if (visual.CurrentActionName != WALK)
                visual.Play(WALK);
        }
        else if (visual.CurrentActionName == WALK || visual.CurrentActionName == "3. Run")
        {
            visual.Play(IDLE);
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            visual.PlayOnce(lastTest, () => visual.Play(IDLE));
            Debug.Log("[Bot] Повтор: " + lastTest);
        }
        if (Input.GetKeyDown(KeyCode.T))
        {
            lastTest = testActions[testIdx++ % testActions.Count];
            visual.PlayOnce(lastTest, () => visual.Play(IDLE));
            Debug.Log("[Bot] Действие: " + lastTest);
        }
        if (Input.GetKeyDown(KeyCode.P))
        {
            visual.playbackSpeed = visual.playbackSpeed > 0.001f ? 0f : lastSpeed;
            Debug.Log("[Bot] " + visual.GetStatus());
        }
        if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus))
        {
            lastSpeed = Mathf.Max(0.05f, lastSpeed / 2f);
            visual.playbackSpeed = lastSpeed;
            Debug.Log("[Bot] " + visual.GetStatus());
        }
        if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            lastSpeed = Mathf.Min(4f, lastSpeed * 2f);
            visual.playbackSpeed = lastSpeed;
            Debug.Log("[Bot] " + visual.GetStatus());
        }
        if (Input.GetKeyDown(KeyCode.Comma))
        {
            visual.StepFrame(-1);
            Debug.Log("[Bot] " + visual.GetStatus());
        }
        if (Input.GetKeyDown(KeyCode.Period))
        {
            visual.StepFrame(1);
            Debug.Log("[Bot] " + visual.GetStatus());
        }
        if (Input.GetKeyDown(KeyCode.Z)) Cycle("Skins");
        if (Input.GetKeyDown(KeyCode.X)) Cycle("Eyes");
        if (Input.GetKeyDown(KeyCode.C)) Cycle("Clothers");
        if (Input.GetKeyDown(KeyCode.V)) Cycle("Hair's");
        if (Input.GetKeyDown(KeyCode.B)) Cycle("Acc");
        if (Input.GetKeyDown(KeyCode.N)) Cycle("Beard");
        if (Input.GetKeyDown(KeyCode.M)) Cycle("Elf");
        if (Input.GetKeyDown(KeyCode.G)) Cycle("Weapons");
        if (Input.GetKeyDown(KeyCode.J)) Cycle("FX");
        if (Input.GetKeyDown(KeyCode.I)) Debug.Log("[Bot] " + visual.GetDebugInfo());
    }

    void Cycle(string category)
    {
        string v = visual.CycleVariant(category);
        Debug.Log("[Bot] " + category + " = " + (v ?? "(нет вариантов)"));
    }
}
