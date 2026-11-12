using ColorMatch.Mobile.ViewModels;

namespace ColorMatch.Mobile.Views;

public partial class ChallengesPage : ContentPage
{
    private readonly ChallengesViewModel _vm;

    public ChallengesPage(ChallengesViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }
}
