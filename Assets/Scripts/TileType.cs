// What one tile of a base interior is. Every type has its own prefab (see BaseInterior.Prefabs)
// and its own rules; the letters are what a layout is authored with, see BaseLayout.
public enum TileType
{
    // F - the default. Nothing on it, units walk over it freely.
    Floor,

    // O - solid. Units can't walk through it. Today these are only ever the base's outer walls.
    Obstacle,

    // Q - where the Queen stands. She always covers exactly two of these, side by side, and parks
    // herself on the seam between them.
    Queen,

    // B - home of exactly one Attacker. An Attacker can only be ordered while a barrack is free.
    Barrack,

    // M - a magazine: a deposit point holding up to eight resources (four StoredResource piles of
    // two), and nothing beyond that - a full one turns Gatherers away.
    Magazine,

    // G - where exactly one unit at a time grows up.
    GrowthTile,

    // E - the gap in the wall units warp in and out of the interior through.
    Entry
}
