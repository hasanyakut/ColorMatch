using ColorMatch.Mobile.ViewModels;

namespace ColorMatch.Mobile.Views;

public partial class BingoPage : ContentPage
{
    private readonly BingoViewModel _vm;

    public BingoPage(BingoViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    // three squares per row, whatever the screen width (20dp page padding, 8dp gaps)
    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0) return;
        _vm.TileSize = Math.Floor((Width - 40 - 2 * 8) / 3) - 0.5;
    }
}
