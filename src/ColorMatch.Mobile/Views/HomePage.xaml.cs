using ColorMatch.Mobile.ViewModels;

namespace ColorMatch.Mobile.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _vm;
    private IDispatcherTimer? _timer;

    public HomePage(HomeViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _vm.LoadAsync();

        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
    }

    private void OnTick(object? sender, EventArgs e) => _vm.UpdateCountdown();
}
