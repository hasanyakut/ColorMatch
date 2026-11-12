using ColorMatch.Application.Interfaces;
using ColorMatch.Application.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ColorMatch.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ChallengeOptions>(configuration.GetSection(ChallengeOptions.Section));
        services.Configure<ColorMatch.Domain.Rules.GameBalance>(configuration.GetSection(ColorMatch.Domain.Rules.GameBalance.Section));
        services.AddSingleton(sp => new ColorMatch.Domain.Rules.LevelCurve(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ColorMatch.Domain.Rules.GameBalance>>().Value));
        services.TryAddSingletonTimeProvider();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IChallengeService, ChallengeService>();
        services.AddScoped<IColorMatchService, ColorMatchService>();
        services.AddScoped<IColorHuntService, ColorHuntService>();
        services.AddScoped<IRewardService, RewardService>();
        services.AddScoped<IDailyHuntService, DailyHuntService>();
        services.AddScoped<IAtlasService, AtlasService>();
        services.AddScoped<IBingoService, BingoService>();
        services.AddScoped<IDuelService, DuelService>();
        services.AddScoped<IProgressionService, ProgressionService>();
        services.AddScoped<IPostService, PostService>();
        services.AddScoped<IFriendService, FriendService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<IMediaService, MediaService>();
        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
        => Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
            .TryAddSingleton<TimeProvider>(services, TimeProvider.System);
}
