using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Editor-only tooling for the project's 2D sprite sorting setup. Same convention as
// ClaudeUiTools: Claude writes the menu action, the user clicks it so the resulting asset/scene
// changes are theirs.
//
// The problem this solves: every sprite in the project sits on the single built-in "Default"
// sorting layer at order 0 with z = 0, and the URP 2D Renderer's Transparency Sort Mode is
// "Default" - which for an orthographic camera means "sort by distance along the view axis". With
// everything at the same z that comparison is a tie for every pair of sprites, so the actual draw
// order falls out of batching order and looks random and unstable.
//
// The fix is two-part:
//  - Sorting layers give a coarse, absolute back-to-front order for things that must never fight
//    (terrain is always behind, overlays are always in front).
//  - Within a layer, Transparency Sort Mode = Custom Axis (0,1,0) makes Unity sort by world Y, so
//    a sprite standing lower on screen draws in front. Free (no per-frame script), automatic for
//    every sprite, and stable while units move.
public static class ClaudeRenderingTools
{
    // Back-to-front. Unity's built-in "Default" layer stays at index 0, so anything nobody assigned
    // a layer to renders behind all of these - deliberately obvious rather than subtly wrong.
    private const string BackgroundLayer = "Background";
    private const string GroundLayer = "Ground";
    private const string EntitiesLayer = "Entities";
    private const string OverlayLayer = "Overlay";

    private static readonly string[] SortingLayerNames = { BackgroundLayer, GroundLayer, EntitiesLayer, OverlayLayer };

    // Sorting layer IDs are opaque ints. Using fixed ones (rather than Unity's random generator)
    // keeps re-runs idempotent: renderers serialize the ID, so a changed ID would silently orphan
    // every assignment made by a previous run.
    private const int FirstLayerId = 5001;

    // Sprites that need a layer other than Entities, matched on GameObject name.
    //
    // Interior tiles are ground the units walk on, so they sit behind everything on Entities.
    // Obstacles and barracks are deliberately left off this list: they're things units stand in
    // front of or behind depending on where they are, which is exactly what Entities' Y sort does.
    private static readonly Dictionary<string, string> LayerByObjectName = new Dictionary<string, string>
    {
        { "Terrain", BackgroundLayer },
        { "Tile_Floor", GroundLayer },
        { "Tile_Queen", GroundLayer },
        { "Tile_GrowthTile", GroundLayer },
        { "Tile_Entry", GroundLayer },
    };

    private const string Renderer2DPath = "Assets/Settings/Renderer2D.asset";
    private const string TagManagerPath = "ProjectSettings/TagManager.asset";
    private const string GraphicsSettingsPath = "ProjectSettings/GraphicsSettings.asset";
    private const string UndoName = "Setup Sprite Sorting";

    [MenuItem("Claude/Setup Sprite Sorting")]
    public static void SetupSpriteSorting()
    {
        int layersAdded = EnsureSortingLayers();

        // Assigning a sorting layer by a name Unity doesn't know is a silent no-op, so bail out
        // loudly rather than reporting success over renderers that never actually moved.
        string missing = FirstUnregisteredLayer();
        if (missing != null)
        {
            EditorUtility.DisplayDialog("Claude",
                $"Sorting layer \"{missing}\" isn't registered yet, so no renderers were touched.\n\n" +
                "Check Project Settings > Tags and Layers, then run this action again.", "OK");
            return;
        }

        bool sortModeSet = EnableYAxisTransparencySort();
        int prefabsChanged = AssignInPrefabs();
        int sceneChanged = AssignInOpenScene();

        AssetDatabase.SaveAssets();

        string summary =
            $"Sorting layers: {(layersAdded > 0 ? layersAdded + " added" : "already present")} " +
            $"({string.Join(" -> ", SortingLayerNames)})\n" +
            $"Y-axis transparency sort: {(sortModeSet ? "enabled" : "FAILED - see Console")}\n" +
            $"Sprite renderers reassigned: {prefabsChanged} in prefabs, {sceneChanged} in the open scene";

        EditorUtility.DisplayDialog("Claude", summary, "OK");
    }

    // Appends any missing sorting layer to ProjectSettings/TagManager.asset, leaving existing ones
    // (and their IDs) untouched. Returns how many were added.
    private static int EnsureSortingLayers()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);
        if (assets == null || assets.Length == 0)
        {
            Debug.LogError($"Claude: could not load {TagManagerPath} - sorting layers not created.");
            return 0;
        }

        SerializedObject tagManager = new SerializedObject(assets[0]);
        SerializedProperty layers = tagManager.FindProperty("m_SortingLayers");
        if (layers == null)
        {
            Debug.LogError("Claude: TagManager has no m_SortingLayers property - sorting layers not created.");
            return 0;
        }

        HashSet<string> existingNames = new HashSet<string>();
        HashSet<int> usedIds = new HashSet<int>();
        for (int i = 0; i < layers.arraySize; i++)
        {
            SerializedProperty entry = layers.GetArrayElementAtIndex(i);
            existingNames.Add(entry.FindPropertyRelative("name").stringValue);
            usedIds.Add(entry.FindPropertyRelative("uniqueID").intValue);
        }

        int added = 0;
        int nextId = FirstLayerId;
        foreach (string layerName in SortingLayerNames)
        {
            if (existingNames.Contains(layerName)) continue;

            while (usedIds.Contains(nextId)) nextId++;

            int index = layers.arraySize;
            layers.InsertArrayElementAtIndex(index);
            SerializedProperty entry = layers.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("name").stringValue = layerName;
            entry.FindPropertyRelative("uniqueID").intValue = nextId;
            SetLocked(entry.FindPropertyRelative("locked"));

            usedIds.Add(nextId);
            added++;
        }

        if (added > 0) tagManager.ApplyModifiedProperties();
        return added;
    }

    private static string FirstUnregisteredLayer()
    {
        HashSet<string> registered = new HashSet<string>();
        foreach (SortingLayer layer in SortingLayer.layers) registered.Add(layer.name);

        foreach (string layerName in SortingLayerNames)
        {
            if (!registered.Contains(layerName)) return layerName;
        }

        return null;
    }

    private static void SetLocked(SerializedProperty locked)
    {
        if (locked == null) return;
        if (locked.propertyType == SerializedPropertyType.Boolean) locked.boolValue = false;
        else locked.intValue = 0;
    }

    // The 2D Renderer data is what actually drives sorting under URP; GraphicsSettings is set to
    // match so the Scene view and any other camera path agree with the game view.
    private static bool EnableYAxisTransparencySort()
    {
        bool rendererOk = ApplyCustomAxisSort(AssetDatabase.LoadAssetAtPath<Object>(Renderer2DPath), Renderer2DPath);

        Object[] graphicsSettings = AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsPath);
        bool graphicsOk = graphicsSettings != null && graphicsSettings.Length > 0 &&
                          ApplyCustomAxisSort(graphicsSettings[0], GraphicsSettingsPath);

        return rendererOk && graphicsOk;
    }

    private static bool ApplyCustomAxisSort(Object target, string path)
    {
        if (target == null)
        {
            Debug.LogError($"Claude: could not load {path} - transparency sort mode unchanged.");
            return false;
        }

        SerializedObject so = new SerializedObject(target);
        SerializedProperty mode = so.FindProperty("m_TransparencySortMode");
        SerializedProperty axis = so.FindProperty("m_TransparencySortAxis");
        if (mode == null || axis == null)
        {
            Debug.LogError($"Claude: {path} has no transparency sort properties - left unchanged.");
            return false;
        }

        mode.intValue = (int)TransparencySortMode.CustomAxis;
        axis.vector3Value = new Vector3(0f, 1f, 0f);
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
        return true;
    }

    private static int AssignInPrefabs()
    {
        int changed = 0;

        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            bool isVariant = PrefabUtility.GetPrefabAssetType(prefab) == PrefabAssetType.Variant;
            bool prefabChanged = false;

            foreach (SpriteRenderer renderer in prefab.GetComponentsInChildren<SpriteRenderer>(true))
            {
                // A variant inherits its base prefab's renderers - those get handled once, on the
                // base. Overriding them per variant would just add noise to the variant asset.
                if (isVariant && PrefabUtility.GetCorrespondingObjectFromSource(renderer) != null) continue;

                if (!ApplySorting(renderer)) continue;
                prefabChanged = true;
                changed++;
            }

            if (prefabChanged) EditorUtility.SetDirty(prefab);
        }

        return changed;
    }

    private static int AssignInOpenScene()
    {
        int changed = 0;

        foreach (SpriteRenderer renderer in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // Prefab instances follow their prefab - assigning here would create a scene override
            // that then stops tracking the prefab.
            if (PrefabUtility.GetCorrespondingObjectFromSource(renderer) != null) continue;

            if (!ApplySorting(renderer)) continue;
            changed++;
            EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
        }

        return changed;
    }

    private static bool ApplySorting(SpriteRenderer renderer)
    {
        string layer = LayerFor(renderer.gameObject.name);

        // Sorting order is compared before the transparency sort axis, so a nonzero order on an
        // Entities sprite would pin it in front of / behind everything and defeat the Y sorting.
        bool needsZeroOrder = layer == EntitiesLayer;
        bool alreadyCorrect = renderer.sortingLayerName == layer && (!needsZeroOrder || renderer.sortingOrder == 0);
        if (alreadyCorrect) return false;

        Undo.RecordObject(renderer, UndoName);
        renderer.sortingLayerName = layer;
        if (needsZeroOrder) renderer.sortingOrder = 0;
        return true;
    }

    private static string LayerFor(string objectName)
    {
        return LayerByObjectName.TryGetValue(objectName, out string layer) ? layer : EntitiesLayer;
    }
}
