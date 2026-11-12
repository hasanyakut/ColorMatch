using ColorMatch.Mobile.ViewModels;

namespace ColorMatch.Mobile.Views;

public partial class AtlasPage : ContentPage
{
    private readonly AtlasViewModel _vm;

    public AtlasPage(AtlasViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    // four tiles per row, whatever the screen width (20dp page padding, 8dp gaps)
    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0) return;
        _vm.TileSize = Math.Floor((Width - 40 - 3 * 8) / 4) - 0.5;
    }
}
