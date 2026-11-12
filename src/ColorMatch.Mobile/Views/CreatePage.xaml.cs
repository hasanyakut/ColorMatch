using System.ComponentModel;
using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.ViewModels;

namespace ColorMatch.Mobile.Views;

public partial class CreatePage : ContentPage
{
    private readonly CreateViewModel _vm;

    public CreatePage(CreateViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.PropertyChanged += OnVmChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Init();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        // a new layout / colour settles in softly instead of snapping
        if (e.PropertyName == nameof(CreateViewModel.PreviewSource) && _vm.PreviewSource is not null)
            _ = SettlePreviewAsync();
    }

    private async Task SettlePreviewAsync()
    {
        if (Motion.Reduced || !Motion.IsAlive(Preview)) return;
        Preview.CancelAnimations();
        Preview.Opacity = 0.4;
        await Preview.FadeToAsync(1, Motion.Quick + 20, Easing.CubicOut);
    }
}
