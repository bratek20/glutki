using UnityEngine;

// Runtime handle on one tile of a base interior, added to every tile object as it's built. No
// gameplay logic reads it - the authoritative grid is state on PlayerBase - it exists so a tile
// picked out of the hierarchy says what it is and where it sits.
public class BaseTile : MonoBehaviour
{
    public TileType Type { get; private set; }
    public Vector2Int Coords { get; private set; }
    public PlayerBase HomeBase { get; private set; }

    public void Initialize(PlayerBase homeBase, TileType type, Vector2Int coords)
    {
        HomeBase = homeBase;
        Type = type;
        Coords = coords;
    }
}
