using Microsoft.Maui.Controls.Shapes;

namespace ColorMatch.Mobile.Controls;

/// <summary>Remembers where the user came from so the next tab page can slide in from the right side.</summary>
public static class TabNav
{
    public static int Index { get; set; }
    public static int From { get; set; }
    public static int Direction { get; set; }
    /// <summary>Set when the Hunt tab is pressed: the Hunt page leaves bingo mode and shows the daily hunts again.</summary>
    public static bool ExitBingo { get; set; }
    /// <summary>Set by "Tekrar dene": the Hunt page opens the live camera right away instead of showing the target card first.</summary>
    public static bool AutoOpenCamera { get; set; }
}

/// <summary>
/// Floating "glass" navigation capsule: Play / Challenges / Hunt / Atlas / Community (the profile opens from the avatar, Create from the Community tab). Instagram-like liquid glass: a see-through,
/// glossy capsule floating over the page; the selected tab sits in a glass lens that fills the cell from the bar's top edge to its bottom edge
/// (at the far left / right it follows the bar's round end) and glides between tabs. Hunt keeps its rose-to-violet camera capsule.
/// Tapping a tab slides the next page in from the matching side with a slight diagonal drift. The bar hides itself while
/// the keyboard is open.
/// Note: MAUI has no cross-platform backdrop blur, so the glass look is translucency + edge light + shadow, not a real blur.
/// </summary>
public sealed class BottomBar : ContentView
{
    public static readonly BindableProperty ActiveTabProperty = BindableProperty.Create(nameof(ActiveTab), typeof(string), typeof(BottomBar), "home", propertyChanged: (b, _, _) => ((BottomBar)b).Build());
    public string ActiveTab { get => (string)GetValue(ActiveTabProperty); set => SetValue(ActiveTabProperty, value); }

    private sealed record Item(string Key, string Title, string Icon, string Route, string Light, string Dark);

    private static readonly Item[] Items =
    [
        // one calm, jewel-toned colour per tab (tuned for light and dark); only the selected tab shows it
        new("home", "Oyna", "home", "//home", "#3466F0", "#8DB2FF"),
        new("challenges", "Görevler", "trophy", "//challenges", "#7547E8", "#B9A2FF"),
        new("hunt", "Rengi bul", "camera", "//hunt", "#D6336C", "#FF7FA9"),
        new("atlas", "Atlas", "palette", "//atlas", "#0C9670", "#55D6AA"),
        new("feed", "Topluluk", "users", "//feed", "#0A8BA6", "#5CD0E6"),
    ];

    private const double BarHeight = 60, Inset = 3.5;

    private static bool _navigating;
    private Border? _highlight;
    private Grid? _row;
    private Page? _page;
    private DateTime _lastTransition = DateTime.MinValue;
    private bool _keyboardOpen;
    private bool _hostVisible = true;
    private BackdropBlur? _blur;
    private Border? _tint;
#if ANDROID
    private Android.Views.View? _decor;
    private EventHandler? _layoutHandler;
#endif

    public BottomBar()
    {
        Loaded += (_, _) =>
        {
            if (Application.Current is { } a) a.RequestedThemeChanged += OnTheme;
            Build(); // theme is only final once the page is attached
            HookPage();
            HookKeyboard();
            RestartBlur();
            _ = PlayTransitionAsync();
        };
        Unloaded += (_, _) =>
        {
            if (Application.Current is { } a) a.RequestedThemeChanged -= OnTheme;
            if (_page is not null) { _page.Appearing -= OnPageAppearing; _page.Disappearing -= OnPageDisappearing; }
            _blur?.Stop();
            _page = null;
            UnhookKeyboard();
        };
        Build();
    }

    // ---- keyboard: the bar must never ride on top of the keyboard ----
    private void HookKeyboard()
    {
#if ANDROID
        UnhookKeyboard();
        _decor = Platform.CurrentActivity?.Window?.DecorView;
        if (_decor is null) return;
        _layoutHandler = (_, _) =>
        {
            var rect = new Android.Graphics.Rect();
            _decor.GetWindowVisibleDisplayFrame(rect);
            var total = _decor.RootView?.Height ?? _decor.Height;
            var open = total > 0 && (total - rect.Bottom) > total * 0.15;
            if (open == _keyboardOpen) return;
            _keyboardOpen = open;
            MainThread.BeginInvokeOnMainThread(() => base.IsVisible = !_keyboardOpen && _hostVisible);
        };
        _decor.ViewTreeObserver!.GlobalLayout += _layoutHandler;
#endif
    }

    private void UnhookKeyboard()
    {
#if ANDROID
        if (_decor is not null && _layoutHandler is not null && _decor.ViewTreeObserver is { IsAlive: true } vto)
            vto.GlobalLayout -= _layoutHandler;
        _decor = null; _layoutHandler = null;
        _keyboardOpen = false;
#endif
    }

    private void HookPage()
    {
        Element? e = this;
        while (e is not null and not Page) e = e.Parent;
        if (e is Page p && !ReferenceEquals(p, _page))
        {
            if (_page is not null) { _page.Appearing -= OnPageAppearing; _page.Disappearing -= OnPageDisappearing; }
            _page = p;
            p.Appearing += OnPageAppearing;
            p.Disappearing += OnPageDisappearing;
        }
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        // pages bind IsVisible (e.g. hidden on a pushed profile); remember it so the keyboard logic does not override it
        if (propertyName == nameof(IsVisible) && !_keyboardOpen) _hostVisible = IsVisible;
    }

    private void OnPageAppearing(object? s, EventArgs e) { RestartBlur(); _ = PlayTransitionAsync(); }
    private void OnPageDisappearing(object? s, EventArgs e) => _blur?.Stop();

    private Border? _glassView;
    private Image? _backdrop;

    /// <summary>(Re)starts the backdrop snapshots for the current bar; needs the page to exist.</summary>
    private void RestartBlur()
    {
        _blur?.Stop();
        if (_page is null || _glassView is null || _backdrop is null) return;
        var dark = IsDark();
        _blur = new BackdropBlur(_backdrop, _glassView, this, _page, () => Res(dark ? "BgDark" : "BgLight", dark ? Colors.Black : Colors.White));
        var tint = _tint;
        _blur.FirstFrame += () => { if (tint is not null) tint.Background = new SolidColorBrush(FrostTint(dark, blurred: true)); };
        _blur.Start();
    }

    private static bool IsDark()
    {
        var app = Application.Current;
        return (app?.UserAppTheme is AppTheme.Dark or AppTheme.Light ? app.UserAppTheme : app?.RequestedTheme) == AppTheme.Dark;
    }

    /// <summary>The milky veil over the blurred backdrop. Heavier before the first snapshot arrives so the bar never looks bare.</summary>
    private static Color FrostTint(bool dark, bool blurred)
        => dark ? Color.FromRgba(22, 26, 56, blurred ? 125 : 220) : Color.FromRgba(255, 255, 255, blurred ? 120 : 225);
    private void OnTheme(object? s, AppThemeChangedEventArgs e) => Build();

    private static Color Res(string key, Color fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : fallback;

    private static Color TabColor(Item i, bool dark) => Color.FromArgb(dark ? i.Dark : i.Light);

    private int MyIndex => Math.Max(0, Array.FindIndex(Items, i => i.Key == ActiveTab));
    /// <summary>-1 on screens that are not a tab of the bar (Create, Profile): nothing is highlighted there.</summary>
    private int ActiveIndex => Array.FindIndex(Items, i => i.Key == ActiveTab);

    /// <summary>
    /// Only the small active-tab pill glides to its new tab. The page itself is NOT animated: pages open instantly and show a
    /// skeleton while their data loads, which feels faster than moving large content around.
    /// </summary>
    private async Task PlayTransitionAsync()
    {
        if ((DateTime.UtcNow - _lastTransition).TotalMilliseconds < 400) return; // Loaded + Appearing both fire on first show
        var dir = TabNav.Direction;
        var from = TabNav.From;
        TabNav.Direction = 0;
        TabNav.Index = MyIndex;
        if (dir == 0) return;
        _lastTransition = DateTime.UtcNow;

        if (_highlight is not null && _row is not null && _row.Width > 0 && !Motion.Reduced && Motion.IsAlive(_highlight))
        {
            var col = (_row.Width - _row.Padding.HorizontalThickness) / Items.Length;
            _highlight.TranslationX = (from - MyIndex) * col;
            await _highlight.TranslateToAsync(0, 0, Motion.Nav, Easing.CubicInOut);
        }
    }

    private static GradientStop Stop(Color c, float at) => new(c, at);
    private static Color White(float a) => Color.FromRgba(1f, 1f, 1f, a);
    private static Color Black(float a) => Color.FromRgba(0f, 0f, 0f, a);

    private void Build()
    {
        var app = Application.Current;
        var dark = (app?.UserAppTheme is AppTheme.Dark or AppTheme.Light ? app.UserAppTheme : app?.RequestedTheme) == AppTheme.Dark;
        var ink = dark ? Color.FromArgb("#EEF0F5") : Color.FromArgb("#25232B");
        var radius = BarHeight / 2;
        var lensRadius = (BarHeight - 2 * Inset) / 2;   // concentric with the bar: at the far left / right the lens follows the bar's round end

        var row = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)],
            Padding = new Thickness(Inset),
            HeightRequest = BarHeight,
        };
        _row = row;

        // selection lens: a glossy, see-through glass capsule that fills the whole cell from the bar's top edge to its bottom edge
        var activeIndex = ActiveIndex;
        _highlight = null;
        if (activeIndex >= 0)
        {
            _highlight = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = lensRadius },
                StrokeThickness = 1,
                Stroke = dark
                    ? new LinearGradientBrush([Stop(White(0.50f), 0f), Stop(White(0.10f), 0.55f), Stop(White(0.22f), 1f)], new Point(0, 0), new Point(0, 1))
                    : new LinearGradientBrush([Stop(White(1f), 0f), Stop(White(0.6f), 0.5f), Stop(Black(0.07f), 1f)], new Point(0, 0), new Point(0, 1)),
                Background = dark
                    ? new LinearGradientBrush([Stop(White(0.30f), 0f), Stop(White(0.17f), 0.55f), Stop(White(0.13f), 1f)], new Point(0, 0), new Point(0, 1))
                    : new LinearGradientBrush([Stop(White(0.92f), 0f), Stop(Color.FromRgba(255, 255, 255, 170), 0.65f), Stop(Color.FromRgba(235, 238, 246, 190), 1f)], new Point(0, 0), new Point(0, 1)),
                Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Opacity = dark ? 0.30f : 0.10f, Radius = 10, Offset = new Point(0, 2) },
                HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill,
                InputTransparent = true,
            };
            row.Add(_highlight, activeIndex, 0);
        }

        for (var i = 0; i < Items.Length; i++)
        {
            var item = Items[i];
            var active = item.Key == ActiveTab;
            var isHunt = item.Key == "hunt";

            View iconSlot;
            if (isHunt)
            {
                // the main action: a camera capsule in the theme accent colour (a gem sitting in the glass)
                var accent = Res(dark ? "AccentDark" : "AccentLight", Color.FromArgb(dark ? "#7FB0FF" : "#2F66E8"));
                var onAccent = Res(dark ? "OnAccentDark" : "OnAccentLight", dark ? Color.FromArgb("#0A1530") : Colors.White);
                var accentHi = accent.WithLuminosity(Math.Min(1f, accent.GetLuminosity() + 0.10f));
                iconSlot = new Border
                {
                    WidthRequest = 50, HeightRequest = 34, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                    StrokeShape = new RoundRectangle { CornerRadius = 17 },
                    StrokeThickness = 1,
                    Stroke = new LinearGradientBrush([Stop(White(0.70f), 0f), Stop(White(0.10f), 1f)], new Point(0, 0), new Point(0, 1)),
                    Background = new LinearGradientBrush([Stop(accentHi, 0f), Stop(accent, 1f)], new Point(0, 0), new Point(0, 1)),
                    Shadow = new Shadow { Brush = new SolidColorBrush(accent), Opacity = dark ? 0.45f : 0.35f, Radius = 12, Offset = new Point(0, 3) },
                    Content = new IconView { Kind = item.Icon, Size = 20, Tint = onAccent, StrokeWidth = 2.1, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
                };
            }
            else
            {
                iconSlot = new IconView
                {
                    Kind = item.Icon, Size = 24, Tint = ink, StrokeWidth = active ? 2.2 : 1.8,   // every icon in the same colour; the lens alone shows the selection
                    HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                };
            }

            var route = item.Route;
            var index = i;
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                if (active || Shell.Current is null || _navigating) return;
                _navigating = true;
                try
                {
                    var dir = Math.Sign(index - TabNav.Index);
                    TabNav.From = TabNav.Index;
                    TabNav.Direction = dir;
                    Motion.Haptic();
                    if (route == "//hunt") TabNav.ExitBingo = true;
                    _ = Motion.TapAsync(iconSlot, 0.9);
                    await Shell.Current.GoToAsync(route);
                }
                finally { _navigating = false; }
            };

            // the whole cell is the touch target, not just the icon; the title is only read out by screen readers
            var cell = new Grid { BackgroundColor = Colors.Transparent, Children = { iconSlot } };
            cell.GestureRecognizers.Add(tap);
            SemanticProperties.SetDescription(cell, item.Title + (active ? ", seçili" : ""));
            SemanticProperties.SetHint(cell, active ? null : "Geçmek için dokun");
            row.Add(cell, i, 0);
        }

        // gloss: a soft light sheen over the upper half of the glass
        var sheen = new Border
        {
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = radius }, InputTransparent = true,
            Background = new LinearGradientBrush([Stop(White(dark ? 0.16f : 0.75f), 0f), Stop(White(dark ? 0.04f : 0.15f), 0.5f), Stop(White(0f), 1f)], new Point(0, 0), new Point(0, 1)),
        };

        // frosted glass = a real blurred snapshot of what is under the bar + a milky veil (see <see cref="BackdropBlur"/>)
        var backdrop = new Image { Aspect = Aspect.Fill, InputTransparent = true, IsOpaque = true };
        var veil = new Border { StrokeThickness = 0, InputTransparent = true, Background = new SolidColorBrush(FrostTint(dark, blurred: false)) };

        var glass = new Border
        {
            HeightRequest = BarHeight,
            StrokeShape = new RoundRectangle { CornerRadius = radius },
            StrokeThickness = 1,
            Stroke = dark
                ? new LinearGradientBrush([Stop(White(0.45f), 0f), Stop(White(0.06f), 0.55f), Stop(White(0.20f), 1f)], new Point(0, 0), new Point(1, 1))
                : new LinearGradientBrush([Stop(White(1f), 0f), Stop(White(0.7f), 0.5f), Stop(Black(0.08f), 1f)], new Point(0, 0), new Point(1, 1)),
            Background = new SolidColorBrush(dark ? Color.FromRgba(22, 26, 56, 255) : Color.FromRgba(255, 255, 255, 255)),   // replaced by the blurred snapshot + veil below
            Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Opacity = dark ? 0.40f : 0.12f, Radius = 24, Offset = new Point(0, 8) },
            Content = new Grid { Children = { backdrop, veil, sheen, row } },
        };
        _glassView = glass; _backdrop = backdrop; _tint = veil;

        Content = new Grid { Padding = new Thickness(18, 2, 18, 10), Children = { glass } };
        RestartBlur();
    }
}
