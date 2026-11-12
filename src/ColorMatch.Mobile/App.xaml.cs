using ColorMatch.Mobile.Services;

namespace ColorMatch.Mobile;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services, AppSettings settings, ISessionService session)
    {
        InitializeComponent();
        _services = services;
        settings.ApplyTheme();
        RequestedThemeChanged += (_, _) => SystemBars.Apply();

        // any 401 from the API drops the user back to the sign-in screen
        session.Expired += async () =>
        {
            await session.SignOutAsync();
            services.GetRequiredService<IAppNavigator>().ShowAuth(showOnboarding: false);
        };
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(_services.GetRequiredService<Views.SplashPage>());
}
