using System.Windows.Input;

namespace ColorMatch.Mobile.Controls;

public partial class StateView : ContentView
{
    public static readonly BindableProperty IsLoadingProperty = BindableProperty.Create(nameof(IsLoading), typeof(bool), typeof(StateView), false, propertyChanged: Refresh);
    public static readonly BindableProperty ErrorMessageProperty = BindableProperty.Create(nameof(ErrorMessage), typeof(string), typeof(StateView), null, propertyChanged: Refresh);
    public static readonly BindableProperty IsEmptyProperty = BindableProperty.Create(nameof(IsEmpty), typeof(bool), typeof(StateView), false, propertyChanged: Refresh);
    public static readonly BindableProperty EmptyTextProperty = BindableProperty.Create(nameof(EmptyText), typeof(string), typeof(StateView), "Henüz bir şey yok.");
    public static readonly BindableProperty EmptyTitleProperty = BindableProperty.Create(nameof(EmptyTitle), typeof(string), typeof(StateView), "");
    public static readonly BindableProperty EmptyIconProperty = BindableProperty.Create(nameof(EmptyIcon), typeof(string), typeof(StateView), "image");
    public static readonly BindableProperty RetryCommandProperty = BindableProperty.Create(nameof(RetryCommand), typeof(ICommand), typeof(StateView), null, propertyChanged: Refresh);

    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); set => SetValue(IsLoadingProperty, value); }
    public string? ErrorMessage { get => (string?)GetValue(ErrorMessageProperty); set => SetValue(ErrorMessageProperty, value); }
    public bool IsEmpty { get => (bool)GetValue(IsEmptyProperty); set => SetValue(IsEmptyProperty, value); }
    public string EmptyText { get => (string)GetValue(EmptyTextProperty); set => SetValue(EmptyTextProperty, value); }
    public string EmptyTitle { get => (string)GetValue(EmptyTitleProperty); set => SetValue(EmptyTitleProperty, value); }
    public string EmptyIcon { get => (string)GetValue(EmptyIconProperty); set => SetValue(EmptyIconProperty, value); }
    public ICommand? RetryCommand { get => (ICommand?)GetValue(RetryCommandProperty); set => SetValue(RetryCommandProperty, value); }

    public bool ShowError => !IsLoading && !string.IsNullOrEmpty(ErrorMessage);
    public bool ShowEmpty => !IsLoading && string.IsNullOrEmpty(ErrorMessage) && IsEmpty;
    public bool HasRetry => RetryCommand is not null;
    private bool IsNetworkError => ErrorMessage?.Contains("ulaşılamadı", StringComparison.OrdinalIgnoreCase) == true;
    public string ErrorIcon => IsNetworkError ? "wifi-off" : "alert";
    public string ErrorTitle => IsNetworkError ? "Sunucuya ulaşılamadı" : "Bir sorun oluştu";

    public StateView() { InitializeComponent(); UpdateVisibility(this); }

    private static void Refresh(BindableObject b, object _, object __) => UpdateVisibility((StateView)b);

    private static void UpdateVisibility(StateView v)
    {
        foreach (var p in new[] { nameof(ShowError), nameof(ShowEmpty), nameof(HasRetry), nameof(ErrorIcon), nameof(ErrorTitle) })
            v.OnPropertyChanged(p);
        // the control must not swallow touches while it shows nothing
        var visible = v.ShowError || v.ShowEmpty || v.IsLoading;
        v.InputTransparent = !visible;
        v.IsVisible = visible;
    }
}
