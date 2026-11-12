using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ColorMatch.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "ColorMatch";
    public string Audience { get; set; } = "ColorMatch.Mobile";
    /// <summary>Symmetric signing key (>= 32 chars). Supply via user-secrets / environment variable, never commit real keys.</summary>
    public string Key { get; set; } = string.Empty;
    public int ExpiryMinutes { get; set; } = 60 * 24 * 7;
}

public sealed class IdentityService(UserManager<ApplicationUser> users) : IIdentityService
{
    public async Task<AuthUser> CreateUserAsync(string email, string userName, string password, CancellationToken ct)
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = userName, Email = email, CreatedAtUtc = DateTime.UtcNow };
        var result = await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = new Dictionary<string, List<string>>();
            foreach (var e in result.Errors)
            {
                var field = e.Code switch
                {
                    "DuplicateUserName" or "InvalidUserName" => "userName",
                    "DuplicateEmail" or "InvalidEmail" => "email",
                    _ when e.Code.StartsWith("Password", StringComparison.Ordinal) => "password",
                    _ => "request"
                };
                if (!errors.TryGetValue(field, out var list)) errors[field] = list = [];
                list.Add(e.Description);
            }
            throw new ValidationAppException(errors.ToDictionary(k => k.Key, v => v.Value.ToArray()));
        }
        return new AuthUser(user.Id, user.UserName!, user.Email!);
    }

    public async Task<AuthUser?> ValidateCredentialsAsync(string emailOrUserName, string password, CancellationToken ct)
    {
        var user = emailOrUserName.Contains('@')
            ? await users.FindByEmailAsync(emailOrUserName)
            : await users.FindByNameAsync(emailOrUserName);
        if (user is null) return null;

        if (await users.IsLockedOutAsync(user))
            throw new ConflictException("Too many failed attempts. Try again in a few minutes.");

        if (!await users.CheckPasswordAsync(user, password))
        {
            await users.AccessFailedAsync(user);
            return null;
        }

        await users.ResetAccessFailedCountAsync(user);
        return new AuthUser(user.Id, user.UserName!, user.Email!);
    }

    public async Task DeleteUserAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is not null) await users.DeleteAsync(user);
    }
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    public AuthToken CreateToken(AuthUser user)
    {
        var o = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(o.ExpiryMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(o.Issuer, o.Audience, claims, now, expires, creds);
        return new AuthToken(new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}
