namespace ColorMatch.Domain.Entities;

public enum BingoStatus { Active, Completed }

/// <summary>A player's 3×3 Color Bingo board. At most one board per player is Active (filtered unique index).</summary>
public class BingoBoard
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public BingoStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    /// <summary>Concurrency token: two attempts finishing the board at the same moment cannot both close it.</summary>
    public byte[] RowVersion { get; set; } = [];
    public List<BingoCell> Cells { get; set; } = [];
}

/// <summary>One square of a board: a target colour of the Color Atlas catalogue. Completed once, by the first valid attempt.</summary>
public class BingoCell
{
    public Guid Id { get; set; }
    public Guid BoardId { get; set; }
    public BingoBoard? Board { get; set; }
    /// <summary>0..8, row-major.</summary>
    public int Index { get; set; }
    /// <summary>Key of the target colour in <c>AtlasCatalog</c>.</summary>
    public string ColorKey { get; set; } = string.Empty;
    public DateTime? CompletedAtUtc { get; set; }
    public int? BestScore { get; set; }
    public Guid? CompletedByAttemptId { get; set; }
    /// <summary>Concurrency token: a cell is completed (and its lines paid) exactly once.</summary>
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>One try on a cell. The photo itself is not stored, only its hash (basic re-use check).</summary>
public class BingoAttempt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid BoardId { get; set; }
    public int CellIndex { get; set; }
    public int Score { get; set; }
    public double DeltaE { get; set; }
    public string SampledColorHex { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string PhotoHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
