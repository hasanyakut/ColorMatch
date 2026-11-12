using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ColorMatch.Application.Services;

public sealed class AuthService(
    IIdentityService identity,
    ITokenService tokens,
    IAppDbContext db,
    IUserService users,
    TimeProvider clock) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var user = await identity.CreateUserAsync(request.Email.Trim(), request.UserName.Trim(), request.Password, ct);
        try
        {
            db.Profiles.Add(new UserProfile
            {
                Id = user.Id,
                UserName = user.UserName,
                CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
                Progress = new UserProgress { UserId = user.Id, TotalXp = 0 }
            });
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // keep credentials and profile consistent
            await identity.DeleteUserAsync(user.Id, CancellationToken.None);
            throw;
        }

        return await BuildResponseAsync(user, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await identity.ValidateCredentialsAsync(request.EmailOrUserName.Trim(), request.Password, ct)
                   ?? throw new UnauthorizedAppException("Invalid credentials.");

        if (!await db.Profiles.AnyAsync(p => p.Id == user.Id, ct))
            throw new UnauthorizedAppException("Invalid credentials.");

        return await BuildResponseAsync(user, ct);
    }

    private async Task<AuthResponse> BuildResponseAsync(AuthUser user, CancellationToken ct)
    {
        var token = tokens.CreateToken(user);
        var profile = await users.GetProfileAsync(user.Id, ct);
        return new AuthResponse(token.Token, token.ExpiresAtUtc, profile);
    }
}
