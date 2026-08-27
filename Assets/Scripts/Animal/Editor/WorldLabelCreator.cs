using UnityEditor;
using UnityEngine;

/// <summary>
/// Создаёт/обновляет префаб Assets/Resources/WorldLabel.prefab.
/// После настройки параметров лейбла в инспекторе префаба можно
/// вызвать меню ещё раз — дети будут перестроены по текущим значениям.
/// </summary>
public static class WorldLabelCreator
{
    const string PrefabPath = "Assets/Resources/WorldLabel.prefab";

    [MenuItem("Tools/RPG/Создать префаб WorldLabel")]
    public static void Create()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");

        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject go = existing == null
            ? new GameObject("WorldLabel")
            : (GameObject)PrefabUtility.InstantiatePrefab(existing);

        if (go.GetComponent<WorldLabel>() == null)
            go.AddComponent<WorldLabel>();

        go.GetComponent<WorldLabel>().EnsureBuilt();

        PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();

        Debug.Log("[WorldLabel] Префаб сохранён: " + PrefabPath +
                  ". Настраивай параметры (offset, шрифт, обводку) в инспекторе префаба — кормушки и поилки подхватят его автоматически.");
    }
}
