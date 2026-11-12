using ColorMatch.Mobile.ViewModels;

namespace ColorMatch.Mobile.Views;

public partial class ProfilePage : ContentPage
{
    private readonly ProfileViewModel _vm;

    public ProfilePage(ProfileViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    /// <summary>Three equal columns with 4dp gaps; tiles are 4:5 so a photo or a 4:5 collage is shown almost uncropped.</summary>
    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0) return;
        var tileWidth = (Width - 32 - 8) / 3;
        _vm.TileHeight = Math.Floor(tileWidth * 1.25);
    }
}
