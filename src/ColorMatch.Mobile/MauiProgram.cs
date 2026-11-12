using CommunityToolkit.Maui;
using ColorMatch.Mobile.Services;
using ColorMatch.Mobile.ViewModels;
using ColorMatch.Mobile.Views;
using Microsoft.Extensions.Logging;

namespace ColorMatch.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()          // the in-app camera (CommunityToolkit.Maui.Camera) is built on it
            .UseMauiCommunityToolkitCamera()
            .ConfigureMauiHandlers(h =>
            {
#if ANDROID
                // tab switches without the fragment transition (pages appear at once and show a skeleton instead)
                h.AddHandler(typeof(Shell), typeof(Platforms.AndroidImpl.InstantTabShellRenderer));
#endif
                // inputs sit inside a rounded Border: drop the native Android underline
                Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (handler, view) =>
                {
#if ANDROID
                    handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#endif
                });
            })
            .ConfigureFonts(fonts =>
            {
                // text: Instrument Sans · voice (titles, names, scores, buttons): Bricolage Grotesque.
                // "SansBold" is the alias every heading in the app already uses, so it now points at the display face.
                fonts.AddFont("InstrumentSans-Regular.ttf", "SansRegular");
                fonts.AddFont("InstrumentSans-Medium.ttf", "SansMedium");
                fonts.AddFont("InstrumentSans-SemiBold.ttf", "SansSemibold");
                fonts.AddFont("InstrumentSans-Bold.ttf", "TextBold");
                fonts.AddFont("Bricolage-Bold.ttf", "SansBold");
                fonts.AddFont("Bricolage-SemiBold.ttf", "DisplaySemi");
                fonts.AddFont("Bricolage-ExtraBold.ttf", "DisplayHeavy");
            });

        var s = builder.Services;

        // infrastructure
        s.AddSingleton<AppSettings>();
        s.AddSingleton<ISessionService, SessionService>();
        s.AddSingleton<ImageService>();
        s.AddSingleton<IGameService, GameService>();
        s.AddSingleton<IAppNavigator, AppNavigator>();
        s.AddSingleton<IPostPublisher, PostPublisher>();
        s.AddSingleton(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }); // per-request timeouts are enforced in ApiClient
        s.AddSingleton<ApiClient>();
        s.AddSingleton<DemoApiClient>();
        s.AddSingleton<IApiClient, SwitchableApiClient>();
#if ANDROID
        s.AddSingleton<IGallerySaver, Platforms.AndroidImpl.AndroidGallerySaver>();
#elif IOS
        s.AddSingleton<IGallerySaver, Platforms.IosImpl.IosGallerySaver>();
#endif

        // view models
        s.AddTransient<SplashViewModel>();
        s.AddTransient<OnboardingViewModel>();
        s.AddTransient<AuthViewModel>();
        s.AddTransient<HomeViewModel>();
        s.AddTransient<HuntViewModel>();
        s.AddTransient<CreateViewModel>();
        s.AddTransient<FeedViewModel>();
        s.AddTransient<ResultViewModel>();
        s.AddTransient<PostDetailViewModel>();
        s.AddTransient<ProfileViewModel>();
        s.AddTransient<FriendsViewModel>();
        s.AddTransient<AtlasViewModel>();
        s.AddTransient<BingoViewModel>();
        s.AddTransient<DuelsViewModel>();
        s.AddTransient<ChallengesViewModel>();

        // pages
        s.AddTransient<AppShell>();
        s.AddTransient<SplashPage>();
        s.AddTransient<OnboardingPage>();
        s.AddTransient<AuthPage>();
        s.AddTransient<HomePage>();
        s.AddTransient<HuntPage>();
        s.AddTransient<CreatePage>();
        s.AddTransient<FeedPage>();
        s.AddTransient<ResultPage>();
        s.AddTransient<PostDetailPage>();
        s.AddTransient<ProfilePage>();
        s.AddTransient<FriendsPage>();
        s.AddTransient<AtlasPage>();
        s.AddTransient<BingoPage>();
        s.AddTransient<DuelsPage>();
        s.AddTransient<ChallengesPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
