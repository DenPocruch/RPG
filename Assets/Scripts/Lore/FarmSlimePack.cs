using UnityEngine;
using System.Collections;

/// <summary>
/// Путеводитель: пак слаймов участка №9.
/// КАК СТАВИТЬ (руками, 5 минут):
/// 1. На ферме (SampleScene) создай пустой объект "FarmSlimePack".
/// 2. Повесь этот скрипт.
/// 3. Накидай внутрь 15 префабов Slime (Assets/Prefab/Enemy/Slimes/Slime)
///    по участку (дети объекта). TutorialSlime добавится сам.
/// 4. Сохрани сцену.
///
/// Пак сам считает живых (TutorialManager шаг clear), старые сейвы
/// (шаг уже пройден) чистятся автоматически.
/// </summary>
public class FarmSlimePack : MonoBehaviour
{
    public static int Total { get; private set; }
    public static int Alive { get; private set; }
    // Снапшот «сколько было»: мёртвые удаляются, Total тает (1/14, 2/13),
    // поэтому цель фиксируем по максимуму виденного
    public static int Target { get; private set; }

    void Awake()
    {
        // Маркер + ослабление — сразу, до первого кадра (иначе ранний килл
        // уйдёт без farmkill, а слайм возродится обычным)
        foreach (Transform child in transform)
        {
            if (child == null) continue;
            if (child.GetComponent<EnemyHealth>() == null) continue;
            if (child.GetComponent<TutorialSlime>() == null)
                child.gameObject.AddComponent<TutorialSlime>();
        }
        Recount();
    }

    IEnumerator Start()
    {
        // Пару кадров ждём: сейв restoration (TutorialManager.Start) должен успеть
        yield return null;
        yield return null;
        // Обучение уже ушло дальше — пак не нужен, чистим участок
        if (TutorialManager.IsPastClearStep())
        {
            foreach (Transform child in transform)
                if (child != null) Destroy(child.gameObject);
            Total = 0;
            Alive = 0;
        }
        else Recount();
    }

    void Update()
    {
        Recount();
    }

    void Recount()
    {
        int total = 0, alive = 0;
        foreach (Transform child in transform)
        {
            if (child == null) continue;
            var h = child.GetComponent<EnemyHealth>();
            if (h == null) continue;
            total++;
            if (h.currentHealth > 0) alive++;
        }
        Total = total;
        Alive = alive;
        if (total > Target) Target = total;
    }
}
