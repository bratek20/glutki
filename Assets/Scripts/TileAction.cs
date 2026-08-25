// What clicking a tile inside a base does while one of the tile action buttons is armed - see
// TileActionButton. Build is deliberately first: it's the enum's default, so the button that was in
// the scene before the other two existed keeps meaning what it always did.
public enum TileAction
{
    // Put a magazine up on a floor tile. Instant, and costs nothing.
    Build,

    // Have a Builder dig an obstacle out, leaving floor.
    Dig,

    // Have a Builder fill a floor tile in, leaving an obstacle.
    Fill
}
