using Microsoft.Maui.Controls.Shapes;

namespace ColorMatch.Mobile.Controls;

/// <summary>
/// A hunt's completion state: an empty ring while open, a filled disc with a check once done. When a hunt becomes completed while the
/// mark is on screen, the disc grows in (one short scale, no bounce). Reduced-motion aware.
/// </summary>
public sealed class CompletionMark : Grid
{
    public static readonly BindableProperty IsCompletedProperty = BindableProperty.Create(nameof(IsCompleted), typeof(bool), typeof(CompletionMark), false,
        propertyChanged: (b, o, n) => ((CompletionMark)b).Apply(animate: (bool)n && !(bool)o));
    public bool IsCompleted { get => (bool)GetValue(IsCompletedProperty); set => SetValue(IsCompletedProperty, value); }

    private readonly Border _ring;
    private readonly Border _fill;
    private int _token;

    public CompletionMark()
    {
        const double size = 28;
        WidthRequest = HeightRequest = size;
        HorizontalOptions = VerticalOptions = LayoutOptions.Center;

        _ring = new Border { StrokeThickness = 1.75, StrokeShape = new RoundRectangle { CornerRadius = (float)(size / 2) }, BackgroundColor = Colors.Transparent };
        _ring.SetAppTheme<Brush>(Border.StrokeProperty,
            new SolidColorBrush(Color.FromArgb("#D2C9BB")), new SolidColorBrush(Color.FromArgb("#4B443C")));

        _fill = new Border
        {
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = (float)(size / 2) },
            Content = new IconView { Kind = "check", Size = 16, StrokeWidth = 2.6, Tint = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
            Opacity = 0, Scale = 0.6,
        };
        _fill.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#0B8F6F"), Color.FromArgb("#3FE0B5"));

        Children.Add(_ring);
        Children.Add(_fill);
        Unloaded += (_, _) => { _token++; _fill.CancelAnimations(); };
        SemanticProperties.SetDescription(this, "Tamamlanmadı");
        Apply(animate: false);
    }

    private async void Apply(bool animate)
    {
        var token = ++_token;
        SemanticProperties.SetDescription(this, IsCompleted ? "Tamamlandı" : "Tamamlanmadı");

        if (!IsCompleted)
        {
            _fill.CancelAnimations();
            _fill.Opacity = 0; _fill.Scale = 0.6; _ring.Opacity = 1;
            return;
        }

        if (!animate || Motion.Reduced || !IsLoaded)
        {
            _fill.Opacity = 1; _fill.Scale = 1; _ring.Opacity = 0;
            return;
        }

        _fill.CancelAnimations();
        _fill.Opacity = 0; _fill.Scale = 0.6;
        await Task.WhenAll(_fill.FadeToAsync(1, Motion.Quick, Motion.Out), _fill.ScaleToAsync(1, Motion.Reward - 100, Motion.Out), _ring.FadeToAsync(0, Motion.Quick));
        if (token != _token || !Motion.IsAlive(_fill)) return;
        Motion.Haptic();
    }
}

/// <summary>Three ascending bars: 1 = easy, 2 = medium, 3 = hard. Difficulty is shown by shape, never by colour alone.</summary>
public sealed class DifficultyMeter : HorizontalStackLayout
{
    public static readonly BindableProperty LevelProperty = BindableProperty.Create(nameof(Level), typeof(int), typeof(DifficultyMeter), 1, propertyChanged: (b, _, _) => ((DifficultyMeter)b).Rebuild());
    public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }

    public DifficultyMeter()
    {
        Spacing = 2;
        VerticalOptions = LayoutOptions.Center;
        Rebuild();
    }

    private void Rebuild()
    {
        Children.Clear();
        double[] heights = [7, 11, 15];
        for (var i = 0; i < 3; i++)
        {
            var bar = new Border { WidthRequest = 4, HeightRequest = heights[i], StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 2 }, VerticalOptions = LayoutOptions.End };
            if (i < Level) bar.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#0F1226"), Color.FromArgb("#F6F3EA"));
            else bar.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#D8D0C3"), Color.FromArgb("#453E37"));
            Children.Add(bar);
        }
        SemanticProperties.SetDescription(this, Level switch { 1 => "Kolay", 2 => "Orta", _ => "Zor" });
    }
}
