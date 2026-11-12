using System.Windows.Input;

namespace ColorMatch.Mobile.Controls;

public partial class SocialPostCard : ContentView
{
    public static readonly BindableProperty OpenCommandProperty = BindableProperty.Create(nameof(OpenCommand), typeof(ICommand), typeof(SocialPostCard), null);
    public static readonly BindableProperty AuthorCommandProperty = BindableProperty.Create(nameof(AuthorCommand), typeof(ICommand), typeof(SocialPostCard), null);

    public ICommand? OpenCommand { get => (ICommand?)GetValue(OpenCommandProperty); set => SetValue(OpenCommandProperty, value); }
    public ICommand? AuthorCommand { get => (ICommand?)GetValue(AuthorCommandProperty); set => SetValue(AuthorCommandProperty, value); }

    public SocialPostCard() => InitializeComponent();
}
