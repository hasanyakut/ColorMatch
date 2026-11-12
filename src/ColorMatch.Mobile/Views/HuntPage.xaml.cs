using System.ComponentModel;
using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.ViewModels;

namespace ColorMatch.Mobile.Views;

public partial class HuntPage : ContentPage
{
    private readonly HuntViewModel _vm;
    private bool _ringPlaced;

    public HuntPage(HuntViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.PropertyChanged += OnVmChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.CloseCamera();   // the camera never keeps running behind another screen
    }

    /// <summary>The Android back button closes the live camera first.</summary>
    protected override bool OnBackButtonPressed()
    {
        if (_vm.IsCameraOpen) { _vm.CloseCamera(); return true; }
        return base.OnBackButtonPressed();
    }

    private async void OnCameraCaptured(string path) => await _vm.OnCameraCapturedAsync(path);
    private void OnCameraClosed() => _vm.CloseCamera();
    private void OnCameraGallery() { _vm.CloseCamera(); if (_vm.PickPhotoCommand.CanExecute(null)) _vm.PickPhotoCommand.Execute(null); }
    private async void OnCameraFailed() => await _vm.OnCameraFailedAsync();

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HuntViewModel.PhotoPath):
                _ringPlaced = false;
                LayoutStage();
                break;
            case nameof(HuntViewModel.PhotoAspect):
                LayoutStage();
                break;
            case nameof(HuntViewModel.SelX):
            case nameof(HuntViewModel.SelY):
            case nameof(HuntViewModel.SelRadius):
                UpdateRing();
                break;
        }
    }

    /// <summary>Shutter feedback: the disc dips under the finger, then the camera opens.</summary>
    private async void OnShutterTapped(object? sender, TappedEventArgs e)
    {
        if (_vm.IsPreparing) return;
        Motion.Haptic();
        if (!Motion.Reduced) await Task.WhenAll(ShutterDisc.ScaleToAsync(0.84, Motion.Press, Easing.CubicOut), Shutter.ScaleToAsync(0.97, Motion.Press, Easing.CubicOut));
        if (!Motion.Reduced) await Task.WhenAll(ShutterDisc.ScaleToAsync(1, Motion.Press, Easing.CubicOut), Shutter.ScaleToAsync(1, Motion.Press, Easing.CubicOut));
        if (_vm.TakePhotoCommand.CanExecute(null)) _vm.TakePhotoCommand.Execute(null);
    }

    private void OnStageHostSizeChanged(object? sender, EventArgs e) => LayoutStage();

    /// <summary>Sizes the stage to exactly the photo's aspect ratio inside the black matte, so touch coordinates map 1:1 to normalised image coordinates.</summary>
    private void LayoutStage()
    {
        if (!_vm.HasPhoto || StageHost.Width <= 0 || StageHost.Height <= 0) return;
        var maxW = StageHost.Width;
        var maxH = StageHost.Height;
        var aspect = _vm.PhotoAspect <= 0 ? 1 : _vm.PhotoAspect;
        var w = maxW; var h = w / aspect;
        if (h > maxH) { h = maxH; w = h * aspect; }
        Stage.WidthRequest = Math.Max(1, w);
        Stage.HeightRequest = Math.Max(1, h);
        UpdateRing();
    }

    private void UpdateRing()
    {
        var w = Stage.WidthRequest; var h = Stage.HeightRequest;
        if (w <= 0 || h <= 0) return;
        var d = Math.Max(16, _vm.SelRadius * Math.Min(w, h) * 2);
        Ring.WidthRequest = Ring.HeightRequest = d;
        var tx = _vm.SelX * w - d / 2; var ty = _vm.SelY * h - d / 2;
        // the mark glides to the spot the app found; it is display-only (the player cannot move it)
        if (!_ringPlaced || Motion.Reduced || !Motion.IsAlive(Ring)) { Ring.TranslationX = tx; Ring.TranslationY = ty; _ringPlaced = true; return; }
        _ = Ring.TranslateToAsync(tx, ty, 360, Easing.CubicOut);
    }

}
