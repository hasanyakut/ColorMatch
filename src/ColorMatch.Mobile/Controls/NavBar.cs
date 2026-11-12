namespace ColorMatch.Mobile.Controls;

/// <summary>
/// Hosts the one navigation bar of the app above the Shell and tells it what to show.
/// <para>Pages describe themselves with two attached properties on their root:
/// <c>NavBar.Tab="home"</c> (which tab is selected; leave empty on Create / Profile) and <c>NavBar.Visible="True"</c> (may also be bound — e.g.
/// hidden while a photo is being marked or the live camera is open). Pages that do not set <c>Visible</c> (result, post, friends, bingo, duels …)
/// have no bar.</para>
/// <para>The bar is a native overlay added once to the activity's content view, so it exists before, during and after every page change.</para>
/// </summary>
public static class NavBar
{
    public static readonly BindableProperty TabProperty = BindableProperty.CreateAttached("Tab", typeof(string), typeof(NavBar), "");
    public static string GetTab(BindableObject b) => (string)b.GetValue(TabProperty);
    public static void SetTab(BindableObject b, string value) => b.SetValue(TabProperty, value);

    public static readonly BindableProperty VisibleProperty = BindableProperty.CreateAttached("Visible", typeof(bool), typeof(NavBar), false);
    public static bool GetVisible(BindableObject b) => (bool)b.GetValue(VisibleProperty);
    public static void SetVisible(BindableObject b, bool value) => b.SetValue(VisibleProperty, value);

    private static BottomBar? _bar;
    private static Shell? _shell;
    private static Page? _watched;

    /// <summary>Creates the bar and puts it above <paramref name="shell"/>. Safe to call again for a new shell (after sign-in).</summary>
    public static void Attach(Shell shell)
    {
        Detach();
        _shell = shell;
        _bar = new BottomBar();
#if ANDROID
        AttachNative(shell);
#endif
        shell.Navigated += OnNavigated;
        Refresh();
    }

    public static void Detach()
    {
        if (_shell is not null) _shell.Navigated -= OnNavigated;
        Watch(null);
#if ANDROID
        DetachNative();
#endif
        _bar = null; _shell = null;
    }

    private static void OnNavigated(object? sender, ShellNavigatedEventArgs e) => Refresh();

    private static void Watch(Page? page)
    {
        if (ReferenceEquals(page, _watched)) return;
        if (_watched is not null) _watched.PropertyChanged -= OnPageChanged;
        _watched = page;
        if (_watched is not null) _watched.PropertyChanged += OnPageChanged;
    }

    private static void OnPageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Visible" or "Tab") Refresh();
    }

    /// <summary>Reads the current page's attached properties and applies them to the bar.</summary>
    public static void Refresh()
    {
        if (_bar is null || _shell is null) return;
        var page = _shell.CurrentPage;
        Watch(page);
        var visible = page is not null && GetVisible(page);
        _bar.SetActive(page is null ? null : GetTab(page));
        _bar.SetShown(visible, _shell);
    }

#if ANDROID
    private static Android.Views.View? _native;
    private static Android.Widget.FrameLayout? _host;
    private static EventHandler? _insetHandler;
    private static int _lastInset = -1;

    private static void AttachNative(Shell shell)
    {
        var activity = Platform.CurrentActivity;
        var host = activity?.FindViewById<Android.Widget.FrameLayout>(Android.Resource.Id.Content);
        var context = shell.Handler?.MauiContext ?? Application.Current?.Windows.FirstOrDefault()?.Handler?.MauiContext;
        if (host is null || context is null || _bar is null) return;

        _host = host;
        _native = Microsoft.Maui.Platform.ElementExtensions.ToPlatform(_bar, context);
        var lp = new Android.Widget.FrameLayout.LayoutParams(Android.Views.ViewGroup.LayoutParams.MatchParent, Android.Views.ViewGroup.LayoutParams.WrapContent,
            Android.Views.GravityFlags.Bottom | Android.Views.GravityFlags.CenterHorizontal);
        host.AddView(_native, lp);

        // the bar floats above the system navigation bar; the inset can change (gesture bar, 3-button bar, rotation)
        void Apply()
        {
            if (_native is null || _host is null) return;
            var inset = 0;
            var insets = _host.RootWindowInsets;
            if (insets is not null)
                inset = OperatingSystem.IsAndroidVersionAtLeast(30) ? insets.GetInsets(Android.Views.WindowInsets.Type.NavigationBars()).Bottom : insets.SystemWindowInsetBottom;
            if (inset == _lastInset) return;
            _lastInset = inset;
            if (_native.LayoutParameters is Android.Widget.FrameLayout.LayoutParams p) { p.BottomMargin = inset; _native.LayoutParameters = p; }
        }
        _insetHandler = (_, _) => Apply();
        host.ViewTreeObserver!.GlobalLayout += _insetHandler;
        host.Post(Apply);
    }

    private static void DetachNative()
    {
        try
        {
            if (_host is not null && _insetHandler is not null && _host.ViewTreeObserver is { IsAlive: true } vto) vto.GlobalLayout -= _insetHandler;
            if (_host is not null && _native is not null) _host.RemoveView(_native);
        }
        catch { /* the activity may already be gone */ }
        _native = null; _host = null; _insetHandler = null; _lastInset = -1;
    }
#endif
}
