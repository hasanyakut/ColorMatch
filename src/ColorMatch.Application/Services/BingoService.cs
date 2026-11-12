using System.Security.Cryptography;
using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ColorMatch.Application.Services;

/// <summary>
/// Color Bingo: one active 3×3 board per player, generated on the server from the Color Atlas catalogue. A square is completed by the first
/// photo that reaches the minimum score on its colour (and also unlocks that colour in the Atlas). Every row, column and diagonal pays once
/// when its third square is completed; completing all nine squares pays the board bonus once and the next request gets a fresh board.
/// Rewards are guarded by idempotency keys and a concurrency token on each square, so parallel requests cannot pay twice.
/// </summary>
public sealed class BingoService(
    IAppDbContext db,
    IColorMatchService colorMatch,
    IRewardService rewards,
    IAtlasService atlas,
    LevelCurve curve,
    IOptions<GameBalance> balanceOptions,
    TimeProvider clock) : IBingoService
{
    private const int CommitAttempts = 5;
    private BingoBalance Balance => balanceOptions.Value.Bingo;

    public static string LineKey(Guid boardId, string lineId, Guid userId) => $"bingo-line:{boardId:N}:{lineId}:{userId:N}";
    public static string BoardKey(Guid boardId, Guid userId) => $"bingo-board:{boardId:N}:{userId:N}";

    // ------------------------------------------------------------------ the board
    private Task<BingoBoard?> LoadActiveAsync(Guid userId, CancellationToken ct)
        => db.BingoBoards.AsNoTracking().Include(b => b.Cells)
            .FirstOrDefaultAsync(b => b.UserId == userId && b.Status == BingoStatus.Active, ct);

    private async Task<BingoBoard> GetOrCreateActiveAsync(Guid userId, CancellationToken ct)
    {
        var existing = await LoadActiveAsync(userId, ct);
        if (existing is not null) return existing;

        var colors = BingoBoardGenerator.Generate(Random.Shared);
        var board = new BingoBoard { Id = Guid.NewGuid(), UserId = userId, Status = BingoStatus.Active, CreatedAtUtc = clock.GetUtcNow().UtcDateTime };
        for (var i = 0; i < colors.Count; i++)
            board.Cells.Add(new BingoCell { Id = Guid.NewGuid(), BoardId = board.Id, Index = i, ColorKey = colors[i].Key });
        db.BingoBoards.Add(board);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // another request created the active board first: use that one
            db.ChangeTracker.Clear();
            return await LoadActiveAsync(userId, ct) ?? throw new ConflictException("Could not create a bingo board. Please try again.");
        }
        db.ChangeTracker.Clear();
        return (await LoadActiveAsync(userId, ct))!;
    }

    public async Task<BingoBoardDto> GetCurrentAsync(Guid userId, CancellationToken ct)
        => await ToDtoAsync(await GetOrCreateActiveAsync(userId, ct), userId, ct);

    private async Task<BingoBoardDto> ToDtoAsync(BingoBoard board, Guid userId, CancellationToken ct)
    {
        var stats = (await db.BingoAttempts.AsNoTracking().Where(a => a.BoardId == board.Id)
                .GroupBy(a => a.CellIndex).Select(g => new { Index = g.Key, Count = g.Count(), Best = g.Max(a => a.Score) }).ToListAsync(ct))
            .ToDictionary(x => x.Index);
        var boardsDone = await db.BingoBoards.AsNoTracking().CountAsync(b => b.UserId == userId && b.Status == BingoStatus.Completed, ct);

        var cells = board.Cells.OrderBy(c => c.Index).Select(c =>
        {
            var col = AtlasCatalog.FindByKey(c.ColorKey)!;
            stats.TryGetValue(c.Index, out var st);
            var used = st?.Count ?? 0;
            return new BingoCellDto(c.Index, col.Key, col.Name, col.Hex, col.Difficulty.ToString(), c.CompletedAtUtc is not null,
                Math.Max(st?.Best ?? 0, c.BestScore ?? 0), used, Math.Max(0, Balance.MaxAttemptsPerCell - used));
        }).ToList();

        var done = cells.Where(c => c.Completed).Select(c => c.Index).ToHashSet();
        var doneLines = BingoRules.CompletedLines(done).Select(l => l.Id).ToHashSet();
        var lines = BingoRules.Lines.Select(l => new BingoLineDto(l.Id, l.Name, l.Cells, doneLines.Contains(l.Id), Balance.LineXp)).ToList();

        return new BingoBoardDto(board.Id, board.Status.ToString(), board.CreatedAtUtc, cells, lines, done.Count, doneLines.Count,
            board.Status == BingoStatus.Completed, Balance.LineXp, Balance.BoardCompleteXp, Balance.MinScore, Balance.MaxAttemptsPerCell, boardsDone);
    }

    // ------------------------------------------------------------------ an attempt
    public async Task<BingoAttemptResultDto> SubmitAttemptAsync(BingoSubmission s, CancellationToken ct)
    {
        if (!BingoRules.IsValidCell(s.CellIndex)) throw new NotFoundException("Square not found.");

        var board = await GetOrCreateActiveAsync(s.UserId, ct);
        var cell = board.Cells.First(c => c.Index == s.CellIndex);
        var color = AtlasCatalog.FindByKey(cell.ColorKey)!;

        var used = await db.BingoAttempts.AsNoTracking().CountAsync(a => a.BoardId == board.Id && a.CellIndex == s.CellIndex, ct);
        if (used >= Balance.MaxAttemptsPerCell)
            throw new TooManyRequestsException($"You have used all {Balance.MaxAttemptsPerCell} attempts of this square.");

        // official score: computed here from the photo, never taken from the client
        var outcome = colorMatch.Evaluate(s.Photo, color.Hex);
        var hash = Convert.ToHexString(SHA256.HashData(outcome.Image.JpegBytes));

        // basic re-use check: one photo cannot fill two squares of a board (see the security notes for its limits)
        var reused = await db.BingoAttempts.AsNoTracking()
            .AnyAsync(a => a.BoardId == board.Id && a.PhotoHash == hash && a.Passed && a.CellIndex != s.CellIndex, ct);
        if (reused) throw new ConflictException("This photo was already used for another square. Take a new photo.");

        for (var attempt = 1; attempt <= CommitAttempts; attempt++)
        {
            try { return await CommitAsync(s, board.Id, color, outcome, hash, ct); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
            catch (DbUpdateException ex) when (IsDuplicateOfOurs(ex)) { db.ChangeTracker.Clear(); }
        }
        throw new ConflictException("Your progress changed at the same time. Please try again.");
    }

    private static bool IsDuplicateOfOurs(DbUpdateException ex)
        => ex.InnerException?.Message is { } m
           && (m.Contains("IdempotencyKey", StringComparison.OrdinalIgnoreCase) || m.Contains("UserAtlasColors", StringComparison.OrdinalIgnoreCase));

    /// <summary>The whole attempt as one unit of work: attempt row, square, line rewards, board bonus and atlas unlock commit together or not at all.</summary>
    private async Task<BingoAttemptResultDto> CommitAsync(BingoSubmission s, Guid boardId, AtlasColor color, ColorMatchOutcome outcome, string hash, CancellationToken ct)
    {
        var board = await db.BingoBoards.Include(b => b.Cells).FirstOrDefaultAsync(b => b.Id == boardId && b.UserId == s.UserId, ct)
                    ?? throw new ConflictException("This board is no longer active.");
        if (board.Status != BingoStatus.Active) throw new ConflictException("This board is already completed.");
        var cell = board.Cells.First(c => c.Index == s.CellIndex);
        var now = clock.GetUtcNow().UtcDateTime;

        var passed = outcome.Score >= Balance.MinScore;
        var previousBest = await db.BingoAttempts.AsNoTracking()
            .Where(a => a.BoardId == boardId && a.CellIndex == s.CellIndex).MaxAsync(a => (int?)a.Score, ct) ?? 0;
        var attemptId = Guid.NewGuid();

        db.BingoAttempts.Add(new BingoAttempt
        {
            Id = attemptId, UserId = s.UserId, BoardId = boardId, CellIndex = s.CellIndex, Score = outcome.Score,
            DeltaE = Math.Round(outcome.DeltaE, 3), SampledColorHex = outcome.Found.ToHex(), Passed = passed, PhotoHash = hash, CreatedAtUtc = now,
        });

        var before = board.Cells.Where(c => c.CompletedAtUtc is not null).Select(c => c.Index).ToHashSet();
        var cellCompleted = passed && cell.CompletedAtUtc is null;
        var after = new HashSet<int>(before);
        if (cellCompleted)
        {
            cell.CompletedAtUtc = now; cell.BestScore = outcome.Score; cell.CompletedByAttemptId = attemptId;
            after.Add(s.CellIndex);
        }

        var rewardResults = new List<RewardResult>();
        var newLines = new List<BingoLineRewardDto>();
        var lineXp = 0;
        if (cellCompleted)
        {
            var doneBefore = BingoRules.CompletedLines(before).Select(l => l.Id).ToHashSet();
            foreach (var line in BingoRules.CompletedLines(after).Where(l => !doneBefore.Contains(l.Id)))
            {
                var r = await rewards.GrantAsync(new RewardRequest(s.UserId, Balance.LineXp, XpSourceType.BingoLine, $"{boardId:N}:{line.Id}",
                    $"Color Bingo line: {line.Name}", LineKey(boardId, line.Id, s.UserId)), ct);
                rewardResults.Add(r);
                if (r.Granted) { lineXp += Balance.LineXp; newLines.Add(new BingoLineRewardDto(line.Id, line.Name, Balance.LineXp)); }
            }
        }

        var boardDone = cellCompleted && BingoRules.IsBoardComplete(after);
        var boardXp = 0;
        if (boardDone)
        {
            board.Status = BingoStatus.Completed;
            board.CompletedAtUtc = now;
            var r = await rewards.GrantAsync(new RewardRequest(s.UserId, Balance.BoardCompleteXp, XpSourceType.BingoBoardComplete, boardId.ToString("N"),
                "Color Bingo board completed", BoardKey(boardId, s.UserId)), ct);
            rewardResults.Add(r);
            if (r.Granted) boardXp = Balance.BoardCompleteXp;
        }

        // a valid result on an atlas colour also unlocks it there
        var atlasStaged = passed ? await atlas.StageUnlockAsync(s.UserId, color.Hex, attemptId, outcome.Score, ct) : null;
        if (atlasStaged?.CompletionReward is { } ar) rewardResults.Add(ar);

        await db.SaveChangesAsync(ct);

        var progress = await db.Progress.AsNoTracking().FirstAsync(p => p.UserId == s.UserId, ct);
        var levelBefore = rewardResults.Count > 0 ? rewardResults[0].PreviousLevel : progress.Level;
        var title = curve.TitleForLevel(progress.Level);

        return new BingoAttemptResultDto(
            attemptId, s.CellIndex, color.Name, color.Hex, outcome.Found.ToHex(), Math.Round(outcome.DeltaE, 2), outcome.Score,
            ScoreTiers.For(outcome.Score).ToString(), passed, Balance.MinScore, cellCompleted, Math.Max(previousBest, outcome.Score), outcome.Score > previousBest,
            after.Count, BingoRules.CompletedLines(after).Count, newLines, lineXp, boardDone, boardXp,
            progress.TotalXp, progress.Level, progress.Level > levelBefore, title, title != curve.TitleForLevel(levelBefore), atlasStaged?.Unlock, outcome.RegionDto);
    }
}
