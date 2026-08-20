using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Editor-only tooling for wiring gameplay prefabs, same convention as ClaudeUiTools: Claude writes
// the menu action, the user clicks it so the resulting asset changes are theirs.
public static class ClaudeGameplayTools
{
    private const string BuilderPrefabPath = "Assets/Prefabs/Builder.prefab";
    private const string GathererPrefabPath = "Assets/Prefabs/Gatherer.prefab";
    private const string PlayerBasePrefabPath = "Assets/Prefabs/PlayerBase.prefab";

    // Wires up everything the Builder feature needs that lives in prefab data rather than code:
    // the Builder prefab has to actually declare itself a Builder, and PlayerBase has to know which
    // prefabs to hand out at game start. Re-runnable - it won't overwrite a loadout already set.
    [MenuItem("Claude/Setup Builder Loadout")]
    public static void SetupBuilderLoadout()
    {
        GameObject builder = LoadPrefab(BuilderPrefabPath);
        GameObject gatherer = LoadPrefab(GathererPrefabPath);
        GameObject playerBase = LoadPrefab(PlayerBasePrefabPath);
        if (builder == null || gatherer == null || playerBase == null) return;

        List<string> changes = new List<string>();

        ConfigureBuilderPrefab(builder, changes);
        ConfigurePlayerBasePrefab(playerBase, builder, gatherer, changes);

        AssetDatabase.SaveAssets();

        string summary = changes.Count > 0
            ? "Applied:\n- " + string.Join("\n- ", changes)
            : "Everything was already wired up - nothing to change.";

        EditorUtility.DisplayDialog("Claude", summary, "OK");
    }

    private static void ConfigureBuilderPrefab(GameObject builder, List<string> changes)
    {
        UnitController controller = builder.GetComponent<UnitController>();
        if (controller == null)
        {
            Debug.LogError($"Claude: {BuilderPrefabPath} has no UnitController.");
            return;
        }

        SerializedObject so = new SerializedObject(controller);

        SerializedProperty unitType = so.FindProperty("unitType");
        if (unitType != null && unitType.intValue != (int)UnitType.Builder)
        {
            unitType.intValue = (int)UnitType.Builder;
            changes.Add("Builder.prefab: Unit Type -> Builder");
        }

        // A Builder that hunts enemies would chase them across the interior instead of carrying
        // food, and it's the one unit whose death loses the base - it should never pick a fight.
        SerializedProperty isAggressive = so.FindProperty("isAggressive");
        if (isAggressive != null && isAggressive.boolValue)
        {
            isAggressive.boolValue = false;
            changes.Add("Builder.prefab: Is Aggressive -> off");
        }

        if (so.hasModifiedProperties)
        {
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(builder);
        }
    }

    private static void ConfigurePlayerBasePrefab(GameObject playerBase, GameObject builder, GameObject gatherer, List<string> changes)
    {
        PlayerBase basePrefab = playerBase.GetComponent<PlayerBase>();
        if (basePrefab == null)
        {
            Debug.LogError($"Claude: {PlayerBasePrefabPath} has no PlayerBase.");
            return;
        }

        SerializedObject so = new SerializedObject(basePrefab);

        SerializedProperty builderPrefab = so.FindProperty("builderPrefab");
        if (builderPrefab != null && builderPrefab.objectReferenceValue != builder)
        {
            builderPrefab.objectReferenceValue = builder;
            changes.Add("PlayerBase.prefab: Builder Prefab -> Builder");
        }

        // Only filled when empty - a loadout the user has already chosen is theirs to keep.
        SerializedProperty startingUnits = so.FindProperty("startingUnitPrefabs");
        if (startingUnits != null && startingUnits.arraySize == 0)
        {
            startingUnits.arraySize = 2;
            startingUnits.GetArrayElementAtIndex(0).objectReferenceValue = builder;
            startingUnits.GetArrayElementAtIndex(1).objectReferenceValue = gatherer;
            changes.Add("PlayerBase.prefab: Starting Unit Prefabs -> [Builder, Gatherer]");
        }

        if (so.hasModifiedProperties)
        {
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(playerBase);
        }
    }

    private static GameObject LoadPrefab(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) Debug.LogError($"Claude: could not load {path}.");
        return prefab;
    }
}
