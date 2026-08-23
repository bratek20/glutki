using System.Collections.Generic;

// Parses the letter grid a base interior is authored with. Rows are written top row first, the way
// they read on screen, and flipped here so row 0 of the parsed grid is the bottom row - grid Y
// grows upward, like world Y does.
public static class BaseLayout
{
    public const string Default =
        "OOOEOOO\n" +
        "OFBFBFO\n" +
        "OQQFGGO\n" +
        "OFFFFFO\n" +
        "ORRRRRO\n" +
        "OOOOOOO";

    // Row-major from the bottom-left: index = y * columns + x. Returns false with a human-readable
    // error rather than throwing - a mistyped layout is an authoring slip, not a crash.
    public static bool TryParse(string text, out TileType[] tiles, out int columns, out int rows, out string error)
    {
        tiles = null;
        columns = 0;
        rows = 0;
        error = null;

        List<string> lines = new List<string>();
        foreach (string rawLine in (text ?? string.Empty).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length > 0) lines.Add(line);
        }

        if (lines.Count == 0)
        {
            error = "layout is empty";
            return false;
        }

        columns = lines[0].Length;
        rows = lines.Count;
        tiles = new TileType[columns * rows];

        for (int line = 0; line < rows; line++)
        {
            if (lines[line].Length != columns)
            {
                error = $"row {line + 1} is {lines[line].Length} tiles wide, expected {columns}";
                return false;
            }

            int y = rows - 1 - line;
            for (int x = 0; x < columns; x++)
            {
                char letter = lines[line][x];
                if (!TryParseLetter(letter, out TileType type))
                {
                    error = $"unknown letter '{letter}' at row {line + 1}, column {x + 1}";
                    return false;
                }

                tiles[y * columns + x] = type;
            }
        }

        return true;
    }

    public static bool TryParseLetter(char letter, out TileType type)
    {
        switch (char.ToUpperInvariant(letter))
        {
            case 'F': type = TileType.Floor; return true;
            case 'O': type = TileType.Obstacle; return true;
            case 'Q': type = TileType.Queen; return true;
            case 'B': type = TileType.Barrack; return true;
            case 'R': type = TileType.ResourceStock; return true;
            case 'G': type = TileType.GrowthTile; return true;
            case 'E': type = TileType.Entry; return true;
            default: type = TileType.Floor; return false;
        }
    }
}
