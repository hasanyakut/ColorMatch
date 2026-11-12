using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace ColorMatch.Mobile.Controls;

/// <summary>
/// The app's button: a tactile key (face + ledge) with four kinds and a loading state.
/// Kind: "primary" (the one main action: lime face, ink text), "secondary" (paper face with an ink edge), "ghost" (text only),
/// "ink" (solid ink). Compact = 42 dp variant. While <see cref="IsLoading"/> the label is replaced by a spinner and taps are ignored.
/// Pressing pushes the face down onto its ledge with a tick; releasing springs it back.
/// </summary>
public partial class AppButton : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(AppButton), "", propertyChanged: (b, _, _) => ((AppButton)b).Apply());
    public static readonly BindableProperty KindProperty = BindableProperty.Create(nameof(Kind), typeof(string), typeof(AppButton), "primary", propertyChanged: (b, _, _) => ((AppButton)b).Apply());
    public static readonly BindableProperty CompactProperty = BindableProperty.Create(nameof(Compact), typeof(bool), typeof(AppButton), false, propertyChanged: (b, _, _) => ((AppButton)b).Apply());
    public static readonly BindableProperty IsLoadingProperty = BindableProperty.Create(nameof(IsLoading), typeof(bool), typeof(AppButton), false, propertyChanged: (b, _, _) => ((AppButton)b).Apply());
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(AppButton), null, propertyChanged: (b, _, _) => ((AppButton)b).Apply());
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(AppButton), null);

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    private DateTime _lastClick = DateTime.MinValue;
    private double _ledge = 4;

    public AppButton()
    {
        InitializeComponent();
        Inner.Pressed += async (_, _) =>
        {
            if (!Motion.IsAlive(Face)) return;
            Motion.Haptic();
            if (Motion.Reduced) { Face.TranslationY = _ledge; return; }
            Face.CancelAnimations();
            await Face.TranslateToAsync(0, _ledge, 70, Easing.CubicOut);
        };
        Inner.Released += async (_, _) =>
        {
            if (!Motion.IsAlive(Face)) return;
            if (Motion.Reduced) { Face.TranslationY = 0; return; }
            Face.CancelAnimations();
            await Face.TranslateToAsync(0, 0, 260, Easing.SpringOut);
        };
        Loaded += (_, _) => { if (Application.Current is { } a) a.RequestedThemeChanged += OnTheme; Apply(); };
        Unloaded += (_, _) => { if (Application.Current is { } a) a.RequestedThemeChanged -= OnTheme; };
        Apply();
    }

    private void OnTheme(object? s, AppThemeChangedEventArgs e) => Apply();

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == IsEnabledProperty.PropertyName) Apply();
    }

    private static Color Res(string key, string fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : Color.FromArgb(fallback);

    private void Apply()
    {
        if (Inner is null) return;
        var app = Application.Current;
        var dark = (app?.UserAppTheme is AppTheme.Dark or AppTheme.Light ? app.UserAppTheme : app?.RequestedTheme) == AppTheme.Dark;

        var ink = Res(dark ? "InkDark" : "InkLight", dark ? "#F6F3EA" : "#0F1226");
        var night = Res("Night", "#0F1226");
        var surface = Res(dark ? "SurfaceDark" : "SurfaceLight", dark ? "#1A1E3D" : "#FFFFFF");

        Color face, text, ledge; Color? edge = null;
        switch (Kind)
        {
            case "secondary":
                face = surface; text = ink;
                edge = dark ? Res("OutlineDark", "#323869") : night;
                ledge = dark ? Color.FromArgb("#0A0C1C") : night;
                break;
            case "ink":
                face = ink; text = Res(dark ? "OnInkDark" : "OnInkLight", dark ? "#0F1226" : "#FBF7EF");
                ledge = dark ? Color.FromArgb("#B9B4A6") : Colors.Black;
                break;
            case "ghost":
                face = Colors.Transparent; text = Res(dark ? "AccentDark" : "AccentLight", dark ? "#C6F432" : "#4353FF");
                ledge = Colors.Transparent;
                break;
            default:   // primary: the highlighter
                face = Res("Lime", "#C6F432"); text = night;
                ledge = dark ? Res("LimeDeep", "#8FB61A") : night;
                break;
        }

        var ghost = Kind == "ghost";
        var height = ghost ? 44 : Compact ? 42 : 54;
        var radius = Compact ? 14 : 18;
        _ledge = ghost ? 0 : Compact ? 3 : 4;

        Root.HeightRequest = height + _ledge;
        Face.Margin = new Thickness(0, 0, 0, _ledge);
        Ledge.Margin = new Thickness(0, _ledge, 0, 0);
        Face.StrokeShape = new RoundRectangle { CornerRadius = radius };
        Ledge.StrokeShape = new RoundRectangle { CornerRadius = radius };
        Face.BackgroundColor = face;
        Face.Stroke = edge is null ? Colors.Transparent : new SolidColorBrush(edge);
        Face.StrokeThickness = edge is null ? 0 : 1.6;
        Face.Padding = new Thickness(Compact ? 14 : 22, 0);
        Ledge.BackgroundColor = ledge;
        Ledge.IsVisible = !ghost;
        Inner.CornerRadius = radius;

        Caption.Text = IsLoading ? "" : Text;
        Caption.TextColor = text;
        Caption.FontFamily = ghost ? "SansSemibold" : "SansBold";
        Caption.FontSize = ghost ? 14 : Compact ? 14 : 16.5;
        Caption.CharacterSpacing = ghost ? 0 : -0.2;

        Inner.IsEnabled = IsEnabled && !IsLoading;
        Root.Opacity = IsEnabled ? 1 : 0.45;
        Spinner.IsVisible = Spinner.IsRunning = IsLoading;
        Spinner.Color = text;
        SemanticProperties.SetDescription(Inner, Text);
    }

    private void OnInnerClicked(object? sender, EventArgs e)
    {
        if (IsLoading || !IsEnabled) return;
        // double-tap protection: a command (publish, sign in, submit) must not run twice from one impatient tap
        if ((DateTime.UtcNow - _lastClick).TotalMilliseconds < 450) return;
        _lastClick = DateTime.UtcNow;
        if (Command?.CanExecute(CommandParameter) == true) Command.Execute(CommandParameter);
    }
}
