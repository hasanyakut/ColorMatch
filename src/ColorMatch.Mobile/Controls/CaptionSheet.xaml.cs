using System.Windows.Input;

namespace ColorMatch.Mobile.Controls;

/// <summary>
/// Bottom sheet that asks for the optional note of a post. The host page binds <see cref="IsOpen"/>, <see cref="Text"/> and the two commands;
/// the sheet slides up over a dimmed scrim, focuses the editor, and slides away again.
/// </summary>
public partial class CaptionSheet : ContentView
{
    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(nameof(IsOpen), typeof(bool), typeof(CaptionSheet), false, propertyChanged: (b, _, v) => _ = ((CaptionSheet)b).SetOpenAsync((bool)v));
    public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(CaptionSheet), "", BindingMode.TwoWay);
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(CaptionSheet), "Not ekle");
    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(CaptionSheet), "");
    public static readonly BindableProperty ConfirmTextProperty = BindableProperty.Create(nameof(ConfirmText), typeof(string), typeof(CaptionSheet), "Paylaş");
    public static readonly BindableProperty ConfirmCommandProperty = BindableProperty.Create(nameof(ConfirmCommand), typeof(ICommand), typeof(CaptionSheet), null);
    public static readonly BindableProperty CancelCommandProperty = BindableProperty.Create(nameof(CancelCommand), typeof(ICommand), typeof(CaptionSheet), null);

    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string ConfirmText { get => (string)GetValue(ConfirmTextProperty); set => SetValue(ConfirmTextProperty, value); }
    public ICommand? ConfirmCommand { get => (ICommand?)GetValue(ConfirmCommandProperty); set => SetValue(ConfirmCommandProperty, value); }
    public ICommand? CancelCommand { get => (ICommand?)GetValue(CancelCommandProperty); set => SetValue(CancelCommandProperty, value); }

    private int _token;
#if ANDROID
    private Android.Views.View? _decor;
    private EventHandler? _layoutHandler;
#endif

    /// <summary>Android 15 draws edge to edge and does not resize the window for the keyboard, so the sheet lifts itself by the keyboard height.</summary>
    private void HookKeyboard()
    {
#if ANDROID
        UnhookKeyboard();
        _decor = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window?.DecorView;
        if (_decor is null) return;
        _layoutHandler = (_, _) =>
        {
            var rect = new Android.Graphics.Rect();
            _decor.GetWindowVisibleDisplayFrame(rect);
            var total = _decor.RootView?.Height ?? _decor.Height;
            var keyboardPx = Math.Max(0, total - rect.Bottom);
            var dp = keyboardPx / (double)_decor.Resources!.DisplayMetrics!.Density;
            MainThread.BeginInvokeOnMainThread(() => { if (Math.Abs(Sheet.Padding.Bottom - (18 + dp)) > 1) Sheet.Padding = new Thickness(20, 10, 20, 18 + dp); });
        };
        _decor.ViewTreeObserver!.GlobalLayout += _layoutHandler;
#endif
    }

    private void UnhookKeyboard()
    {
#if ANDROID
        if (_decor is not null && _layoutHandler is not null && _decor.ViewTreeObserver is { IsAlive: true } vto) vto.GlobalLayout -= _layoutHandler;
        _decor = null; _layoutHandler = null;
#endif
        Sheet.Padding = new Thickness(20, 10, 20, 18);
    }

    public CaptionSheet()
    {
        InitializeComponent();
        Unloaded += (_, _) => UnhookKeyboard();
    }

    private async Task SetOpenAsync(bool open)
    {
        var token = ++_token;
        if (open)
        {
            HookKeyboard();
            IsVisible = true;
            Scrim.Opacity = 0;
            Sheet.TranslationY = Math.Max(Height, 420);
            if (Motion.Reduced) { Scrim.Opacity = 1; Sheet.TranslationY = 0; }
            else await Task.WhenAll(Scrim.FadeToAsync(1, Motion.Quick, Motion.Out), Sheet.TranslateToAsync(0, 0, Motion.Content, Motion.Out));
            if (token != _token || !Motion.IsAlive(Input)) return;
            Input.Focus();   // the keyboard comes up with the sheet already in place
        }
        else
        {
            Input.Unfocus();
            try { await Input.HideSoftInputAsync(CancellationToken.None); } catch { /* keyboard already gone */ }
            UnhookKeyboard();
            if (!Motion.Reduced && Motion.IsAlive(Sheet))
                await Task.WhenAll(Scrim.FadeToAsync(0, Motion.Quick, Motion.In), Sheet.TranslateToAsync(0, Math.Max(Sheet.Height, 320), Motion.Quick + 20, Motion.In));
            if (token == _token) IsVisible = false;
        }
    }

    private void OnScrimTapped(object? sender, TappedEventArgs e)
    {
        if (CancelCommand?.CanExecute(null) == true) CancelCommand.Execute(null);
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e) => Counter.Text = $"{e.NewTextValue?.Length ?? 0} / 500";
}
