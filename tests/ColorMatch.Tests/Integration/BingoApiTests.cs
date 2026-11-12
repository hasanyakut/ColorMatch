using System.Net;
using System.Net.Http.Json;
using ColorMatch.Application.DTOs;
using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Enums;
using ColorMatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ColorMatch.Tests.Integration;

[Collection("api")]
public class BingoApiTests(ApiFactory factory)
{
    private static Task<BingoBoardDto> Board(HttpClient c) => c.GetFromJsonAsync<BingoBoardDto>("/api/game/bingo")!;

    private static Task<HttpResponseMessage> Post(HttpClient c, int cell, byte[] jpeg)
        => c.PostAsync($"/api/game/bingo/cells/{cell}/attempts", ApiFactory.HuntForm(jpeg, 0.5, 0.5, 0.1));

    private static async Task<BingoAttemptResultDto> Attempt(HttpClient c, int cell, byte[] jpeg)
    {
        var resp = await Post(c, cell, jpeg);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<BingoAttemptResultDto>())!;
    }

    /// <summary>A photo that can never pass for <paramref name="hex"/>: pure black or white, whichever is farther (boards are random, so no fixed colour is safe).</summary>
    private static byte[] Wrong(string hex)
    {
        var c = ColorMatch.ColorEngine.ColorConversion.Rgb.FromHex(hex);
        return ApiFactory.SolidJpeg((0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 128 ? "#000000" : "#FFFFFF");
    }

    /// <summary>A valid photo for a square: exactly its colour.</summary>
    private static async Task<BingoAttemptResultDto> Fill(HttpClient c, BingoBoardDto board, int cell)
        => await Attempt(c, cell, ApiFactory.SolidJpeg(board.Cells.Single(x => x.Index == cell).Hex));

    private async Task<(int Total, int Ledger)> Xp(Guid user)
    {
        using var s = factory.Services.CreateScope();
        var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
        var total = (await db.Progress.AsNoTracking().FirstAsync(p => p.UserId == user)).TotalXp;
        var ledger = await db.XpTransactions.Where(t => t.UserId == user).SumAsync(t => (int?)t.Amount) ?? 0;
        return (total, ledger);
    }

    [Fact]
    public async Task Bingo_endpoints_require_authentication()
    {
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/game/bingo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/api/game/bingo/cells/0/attempts", ApiFactory.HuntForm(ApiFactory.SolidJpeg("#112233")))).StatusCode);
    }

    [Fact]
    public async Task A_new_player_gets_one_nine_square_board_that_stays_the_same()
    {
        var (c, _) = await factory.RegisterAsync();
        var board = await Board(c);

        Assert.Equal("Active", board.Status);
        Assert.Equal(9, board.Cells.Count);
        Assert.Equal(Enumerable.Range(0, 9), board.Cells.Select(x => x.Index));
        Assert.Equal(9, board.Cells.Select(x => x.Hex).Distinct().Count());
        Assert.Equal(new[] { 3, 3, 3 }, new[] { "Easy", "Medium", "Hard" }.Select(d => board.Cells.Count(x => x.Difficulty == d)));
        Assert.Equal(8, board.Lines.Count);
        Assert.All(board.Cells, x => { Assert.False(x.Completed); Assert.Equal(0, x.Attempts); Assert.Equal(10, x.AttemptsLeft); });
        Assert.Equal(0, board.CompletedLines);
        Assert.Equal((100, 400, 700), (board.LineXp, board.BoardXp, board.MinScore));

        var again = await Board(c);
        Assert.Equal(board.BoardId, again.BoardId);   // one active board
        Assert.Equal(board.Cells.Select(x => x.ColorKey), again.Cells.Select(x => x.ColorKey));
    }

    [Fact]
    public async Task Every_player_has_their_own_board()
    {
        var (a, _) = await factory.RegisterAsync();
        var (b, _) = await factory.RegisterAsync();
        Assert.NotEqual((await Board(a)).BoardId, (await Board(b)).BoardId);
    }

    [Fact]
    public async Task A_failed_attempt_completes_nothing_and_a_valid_one_completes_the_square_and_unlocks_the_atlas_colour()
    {
        var (c, auth) = await factory.RegisterAsync();
        var board = await Board(c);
        var cell = board.Cells[4];

        var bad = await Attempt(c, 4, Wrong(cell.Hex));
        Assert.False(bad.Passed);
        Assert.False(bad.CellCompleted);
        Assert.Equal(0, bad.LineXpAwarded);
        Assert.Null(bad.AtlasUnlock);
        Assert.Equal(0, bad.TotalXp);

        var good = await Fill(c, board, 4);
        Assert.True(good.Passed);
        Assert.True(good.CellCompleted);
        Assert.Equal(1, good.CompletedCells);
        Assert.Equal(0, good.CompletedLines);
        Assert.Equal(cell.Hex, good.TargetHex);
        Assert.NotNull(good.AtlasUnlock);
        Assert.Equal(cell.ColorKey, good.AtlasUnlock!.ColorKey);
        Assert.Equal(0, good.TotalXp);   // squares themselves pay nothing; lines and the board do

        var now = await Board(c);
        var c4 = now.Cells[4];
        Assert.True(c4.Completed);
        Assert.Equal(2, c4.Attempts);
        Assert.True(c4.BestScore >= 700);

        // a replay does not complete it again or unlock again
        var replay = await Fill(c, board, 4);
        Assert.True(replay.Passed);
        Assert.False(replay.CellCompleted);
        Assert.Null(replay.AtlasUnlock);
        Assert.Equal(1, (await c.GetFromJsonAsync<AtlasDto>("/api/game/atlas"))!.Unlocked);
        Assert.Equal((0, 0), await Xp(auth.User.Id));
    }

    [Fact]
    public async Task A_row_pays_its_xp_once_when_the_third_square_is_filled()
    {
        var (c, auth) = await factory.RegisterAsync();
        var board = await Board(c);

        Assert.Equal(0, (await Fill(c, board, 0)).LineXpAwarded);
        Assert.Equal(0, (await Fill(c, board, 1)).LineXpAwarded);
        var third = await Fill(c, board, 2);
        Assert.Equal(100, third.LineXpAwarded);
        Assert.Equal(new[] { "row-0" }, third.NewLines.Select(l => l.Id));
        Assert.Equal(1, third.CompletedLines);
        Assert.Equal(100, third.TotalXp);
        Assert.True(third.LeveledUp);   // 100 XP is level 2

        // replaying a filled square pays nothing more
        var replay = await Fill(c, board, 2);
        Assert.Equal(0, replay.LineXpAwarded);
        Assert.Empty(replay.NewLines);
        Assert.Equal(100, replay.TotalXp);
        Assert.Equal((100, 100), await Xp(auth.User.Id));
    }

    [Fact]
    public async Task One_square_can_finish_a_row_a_column_and_a_diagonal_at_once()
    {
        var (c, _) = await factory.RegisterAsync();
        var board = await Board(c);
        // everything on the lines through the centre except the centre itself, then the centre closes four lines
        foreach (var i in new[] { 0, 1, 2, 3, 5, 6, 7, 8 })
        {
            var r = await Fill(c, board, i);
            Assert.True(r.CellCompleted);
        }
        var centre = await Fill(c, board, 4);
        Assert.True(centre.BoardCompleted);
        Assert.Equal(400, centre.BoardXpAwarded);
        Assert.Equal(new[] { "row-1", "col-1", "diag-main", "diag-anti" }.OrderBy(x => x), centre.NewLines.Select(l => l.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task Completing_all_nine_squares_pays_eight_lines_and_the_board_bonus_then_a_new_board_starts()
    {
        var (c, auth) = await factory.RegisterAsync();
        var board = await Board(c);

        var lineXp = 0; BingoAttemptResultDto? last = null;
        for (var i = 0; i < 9; i++) { last = await Fill(c, board, i); lineXp += last.LineXpAwarded; }

        Assert.Equal(800, lineXp);
        Assert.True(last!.BoardCompleted);
        Assert.Equal(400, last.BoardXpAwarded);
        Assert.Equal(9, last.CompletedCells);
        Assert.Equal(8, last.CompletedLines);
        Assert.Equal(1200, last.TotalXp);
        var (total, ledger) = await Xp(auth.User.Id);
        Assert.Equal((1200, 1200), (total, ledger));

        using (var s = factory.Services.CreateScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(8, await db.XpTransactions.CountAsync(t => t.UserId == auth.User.Id && t.SourceType == XpSourceType.BingoLine));
            Assert.Equal(1, await db.XpTransactions.CountAsync(t => t.UserId == auth.User.Id && t.SourceType == XpSourceType.BingoBoardComplete));
        }

        // the completed board accepts nothing more; the next board request is a fresh one
        var next = await Board(c);
        Assert.NotEqual(board.BoardId, next.BoardId);
        Assert.Equal("Active", next.Status);
        Assert.Equal(1, next.BoardsCompleted);
        Assert.Equal(0, next.CompletedCells);
        Assert.Equal((1200, 1200), await Xp(auth.User.Id));
    }

    [Fact]
    public async Task A_square_has_a_limited_number_of_attempts()
    {
        var (c, _) = await factory.RegisterAsync();
        var board = await Board(c);
        var wrong = Wrong(board.Cells[8].Hex);
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.OK, (await Post(c, 8, wrong)).StatusCode);
        Assert.Equal((HttpStatusCode)429, (await Post(c, 8, wrong)).StatusCode);
        Assert.Equal(0, (await Board(c)).Cells[8].AttemptsLeft);
        Assert.Equal(HttpStatusCode.OK, (await Post(c, 7, Wrong(board.Cells[7].Hex))).StatusCode);   // other squares are not affected
    }

    [Fact]
    public async Task One_photo_cannot_fill_two_squares_and_bad_squares_are_not_found()
    {
        var (c, _) = await factory.RegisterAsync();
        var board = await Board(c);
        var photo = ApiFactory.SolidJpeg(board.Cells[0].Hex);
        Assert.True((await Attempt(c, 0, photo)).CellCompleted);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(c, 1, photo)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, 9, photo)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(c, -1, photo)).StatusCode);
    }

    [Fact]
    public async Task Parallel_valid_attempts_on_one_square_complete_it_and_pay_its_line_once()
    {
        var (c, auth) = await factory.RegisterAsync();
        var board = await Board(c);
        await Fill(c, board, 0);
        await Fill(c, board, 1);

        var photo = ApiFactory.SolidJpeg(board.Cells[2].Hex);
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Post(c, 2, photo)));
        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict));
        var results = await Task.WhenAll(responses.Where(r => r.StatusCode == HttpStatusCode.OK).Select(r => r.Content.ReadFromJsonAsync<BingoAttemptResultDto>()));

        Assert.Equal(1, results.Count(r => r!.CellCompleted));
        Assert.Equal(100, results.Sum(r => r!.LineXpAwarded));
        Assert.Equal((100, 100), await Xp(auth.User.Id));
        Assert.Equal(1, (await Board(c)).CompletedLines);
    }

    [Fact]
    public async Task A_player_cannot_play_on_someone_elses_board()
    {
        var (a, _) = await factory.RegisterAsync();
        var (b, _) = await factory.RegisterAsync();
        var boardA = await Board(a);
        await Board(b);

        // the endpoint only ever addresses the caller's own active board: A's colours do not fill B's squares unless they happen to match
        var r = await Post(b, 0, ApiFactory.SolidJpeg(boardA.Cells[0].Hex));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var boardB = await Board(b);
        Assert.Equal(boardB.Cells[0].Completed, boardB.Cells[0].Hex == boardA.Cells[0].Hex);
        Assert.Equal(0, (await Board(a)).CompletedCells);
    }
}
