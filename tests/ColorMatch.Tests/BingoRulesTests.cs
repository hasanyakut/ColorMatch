using ColorMatch.Application.Services;
using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.ColorEngine.ColorScoring;
using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;

namespace ColorMatch.Tests;

public class BingoRulesTests
{
    private static IReadOnlySet<int> Set(params int[] cells) => cells.ToHashSet();

    [Fact]
    public void There_are_eight_lines_of_three_distinct_cells()
    {
        Assert.Equal(8, BingoRules.Lines.Count);
        Assert.Equal(8, BingoRules.Lines.Select(l => l.Id).Distinct().Count());
        Assert.All(BingoRules.Lines, l => { Assert.Equal(3, l.Cells.Count); Assert.Equal(3, l.Cells.Distinct().Count()); Assert.All(l.Cells, c => Assert.True(BingoRules.IsValidCell(c))); });
        Assert.Equal(8, BingoRules.Lines.Select(l => string.Join(',', l.Cells.OrderBy(x => x))).Distinct().Count());
    }

    [Fact]
    public void The_centre_is_in_four_lines_corners_in_three_and_edges_in_two()
    {
        int In(int cell) => BingoRules.Lines.Count(l => l.Cells.Contains(cell));
        Assert.Equal(4, In(4));
        Assert.All(new[] { 0, 2, 6, 8 }, c => Assert.Equal(3, In(c)));
        Assert.All(new[] { 1, 3, 5, 7 }, c => Assert.Equal(2, In(c)));
    }

    [Theory]
    [InlineData(new[] { 0, 1, 2 }, "row-0")]
    [InlineData(new[] { 3, 4, 5 }, "row-1")]
    [InlineData(new[] { 6, 7, 8 }, "row-2")]
    [InlineData(new[] { 0, 3, 6 }, "col-0")]
    [InlineData(new[] { 1, 4, 7 }, "col-1")]
    [InlineData(new[] { 2, 5, 8 }, "col-2")]
    [InlineData(new[] { 0, 4, 8 }, "diag-main")]
    [InlineData(new[] { 2, 4, 6 }, "diag-anti")]
    public void Each_line_is_detected_exactly_when_its_three_cells_are_done(int[] cells, string id)
    {
        Assert.Equal(new[] { id }, BingoRules.CompletedLines(Set(cells)).Select(l => l.Id));
        Assert.Empty(BingoRules.CompletedLines(Set(cells.Take(2).ToArray())));
    }

    [Fact]
    public void Scattered_cells_make_no_line_and_all_nine_make_all_eight()
    {
        Assert.Empty(BingoRules.CompletedLines(Set(0, 1, 5, 6, 7)));
        Assert.Equal(8, BingoRules.CompletedLines(Set(0, 1, 2, 3, 4, 5, 6, 7, 8)).Count);
        Assert.True(BingoRules.IsBoardComplete(Set(0, 1, 2, 3, 4, 5, 6, 7, 8)));
        Assert.False(BingoRules.IsBoardComplete(Set(0, 1, 2, 3, 4, 5, 6, 7)));
    }

    [Fact]
    public void Cell_index_validity()
    {
        Assert.True(BingoRules.IsValidCell(0));
        Assert.True(BingoRules.IsValidCell(8));
        Assert.False(BingoRules.IsValidCell(-1));
        Assert.False(BingoRules.IsValidCell(9));
    }

    [Fact]
    public void Generated_boards_have_nine_distinct_well_separated_colours_three_per_difficulty()
    {
        for (var seed = 0; seed < 60; seed++)
        {
            var board = BingoBoardGenerator.Generate(new Random(seed));
            Assert.Equal(9, board.Count);
            Assert.Equal(9, board.Select(c => c.Key).Distinct().Count());
            Assert.Equal(new[] { 3, 3, 3 }, new[] { HuntDifficulty.Easy, HuntDifficulty.Medium, HuntDifficulty.Hard }.Select(d => board.Count(c => c.Difficulty == d)));
            for (var i = 0; i < 9; i++)
                for (var j = i + 1; j < 9; j++)
                    Assert.True(Ciede2000.Difference(Rgb.FromHex(board[i].Hex), Rgb.FromHex(board[j].Hex)) >= BingoBoardGenerator.MinSeparation,
                        $"seed {seed}: {board[i].Name} / {board[j].Name} too close");
        }
    }

    [Fact]
    public void The_same_seed_gives_the_same_board_and_different_seeds_differ()
    {
        Assert.Equal(BingoBoardGenerator.Generate(new Random(7)).Select(c => c.Key), BingoBoardGenerator.Generate(new Random(7)).Select(c => c.Key));
        var boards = Enumerable.Range(0, 20).Select(s => string.Join('|', BingoBoardGenerator.Generate(new Random(s)).Select(c => c.Key))).Distinct().Count();
        Assert.True(boards > 15);
    }

    [Fact]
    public void Bingo_balance_defaults()
    {
        var b = new GameBalance().Bingo;
        Assert.Equal(100, b.LineXp);
        Assert.Equal(400, b.BoardCompleteXp);
        Assert.Equal(500, b.MinScore);
        Assert.Equal(10, b.MaxAttemptsPerCell);
    }
}
