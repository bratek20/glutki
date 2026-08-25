using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Editor-only tooling for the base interior tile map. Same convention as the other Claude tools:
// Claude writes the menu action, the user clicks it so the resulting asset changes are theirs.
//
// It does the whole job in one click: makes a placeholder square sprite, builds a prefab per tile
// type from it, converts the existing Magazine into a plain (non-networked) tile with four
// StoredResource piles on it, and wires all of that into PlayerBase.prefab. Re-runnable - it only
// ever fills in what's missing, and never overwrites a prefab or a reference already there.
public static class ClaudeBaseTileTools
{
    private const string TilesFolder = "Assets/Prefabs/Tiles";
    private const string GeneratedSpritesFolder = "Assets/Sprites/Generated";
    private const string TileSpritePath = GeneratedSpritesFolder + "/TileSquare.png";
    private const string MagazinePrefabPath = TilesFolder + "/Magazine.prefab";
    private const string PlayerBasePrefabPath = "Assets/Prefabs/PlayerBase.prefab";
    private const string NetworkManagerPrefabPath = "Assets/Prefabs/NetworkManager.prefab";

    private const int TilePixels = 64;

    private struct TileSpec
    {
        public TileType type;
        public string prefabName;
        public string serializedField;
        public Color tint;
        public string sortingLayer;
    }

    // Magazine is deliberately absent - it already exists as a hand-made prefab and is wired
    // up rather than generated.
    private static readonly TileSpec[] TileSpecs =
    {
        new TileSpec { type = TileType.Floor, prefabName = "Tile_Floor", serializedField = "floor", tint = new Color(0.76f, 0.66f, 0.47f), sortingLayer = "Ground" },
        new TileSpec { type = TileType.Obstacle, prefabName = "Tile_Obstacle", serializedField = "obstacle", tint = new Color(0.29f, 0.25f, 0.22f), sortingLayer = "Entities" },
        new TileSpec { type = TileType.Queen, prefabName = "Tile_Queen", serializedField = "queen", tint = new Color(0.85f, 0.78f, 0.60f), sortingLayer = "Ground" },
        new TileSpec { type = TileType.Barrack, prefabName = "Tile_Barrack", serializedField = "barrack", tint = new Color(0.50f, 0.65f, 0.79f), sortingLayer = "Entities" },
        new TileSpec { type = TileType.GrowthTile, prefabName = "Tile_GrowthTile", serializedField = "growthTile", tint = new Color(0.56f, 0.79f, 0.50f), sortingLayer = "Ground" },
        new TileSpec { type = TileType.Entry, prefabName = "Tile_Entry", serializedField = "entry", tint = new Color(0.91f, 0.87f, 0.75f), sortingLayer = "Ground" },
    };

    [MenuItem("Claude/Setup Base Tiles")]
    public static void SetupBaseTiles()
    {
        List<string> changes = new List<string>();
        List<string> notes = new List<string>();

        Sprite tileSprite = EnsureTileSprite(changes);
        Dictionary<string, GameObject> prefabs = EnsureTilePrefabs(tileSprite, changes);

        GameObject magazine = ConvertMagazinePrefab(changes);
        if (magazine != null) prefabs["magazine"] = magazine;

        WirePlayerBase(prefabs, changes, notes);
        CleanSpawnablePrefabLists(changes);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string summary = changes.Count > 0
            ? "Applied:\n- " + string.Join("\n- ", changes)
            : "Everything was already set up - nothing to change.";

        notes.Add("Tile prefabs are authored one world unit square; each base scales them to its own Tile Size.");
        notes.Add("Run Claude > Setup Sprite Sorting afterwards so the new prefabs land on the right sorting layers.");

        summary += "\n\nNotes:\n- " + string.Join("\n- ", notes);

        EditorUtility.DisplayDialog("Claude", summary, "OK");
    }

    // A plain square with a slightly darker rim, so the tile grid reads as a grid before any real
    // art exists. Only generated once - the user is free to replace the PNG in place.
    private static Sprite EnsureTileSprite(List<string> changes)
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(TileSpritePath);
        if (existing != null) return existing;

        EnsureFolder(GeneratedSpritesFolder);

        Texture2D texture = new Texture2D(TilePixels, TilePixels, TextureFormat.RGBA32, false);
        for (int y = 0; y < TilePixels; y++)
        {
            for (int x = 0; x < TilePixels; x++)
            {
                bool rim = x < 2 || y < 2 || x >= TilePixels - 2 || y >= TilePixels - 2;
                texture.SetPixel(x, y, rim ? new Color(0.82f, 0.82f, 0.82f) : Color.white);
            }
        }
        texture.Apply();

        File.WriteAllBytes(TileSpritePath, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(TileSpritePath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(TileSpritePath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            // 64 pixels at 64 per unit makes the sprite exactly one world unit square.
            importer.spritePixelsPerUnit = TilePixels;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }

        changes.Add($"Created {TileSpritePath}");
        return AssetDatabase.LoadAssetAtPath<Sprite>(TileSpritePath);
    }

    private static Dictionary<string, GameObject> EnsureTilePrefabs(Sprite tileSprite, List<string> changes)
    {
        EnsureFolder(TilesFolder);

        Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();

        foreach (TileSpec spec in TileSpecs)
        {
            string path = $"{TilesFolder}/{spec.prefabName}.prefab";
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                prefabs[spec.serializedField] = existing;
                continue;
            }

            GameObject tile = new GameObject(spec.prefabName);
            SpriteRenderer renderer = tile.AddComponent<SpriteRenderer>();
            renderer.sprite = tileSprite;
            renderer.color = spec.tint;
            ApplySortingLayer(renderer, spec.sortingLayer);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(tile, path);
            Object.DestroyImmediate(tile);

            prefabs[spec.serializedField] = saved;
            changes.Add($"Created {path}");
        }

        return prefabs;
    }

    // Turns the old networked magazine into an ordinary tile prefab: no NetworkIdentity (the
    // tile map is built locally on every peer from PlayerBase's synced grid), plus the four
    // StoredResource piles that show how full it is.
    private static GameObject ConvertMagazinePrefab(List<string> changes)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(MagazinePrefabPath);
        if (asset == null)
        {
            Debug.LogError($"Claude: could not load {MagazinePrefabPath}.");
            return null;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(MagazinePrefabPath);
        bool dirty = false;

        NetworkIdentity identity = contents.GetComponent<NetworkIdentity>();
        if (identity != null)
        {
            Object.DestroyImmediate(identity, true);
            changes.Add("Magazine.prefab: removed NetworkIdentity (tiles are local now)");
            dirty = true;
        }

        if (contents.GetComponentInChildren<StoredResource>(true) == null)
        {
            Sprite placeholder = contents.GetComponent<SpriteRenderer>() != null
                ? contents.GetComponent<SpriteRenderer>().sprite
                : null;

            AddPiles(contents, placeholder);
            changes.Add($"Magazine.prefab: added {Magazine.DefaultPiles} StoredResource piles (limit {Magazine.DefaultCapacity})");
            dirty = true;
        }

        if (dirty) PrefabUtility.SaveAsPrefabAsset(contents, MagazinePrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);

        return AssetDatabase.LoadAssetAtPath<GameObject>(MagazinePrefabPath);
    }

    // Laid out left to right with a slight downward drift, so the Y sort draws the right-hand piles
    // in front rather than leaving same-Y sprites to fight over the order.
    private static void AddPiles(GameObject magazine, Sprite placeholder)
    {
        for (int i = 0; i < Magazine.DefaultPiles; i++)
        {
            GameObject pile = new GameObject($"Pile_{i + 1}");
            pile.transform.SetParent(magazine.transform, false);

            float t = Magazine.DefaultPiles > 1 ? i / (float)(Magazine.DefaultPiles - 1) : 0.5f;
            pile.transform.localPosition = new Vector3(Mathf.Lerp(-0.3f, 0.3f, t), Mathf.Lerp(0.08f, -0.08f, t), 0f);
            pile.transform.localScale = Vector3.one * 0.35f;

            SpriteRenderer renderer = pile.AddComponent<SpriteRenderer>();
            renderer.sprite = placeholder;
            ApplySortingLayer(renderer, "Entities");

            StoredResource stored = pile.AddComponent<StoredResource>();
            SerializedObject so = new SerializedObject(stored);
            so.FindProperty("spriteRenderer").objectReferenceValue = renderer;
            // Both stages start on the same placeholder - the user swaps in the real one/two-
            // resource art per pile.
            so.FindProperty("singleSprite").objectReferenceValue = placeholder;
            so.FindProperty("doubleSprite").objectReferenceValue = placeholder;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void WirePlayerBase(Dictionary<string, GameObject> prefabs, List<string> changes, List<string> notes)
    {
        GameObject playerBase = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerBasePrefabPath);
        if (playerBase == null)
        {
            Debug.LogError($"Claude: could not load {PlayerBasePrefabPath}.");
            return;
        }

        PlayerBase component = playerBase.GetComponent<PlayerBase>();
        if (component == null)
        {
            Debug.LogError($"Claude: {PlayerBasePrefabPath} has no PlayerBase.");
            return;
        }

        SerializedObject so = new SerializedObject(component);

        foreach (KeyValuePair<string, GameObject> entry in prefabs)
        {
            if (entry.Value == null) continue;

            SerializedProperty property = so.FindProperty($"tilePrefabs.{entry.Key}");
            // Only ever fills an empty slot - a prefab the user has deliberately chosen is theirs.
            if (property == null || property.objectReferenceValue != null) continue;

            property.objectReferenceValue = entry.Value;
            changes.Add($"PlayerBase.prefab: Tile Prefabs > {entry.Key} -> {entry.Value.name}");
        }

        SerializedProperty layout = so.FindProperty("layout");
        if (layout != null && string.IsNullOrWhiteSpace(layout.stringValue))
        {
            layout.stringValue = BaseLayout.Default;
            changes.Add("PlayerBase.prefab: Layout -> the default 7x6 room");
        }

        if (so.hasModifiedProperties)
        {
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(playerBase);
        }

        ReportFootprint(so, notes);
    }

    // Tile Size used to mean "one build-grid cell inside a separately-sized room"; it now sets the
    // room's whole footprint. Reported rather than silently corrected - the value is the user's.
    private static void ReportFootprint(SerializedObject so, List<string> notes)
    {
        SerializedProperty layout = so.FindProperty("layout");
        SerializedProperty tileSize = so.FindProperty("tileSize");
        if (layout == null || tileSize == null) return;

        if (!BaseLayout.TryParse(layout.stringValue, out _, out int columns, out int rows, out string error))
        {
            notes.Add($"WARNING - PlayerBase.prefab layout does not parse: {error}");
            return;
        }

        notes.Add($"PlayerBase.prefab interior is now {columns}x{rows} tiles at Tile Size " +
                    $"{tileSize.floatValue} = {columns * tileSize.floatValue} x {rows * tileSize.floatValue} " +
                    "world units. Tune Tile Size (and the camera's Base View Orthographic Size) to taste.");
    }

    // A magazine tile no longer has a NetworkIdentity, so leaving it in a NetworkManager's spawnable
    // list would just make Mirror log an error at startup.
    private static void CleanSpawnablePrefabLists(List<string> changes)
    {
        GameObject managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
        NetworkManager prefabManager = managerPrefab != null ? managerPrefab.GetComponent<NetworkManager>() : null;
        if (prefabManager != null && RemoveUnspawnable(prefabManager))
        {
            EditorUtility.SetDirty(managerPrefab);
            changes.Add("NetworkManager.prefab: dropped spawn prefabs that no longer have a NetworkIdentity");
        }

        // The MainMenu instance keeps its own overridden list, so it has to be cleaned separately -
        // and only if that scene happens to be open.
        foreach (NetworkManager sceneManager in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!RemoveUnspawnable(sceneManager)) continue;

            EditorSceneManager.MarkSceneDirty(sceneManager.gameObject.scene);
            changes.Add($"{sceneManager.gameObject.scene.name}: dropped spawn prefabs that no longer have a NetworkIdentity");
        }
    }

    private static bool RemoveUnspawnable(NetworkManager manager)
    {
        SerializedObject so = new SerializedObject(manager);
        SerializedProperty spawnPrefabs = so.FindProperty("spawnPrefabs");
        if (spawnPrefabs == null) return false;

        bool changed = false;
        for (int i = spawnPrefabs.arraySize - 1; i >= 0; i--)
        {
            GameObject entry = spawnPrefabs.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            if (entry != null && entry.GetComponent<NetworkIdentity>() != null) continue;

            spawnPrefabs.DeleteArrayElementAtIndex(i);
            changed = true;
        }

        if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        return changed;
    }

    // Assigning a layer Unity doesn't know about is a silent no-op, so leave the renderer on
    // Default and let Claude > Setup Sprite Sorting create the layers and fix it up.
    private static void ApplySortingLayer(SpriteRenderer renderer, string layerName)
    {
        foreach (SortingLayer layer in SortingLayer.layers)
        {
            if (layer.name != layerName) continue;

            renderer.sortingLayerName = layerName;
            renderer.sortingOrder = 0;
            return;
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
