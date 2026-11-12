using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.ViewModels;

namespace ColorMatch.Mobile.Views;

public partial class PostDetailPage : ContentPage
{
    private readonly PostDetailViewModel _vm;

    public PostDetailPage(PostDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // the post rises into place instead of popping in

        await _vm.LoadAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
