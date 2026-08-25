using System;
using UnityEngine;
using UnityEngine.Serialization;

// Builds and owns the GameObjects that make one base's interior visible: a tile prefab per grid
// cell, parented under a single container.
//
// Purely local to whichever peer built it. The tile grid itself is synced state on PlayerBase, so
// nothing here needs a NetworkIdentity - every peer instantiates the same tiles from the same grid,
// which is far cheaper than spawning a few dozen network objects per base.
public class BaseInterior
{
    [Serializable]
    public class Prefabs
    {
        public GameObject floor;
        public GameObject obstacle;
        public GameObject queen;
        public GameObject barrack;
        [FormerlySerializedAs("resourceStock")] public GameObject magazine;
        public GameObject growthTile;
        public GameObject entry;

        // Floor is every type's fallback, so a base with only a floor prefab assigned still renders
        // as a complete room rather than a grid of holes.
        public GameObject For(TileType type)
        {
            GameObject prefab;
            switch (type)
            {
                case TileType.Obstacle: prefab = obstacle; break;
                case TileType.Queen: prefab = queen; break;
                case TileType.Barrack: prefab = barrack; break;
                case TileType.Magazine: prefab = magazine; break;
                case TileType.GrowthTile: prefab = growthTile; break;
                case TileType.Entry: prefab = entry; break;
                default: prefab = floor; break;
            }

            return prefab != null ? prefab : floor;
        }
    }

    private readonly PlayerBase homeBase;
    private readonly Prefabs prefabs;
    private readonly Transform root;
    private readonly GameObject[] tileObjects;

    public BaseInterior(PlayerBase homeBase, Prefabs prefabs)
    {
        this.homeBase = homeBase;
        this.prefabs = prefabs;

        root = new GameObject($"{homeBase.name}_Interior").transform;
        root.SetParent(homeBase.transform, worldPositionStays: true);
        root.position = homeBase.InteriorCenter;

        tileObjects = new GameObject[homeBase.GridColumns * homeBase.GridRows];

        for (int y = 0; y < homeBase.GridRows; y++)
        {
            for (int x = 0; x < homeBase.GridColumns; x++)
            {
                BuildTile(new Vector2Int(x, y));
            }
        }
    }

    public void Destroy()
    {
        if (root != null) UnityEngine.Object.Destroy(root.gameObject);
    }

    // (Re)builds a single tile from whatever PlayerBase currently says is there - used both to lay
    // the interior out at startup and to swap one tile when a player builds on it.
    public void BuildTile(Vector2Int coords)
    {
        int index = coords.y * homeBase.GridColumns + coords.x;
        if (index < 0 || index >= tileObjects.Length) return;

        if (tileObjects[index] != null)
        {
            UnityEngine.Object.Destroy(tileObjects[index]);
            tileObjects[index] = null;
        }

        TileType type = homeBase.TileAt(coords);
        GameObject prefab = prefabs != null ? prefabs.For(type) : null;
        if (prefab != null)
        {
            GameObject tile = UnityEngine.Object.Instantiate(prefab, homeBase.TileCenter(coords), Quaternion.identity, root);
            tile.name = $"Tile_{type}_{coords.x}_{coords.y}";

            // Tile prefabs are authored one world unit square, so a base with a bigger tileSize
            // scales them up rather than leaving gaps. Multiplying keeps whatever scale the prefab
            // was authored with - a decoration deliberately set to 0.9 stays nine tenths of a tile.
            tile.transform.localScale *= homeBase.TileSize;
            tile.AddComponent<BaseTile>().Initialize(homeBase, type, coords);
            tileObjects[index] = tile;
        }

        ShowMagazineFill(coords);
    }

    // Draws one magazine's piles from what the base says is in it. Anything that isn't a magazine
    // has nothing to show, so this is safe to call for any tile.
    public void ShowMagazineFill(Vector2Int coords)
    {
        int index = coords.y * homeBase.GridColumns + coords.x;
        if (index < 0 || index >= tileObjects.Length || tileObjects[index] == null) return;

        Magazine magazine = tileObjects[index].GetComponent<Magazine>();
        if (magazine != null) magazine.SetFill(homeBase.MagazineAmount(coords));
    }
}
