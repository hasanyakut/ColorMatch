using System.Security.Claims;
using ColorMatch.API.Configuration;
using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ColorMatch.API.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected Guid CurrentUserId =>
        Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : throw new UnauthorizedAccessException();

    protected const long MaxUploadBytes = 13L * 1024 * 1024;
}

[ApiController, Route("api/auth"), EnableRateLimiting(ApiServiceExtensions.AuthRateLimitPolicy)]
[Produces("application/json")]
public sealed class AuthController(IAuthService auth) : ApiControllerBase
{
    /// <summary>Create an account and sign in.</summary>
    [HttpPost("register"), AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    /// <summary>Sign in with e-mail or user name.</summary>
    [HttpPost("login"), AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
        => await auth.LoginAsync(request, ct);
}

[ApiController, Route("api/users"), Authorize]
[Produces("application/json")]
public sealed class UsersController(IUserService users, IPostService posts) : ApiControllerBase
{
    /// <summary>Current user's profile, XP and level.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<UserProfileDto>> Me(CancellationToken ct) => await users.GetProfileAsync(CurrentUserId, ct);

    /// <summary>Any user's public profile.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserProfileDto>> Get(Guid id, CancellationToken ct) => await users.GetProfileAsync(id, ct);

    /// <summary>Posts of a user (newest first, cursor pagination).</summary>
    [HttpGet("{id:guid}/posts")]
    public async Task<ActionResult<PagedResult<PostDto>>> Posts(Guid id, [FromQuery] string? cursor, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => await posts.GetFeedAsync(CurrentUserId, id, FeedScope.All, null, cursor, pageSize, ct);

    /// <summary>Upload / replace the profile picture (multipart field "file").</summary>
    [HttpPost("me/avatar"), RequestSizeLimit(MaxUploadBytes)]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<UserProfileDto>> Avatar(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return await users.SetAvatarAsync(CurrentUserId, stream, ct);
    }
}

[ApiController, Route("api/friends"), Authorize]
[Produces("application/json")]
public sealed class FriendsController(IFriendService friends) : ApiControllerBase
{
    /// <summary>Accepted friends.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<FriendDto>>> List(CancellationToken ct) => Ok(await friends.ListFriendsAsync(CurrentUserId, ct));

    /// <summary>Pending requests: incoming (to accept/decline) and outgoing (to cancel).</summary>
    [HttpGet("requests")]
    public async Task<ActionResult<FriendRequestsDto>> Requests(CancellationToken ct) => await friends.ListRequestsAsync(CurrentUserId, ct);

    /// <summary>Search users by user-name prefix (min. 2 characters), with your relationship to each.</summary>
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<UserSearchResultDto>>> Search([FromQuery] string q, CancellationToken ct)
        => Ok(await friends.SearchAsync(CurrentUserId, q, ct));

    /// <summary>Relationship with one user: none | friends | request_sent | request_received | self.</summary>
    [HttpGet("status/{userId:guid}")]
    public async Task<ActionResult<RelationshipDto>> Status(Guid userId, CancellationToken ct) => await friends.GetRelationshipAsync(CurrentUserId, userId, ct);

    /// <summary>Send a friend request (idempotent; accepts automatically if they already asked you).</summary>
    [HttpPost("requests")]
    public async Task<ActionResult<RelationshipDto>> Send(SendFriendRequestBody body, CancellationToken ct) => await friends.SendRequestAsync(CurrentUserId, body.UserId, ct);

    /// <summary>Accept an incoming request.</summary>
    [HttpPost("requests/{requesterId:guid}/accept")]
    public async Task<ActionResult<RelationshipDto>> Accept(Guid requesterId, CancellationToken ct) => await friends.AcceptAsync(CurrentUserId, requesterId, ct);

    /// <summary>Decline an incoming request, cancel an outgoing one, or remove a friend.</summary>
    [HttpDelete("{otherId:guid}")]
    public async Task<ActionResult<RelationshipDto>> Remove(Guid otherId, CancellationToken ct) => await friends.RemoveAsync(CurrentUserId, otherId, ct);
}

[ApiController, Route("api/challenges"), Authorize]
[Produces("application/json")]
public sealed class ChallengesController(IChallengeService challenges) : ApiControllerBase
{
    /// <summary>Today's colour (same for every user) with the caller's progress.</summary>
    [HttpGet("today")]
    public async Task<ActionResult<DailyChallengeDto>> Today(CancellationToken ct) => await challenges.GetTodayAsync(CurrentUserId, ct);

    /// <summary>Top scores for today's colour (plus the caller's rank).</summary>
    [HttpGet("today/leaderboard")]
    public async Task<ActionResult<LeaderboardDto>> Leaderboard([FromQuery] int take = 10, CancellationToken ct = default)
        => await challenges.GetLeaderboardAsync(CurrentUserId, take, ct);

    /// <summary>The previous days' colours with the caller's best score for each.</summary>
    [HttpGet("recent")]
    public async Task<ActionResult<IReadOnlyList<PastChallengeDto>>> Recent([FromQuery] int days = 6, CancellationToken ct = default)
        => Ok(await challenges.GetRecentAsync(CurrentUserId, days, ct));
}

public sealed class HuntForm
{
    /// <summary>Photo (JPEG/PNG/WebP, max 12 MB).</summary>
    public required IFormFile Photo { get; init; }
    /// <summary>Ignored. Older clients still send a region; the server now finds the best matching region of the photo itself, so the player cannot steer the measurement.</summary>
    public double RegionX { get; init; }
    /// <summary>Region centre Y, 0..1 of the (upright) image height.</summary>
    public double RegionY { get; init; }
    /// <summary>Region radius as a fraction (0.005–0.25) of the shorter image side.</summary>
    public double RegionRadius { get; init; }
}

[ApiController, Route("api/color-hunts"), Authorize]
[Produces("application/json")]
public sealed class ColorHuntsController(IColorHuntService hunts) : ApiControllerBase
{
    /// <summary>Submit a photo + sample region. The official score is computed on the server.</summary>
    [HttpPost, RequestSizeLimit(MaxUploadBytes), Consumes("multipart/form-data")]
    [ProducesResponseType<HuntAttemptResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HuntAttemptResultDto>> Submit([FromForm] HuntForm form, CancellationToken ct)
    {
        await using var stream = form.Photo.OpenReadStream();
        return await hunts.SubmitAsync(new HuntSubmission(CurrentUserId, stream), ct);
    }

    /// <summary>The caller's attempts for today's colour.</summary>
    [HttpGet("today")]
    public async Task<ActionResult<IReadOnlyList<HuntAttemptSummaryDto>>> Today(CancellationToken ct)
        => Ok(await hunts.GetMyTodayAsync(CurrentUserId, ct));
}

[ApiController, Route("api/posts"), Authorize]
[Produces("application/json")]
public sealed class PostsController(IPostService posts, ICommentService comments) : ApiControllerBase
{
    /// <summary>
    /// Feed, newest first. <c>scope=friends</c> shows yourself + accepted friends; <c>scope=all</c> (default) is discovery.
    /// <c>color</c> filters by colour family (e.g. "Mavi"). Pass <c>nextCursor</c> back as <c>cursor</c> for the next page.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<PostDto>>> Feed([FromQuery] FeedScope scope = FeedScope.All, [FromQuery] string? color = null,
        [FromQuery] string? cursor = null, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => await posts.GetFeedAsync(CurrentUserId, null, scope, color, cursor, pageSize, ct);

    /// <summary>Colour families that have posts, with counts (drives the discovery filter chips).</summary>
    [HttpGet("colors")]
    public async Task<ActionResult<IReadOnlyList<ColorFacetDto>>> Colors(CancellationToken ct) => Ok(await posts.GetColorFacetsAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PostDto>> Get(Guid id, CancellationToken ct) => await posts.GetAsync(CurrentUserId, id, ct);

    /// <summary>Publish a photo or collage that was uploaded via /api/media or a colour hunt.</summary>
    [HttpPost]
    [ProducesResponseType<PostDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PostDto>> Create(CreatePostRequest request, CancellationToken ct)
    {
        var post = await posts.CreateAsync(CurrentUserId, request, ct);
        return CreatedAtAction(nameof(Get), new { id = post.Id }, post);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await posts.DeleteAsync(CurrentUserId, id, ct);
        return NoContent();
    }

    /// <summary>Like (idempotent).</summary>
    [HttpPut("{id:guid}/like")]
    public async Task<ActionResult<LikeStateDto>> Like(Guid id, CancellationToken ct) => await posts.SetLikeAsync(CurrentUserId, id, true, ct);

    /// <summary>Remove like (idempotent).</summary>
    [HttpDelete("{id:guid}/like")]
    public async Task<ActionResult<LikeStateDto>> Unlike(Guid id, CancellationToken ct) => await posts.SetLikeAsync(CurrentUserId, id, false, ct);

    [HttpGet("{id:guid}/comments")]
    public async Task<ActionResult<PagedResult<CommentDto>>> Comments(Guid id, [FromQuery] string? cursor, [FromQuery] int pageSize = 30, CancellationToken ct = default)
        => await comments.ListAsync(id, cursor, pageSize, ct);

    [HttpPost("{id:guid}/comments")]
    [ProducesResponseType<CommentDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CommentDto>> AddComment(Guid id, CreateCommentRequest request, CancellationToken ct)
    {
        var dto = await comments.AddAsync(CurrentUserId, id, request, ct);
        return StatusCode(StatusCodes.Status201Created, dto);
    }
}

[ApiController, Route("api/media"), Authorize]
[Produces("application/json")]
public sealed class MediaController(IMediaService media) : ApiControllerBase
{
    /// <summary>Upload an image (multipart field "file"). Returns a key to reference from a post.</summary>
    [HttpPost, RequestSizeLimit(MaxUploadBytes), Consumes("multipart/form-data")]
    [ProducesResponseType<MediaUploadResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<MediaUploadResponse>> Upload(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return StatusCode(StatusCodes.Status201Created, await media.UploadAsync(CurrentUserId, stream, ct));
    }
}
