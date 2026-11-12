namespace ColorMatch.Mobile.Controls;

public partial class ColorComparisonCard : ContentView
{
    public static readonly BindableProperty TargetHexProperty = BindableProperty.Create(nameof(TargetHex), typeof(string), typeof(ColorComparisonCard), "#CCCCCC");
    public static readonly BindableProperty FoundHexProperty = BindableProperty.Create(nameof(FoundHex), typeof(string), typeof(ColorComparisonCard), "#CCCCCC");
    public static readonly BindableProperty DeltaTextProperty = BindableProperty.Create(nameof(DeltaText), typeof(string), typeof(ColorComparisonCard), "");
    public static readonly BindableProperty CardHeightProperty = BindableProperty.Create(nameof(CardHeight), typeof(double), typeof(ColorComparisonCard), 260d);

    public string TargetHex { get => (string)GetValue(TargetHexProperty); set => SetValue(TargetHexProperty, value); }
    public string FoundHex { get => (string)GetValue(FoundHexProperty); set => SetValue(FoundHexProperty, value); }
    public string DeltaText { get => (string)GetValue(DeltaTextProperty); set => SetValue(DeltaTextProperty, value); }
    public double CardHeight { get => (double)GetValue(CardHeightProperty); set => SetValue(CardHeightProperty, value); }

    public ColorComparisonCard() => InitializeComponent();
}
