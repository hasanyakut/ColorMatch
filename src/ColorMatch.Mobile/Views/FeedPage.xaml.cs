using ColorMatch.Mobile.ViewModels;

namespace ColorMatch.Mobile.Views;

public partial class FeedPage : ContentPage
{
    private readonly FeedViewModel _vm;

    public FeedPage(FeedViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshAsync();
    }
}
