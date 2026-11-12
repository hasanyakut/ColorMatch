using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.API.Configuration;
using ColorMatch.Application.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColorMatch.API.Controllers;

/// <summary>Player progression: XP, level, title, streak and the XP ledger. Read-only: XP is only ever created by game rules on the server.</summary>
[ApiController, Route("api/game/progression"), Authorize]
[Produces("application/json")]
public sealed class ProgressionController(IProgressionService progression) : ApiControllerBase
{
    /// <summary>The caller's level, title, XP progress and streak.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<ProgressionDto>> Me(CancellationToken ct) => await progression.GetAsync(CurrentUserId, ct);

    /// <summary>The caller's XP ledger, newest first (cursor pagination).</summary>
    [HttpGet("history")]
    public async Task<ActionResult<PagedResult<XpTransactionDto>>> History([FromQuery] string? cursor, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => await progression.GetHistoryAsync(CurrentUserId, cursor, pageSize, ct);
}

/// <summary>Daily Hunt: three server-chosen colours per game day.</summary>
[ApiController, Route("api/game/daily-hunts"), Authorize]
[Produces("application/json")]
public sealed class DailyHuntsController(IDailyHuntService hunts) : ApiControllerBase
{
    /// <summary>Today's easy / medium / hard hunt with the caller's best scores and completion state.</summary>
    [HttpGet]
    public async Task<ActionResult<DailyHuntsDto>> Today(CancellationToken ct) => await hunts.GetTodayAsync(CurrentUserId, ct);

    /// <summary>How many of today's hunts are done, and the completion bonus state.</summary>
    [HttpGet("progress")]
    public async Task<ActionResult<DailyProgressDto>> Progress(CancellationToken ct) => await hunts.GetProgressAsync(CurrentUserId, ct);

    /// <summary>The caller's last seven game days (oldest first): completed hunts per day.</summary>
    [HttpGet("week")]
    public async Task<ActionResult<WeekDto>> Week(CancellationToken ct) => await hunts.GetWeekAsync(CurrentUserId, ct);

    /// <summary>Today's ranking: sum of the best score of each of the three hunts.</summary>
    [HttpGet("leaderboard")]
    public async Task<ActionResult<LeaderboardDto>> Leaderboard([FromQuery] int take = 5, CancellationToken ct = default)
        => await hunts.GetLeaderboardAsync(CurrentUserId, take, ct);

    /// <summary>Submit a photo + sample region for one hunt. Score, XP and completion are decided on the server.</summary>
    [HttpPost("{id:int}/attempts"), RequestSizeLimit(MaxUploadBytes), Consumes("multipart/form-data")]
    [EnableRateLimiting(ApiServiceExtensions.AttemptRateLimitPolicy)]
    [ProducesResponseType<DailyHuntAttemptResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<DailyHuntAttemptResultDto>> Attempt(int id, [FromForm] HuntForm form, CancellationToken ct)
    {
        await using var stream = form.Photo.OpenReadStream();
        return await hunts.SubmitAttemptAsync(new DailyHuntSubmission(CurrentUserId, id, stream), ct);
    }
}

/// <summary>Color Atlas: five collections of twenty colours, unlocked by valid hunt results.</summary>
[ApiController, Route("api/game/atlas"), Authorize]
[Produces("application/json")]
public sealed class AtlasController(IAtlasService atlas) : ApiControllerBase
{
    /// <summary>The caller's atlas. Locked colours do not reveal their name or hex.</summary>
    [HttpGet]
    public async Task<ActionResult<AtlasDto>> Get(CancellationToken ct) => await atlas.GetAsync(CurrentUserId, ct);
}

/// <summary>Color Bingo: one active 3×3 board per player.</summary>
[ApiController, Route("api/game/bingo"), Authorize]
[Produces("application/json")]
public sealed class BingoController(IBingoService bingo) : ApiControllerBase
{
    /// <summary>The caller's active board (created on first use, or after the previous board was completed).</summary>
    [HttpGet]
    public async Task<ActionResult<BingoBoardDto>> Current(CancellationToken ct) => await bingo.GetCurrentAsync(CurrentUserId, ct);

    /// <summary>Submit a photo + sample region for one square (0..8) of the active board. Score, completion and rewards are decided on the server.</summary>
    [HttpPost("cells/{index:int}/attempts"), RequestSizeLimit(MaxUploadBytes), Consumes("multipart/form-data")]
    [EnableRateLimiting(ApiServiceExtensions.AttemptRateLimitPolicy)]
    [ProducesResponseType<BingoAttemptResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BingoAttemptResultDto>> Attempt(int index, [FromForm] HuntForm form, CancellationToken ct)
    {
        await using var stream = form.Photo.OpenReadStream();
        return await bingo.SubmitAttemptAsync(new BingoSubmission(CurrentUserId, index, stream), ct);
    }
}

/// <summary>Friend duels: asynchronous head-to-head on one target colour, 3 attempts each, 24 hours.</summary>
[ApiController, Route("api/game/duels"), Authorize]
[Produces("application/json")]
public sealed class DuelsController(IDuelService duels) : ApiControllerBase
{
    /// <summary>The caller's duels. Overdue ones are settled first (lazy expiry); the other player's score stays hidden until a duel is over.</summary>
    [HttpGet]
    public async Task<ActionResult<DuelListDto>> List(CancellationToken ct) => await duels.ListAsync(CurrentUserId, ct);

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DuelDto>> Get(Guid id, CancellationToken ct) => await duels.GetAsync(CurrentUserId, id, ct);

    /// <summary>Challenge an accepted friend. The colour is chosen by the server.</summary>
    [HttpPost]
    [ProducesResponseType<DuelDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<DuelDto>> Create([FromBody] CreateDuelRequest body, CancellationToken ct) => await duels.CreateAsync(CurrentUserId, body.OpponentId, ct);

    [HttpPost("{id:guid}/accept")]
    public async Task<ActionResult<DuelDto>> Accept(Guid id, CancellationToken ct) => await duels.AcceptAsync(CurrentUserId, id, ct);

    /// <summary>Declines an invitation (invited player) or withdraws it (challenger).</summary>
    [HttpPost("{id:guid}/decline")]
    public async Task<ActionResult<DuelDto>> Decline(Guid id, CancellationToken ct) => await duels.DeclineAsync(CurrentUserId, id, ct);

    /// <summary>Stop playing and keep the best score so far.</summary>
    [HttpPost("{id:guid}/finish")]
    public async Task<ActionResult<DuelDto>> Finish(Guid id, CancellationToken ct) => await duels.FinishAsync(CurrentUserId, id, ct);

    /// <summary>One photo + sample region. Score, winner and XP are decided on the server.</summary>
    [HttpPost("{id:guid}/attempts"), RequestSizeLimit(MaxUploadBytes), Consumes("multipart/form-data")]
    [EnableRateLimiting(ApiServiceExtensions.AttemptRateLimitPolicy)]
    [ProducesResponseType<DuelAttemptResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<DuelAttemptResultDto>> Attempt(Guid id, [FromForm] HuntForm form, CancellationToken ct)
    {
        await using var stream = form.Photo.OpenReadStream();
        return await duels.SubmitAttemptAsync(new DuelSubmission(CurrentUserId, id, stream), ct);
    }
}
