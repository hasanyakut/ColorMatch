namespace ColorMatch.Domain.Rules;

/// <summary>A line of the 3×3 board: three row-major cell indexes (0..8).</summary>
public sealed record BingoLine(string Id, string Name, IReadOnlyList<int> Cells);

/// <summary>Pure rules of the 3×3 Color Bingo board.</summary>
public static class BingoRules
{
    public const int Size = 3;
    public const int CellCount = Size * Size;

    /// <summary>The eight lines: three rows, three columns and both diagonals. Cell index = row · 3 + column.</summary>
    public static readonly IReadOnlyList<BingoLine> Lines =
    [
        new("row-0", "Üst satır", [0, 1, 2]),
        new("row-1", "Orta satır", [3, 4, 5]),
        new("row-2", "Alt satır", [6, 7, 8]),
        new("col-0", "Sol sütun", [0, 3, 6]),
        new("col-1", "Orta sütun", [1, 4, 7]),
        new("col-2", "Sağ sütun", [2, 5, 8]),
        new("diag-main", "Çapraz ↘", [0, 4, 8]),
        new("diag-anti", "Çapraz ↙", [2, 4, 6]),
    ];

    public static bool IsValidCell(int index) => index is >= 0 and < CellCount;

    /// <summary>Lines whose three cells are all in <paramref name="completedCells"/>.</summary>
    public static IReadOnlyList<BingoLine> CompletedLines(IReadOnlySet<int> completedCells)
        => Lines.Where(l => l.Cells.All(completedCells.Contains)).ToList();

    public static bool IsBoardComplete(IReadOnlySet<int> completedCells) => completedCells.Count >= CellCount && Enumerable.Range(0, CellCount).All(completedCells.Contains);
}
