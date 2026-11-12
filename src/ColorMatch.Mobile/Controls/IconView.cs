using Microsoft.Maui.Controls.Shapes;
using ShapePath = Microsoft.Maui.Controls.Shapes.Path;

namespace ColorMatch.Mobile.Controls;

/// <summary>
/// One consistent outline icon set (1.8 stroke, round caps) drawn as vector paths: theme-aware and tintable.
/// Tone: default = ink, <see cref="Muted"/> = secondary text, <see cref="Accent"/> = brand accent, or an explicit <see cref="Tint"/>.
/// </summary>
public sealed class IconView : ContentView
{
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["heart"] = "M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z",
        ["comment"] = "M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z",
        ["share"] = "M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8 M16 6l-4-4-4 4 M12 2v13",
        ["camera"] = "M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z M16 13 a4 4 0 1 1-8 0 4 4 0 0 1 8 0z",
        ["image"] = "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z M10 8.5 a1.5 1.5 0 1 1-3 0 1.5 1.5 0 0 1 3 0z M21 15l-5-5L5 21",
        ["x"] = "M18 6L6 18 M6 6l12 12",
        ["check"] = "M20 6L9 17l-5-5",
        ["more"] = "M13 12 a1 1 0 1 1-2 0 1 1 0 0 1 2 0z M20 12 a1 1 0 1 1-2 0 1 1 0 0 1 2 0z M6 12 a1 1 0 1 1-2 0 1 1 0 0 1 2 0z",
        ["bell"] = "M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9 M13.73 21a2 2 0 0 1-3.46 0",
        ["copy"] = "M20 9h-9a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h9a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2z M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1",
        ["refresh"] = "M23 4v6h-6 M1 20v-6h6 M3.51 9a9 9 0 0 1 14.85-3.36L23 10 M1 14l4.64 4.36A9 9 0 0 0 20.49 15",
        ["send"] = "M22 2L11 13 M22 2l-7 20-4-9-9-4 20-7z",
        ["download"] = "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4 M7 10l5 5 5-5 M12 15V3",
        ["plus"] = "M12 5v14 M5 12h14",
        ["target"] = "M22 12 a10 10 0 1 1-20 0 10 10 0 0 1 20 0z M18 12 a6 6 0 1 1-12 0 6 6 0 0 1 12 0z M14 12 a2 2 0 1 1-4 0 2 2 0 0 1 4 0z",
        ["alert"] = "M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z M12 9v4 M12 17h.01",
        ["wifi-off"] = "M1 1l22 22 M16.72 11.06A10.94 10.94 0 0 1 19 12.55 M5 12.55a10.94 10.94 0 0 1 5.17-2.39 M10.71 5.05A16 16 0 0 1 22.58 9 M1.42 9a15.91 15.91 0 0 1 4.7-2.88 M8.53 16.11a6 6 0 0 1 6.95 0 M12 20h.01",
        ["trophy"] = "M6 9H4.5a2.5 2.5 0 0 1 0-5H6 M18 9h1.5a2.5 2.5 0 0 0 0-5H18 M4 22h16 M10 14.66V17c0 .55-.47.98-.97 1.21C7.85 18.75 7 20.24 7 22 M14 14.66V17c0 .55.47.98.97 1.21C16.15 18.75 17 20.24 17 22 M18 2H6v7a6 6 0 0 0 12 0V2z",
        ["layers"] = "M12 2L2 7l10 5 10-5-10-5z M2 17l10 5 10-5 M2 12l10 5 10-5",
        ["edit"] = "M12 20h9 M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5z",
        ["users"] = "M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M13 7 a4 4 0 1 1-8 0 4 4 0 0 1 8 0z M23 21v-2a4 4 0 0 0-3-3.87 M16 3.13a4 4 0 0 1 0 7.75",
        ["user-plus"] = "M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2 M12.5 7 a4 4 0 1 1-8 0 4 4 0 0 1 8 0z M20 8v6 M23 11h-6",
        ["search"] = "M21 21l-4.35-4.35 M19 11 a8 8 0 1 1-16 0 8 8 0 0 1 16 0z",
        ["zap"] = "M13 2L3 14h9l-1 8 10-12h-9l1-8z",
        ["clock"] = "M22 12 a10 10 0 1 1-20 0 10 10 0 0 1 20 0z M12 6v6l4 2",
        ["bulb"] = "M9 18h6 M10 22h4 M15.09 14c.18-.98.65-1.74 1.41-2.5A4.65 4.65 0 0 0 18 8 6 6 0 0 0 6 8c0 1 .23 2.23 1.5 3.5A4.61 4.61 0 0 1 8.91 14",
        ["home"] = "M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z M9 22V12h6v10",
        ["grid"] = "M3 3h7v9H3z M14 3h7v5h-7z M14 12h7v9h-7z M3 16h7v5H3z",
        ["user"] = "M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2 M16 7 a4 4 0 1 1-8 0 4 4 0 0 1 8 0z",
        ["arrow-left"] = "M19 12H5 M12 19l-7-7 7-7",
        ["arrow-right"] = "M5 12h14 M12 5l7 7-7 7",
        ["chevron-right"] = "M9 18l6-6-6-6",
        ["award"] = "M19 8 a7 7 0 1 1-14 0 7 7 0 0 1 14 0z M8.21 13.89L7 23l5-3 5 3-1.21-9.12",
        ["lock"] = "M19 11H5a2 2 0 0 0-2 2v7a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7a2 2 0 0 0-2-2z M7 11V7a5 5 0 0 1 10 0v4",
        ["star"] = "M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z",
        ["palette"] = "M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10c.93 0 1.5-.75 1.5-1.5 0-.4-.15-.75-.4-1-.25-.3-.4-.6-.4-1 0-.83.67-1.5 1.5-1.5H16c3.31 0 6-2.69 6-6 0-4.97-4.5-9-10-9z M7.5 11.5 a1 1 0 1 1-2 0 1 1 0 0 1 2 0z M10.5 7.5 a1 1 0 1 1-2 0 1 1 0 0 1 2 0z M15.5 7.5 a1 1 0 1 1-2 0 1 1 0 0 1 2 0z",
    };

    public static readonly BindableProperty KindProperty = BindableProperty.Create(nameof(Kind), typeof(string), typeof(IconView), "", propertyChanged: (b, _, _) => ((IconView)b).Rebuild());
    public static readonly BindableProperty SizeProperty = BindableProperty.Create(nameof(Size), typeof(double), typeof(IconView), 22d, propertyChanged: (b, _, _) => ((IconView)b).Rebuild());
    public static readonly BindableProperty TintProperty = BindableProperty.Create(nameof(Tint), typeof(Color), typeof(IconView), null, propertyChanged: (b, _, _) => ((IconView)b).Rebuild());
    public static readonly BindableProperty FilledProperty = BindableProperty.Create(nameof(Filled), typeof(bool), typeof(IconView), false, propertyChanged: (b, _, _) => ((IconView)b).Rebuild());
    public static readonly BindableProperty AccentProperty = BindableProperty.Create(nameof(Accent), typeof(bool), typeof(IconView), false, propertyChanged: (b, _, _) => ((IconView)b).Rebuild());
    public static readonly BindableProperty MutedProperty = BindableProperty.Create(nameof(Muted), typeof(bool), typeof(IconView), false, propertyChanged: (b, _, _) => ((IconView)b).Rebuild());
    public static readonly BindableProperty StrokeWidthProperty = BindableProperty.Create(nameof(StrokeWidth), typeof(double), typeof(IconView), 1.8d, propertyChanged: (b, _, _) => ((IconView)b).Rebuild());

    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    /// <summary>Explicit colour; wins over every tone.</summary>
    public Color? Tint { get => (Color?)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    public bool Filled { get => (bool)GetValue(FilledProperty); set => SetValue(FilledProperty, value); }
    public bool Accent { get => (bool)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public bool Muted { get => (bool)GetValue(MutedProperty); set => SetValue(MutedProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    public IconView()
    {
        InputTransparent = true;
        HorizontalOptions = VerticalOptions = LayoutOptions.Center;
        Rebuild();
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        // a "liked" heart pops when it appears
        if (propertyName == IsVisibleProperty.PropertyName && IsVisible && Filled && Accent && IsLoaded)
            _ = Motion.PopAsync(this, 1.2);
    }

    private static Color Lookup(string key, Color fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : fallback;

    private void Rebuild()
    {
        if (!Paths.TryGetValue(Kind ?? "", out var data)) { Content = null; return; }

        var path = new ShapePath
        {
            Data = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(data)!,
            StrokeThickness = StrokeWidth,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Aspect = Stretch.Uniform,
            WidthRequest = Size,
            HeightRequest = Size,
        };

        if (Tint is { } tint)
        {
            path.Stroke = new SolidColorBrush(tint);
            if (Filled) path.Fill = new SolidColorBrush(tint);
        }
        else
        {
            var tone = Accent ? "Accent" : Muted ? "InkMuted" : "Ink";
            var light = Lookup(tone + "Light", Colors.Black);
            var dark = Lookup(tone + "Dark", Colors.White);
            path.SetAppTheme(ShapePath.StrokeProperty, (Brush)new SolidColorBrush(light), new SolidColorBrush(dark));
            if (Filled) path.SetAppTheme(ShapePath.FillProperty, (Brush)new SolidColorBrush(light), new SolidColorBrush(dark));
        }
        Content = path;
    }
}
