using System;
using System.Collections.Generic;
using UnityEngine;

// Routing across a base interior's tile grid. Obstacles used to only ever be the outer walls, so a
// unit could just walk at its destination and slide along whatever it brushed; a layout that puts
// walls in the middle of the room - a magazine tucked away behind one, say - needs a real route.
//
// Breadth-first over the tiles: a room is a few dozen of them, so the cheapest possible search is
// plenty, and because every step costs the same its "fewest tiles" answer is the shortest route.
// The tile path is then string-pulled into as few waypoints as the walls allow, so units cut across
// open floor diagonally instead of stepping tile centre to tile centre.
public static class InteriorPath
{
    // Reused between calls rather than allocated per search: every unit re-routes whenever it picks
    // a new destination, and the server steps one unit at a time, so a single shared set is safe.
    private static int[] cameFrom = new int[0];
    private static int[] frontier = new int[0];

    private static readonly Vector2Int[] Neighbours =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1),
    };

    // Fills waypoints with the route from `from` to `to`, the last one being the destination itself.
    // An unreachable destination (walled off, or a wall itself) gets the closest reachable point
    // instead - a unit is better off walking as far as it can than standing still forever.
    public static void Find(PlayerBase interior, Vector3 from, Vector3 to, List<Vector3> waypoints)
    {
        waypoints.Clear();

        to = interior.ClampToInterior(to);

        Vector2Int start = interior.WorldToTile(from);
        Vector2Int goal = interior.WorldToTile(to);

        // Nothing in the way - by far the most common case, and worth not searching for.
        if (start == goal || HasClearLine(interior, from, to))
        {
            waypoints.Add(to);
            return;
        }

        Flood(interior, start, null, out _);

        int goalIndex = Index(interior, goal);
        int destination = cameFrom[goalIndex] != Unvisited ? goalIndex : ClosestVisited(interior, to);
        if (destination < 0)
        {
            waypoints.Add(to);
            return;
        }

        Trace(interior, start, destination, waypoints);

        // Nowhere better to be than where it already is - stop here rather than leaving an empty
        // route, which would send the unit walking at the wall it can't get past.
        if (waypoints.Count == 0) waypoints.Add(interior.TileCenter(start));

        // Walk to the exact spot asked for rather than the middle of the tile it sits in.
        else if (destination == goalIndex) waypoints[waypoints.Count - 1] = to;

        Smooth(interior, from, waypoints);
    }

    // The nearest tile satisfying `accept`, measured in steps actually walked rather than as the
    // crow flies - a magazine on the far side of a wall really is further away than it looks. False
    // when nothing reachable matches.
    public static bool TryFindNearest(PlayerBase interior, Vector3 from, Predicate<Vector2Int> accept, out Vector2Int found)
    {
        Vector2Int start = interior.WorldToTile(from);

        // Standing on one already - and a flood would never visit its own start tile.
        if (accept(start))
        {
            found = start;
            return true;
        }

        return Flood(interior, start, accept, out found);
    }

    private const int Unvisited = -1;

    // Two boundary crossings this close together are the same crossing - the line is going through
    // the corner rather than past it.
    private const float CornerEpsilon = 1e-4f;

    private static int Index(PlayerBase interior, Vector2Int tile) => tile.y * interior.GridColumns + tile.x;

    private static void Prepare(PlayerBase interior)
    {
        int size = interior.GridColumns * interior.GridRows;
        if (cameFrom.Length < size)
        {
            cameFrom = new int[size];
            frontier = new int[size];
        }

        for (int i = 0; i < size; i++) cameFrom[i] = Unvisited;
    }

    // Breadth-first out from start, recording how each tile was reached in cameFrom. With no accept
    // it floods the whole reachable region - which is what routing wants, so an unreachable
    // destination can still be answered with the closest tile we did get to. With one it stops at
    // the first tile that satisfies it, which is therefore the nearest such tile by steps walked.
    private static bool Flood(PlayerBase interior, Vector2Int start, Predicate<Vector2Int> accept, out Vector2Int found)
    {
        int columns = interior.GridColumns;
        Prepare(interior);
        found = start;

        int head = 0;
        int tail = 0;
        int startIndex = Index(interior, start);
        cameFrom[startIndex] = startIndex;
        frontier[tail++] = startIndex;

        while (head < tail)
        {
            int current = frontier[head++];
            Vector2Int tile = new Vector2Int(current % columns, current / columns);

            foreach (Vector2Int step in Neighbours)
            {
                Vector2Int next = tile + step;
                if (!interior.InBounds(next) || !interior.IsWalkable(next)) continue;

                int index = Index(interior, next);
                if (cameFrom[index] != Unvisited) continue;

                cameFrom[index] = current;

                if (accept != null && accept(next))
                {
                    found = next;
                    return true;
                }

                frontier[tail++] = index;
            }
        }

        return false;
    }

    private static int ClosestVisited(PlayerBase interior, Vector3 to)
    {
        int columns = interior.GridColumns;
        int best = -1;
        float bestDistance = float.MaxValue;

        for (int index = 0; index < columns * interior.GridRows; index++)
        {
            if (cameFrom[index] == Unvisited) continue;

            float distance = (interior.TileCenter(new Vector2Int(index % columns, index / columns)) - to).sqrMagnitude;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = index;
        }

        return best;
    }

    // Walks the cameFrom chain back from the destination and writes it out forwards, skipping the
    // tile the unit is already standing on.
    private static void Trace(PlayerBase interior, Vector2Int start, int destination, List<Vector3> waypoints)
    {
        int columns = interior.GridColumns;
        int startIndex = Index(interior, start);
        int current = destination;

        while (current != startIndex)
        {
            waypoints.Add(interior.TileCenter(new Vector2Int(current % columns, current / columns)));
            current = cameFrom[current];
        }

        waypoints.Reverse();
    }

    // Drops every waypoint that can be skipped without the straight line to the next one touching a
    // wall, turning a staircase of tile centres into the handful of corners it really has.
    private static void Smooth(PlayerBase interior, Vector3 from, List<Vector3> waypoints)
    {
        Vector3 current = from;

        for (int i = 0; i < waypoints.Count; i++)
        {
            int furthest = i;
            for (int j = waypoints.Count - 1; j > i; j--)
            {
                if (!HasClearLine(interior, current, waypoints[j])) continue;

                furthest = j;
                break;
            }

            // Everything between here and the furthest one in sight is a detour.
            if (furthest > i) waypoints.RemoveRange(i, furthest - i);

            current = waypoints[i];
        }
    }

    // Whether a unit can walk straight from a to b without clipping a wall. Steps tile boundary to
    // tile boundary rather than sampling points along the line: waypoints are tile centres, so a
    // shortcut between them is often an exact 45 degree diagonal straight through the corner where
    // four tiles meet, and sampling is happy to skip right over the wall on the far side of it.
    private static bool HasClearLine(PlayerBase interior, Vector3 a, Vector3 b)
    {
        Vector2Int tile = interior.WorldToTile(a);
        Vector2Int goal = interior.WorldToTile(b);
        if (!interior.IsWalkable(tile) || !interior.IsWalkable(goal)) return false;

        float dx = b.x - a.x;
        float dy = b.y - a.y;
        int stepX = dx > 0f ? 1 : dx < 0f ? -1 : 0;
        int stepY = dy > 0f ? 1 : dy < 0f ? -1 : 0;

        Vector3 origin = interior.GridOrigin;
        float size = interior.TileSize;

        // How far along the line (as a fraction of it) the next boundary on each axis lies, and what
        // a whole tile is worth in the same terms.
        float nextX = stepX == 0 ? float.MaxValue : BoundaryFraction(a.x - origin.x, dx, size, stepX);
        float nextY = stepY == 0 ? float.MaxValue : BoundaryFraction(a.y - origin.y, dy, size, stepY);
        float perTileX = stepX == 0 ? float.MaxValue : size / Mathf.Abs(dx);
        float perTileY = stepY == 0 ? float.MaxValue : size / Mathf.Abs(dy);

        int guard = interior.GridColumns + interior.GridRows + 2;

        while (tile != goal && guard-- > 0)
        {
            // Both boundaries are past the end of the line - nothing left to cross.
            if (nextX > 1f && nextY > 1f) break;

            if (Mathf.Abs(nextX - nextY) < CornerEpsilon)
            {
                // Dead through the corner where four tiles meet: both tiles beside it have to be
                // clear, or the line cuts across a wall.
                if (!interior.IsWalkable(new Vector2Int(tile.x + stepX, tile.y))) return false;
                if (!interior.IsWalkable(new Vector2Int(tile.x, tile.y + stepY))) return false;

                tile = new Vector2Int(tile.x + stepX, tile.y + stepY);
                nextX += perTileX;
                nextY += perTileY;
            }
            else if (nextX < nextY)
            {
                tile = new Vector2Int(tile.x + stepX, tile.y);
                nextX += perTileX;
            }
            else
            {
                tile = new Vector2Int(tile.x, tile.y + stepY);
                nextY += perTileY;
            }

            if (!interior.IsWalkable(tile)) return false;
        }

        // Anything other than actually arriving is treated as blocked - the route then stays tile by
        // tile, which is never wrong, only less direct.
        return tile == goal;
    }

    // How far along the line (0..1) its first crossing of a tile boundary on one axis lies.
    private static float BoundaryFraction(float local, float delta, float size, int step)
    {
        float within = local - Mathf.Floor(local / size) * size;
        return (step > 0 ? size - within : within) / Mathf.Abs(delta);
    }
}
