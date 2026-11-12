using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;

namespace ColorMatch.Mobile.Controls;

/// <summary>
/// The in-app camera of the colour hunt: a live preview (CameraX through CommunityToolkit.Maui.Camera) with what a hunter actually needs —
/// flash (off / auto / on), torch, front/rear switch, 1× / 2× / 3× zoom chips and pinch zoom, a rule-of-thirds grid, a self-timer (3 s / 10 s),
/// a "target colour" loupe to hold the colour next to the real thing, the target chip, and the gallery. The photo is handed to the hunt page,
/// which then lets the app find the best matching spot on its own.
/// </summary>
public sealed class LiveCamera : ContentView
{
    public static readonly BindableProperty IsActiveProperty = BindableProperty.Create(nameof(IsActive), typeof(bool), typeof(LiveCamera), false, propertyChanged: (b, _, v) => ((LiveCamera)b).OnActive((bool)v));
    public static readonly BindableProperty TargetHexProperty = BindableProperty.Create(nameof(TargetHex), typeof(string), typeof(LiveCamera), "#888888", propertyChanged: (b, _, _) => ((LiveCamera)b).ApplyTarget());
    public static readonly BindableProperty TargetNameProperty = BindableProperty.Create(nameof(TargetName), typeof(string), typeof(LiveCamera), "", propertyChanged: (b, _, _) => ((LiveCamera)b).ApplyTarget());

    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public string TargetHex { get => (string)GetValue(TargetHexProperty); set => SetValue(TargetHexProperty, value); }
    public string TargetName { get => (string)GetValue(TargetNameProperty); set => SetValue(TargetNameProperty, value); }

    public event Action<string>? Captured;
    public event Action? Closed;
    public event Action? GalleryRequested;
    /// <summary>No camera could be started (no permission handled here, no device camera, driver error).</summary>
    public event Action? Failed;

    // created on first use: building the camera view (CameraX) is expensive and must not slow down opening the Hunt tab
    private CameraView _camera = null!;
    private bool _built;
    private readonly Grid _grid = new() { IsVisible = false, InputTransparent = true };
    private readonly Border _loupe = new() { IsVisible = false, InputTransparent = true };
    private readonly Border _targetSwatch = new();
    private readonly Label _targetName = new();
    private readonly Label _targetHex = new();
    private readonly Label _countdown = new();
    private readonly BoxView _flash = new() { Color = Colors.White, Opacity = 0, InputTransparent = true };
    private readonly Border _shutter = new();
    private readonly Border _shutterDisc = new();
    private readonly List<(double Factor, Border Chip, Label Text)> _zoomChips = [];
    private readonly Dictionary<string, (Border Button, IconView Icon, Label Caption)> _tools = [];

    private IReadOnlyList<CameraInfo> _cameras = [];
    private CameraInfo? _current;
    private CancellationTokenSource? _cts;
    private bool _capturing, _started;
    private int _timerSeconds;                     // 0 = off, 3, 10
    private CameraFlashMode _flashMode = CameraFlashMode.Off;
    private double _pinchStart = 1;

    public LiveCamera()
    {
        BackgroundColor = Colors.Black;
        ApplyTarget();
    }

    // ------------------------------------------------------------------ layout
    private View Build()
    {
        var root = new Grid { BackgroundColor = Colors.Black };
        root.Add(_camera);

        // pinch anywhere on the preview zooms
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += OnPinch;
        _camera.GestureRecognizers.Add(pinch);

        // rule-of-thirds grid
        _grid.RowDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)];
        _grid.ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star), new(GridLength.Star)];
        for (var i = 0; i < 9; i++)
            _grid.Add(new Border { Stroke = Color.FromRgba(255, 255, 255, 70), StrokeThickness = 0.6, BackgroundColor = Colors.Transparent, StrokeShape = new Rectangle() }, i % 3, i / 3);
        root.Add(_grid);

        // target loupe: a round field of the target colour to hold up against the real world
        _loupe.WidthRequest = 118; _loupe.HeightRequest = 118;
        _loupe.StrokeShape = new RoundRectangle { CornerRadius = 59 };
        _loupe.StrokeThickness = 3; _loupe.Stroke = Colors.White;
        _loupe.HorizontalOptions = LayoutOptions.End; _loupe.VerticalOptions = LayoutOptions.Start;
        _loupe.Margin = new Thickness(0, 190, 18, 0);
        _loupe.Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Opacity = 0.45f, Radius = 12, Offset = new Point(0, 4) };
        root.Add(_loupe);

        root.Add(_flash);

        // top: close · target chip
        var close = RoundButton("x", 44, () => Closed?.Invoke(), "Kamerayı kapat");
        var chip = new Border
        {
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 24 }, Padding = new Thickness(8, 6, 16, 6),
            BackgroundColor = Color.FromRgba(0, 0, 0, 120), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    _targetSwatch,
                    new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 0, Children = { _targetName, _targetHex } },
                },
            },
        };
        _targetSwatch.WidthRequest = 30; _targetSwatch.HeightRequest = 30; _targetSwatch.StrokeThickness = 2; _targetSwatch.Stroke = Colors.White;
        _targetSwatch.StrokeShape = new RoundRectangle { CornerRadius = 15 }; _targetSwatch.VerticalOptions = LayoutOptions.Center;
        _targetName.TextColor = Colors.White; _targetName.FontFamily = "SansSemibold"; _targetName.FontSize = 13; _targetName.LineBreakMode = LineBreakMode.TailTruncation; _targetName.MaximumWidthRequest = 170;
        _targetHex.TextColor = Color.FromRgba(255, 255, 255, 200); _targetHex.FontFamily = "SansRegular"; _targetHex.FontSize = 11; _targetHex.CharacterSpacing = 1.2;
        var top = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(44)], Padding = new Thickness(16, 10), VerticalOptions = LayoutOptions.Start };
        top.Add(close, 0, 0); top.Add(chip, 1, 0);
        root.Add(top);

        // tools row under the top bar
        var tools = new Border
        {
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 26 }, Padding = new Thickness(8, 6),
            BackgroundColor = Color.FromRgba(0, 0, 0, 105), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 82, 0, 0),
            Content = new HorizontalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    Tool("flash", "zap", "Flaş: kapalı", CycleFlash),
                    Tool("torch", "bulb", "Fener", ToggleTorch),
                    Tool("grid", "grid", "Izgara", ToggleGrid),
                    Tool("timer", "clock", "Zamanlayıcı", CycleTimer),
                    Tool("loupe", "palette", "Hedef renk", ToggleLoupe),
                },
            },
        };
        root.Add(tools);

        // bottom: zoom chips · gallery / shutter / flip
        var zoom = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center };
        foreach (var f in new[] { 1.0, 2.0, 3.0 })
        {
            var label = new Label { Text = $"{f:0}×", TextColor = Colors.White, FontFamily = "SansSemibold", FontSize = 13, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
            var chipBorder = new Border { WidthRequest = 42, HeightRequest = 42, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 21 }, BackgroundColor = Color.FromRgba(0, 0, 0, 110), Content = label };
            var factor = f;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => SetZoom(factor);
            chipBorder.GestureRecognizers.Add(tap);
            SemanticProperties.SetDescription(chipBorder, $"{f:0} kat yakınlaştırma");
            zoom.Add(chipBorder);
            _zoomChips.Add((f, chipBorder, label));
        }

        _countdown.TextColor = Colors.White; _countdown.FontFamily = "SansBold"; _countdown.FontSize = 120; _countdown.HorizontalOptions = LayoutOptions.Center; _countdown.VerticalOptions = LayoutOptions.Center; _countdown.InputTransparent = true; _countdown.Opacity = 0;
        _countdown.Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Opacity = 0.5f, Radius = 14 };
        root.Add(_countdown);

        _shutter.WidthRequest = 82; _shutter.HeightRequest = 82; _shutter.StrokeShape = new RoundRectangle { CornerRadius = 41 }; _shutter.StrokeThickness = 4; _shutter.Stroke = Colors.White; _shutter.Padding = 5; _shutter.BackgroundColor = Colors.Transparent; _shutter.HorizontalOptions = LayoutOptions.Center;
        _shutterDisc.StrokeShape = new RoundRectangle { CornerRadius = 35 }; _shutterDisc.StrokeThickness = 0; _shutterDisc.BackgroundColor = Colors.White;
        _shutter.Content = _shutterDisc;
        var shutterTap = new TapGestureRecognizer();
        shutterTap.Tapped += async (_, _) => await OnShutterAsync();
        _shutter.GestureRecognizers.Add(shutterTap);
        SemanticProperties.SetDescription(_shutter, "Fotoğraf çek");

        var gallery = RoundButton("image", 54, () => GalleryRequested?.Invoke(), "Galeriden seç");
        var flip = RoundButton("refresh", 54, async () => await FlipAsync(), "Kamerayı çevir");
        gallery.HorizontalOptions = LayoutOptions.Start; flip.HorizontalOptions = LayoutOptions.End;
        gallery.VerticalOptions = flip.VerticalOptions = LayoutOptions.Center;

        var controls = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Star)], Padding = new Thickness(28, 0) };
        controls.Add(gallery, 0, 0); controls.Add(_shutter, 1, 0); controls.Add(flip, 2, 0);

        var bottom = new VerticalStackLayout { Spacing = 20, VerticalOptions = LayoutOptions.End, Padding = new Thickness(0, 0, 0, 34), Children = { zoom, controls } };
        var bottomShade = new BoxView
        {
            VerticalOptions = LayoutOptions.End, HeightRequest = 230, InputTransparent = true,
            Background = new LinearGradientBrush([new GradientStop(Color.FromRgba(0, 0, 0, 0), 0f), new GradientStop(Color.FromRgba(0, 0, 0, 150), 1f)], new Point(0, 0), new Point(0, 1)),
        };
        root.Add(bottomShade);
        root.Add(bottom);
        return root;
    }

    private Border RoundButton(string icon, double size, Action onTap, string description)
    {
        var b = new Border
        {
            WidthRequest = size, HeightRequest = size, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = size / 2 },
            BackgroundColor = Color.FromRgba(0, 0, 0, 120), Padding = 0,
            Content = new IconView { Kind = icon, Size = size * 0.48, Tint = Colors.White, StrokeWidth = 2, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => { Motion.Haptic(); onTap(); };
        b.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(b, description);
        return b;
    }

    private View Tool(string key, string icon, string caption, Action onTap)
    {
        var iconView = new IconView { Kind = icon, Size = 22, Tint = Colors.White, StrokeWidth = 1.9, HorizontalOptions = LayoutOptions.Center };
        var label = new Label { Text = caption, TextColor = Color.FromRgba(255, 255, 255, 215), FontFamily = "SansRegular", FontSize = 9.5, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.NoWrap };
        var b = new Border
        {
            StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 18 }, Padding = new Thickness(8, 7), BackgroundColor = Colors.Transparent, MinimumWidthRequest = 58,
            Content = new VerticalStackLayout { Spacing = 3, Children = { iconView, label } },
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => { Motion.Haptic(); onTap(); };
        b.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(b, caption);
        _tools[key] = (b, iconView, label);
        return b;
    }

    private static readonly Color On = Color.FromArgb("#FFD54A");

    private void MarkTool(string key, bool active, string? caption = null)
    {
        if (!_tools.TryGetValue(key, out var t)) return;
        t.Icon.Tint = active ? On : Colors.White;
        t.Button.BackgroundColor = active ? Color.FromRgba(255, 213, 74, 40) : Colors.Transparent;
        if (caption is not null) { t.Caption.Text = caption; SemanticProperties.SetDescription(t.Button, caption); }
    }

    private void ApplyTarget()
    {
        var c = Color.FromArgb(string.IsNullOrWhiteSpace(TargetHex) ? "#888888" : TargetHex);
        _targetSwatch.BackgroundColor = c;
        _loupe.BackgroundColor = c;
        _targetName.Text = TargetName;
        _targetHex.Text = TargetHex;
    }

    // ------------------------------------------------------------------ lifecycle
    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        _camera = new CameraView { HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill, CameraFlashMode = CameraFlashMode.Off };
        _camera.MediaCaptured += OnMediaCaptured;
        _camera.MediaCaptureFailed += (_, _) => MainThread.BeginInvokeOnMainThread(() => { _capturing = false; SetShutterBusy(false); });
        Content = Build();
        ApplyTarget();
    }

    private async void OnActive(bool active)
    {
        if (!active) { if (_built) Stop(); return; }
        EnsureBuilt();
        await Task.Yield();   // let the freshly built view attach before the preview starts
        await StartAsync();
    }

    private async Task StartAsync()
    {
        _cts?.Cancel(); _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try
        {
            _cameras = await _camera.GetAvailableCameras(ct);
            if (_cameras.Count == 0) { Failed?.Invoke(); return; }
            _current = _cameras.FirstOrDefault(c => c.Position == CameraPosition.Rear) ?? _cameras[0];
            _camera.SelectedCamera = _current;
            _flashMode = CameraFlashMode.Off; _camera.CameraFlashMode = _flashMode;
            _camera.IsTorchOn = false;
            _timerSeconds = 0;
            _grid.IsVisible = false; _loupe.IsVisible = false;
            MarkTool("flash", false, "Flaş: kapalı"); MarkTool("torch", false); MarkTool("grid", false); MarkTool("timer", false, "Zamanlayıcı"); MarkTool("loupe", false);
            await _camera.StartCameraPreview(ct);
            _started = true;
            SetZoom(1, animate: false);
            RefreshCapabilities();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("camera start: " + ex); Failed?.Invoke(); }
    }

    private void Stop()
    {
        _cts?.Cancel();
        _countdownCts?.Cancel();
        if (_started) { try { _camera.IsTorchOn = false; _camera.StopCameraPreview(); } catch { /* the view may already be gone */ } }
        _started = false; _capturing = false;
        SetShutterBusy(false);
    }

    private void RefreshCapabilities()
    {
        var flash = _current?.IsFlashSupported == true;
        if (_tools.TryGetValue("flash", out var f)) f.Button.Opacity = flash ? 1 : 0.35;
        if (_tools.TryGetValue("torch", out var t)) t.Button.Opacity = flash && _current?.Position == CameraPosition.Rear ? 1 : 0.35;
        foreach (var (factor, chip, _) in _zoomChips)
            chip.IsVisible = _current is null || factor <= _current.MaximumZoomFactor + 0.01;
        HighlightZoom();
    }

    // ------------------------------------------------------------------ tools
    private void CycleFlash()
    {
        if (_current?.IsFlashSupported != true) return;
        _flashMode = _flashMode switch { CameraFlashMode.Off => CameraFlashMode.Auto, CameraFlashMode.Auto => CameraFlashMode.On, _ => CameraFlashMode.Off };
        _camera.CameraFlashMode = _flashMode;
        MarkTool("flash", _flashMode != CameraFlashMode.Off, _flashMode switch { CameraFlashMode.Auto => "Flaş: otomatik", CameraFlashMode.On => "Flaş: açık", _ => "Flaş: kapalı" });
    }

    private void ToggleTorch()
    {
        if (_current?.IsFlashSupported != true || _current.Position != CameraPosition.Rear) return;
        _camera.IsTorchOn = !_camera.IsTorchOn;
        MarkTool("torch", _camera.IsTorchOn, _camera.IsTorchOn ? "Fener açık" : "Fener");
    }

    private void ToggleGrid() { _grid.IsVisible = !_grid.IsVisible; MarkTool("grid", _grid.IsVisible); }
    private void ToggleLoupe() { _loupe.IsVisible = !_loupe.IsVisible; MarkTool("loupe", _loupe.IsVisible, _loupe.IsVisible ? "Hedef açık" : "Hedef renk"); }

    private void CycleTimer()
    {
        _timerSeconds = _timerSeconds switch { 0 => 3, 3 => 10, _ => 0 };
        MarkTool("timer", _timerSeconds > 0, _timerSeconds == 0 ? "Zamanlayıcı" : $"{_timerSeconds} saniye");
    }

    private async Task FlipAsync()
    {
        if (_cameras.Count < 2 || _current is null) return;
        var wanted = _current.Position == CameraPosition.Rear ? CameraPosition.Front : CameraPosition.Rear;
        var next = _cameras.FirstOrDefault(c => c.Position == wanted);
        if (next is null) return;
        _camera.IsTorchOn = false; MarkTool("torch", false, "Fener");
        _current = next;
        _camera.SelectedCamera = next;
        await Task.Delay(120);
        SetZoom(1, animate: false);
        RefreshCapabilities();
    }

    // ------------------------------------------------------------------ zoom
    private double Clamp(double z) => Math.Clamp(z, _current?.MinimumZoomFactor ?? 1, Math.Max(_current?.MaximumZoomFactor ?? 1, 1));

    private void SetZoom(double factor, bool animate = true)
    {
        _camera.ZoomFactor = (float)Clamp(factor);
        HighlightZoom();
    }

    private void OnPinch(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started: _pinchStart = _camera.ZoomFactor; break;
            case GestureStatus.Running: _camera.ZoomFactor = (float)Clamp(_pinchStart * e.Scale); HighlightZoom(); break;
        }
    }

    private void HighlightZoom()
    {
        var z = _camera.ZoomFactor;
        var near = _zoomChips.Where(c => c.Chip.IsVisible).OrderBy(c => Math.Abs(c.Factor - z)).FirstOrDefault();
        foreach (var (factor, chip, text) in _zoomChips)
        {
            var active = near.Chip == chip && Math.Abs(factor - z) < 0.35;
            chip.BackgroundColor = active ? Color.FromRgba(255, 213, 74, 235) : Color.FromRgba(0, 0, 0, 110);
            text.TextColor = active ? Colors.Black : Colors.White;
            text.Text = active && Math.Abs(factor - z) > 0.05 ? $"{z:0.0}×" : $"{factor:0}×";
        }
        if (_zoomChips.All(c => Math.Abs(c.Factor - z) >= 0.35) && _zoomChips.Count > 0)
        {
            // between the presets (pinching): show the exact value on the closest chip
            var closest = _zoomChips.Where(c => c.Chip.IsVisible).OrderBy(c => Math.Abs(c.Factor - z)).First();
            closest.Text.Text = $"{z:0.0}×"; closest.Chip.BackgroundColor = Color.FromRgba(255, 213, 74, 235); closest.Text.TextColor = Colors.Black;
        }
    }

    // ------------------------------------------------------------------ capture
    private CancellationTokenSource? _countdownCts;

    private void SetShutterBusy(bool busy) { _shutter.Opacity = busy ? 0.6 : 1; }

    private async Task OnShutterAsync()
    {
        // tapping the shutter while the self-timer runs cancels it
        if (_countdownCts is not null) { _countdownCts.Cancel(); return; }
        if (_capturing || !_started) return;

        if (_timerSeconds > 0)
        {
            _countdownCts = new CancellationTokenSource();
            var ct = _countdownCts.Token;
            try
            {
                for (var s = _timerSeconds; s > 0; s--)
                {
                    _countdown.Text = s.ToString(); _countdown.Scale = 1.25; _countdown.Opacity = 1;
                    Motion.Haptic();
                    _ = _countdown.ScaleToAsync(0.9, 850, Easing.CubicOut);
                    await Task.Delay(1000, ct);
                }
            }
            catch (OperationCanceledException) { _countdown.Opacity = 0; _countdownCts = null; return; }
            _countdown.Opacity = 0; _countdownCts = null;
        }
        await CaptureAsync();
    }

    private async Task CaptureAsync()
    {
        if (_capturing) return;
        _capturing = true; SetShutterBusy(true);
        Motion.Haptic();
        _ = FlashAsync();
        try { await _camera.CaptureImage(_cts?.Token ?? CancellationToken.None); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("capture: " + ex); _capturing = false; SetShutterBusy(false); }
    }

    private async Task FlashAsync()
    {
        if (Motion.Reduced) return;
        _flash.Opacity = 0.85;
        await _flash.FadeToAsync(0, 220, Easing.CubicOut);
    }

    private void OnMediaCaptured(object? sender, MediaCapturedEventArgs e)
    {
        try
        {
            var dir = System.IO.Path.Combine(FileSystem.CacheDirectory, "photos");
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, Guid.NewGuid().ToString("N") + ".jpg");
            using (var fs = File.Create(path)) e.Media.CopyTo(fs);
            MainThread.BeginInvokeOnMainThread(() => { _capturing = false; SetShutterBusy(false); Captured?.Invoke(path); });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("save capture: " + ex);
            MainThread.BeginInvokeOnMainThread(() => { _capturing = false; SetShutterBusy(false); });
        }
    }
}
