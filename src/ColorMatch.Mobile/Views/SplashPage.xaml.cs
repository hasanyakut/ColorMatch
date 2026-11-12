using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.ViewModels;

namespace ColorMatch.Mobile.Views;

public partial class SplashPage : ContentPage
{
    private readonly SplashViewModel _vm;

    public SplashPage(SplashViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Stack.Scale = 1;
        _ = Motion.FadeInAsync(Stack, 320, 0);
        await _vm.StartAsync();
    }
}
