using ColorMatch.Mobile.Models;

namespace ColorMatch.Mobile.Services;

/// <summary>User-adjustable app settings (server address, theme, onboarding flag) stored in Preferences.</summary>
public sealed class AppSettings
{
    private const string BaseUrlKey = "base_url";
    private const string ThemeKey = "theme";
    private const string OnboardedKey = "onboarded";

    /// <summary>10.0.2.2 is the host machine as seen from the Android emulator.</summary>
    public static string DefaultBaseUrl =>
        DeviceInfo.Platform == DevicePlatform.Android ? "http://10.0.2.2:5030" : "http://localhost:5030";

    public string BaseUrl
    {
        get => Preferences.Default.Get(BaseUrlKey, DefaultBaseUrl);
        set => Preferences.Default.Set(BaseUrlKey, value.Trim().TrimEnd('/'));
    }

    /// <summary>Offline demo: everything is served from memory on the device, no server needed.</summary>
    public bool DemoMode
    {
        get => Preferences.Default.Get("demo_mode", false);
        set => Preferences.Default.Set("demo_mode", value);
    }

    public bool Onboarded
    {
        get => Preferences.Default.Get(OnboardedKey, false);
        set => Preferences.Default.Set(OnboardedKey, value);
    }

    /// <summary>"system", "light" or "dark".</summary>
    public string Theme
    {
        get => Preferences.Default.Get(ThemeKey, "dark");
        set { Preferences.Default.Set(ThemeKey, value); ApplyTheme(); }
    }

    public void ApplyTheme()
    {
        if (Application.Current is { } app)
            app.UserAppTheme = Theme switch { "light" => AppTheme.Light, "dark" => AppTheme.Dark, _ => AppTheme.Unspecified };
    }
}

public interface ISessionService
{
    string? Token { get; }
    UserProfile? User { get; }
    bool IsSignedIn { get; }
    event Action? Expired;
    event Action<UserProfile>? UserChanged;
    Task<bool> RestoreAsync();
    Task SignInAsync(AuthResponse auth);
    Task SignOutAsync();
    void UpdateUser(UserProfile user);
    void NotifyExpired();
}

/// <summary>Keeps the JWT in SecureStorage (Android Keystore / iOS Keychain).</summary>
public sealed class SessionService : ISessionService
{
    private const string TokenKey = "auth_token";
    private const string ExpiryKey = "auth_expiry";

    public string? Token { get; private set; }
    public UserProfile? User { get; private set; }
    public bool IsSignedIn => Token is not null;
    public event Action? Expired;
    public event Action<UserProfile>? UserChanged;

    public async Task<bool> RestoreAsync()
    {
        try
        {
            var token = await SecureStorage.Default.GetAsync(TokenKey);
            var expiry = await SecureStorage.Default.GetAsync(ExpiryKey);
            if (string.IsNullOrEmpty(token)) return false;
            if (DateTime.TryParse(expiry, null, System.Globalization.DateTimeStyles.RoundtripKind, out var exp) && exp <= DateTime.UtcNow)
            {
                await SignOutAsync();
                return false;
            }
            Token = token;
            return true;
        }
        catch
        {
            // keystore can be unavailable/corrupt after reinstall: start signed out
            SecureStorage.Default.RemoveAll();
            return false;
        }
    }

    public async Task SignInAsync(AuthResponse auth)
    {
        Token = auth.Token;
        User = auth.User;
        await SecureStorage.Default.SetAsync(TokenKey, auth.Token);
        await SecureStorage.Default.SetAsync(ExpiryKey, auth.ExpiresAtUtc.ToString("O"));
    }

    public Task SignOutAsync()
    {
        Token = null; User = null;
        SecureStorage.Default.Remove(TokenKey);
        SecureStorage.Default.Remove(ExpiryKey);
        return Task.CompletedTask;
    }

    public void UpdateUser(UserProfile user) { User = user; UserChanged?.Invoke(user); }
    public void NotifyExpired() { if (Token is not null) Expired?.Invoke(); }
}
