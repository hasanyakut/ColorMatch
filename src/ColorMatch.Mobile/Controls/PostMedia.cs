using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;

namespace ColorMatch.Mobile.Controls;

/// <summary>
/// The picture of a post, shown at its real aspect ratio (capped for very tall images). While it loads a tinted placeholder
/// in the post's own colour holds the space, so the feed does not jump. Single tap opens, double tap likes (with a heart burst).
/// </summary>
public sealed class PostMedia : Border
{
    public static readonly BindableProperty ImageUrlProperty = BindableProperty.Create(nameof(ImageUrl), typeof(string), typeof(PostMedia), null, propertyChanged: (b, _, _) => ((PostMedia)b).Refresh());
    public static readonly BindableProperty HexProperty = BindableProperty.Create(nameof(Hex), typeof(string), typeof(PostMedia), null, propertyChanged: (b, _, _) => ((PostMedia)b).Refresh());
    public static readonly BindableProperty IsCollageProperty = BindableProperty.Create(nameof(IsCollage), typeof(bool), typeof(PostMedia), false, propertyChanged: (b, _, _) => ((PostMedia)b).Refresh());
    public static readonly BindableProperty RadiusProperty = BindableProperty.Create(nameof(Radius), typeof(double), typeof(PostMedia), 24d, propertyChanged: (b, _, v) => ((PostMedia)b).StrokeShape = new RoundRectangle { CornerRadius = (float)(double)v });
    public static readonly BindableProperty PlaceholderHeightProperty = BindableProperty.Create(nameof(PlaceholderHeight), typeof(double), typeof(PostMedia), 340d, propertyChanged: (b, _, v) => ((PostMedia)b)._placeholder.HeightRequest = (double)v);
    public static readonly BindableProperty MaxPhotoHeightProperty = BindableProperty.Create(nameof(MaxPhotoHeight), typeof(double), typeof(PostMedia), 520d, propertyChanged: (b, _, v) => ((PostMedia)b)._image.MaximumHeightRequest = (double)v);
    public static readonly BindableProperty TappedCommandProperty = BindableProperty.Create(nameof(TappedCommand), typeof(ICommand), typeof(PostMedia), null);
    public static readonly BindableProperty TappedParameterProperty = BindableProperty.Create(nameof(TappedParameter), typeof(object), typeof(PostMedia), null);
    public static readonly BindableProperty DoubleTappedCommandProperty = BindableProperty.Create(nameof(DoubleTappedCommand), typeof(ICommand), typeof(PostMedia), null);

    public string? ImageUrl { get => (string?)GetValue(ImageUrlProperty); set => SetValue(ImageUrlProperty, value); }
    public string? Hex { get => (string?)GetValue(HexProperty); set => SetValue(HexProperty, value); }
    public bool IsCollage { get => (bool)GetValue(IsCollageProperty); set => SetValue(IsCollageProperty, value); }
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public double PlaceholderHeight { get => (double)GetValue(PlaceholderHeightProperty); set => SetValue(PlaceholderHeightProperty, value); }
    public double MaxPhotoHeight { get => (double)GetValue(MaxPhotoHeightProperty); set => SetValue(MaxPhotoHeightProperty, value); }
    public ICommand? TappedCommand { get => (ICommand?)GetValue(TappedCommandProperty); set => SetValue(TappedCommandProperty, value); }
    public object? TappedParameter { get => GetValue(TappedParameterProperty); set => SetValue(TappedParameterProperty, value); }
    public ICommand? DoubleTappedCommand { get => (ICommand?)GetValue(DoubleTappedCommandProperty); set => SetValue(DoubleTappedCommandProperty, value); }

    private readonly Image _image = new() { Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Fill, MinimumHeightRequest = 140, MaximumHeightRequest = 520 };
    private readonly Border _placeholder = new() { StrokeThickness = 0, HeightRequest = 340 };
    private readonly Border _mark;
    private readonly IconView _heart = new() { Kind = "heart", Filled = true, Size = 92, Tint = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, InputTransparent = true, Opacity = 0 };
    private DateTime _lastTap = DateTime.MinValue;
    private int _tapToken;

    public PostMedia()
    {
        StrokeThickness = 0;
        Padding = 0;
        StrokeShape = new RoundRectangle { CornerRadius = 24 };
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EEE9E1"), Color.FromArgb("#28241F"));

        _mark = new Border
        {
            WidthRequest = 30, HeightRequest = 30, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 15 },
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, Margin = 12, InputTransparent = true, IsVisible = false,
            Content = new IconView { Kind = "layers", Size = 16, StrokeWidth = 2 }
        };
        _mark.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#1D1A17"));
        SemanticProperties.SetDescription(_mark, "Kolaj");

        _image.PropertyChanged += (_, e) => { if (e.PropertyName == Image.IsLoadingProperty.PropertyName) UpdatePlaceholder(); };
        Content = new Grid { Children = { _placeholder, _image, _mark, _heart } };

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);
    }

    private void Refresh()
    {
        _image.Source = string.IsNullOrEmpty(ImageUrl) ? null : ImageHelper.From(ImageUrl);
        // the placeholder is the post's own colour, softly tinted, so a loading photo already has the right mood
        _placeholder.BackgroundColor = string.IsNullOrEmpty(Hex) ? Color.FromArgb("#00000000") : ColorHelper.FromHex(Hex).WithAlpha(0.55f);
        _mark.IsVisible = IsCollage;
        UpdatePlaceholder();
    }

    private void UpdatePlaceholder() => _placeholder.IsVisible = _image.Source is null || _image.IsLoading;

    /// <summary>One tap opens (after a short wait to rule out a double tap); two quick taps like.</summary>
    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastTap).TotalMilliseconds < 300)
        {
            _lastTap = DateTime.MinValue;
            _tapToken++;                        // cancels the pending single tap
            if (DoubleTappedCommand?.CanExecute(null) == true) DoubleTappedCommand.Execute(null);
            await BurstAsync();
            return;
        }
        _lastTap = now;
        var token = ++_tapToken;
        await Task.Delay(300);
        if (token != _tapToken) return;
        if (TappedCommand?.CanExecute(TappedParameter) == true) TappedCommand.Execute(TappedParameter);
    }

    private async Task BurstAsync()
    {
        if (Motion.Reduced || !Motion.IsAlive(_heart)) return;
        _heart.CancelAnimations();
        _heart.Scale = 0.7; _heart.Opacity = 1;
        await _heart.ScaleToAsync(1.1, Motion.Quick, Easing.CubicOut);
        await _heart.ScaleToAsync(1, Motion.Press, Easing.CubicOut);
        await Task.Delay(260);
        if (Motion.IsAlive(_heart)) await _heart.FadeToAsync(0, Motion.Quick);
    }
}
