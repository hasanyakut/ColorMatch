namespace ColorMatch.Domain.Entities;

public class DailyColorChallenge
{
    public int Id { get; set; }
    /// <summary>Challenge day in the configured challenge time zone. Unique.</summary>
    public DateOnly Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Hex { get; set; } = string.Empty;
}
