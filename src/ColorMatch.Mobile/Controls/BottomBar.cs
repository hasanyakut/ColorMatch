using Microsoft.Maui.Controls.Shapes;

namespace ColorMatch.Mobile.Controls;

/// <summary>Navigation hints shared between the pages and the bar.</summary>
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
/// The navigation capsule: Play / Challenges / Hunt / Atlas / Community (the profile opens from the avatar, Create from the Community tab).
/// Instagram-like frosted glass floating over the page; the selected tab sits in a glass lens that fills the cell from the bar's top edge to
/// its bottom edge (at the far left / right it follows the bar's round end) and glides between tabs.
/// <para>There is exactly ONE bar for the whole app (see <see cref="NavBar"/>): it lives above the Shell, is built once and is never recreated
/// when pages change. Switching tabs only slides the lens and changes the stroke of the icons. Earlier every page owned its own bar, so each tab
/// switch built a new one while the UI thread was busy creating the page — the bar was missing or sat in the wrong place for a few hundred
/// milliseconds and then jumped into position.</para>
/// </summary>
public sealed class BottomBar : ContentView
{
    private sealed record Item(string Key, string Title, string Icon, string Route);

    private static readonly Item[] Items =
    [
        new("home", "Oyna", "home", "//home"),
        new("challenges", "Görevler", "trophy", "//challenges"),
        new("hunt", "Rengi bul", "camera", "//hunt"),
        new("atlas", "Atlas", "palette", "//atlas"),
        new("feed", "Topluluk", "users", "//feed"),
    ];

    private const double BarHeight = 60, Inset = 3.5;

    private static bool _navigating;
    private Border? _lens;
    private Grid? _row;
    private readonly List<(Item Item, IconView? Icon, Grid Cell)> _cells = [];
    private string? _activeKey;
    private int _activeIndex = -1;
    private bool _wanted, _keyboardOpen, _shown;
    private BackdropBlur? _blur;
    private Border? _tint, _glassView;
    private Image? _backdrop;
    private Page? _blurPage;
#if ANDROID
    private Android.Views.View? _decor;
    private EventHandler? _layoutHandler;
#endif

    public BottomBar()
    {
        InputTransparent = false;
        Opacity = 0; IsVisible = false;
        Build();
        Loaded += (_, _) =>
        {
            if (Application.Current is { } a) a.RequestedThemeChanged += OnTheme;
            HookKeyboard();
        };
        Unloaded += (_, _) =>
        {
            if (Application.Current is { } a) a.RequestedThemeChanged -= OnTheme;
            UnhookKeyboard();
            _blur?.Stop();
        };
    }

    // ------------------------------------------------------------------ driven by NavBar
    /// <summary>Which tab is selected (null / "" = none, e.g. on Create or Profile). The lens glides there.</summary>
    public void SetActive(string? key, bool animate = true)
    {
        key = string.IsNullOrEmpty(key) ? null : key;
        if (key == _activeKey && _lens is not null) return;
        var from = _activeIndex;
        _activeKey = key;
        _activeIndex = key is null ? -1 : Array.FindIndex(Items, i => i.Key == key);
        TabNav.Index = Math.Max(0, _activeIndex);

        foreach (var (item, icon, cell) in _cells)
        {
            var on = item.Key == key;
            if (icon is not null) icon.StrokeWidth = on ? 2.2 : 1.8;
            SemanticProperties.SetDescription(cell, item.Title + (on ? ", seçili" : ""));
            SemanticProperties.SetHint(cell, on ? null : "Geçmek için dokun");
        }
        MoveLens(from, animate);
    }

    private void MoveLens(int from, bool animate)
    {
        if (_lens is null || _row is null) return;
        if (_activeIndex < 0) { _lens.IsVisible = false; return; }
        var wasHidden = !_lens.IsVisible || from < 0;
        _lens.IsVisible = true;
        Grid.SetColumn(_lens, _activeIndex);
        _lens.CancelAnimations();
        if (!animate || wasHidden || Motion.Reduced || _row.Width <= 0 || !_shown) { _lens.TranslationX = 0; return; }
        var col = (_row.Width - _row.Padding.HorizontalThickness) / Items.Length;
        _lens.TranslationX = (from - _activeIndex) * col;
        _ = _lens.TranslateToAsync(0, 0, 240, Easing.CubicOut);
    }

    /// <summary>Show or hide the whole bar (hidden on pushed pages, while a photo is being marked, under the live camera, with the keyboard).</summary>
    public void SetShown(bool wanted, Page? blurPage)
    {
        _wanted = wanted; _blurPage = blurPage;
        UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        var show = _wanted && !_keyboardOpen;
        if (show == _shown) { if (show) EnsureBlur(); return; }
        _shown = show;
        this.CancelAnimations();
        if (show)
        {
            IsVisible = true;
            EnsureBlur();
            if (Motion.Reduced) { Opacity = 1; TranslationY = 0; return; }
            TranslationY = 18;
            _ = Task.WhenAll(this.FadeToAsync(1, 160, Easing.CubicOut), this.TranslateToAsync(0, 0, 240, Easing.CubicOut));
        }
        else
        {
            _blur?.Stop();
            if (Motion.Reduced) { Opacity = 0; IsVisible = false; return; }
            _ = HideAsync();
        }
    }

    private async Task HideAsync()
    {
        await Task.WhenAll(this.FadeToAsync(0, 120, Easing.CubicIn), this.TranslateToAsync(0, 12, 140, Easing.CubicIn));
        if (!_shown) IsVisible = false;
    }

    private void EnsureBlur()
    {
        if (_blur is not null || _blurPage is null || _glassView is null || _backdrop is null) { _blur?.Start(); return; }
        RestartBlur();
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
            MainThread.BeginInvokeOnMainThread(UpdateVisibility);
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

    // ------------------------------------------------------------------ frosted glass
    /// <summary>(Re)starts the backdrop snapshots for the current glass.</summary>
    private void RestartBlur()
    {
        _blur?.Stop();
        if (_blurPage is null || _glassView is null || _backdrop is null) return;
        var dark = IsDark();
        _blur = new BackdropBlur(_backdrop, _glassView, this, _blurPage, () => Res(dark ? "BgDark" : "BgLight", dark ? Colors.Black : Colors.White));
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

    private void OnTheme(object? s, AppThemeChangedEventArgs e)
    {
        var key = _activeKey;
        _activeKey = "\0"; _activeIndex = -1;   // force the rebuilt bar to apply the selection again
        Build();
        SetActive(key, animate: false);
        _blur = null;
        if (_shown) EnsureBlur();
    }

    private static Color Res(string key, Color fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : fallback;

    private static GradientStop Stop(Color c, float at) => new(c, at);
    private static Color White(float a) => Color.FromRgba(1f, 1f, 1f, a);
    private static Color Black(float a) => Color.FromRgba(0f, 0f, 0f, a);

    private void Build()
    {
        var dark = IsDark();
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
        _cells.Clear();

        // selection lens: a glossy, see-through glass capsule that fills the whole cell from the bar's top edge to its bottom edge
        _lens = new Border
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
            InputTransparent = true, IsVisible = false,
        };
        row.Add(_lens, 0, 0);

        for (var i = 0; i < Items.Length; i++)
        {
            var item = Items[i];
            var isHunt = item.Key == "hunt";

            View iconSlot;
            IconView? plainIcon = null;
            if (isHunt)
            {
                // the main action: a camera capsule in the theme accent colour (a gem sitting in the glass)
                var accent = Res(dark ? "AccentDark" : "AccentLight", Color.FromArgb(dark ? "#C6F432" : "#4353FF"));
                var onAccent = Res(dark ? "OnAccentDark" : "OnAccentLight", dark ? Color.FromArgb("#0F1226") : Colors.White);
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
                plainIcon = new IconView
                {
                    Kind = item.Icon, Size = 24, Tint = ink, StrokeWidth = 1.8,   // every icon in the same colour; the lens alone shows the selection
                    HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                };
                iconSlot = plainIcon;
            }

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                if (item.Key == _activeKey || Shell.Current is null || _navigating) return;
                _navigating = true;
                try
                {
                    TabNav.From = TabNav.Index;
                    Motion.Haptic();
                    if (item.Route == "//hunt") TabNav.ExitBingo = true;
                    SetActive(item.Key);                 // the lens moves at once; the page follows
                    _ = Motion.TapAsync(iconSlot, 0.9);
                    await Shell.Current.GoToAsync(item.Route);
                }
                finally { _navigating = false; }
            };

            // the whole cell is the touch target, not just the icon; the title is only read out by screen readers
            var cell = new Grid { BackgroundColor = Colors.Transparent, Children = { iconSlot } };
            cell.GestureRecognizers.Add(tap);
            row.Add(cell, i, 0);
            _cells.Add((item, plainIcon, cell));
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
    }
}
