using System.Text.RegularExpressions;
using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ColorMatch.Mobile.ViewModels;

public partial class SplashViewModel(ISessionService session, IApiClient api, AppSettings settings, IAppNavigator nav) : BaseViewModel
{
    public async Task StartAsync()
    {
        var started = DateTime.UtcNow;
        var restored = await session.RestoreAsync();
        var valid = false;
        if (restored)
        {
            try
            {
                session.UpdateUser(await api.GetMeAsync());
                valid = true;
            }
            catch (ApiException ex) when (ex.Status is null)
            {
                // server unreachable: keep the session, the screens show a retry state instead of logging the user out
                valid = true;
            }
            catch (ApiException)
            {
                await session.SignOutAsync();
            }
        }

        var remaining = TimeSpan.FromMilliseconds(1100) - (DateTime.UtcNow - started);
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining);

        if (valid) nav.ShowMain();
        else nav.ShowAuth(showOnboarding: !settings.Onboarded);
    }
}

public sealed record OnboardingSlide(string Hex, string Title, string Text);

public partial class OnboardingViewModel(AppSettings settings, IAppNavigator nav) : BaseViewModel
{
    public IReadOnlyList<OnboardingSlide> Slides { get; } =
    [
        new("#E86C52", "Günün rengini keşfet", "Her gün herkes için aynı renk. Gerçek dünyada ona en yakın nesneyi bul."),
        new("#2BB5B0", "Çek, seç, puan kazan", "Fotoğrafını çek, rengi seç. Renk farkını CIEDE2000 ile ölçüp 0–1000 puan veriyoruz."),
        new("#F4C542", "Kolaj yap, paylaş", "Renk temalı kolajlar hazırla, Story olarak dışarı aktar ya da akışta paylaş."),
    ];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsLast)), NotifyPropertyChangedFor(nameof(NextText))] private int position;
    public bool IsLast => Position >= Slides.Count - 1;
    public string NextText => IsLast ? "Başla" : "Devam";

    [RelayCommand]
    private void Next()
    {
        if (!IsLast) { Position++; return; }
        Finish();
    }

    [RelayCommand] private void Skip() => Finish();

    private void Finish()
    {
        settings.Onboarded = true;
        nav.ShowAuth(showOnboarding: false);
    }
}

public partial class AuthViewModel(IApiClient api, ISessionService session, AppSettings settings, IAppNavigator nav) : BaseViewModel
{
    private static readonly Regex EmailRx = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);
    private static readonly Regex UserRx = new(@"^[A-Za-z0-9_.]{3,24}$", RegexOptions.Compiled);

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsRegister)), NotifyPropertyChangedFor(nameof(SubmitText)), NotifyPropertyChangedFor(nameof(TitleText)),
     NotifyPropertyChangedFor(nameof(SubtitleText)), NotifyPropertyChangedFor(nameof(SwitchPrompt)), NotifyPropertyChangedFor(nameof(SwitchAction)),
     NotifyPropertyChangedFor(nameof(IdentityLabel)), NotifyPropertyChangedFor(nameof(IdentityPlaceholder))]
    private bool isLoginMode = true;

    [ObservableProperty] private string email = "";
    [ObservableProperty] private string userName = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string? fieldError;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PasswordToggleText))] private bool showPassword;

    public bool IsRegister => !IsLoginMode;
    public string SubmitText => IsLoginMode ? "Giriş yap" : "Hesap oluştur";
    public string TitleText => IsLoginMode ? "Tekrar hoş geldin" : "Hesabını oluştur";
    public string SubtitleText => IsLoginMode
        ? "Günün rengi seni bekliyor. Kaldığın yerden devam et."
        : "Her gün tek bir renk, herkes için aynı. Hesabını aç ve avlamaya başla.";
    public string SwitchPrompt => IsLoginMode ? "Hesabın yok mu?" : "Zaten hesabın var mı?";
    public string SwitchAction => IsLoginMode ? "Kayıt ol" : "Giriş yap";
    public string IdentityLabel => IsLoginMode ? "E-posta veya kullanıcı adı" : "E-posta";
    public string IdentityPlaceholder => IsLoginMode ? "ornek@mail.com ya da kullanıcı adın" : "ornek@mail.com";
    public string PasswordToggleText => ShowPassword ? "Gizle" : "Göster";
    public string ServerUrl => settings.BaseUrl;

    [RelayCommand] private void SetMode(string mode) { IsLoginMode = mode == "login"; FieldError = null; ErrorMessage = null; }
    [RelayCommand] private void ToggleMode() { IsLoginMode = !IsLoginMode; FieldError = null; ErrorMessage = null; }
    [RelayCommand] private void TogglePassword() => ShowPassword = !ShowPassword;

    [RelayCommand]
    private async Task SubmitAsync()
    {
        FieldError = Validate();
        if (FieldError is not null) return;
        settings.DemoMode = false;

        await GuardAsync(async () =>
        {
            var auth = IsLoginMode
                ? await api.LoginAsync(Email.Trim(), Password)
                : await api.RegisterAsync(Email.Trim(), UserName.Trim(), Password);
            await session.SignInAsync(auth);
            session.UpdateUser(auth.User);
            settings.Onboarded = true;
            nav.ShowMain();
        });
    }

    /// <summary>Offline demo: no server, sample data served from memory on the device.</summary>
    [RelayCommand]
    private async Task StartDemoAsync()
    {
        FieldError = null; ErrorMessage = null;
        settings.DemoMode = true;
        await GuardAsync(async () =>
        {
            var auth = await api.LoginAsync("demo", "demo");
            await session.SignInAsync(auth);
            session.UpdateUser(auth.User);
            settings.Onboarded = true;
            nav.ShowMain();
        });
    }

    private string? Validate()
    {
        if (IsLoginMode)
        {
            if (string.IsNullOrWhiteSpace(Email)) return "E-posta veya kullanıcı adını gir.";
            if (string.IsNullOrEmpty(Password)) return "Şifreni gir.";
            return null;
        }
        if (!EmailRx.IsMatch(Email.Trim())) return "Geçerli bir e-posta adresi gir.";
        if (!UserRx.IsMatch(UserName.Trim())) return "Kullanıcı adı 3–24 karakter: harf, rakam, _ veya .";
        if (Password.Length < 8 || !Password.Any(char.IsDigit) || !Password.Any(char.IsLower)) return "Şifre en az 8 karakter olmalı; küçük harf ve rakam içermeli.";
        return null;
    }

    [RelayCommand]
    private async Task ChangeServerAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return;
        var value = await page.DisplayPromptAsync("Sunucu adresi",
            "Emülatör için http://10.0.2.2:5030, gerçek cihaz için bilgisayarının yerel IP adresi (ör. http://192.168.1.20:5030).",
            "Kaydet", "Vazgeç", initialValue: settings.BaseUrl, keyboard: Keyboard.Url);
        if (string.IsNullOrWhiteSpace(value)) return;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            ErrorMessage = "Geçerli bir adres gir (http:// veya https:// ile başlamalı).";
            return;
        }
        settings.BaseUrl = value;
        OnPropertyChanged(nameof(ServerUrl));
        ErrorMessage = null;
    }
}
