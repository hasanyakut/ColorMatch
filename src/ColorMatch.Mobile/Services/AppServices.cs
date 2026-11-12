using ColorMatch.Mobile.Models;
using ColorMatch.Mobile.Views;

namespace ColorMatch.Mobile.Services;

public interface IGallerySaver
{
    /// <summary>Saves a JPEG into the device photo gallery and returns a user-facing location text.</summary>
    Task<string> SaveAsync(string jpegPath, string displayName);
}

/// <summary>Caches today's three hunts so Home / Hunt / Result stay in sync without refetching on every page.</summary>
public interface IGameService
{
    Task<DailyHuntsState> GetHuntsAsync(bool force = false, CancellationToken ct = default);
    void Invalidate();
    event Action<DailyHuntsState>? Changed;

    /// <summary>The hunt the player tapped on Home; the Hunt tab opens on it (and clears it).</summary>
    int? PendingHuntId { get; set; }

    /// <summary>The bingo square the player tapped; the Hunt tab opens in bingo mode on it (and clears it).</summary>
    int? PendingBingoCell { get; set; }

    /// <summary>The duel the player chose to play; the Hunt tab opens in duel mode on it (and clears it).</summary>
    Guid? PendingDuelId { get; set; }
}

public sealed class GameService(IApiClient api) : IGameService
{
    private DailyHuntsState? _cached;
    private DateTime _fetchedAt;
    public event Action<DailyHuntsState>? Changed;
    public int? PendingHuntId { get; set; }
    public int? PendingBingoCell { get; set; }
    public Guid? PendingDuelId { get; set; }

    public async Task<DailyHuntsState> GetHuntsAsync(bool force = false, CancellationToken ct = default)
    {
        var expired = _cached is null || DateTime.UtcNow >= _cached.ResetsAtUtc || DateTime.UtcNow - _fetchedAt > TimeSpan.FromMinutes(2);
        if (force || expired)
        {
            _cached = await api.GetDailyHuntsAsync(ct);
            _fetchedAt = DateTime.UtcNow;
            Changed?.Invoke(_cached);
        }
        return _cached!;
    }

    public void Invalidate() { _cached = null; }
}

/// <summary>Switches between the signed-out flow and the signed-in tab shell.</summary>
public interface IAppNavigator
{
    void ShowSplash();
    void ShowAuth(bool showOnboarding);
    void ShowMain();
}

public sealed class AppNavigator(IServiceProvider services, IGameService game) : IAppNavigator
{
    private static Window? MainWindow => Application.Current?.Windows.FirstOrDefault();

    public void ShowSplash() => Set(services.GetRequiredService<SplashPage>());

    public void ShowAuth(bool showOnboarding)
    {
        game.Invalidate();
        Page root = showOnboarding
            ? services.GetRequiredService<OnboardingPage>()
            : services.GetRequiredService<AuthPage>();
        Set(new NavigationPage(root) { BarBackgroundColor = Colors.Transparent });
    }

    public void ShowMain() => Set(services.GetRequiredService<AppShell>());

    private static void Set(Page page)
    {
        if (MainWindow is { } w) MainThread.BeginInvokeOnMainThread(() => { w.Page = page; SystemBars.Apply(); });
    }
}
