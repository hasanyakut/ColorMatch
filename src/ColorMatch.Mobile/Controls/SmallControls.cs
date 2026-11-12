using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace ColorMatch.Mobile.Controls;

/// <summary>Round 44 dp icon-only button (back, like, share…). Tones: plain / soft (recessed) / accent.</summary>
public sealed class IconButton : Border
{
    public static readonly BindableProperty KindProperty = BindableProperty.Create(nameof(Kind), typeof(string), typeof(IconButton), "", propertyChanged: (b, _, _) => ((IconButton)b).Rebuild());
    public static readonly BindableProperty ToneProperty = BindableProperty.Create(nameof(Tone), typeof(string), typeof(IconButton), "soft", propertyChanged: (b, _, _) => ((IconButton)b).Rebuild());
    public static readonly BindableProperty SizeProperty = BindableProperty.Create(nameof(Size), typeof(double), typeof(IconButton), 44d, propertyChanged: (b, _, _) => ((IconButton)b).Rebuild());
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(IconButton), null);
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(IconButton), null);
    public static readonly BindableProperty LabelProperty = BindableProperty.Create(nameof(Label), typeof(string), typeof(IconButton), "", propertyChanged: (b, _, v) => SemanticProperties.SetDescription((IconButton)b, v as string));

    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string Tone { get => (string)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    /// <summary>Accessibility label (screen readers).</summary>
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

    public IconButton()
    {
        StrokeThickness = 0;
        Padding = 0;
        HorizontalOptions = VerticalOptions = LayoutOptions.Center;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (!IsEnabled) return;
            Motion.Haptic(); _ = Motion.TapAsync(this, 0.92);   // pressed feedback, the command runs immediately
            if (Command?.CanExecute(CommandParameter) == true) Command.Execute(CommandParameter);
        };
        GestureRecognizers.Add(tap);
        Rebuild();
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == IsEnabledProperty.PropertyName) Opacity = IsEnabled ? 1 : 0.4;   // disabled state
    }

    private void Rebuild()
    {
        WidthRequest = HeightRequest = Math.Max(Size, 44);
        StrokeShape = new RoundRectangle { CornerRadius = (float)(Math.Max(Size, 44) / 2) };
        switch (Tone)
        {
            case "plain": BackgroundColor = Colors.Transparent; break;
            case "accent": this.SetAppThemeColor(BackgroundColorProperty, Resource("AccentSoftLight"), Resource("AccentSoftDark")); break;
            default: this.SetAppThemeColor(BackgroundColorProperty, Resource("SurfaceAltLight"), Resource("SurfaceAltDark")); break;
        }
        Content = new IconView { Kind = Kind, Size = 20, Accent = Tone == "accent" };
    }

    private static Color Resource(string key)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : Colors.Gray;
}

/// <summary>A colour sample: circle or rounded square, with a selection ring.</summary>
public sealed class ColorSwatch : Border
{
    public static readonly BindableProperty HexProperty = BindableProperty.Create(nameof(Hex), typeof(string), typeof(ColorSwatch), "#CCCCCC", propertyChanged: (b, _, _) => ((ColorSwatch)b).Rebuild());
    public static readonly BindableProperty SizeProperty = BindableProperty.Create(nameof(Size), typeof(double), typeof(ColorSwatch), 40d, propertyChanged: (b, _, _) => ((ColorSwatch)b).Rebuild());
    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(ColorSwatch), false, propertyChanged: (b, _, _) => ((ColorSwatch)b).Rebuild());
    public static readonly BindableProperty RoundedSquareProperty = BindableProperty.Create(nameof(RoundedSquare), typeof(bool), typeof(ColorSwatch), false, propertyChanged: (b, _, _) => ((ColorSwatch)b).Rebuild());
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(ColorSwatch), null);
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(ColorSwatch), null);

    public string Hex { get => (string)GetValue(HexProperty); set => SetValue(HexProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }
    public bool RoundedSquare { get => (bool)GetValue(RoundedSquareProperty); set => SetValue(RoundedSquareProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    private readonly Border _fill = new() { StrokeThickness = 1 };

    public ColorSwatch()
    {
        Padding = 3;
        HorizontalOptions = VerticalOptions = LayoutOptions.Center;
        Content = _fill;
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => { if (Command?.CanExecute(CommandParameter) == true) Command.Execute(CommandParameter); };
        GestureRecognizers.Add(tap);
        Rebuild();
    }

    private void Rebuild()
    {
        var outer = Size + 6;
        WidthRequest = HeightRequest = outer;
        var radius = RoundedSquare ? (float)(Size * 0.28) : (float)(Size / 2);
        StrokeShape = new RoundRectangle { CornerRadius = RoundedSquare ? radius + 3 : (float)(outer / 2) };
        _fill.StrokeShape = new RoundRectangle { CornerRadius = radius };
        _fill.BackgroundColor = ColorHelper.FromHex(Hex);
        _fill.WidthRequest = _fill.HeightRequest = Size;
        _fill.Stroke = Color.FromRgba(0, 0, 0, 0.08);
        StrokeThickness = IsSelected ? 2 : 0;
        if (IsSelected) this.SetAppTheme<Brush>(StrokeProperty, new SolidColorBrush(Color.FromArgb("#191922")), new SolidColorBrush(Color.FromArgb("#F6F5F8")));
        else Stroke = Colors.Transparent;
        BackgroundColor = Colors.Transparent;
        SemanticProperties.SetDescription(this, Hex + (IsSelected ? ", seçili" : ""));
    }
}

/// <summary>Placeholder block that gently pulses while content loads.</summary>
public sealed class SkeletonView : Border
{
    public static readonly BindableProperty RadiusProperty = BindableProperty.Create(nameof(Radius), typeof(double), typeof(SkeletonView), 12d, propertyChanged: (b, _, v) => ((SkeletonView)b).StrokeShape = new RoundRectangle { CornerRadius = (float)(double)v });

    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    /// <summary>A flat placeholder block. It does not animate itself: put blocks inside a <see cref="PulseHost"/> so one animation serves the whole skeleton.</summary>
    public SkeletonView()
    {
        StrokeThickness = 0;
        StrokeShape = new RoundRectangle { CornerRadius = 12 };
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EEE9E1"), Color.FromArgb("#28241F"));
        SemanticProperties.SetDescription(this, "Yükleniyor");
    }
}

/// <summary>
/// Hosts a skeleton and gives it a single, slow opacity pulse (one animation for the whole placeholder, not one per block).
/// The pulse runs only while the host is visible and attached, and stops with it.
/// </summary>
public sealed class PulseHost : ContentView
{
    public PulseHost()
    {
        Loaded += (_, _) => Sync();
        Unloaded += (_, _) => this.AbortAnimation("pulse");
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == IsVisibleProperty.PropertyName && IsLoaded) Sync();
    }

    private void Sync()
    {
        this.AbortAnimation("pulse");
        Opacity = 1;
        if (!IsVisible || Motion.Reduced) return;
        var pulse = new Animation { { 0, 0.5, new Animation(v => Opacity = v, 1, 0.55, Easing.SinInOut) }, { 0.5, 1, new Animation(v => Opacity = v, 0.55, 1, Easing.SinInOut) } };
        pulse.Commit(this, "pulse", 32, 1400, repeat: () => IsVisible);
    }
}

/// <summary>Tiny drawing of a collage template (its cell layout), used on the template picker.</summary>
public sealed class TemplateGlyph : Border
{
    public static readonly BindableProperty TemplateProperty = BindableProperty.Create(nameof(Template), typeof(Services.CollageTemplate), typeof(TemplateGlyph), null, propertyChanged: (b, _, _) => ((TemplateGlyph)b).Rebuild());
    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(TemplateGlyph), false, propertyChanged: (b, _, _) => ((TemplateGlyph)b).Rebuild());

    public Services.CollageTemplate? Template { get => (Services.CollageTemplate?)GetValue(TemplateProperty); set => SetValue(TemplateProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }

    public TemplateGlyph()
    {
        WidthRequest = 44; HeightRequest = 58; Padding = 5;
        StrokeShape = new RoundRectangle { CornerRadius = 10 };
        Rebuild();
    }

    private void Rebuild()
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var accent = Color.FromArgb(dark ? "#C6F432" : "#4353FF");
        var cell = IsSelected ? accent : Color.FromArgb(dark ? "#4B443C" : "#CFC7BB");
        BackgroundColor = Color.FromArgb(IsSelected ? (dark ? "#1B2A4A" : "#E1EAFD") : (dark ? "#28241F" : "#EEE9E1"));
        StrokeThickness = IsSelected ? 1.5 : 0;
        if (IsSelected && IsLoaded) _ = Motion.PopAsync(this, 1.08);   // soft confirmation when a layout is picked
        Stroke = new SolidColorBrush(accent);

        var layout = new AbsoluteLayout();
        if (Template is { } t)
            foreach (var (x, y, w, h) in t.Cells)
            {
                const float inset = 0.035f;
                var r = new Border { BackgroundColor = cell, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 2.5f } };
                AbsoluteLayout.SetLayoutFlags(r, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
                // proportional bounds are relative to the free space, so map position as x/(1-w)
                var cw = w - inset; var ch = h - inset;
                AbsoluteLayout.SetLayoutBounds(r, new Rect(x / (1 - cw), y / (1 - ch), cw, ch));
                layout.Children.Add(r);
            }
        Content = layout;
    }
}
