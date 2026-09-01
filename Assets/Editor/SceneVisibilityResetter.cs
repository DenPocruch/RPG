using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class SceneVisibilityResetter
{
    [MenuItem("Tools/UI/Reset Scene Visibility")]
    public static void ResetAll()
    {
        var svm = SceneVisibilityManager.instance;
        int count = 0;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;

            foreach (var go in scene.GetRootGameObjects())
                ShowRecursive(go, svm, ref count);
        }

        Debug.Log($"[SceneVisibilityResetter] Показано заново объектов: {count}. Теперь сохрани сцену (Ctrl+S).");
    }

    private static void ShowRecursive(GameObject go, SceneVisibilityManager svm, ref int count)
    {
        if (svm.IsHidden(go)) count++;
        svm.Show(go, false);
        svm.EnablePicking(go, false);

        foreach (Transform child in go.transform)
            ShowRecursive(child.gameObject, svm, ref count);
    }

    [MenuItem("Tools/UI/Fix Tooltip Visibility")]
    public static void FixTooltip()
    {
        var svm = SceneVisibilityManager.instance;
        bool found = false;

        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (go.name != "Tooltip") continue;
            found = true;

            Debug.Log($"[FixTooltip] {GetPath(go)}\n" +
                      $"  activeSelf={go.activeSelf}, activeInHierarchy={go.activeInHierarchy}\n" +
                      $"  sceneHidden={svm.IsHidden(go)}, pickingOff={svm.IsPickingDisabled(go)}");

            svm.Show(go, true);
            svm.EnablePicking(go, false);

            var img = go.GetComponent<Image>();
            if (img != null)
                Debug.Log($"  Image: enabled={img.enabled}, color={img.color}, sprite={(img.sprite != null ? img.sprite.name : "NULL")}");
            else
                Debug.Log("  Image: НЕТ компонента");

            var cg = go.GetComponent<CanvasGroup>();
            if (cg != null)
                Debug.Log($"  CanvasGroup: alpha={cg.alpha}, interactable={cg.interactable}, blocksRaycasts={cg.blocksRaycasts}, ignoreParent={cg.ignoreParentGroups}");

            var canvas = go.GetComponentInParent<Canvas>(true);
            if (canvas != null)
                Debug.Log($"  Canvas: enabled={canvas.enabled}, sortingOrder={canvas.sortingOrder}, renderMode={canvas.renderMode}");

            var cr = go.GetComponent<CanvasRenderer>();
            if (cr != null)
                Debug.Log($"  CanvasRenderer: cull={cr.cull}, alpha={cr.GetAlpha()}");

            Debug.Log($"  Слоёв детей: {go.transform.childCount}, позиция={go.transform.position}, sizeDelta={((RectTransform)go.transform).sizeDelta}");
        }

        if (!found) Debug.LogWarning("[FixTooltip] Объект 'Tooltip' в открытых сценах не найден!");
    }

    private static string GetPath(GameObject go)
    {
        var sb = new System.Text.StringBuilder(go.name);
        var t = go.transform.parent;
        while (t != null)
        {
            sb.Insert(0, t.name + "/");
            t = t.parent;
        }
        return sb.ToString();
    }
}
