namespace ColorMatch.Mobile.Controls;

public partial class PostColorStrip : ContentView
{
    private bool _open;

    public PostColorStrip() => InitializeComponent();

    private async void OnToggle(object? sender, TappedEventArgs e)
    {
        _open = !_open;
        if (_open)
        {
            Palette.IsVisible = true;
            await Task.WhenAll(Palette.FadeToAsync(1, Motion.Quick, Easing.CubicOut), Chevron.RotateToAsync(90, Motion.Quick, Easing.CubicOut));
        }
        else
        {
            await Task.WhenAll(Palette.FadeToAsync(0, Motion.Press), Chevron.RotateToAsync(0, Motion.Press, Easing.CubicOut));
            if (!_open) Palette.IsVisible = false;
        }
    }
}
