using ColorMatch.Mobile.ViewModels;

namespace ColorMatch.Mobile.Views;

public partial class AuthPage : ContentPage
{
    public AuthPage(AuthViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
