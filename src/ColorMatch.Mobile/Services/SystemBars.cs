namespace ColorMatch.Mobile.Services;

/// <summary>Keeps the status / navigation bar icons readable: dark icons on the light theme, light icons on the dark theme.</summary>
public static class SystemBars
{
    public static void Apply()
    {
#if ANDROID
        var light = Application.Current?.RequestedTheme != AppTheme.Dark;
        var window = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window;
        if (window is null) return;

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var controller = window.InsetsController;
            if (controller is null) return;
            const Android.Views.WindowInsetsControllerAppearance mask =
                Android.Views.WindowInsetsControllerAppearance.LightStatusBars | Android.Views.WindowInsetsControllerAppearance.LightNavigationBars;
            controller.SetSystemBarsAppearance(light ? (int)mask : 0, (int)mask);
        }
        else
        {
#pragma warning disable CA1422 // legacy API for Android < 11
            var flags = (Android.Views.StatusBarVisibility)window.DecorView.SystemUiVisibility;
            window.DecorView.SystemUiVisibility = light
                ? flags | (Android.Views.StatusBarVisibility)Android.Views.SystemUiFlags.LightStatusBar
                : flags & ~(Android.Views.StatusBarVisibility)Android.Views.SystemUiFlags.LightStatusBar;
#pragma warning restore CA1422
        }
#endif
    }
}
