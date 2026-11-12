namespace ColorMatch.Mobile.Controls;

/// <summary>Level badge + XP bar. The bar animates to the new value.</summary>
public partial class XpProgress : ContentView
{
    public static readonly BindableProperty LevelProperty = BindableProperty.Create(nameof(Level), typeof(int), typeof(XpProgress), 1);
    public static readonly BindableProperty CaptionProperty = BindableProperty.Create(nameof(Caption), typeof(string), typeof(XpProgress), "");
    public static readonly BindableProperty XpTextProperty = BindableProperty.Create(nameof(XpText), typeof(string), typeof(XpProgress), "");
    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(nameof(Progress), typeof(double), typeof(XpProgress), 0d, propertyChanged: (b, _, _) => ((XpProgress)b).AnimateFill());

    public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public string XpText { get => (string)GetValue(XpTextProperty); set => SetValue(XpTextProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    public XpProgress() => InitializeComponent();

    private void OnTrackSizeChanged(object? sender, EventArgs e) => AnimateFill(animate: false);

    private void AnimateFill(bool animate = true)
    {
        if (Track is null || Track.Width <= 0) return;
        var target = Math.Clamp(Progress, 0, 1) * Track.Width;
        // keep a visible nub once any XP exists
        if (Progress > 0) target = Math.Max(target, 10);
        this.AbortAnimation("xpfill");
        if (!animate || Math.Abs(Fill.WidthRequest - target) < 1) { Fill.WidthRequest = target; return; }
        var from = Math.Max(0, Fill.WidthRequest);
        new Animation(v => Fill.WidthRequest = v, from, target, Easing.CubicOut).Commit(this, "xpfill", 16, 700);
    }
}
