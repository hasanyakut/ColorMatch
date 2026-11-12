namespace ColorMatch.Mobile.Controls;

/// <summary>
/// Page entrance: <c>controls:Reveal.Stagger="True"</c> on the main stack of a screen lets its blocks arrive one after another
/// (a short rise + fade, 45 ms apart) every time the screen comes on, and when its content first becomes visible after loading.
/// Short and interruptible; skipped entirely when the system has animations turned off.
/// </summary>
public static class Reveal
{
    public static readonly BindableProperty StaggerProperty = BindableProperty.CreateAttached("Stagger", typeof(bool), typeof(Reveal), false, propertyChanged: OnChanged);
    public static bool GetStagger(BindableObject b) => (bool)b.GetValue(StaggerProperty);
    public static void SetStagger(BindableObject b, bool value) => b.SetValue(StaggerProperty, value);

    private static readonly BindableProperty LastPlayedProperty = BindableProperty.CreateAttached("LastPlayed", typeof(DateTime), typeof(Reveal), DateTime.MinValue);
    private static readonly BindableProperty HookedPageProperty = BindableProperty.CreateAttached("HookedPage", typeof(Page), typeof(Reveal), null);

    private const int MaxAnimated = 6;

    private static void OnChanged(BindableObject b, object? oldValue, object? newValue)
    {
        if (b is not Layout layout || newValue is not true) return;
        layout.Loaded += (_, _) => { Hook(layout); _ = PlayAsync(layout); };
        layout.PropertyChanged += (_, e) =>
        {
            // content that appears after loading (IsVisible bound to "Loaded") gets the same entrance
            if (e.PropertyName == VisualElement.IsVisibleProperty.PropertyName && layout.IsVisible) _ = PlayAsync(layout);
        };
    }

    private static void Hook(Layout layout)
    {
        Element? e = layout;
        while (e is not null and not Page) e = e.Parent;
        if (e is not Page page || ReferenceEquals(layout.GetValue(HookedPageProperty), page)) return;
        layout.SetValue(HookedPageProperty, page);
        page.Appearing += (_, _) => _ = PlayAsync(layout);
    }

    public static async Task PlayAsync(Layout layout)
    {
        if (Motion.Reduced || !Motion.IsAlive(layout) || !layout.IsVisible) return;
        var last = (DateTime)layout.GetValue(LastPlayedProperty);
        if ((DateTime.UtcNow - last).TotalMilliseconds < 500) return;   // Loaded + Appearing + IsVisible often arrive together
        layout.SetValue(LastPlayedProperty, DateTime.UtcNow);

        var items = layout.Children.OfType<VisualElement>().Where(v => v.IsVisible).Take(MaxAnimated).ToList();
        // each block animates as one GPU layer: fading a whole card tree without a layer redraws every child on every frame
        foreach (var v in items) { v.CancelAnimations(); Layer(v, true); v.Opacity = 0; v.TranslationY = 18; }
        var tasks = new List<Task>();
        for (var i = 0; i < items.Count; i++)
        {
            var v = items[i];
            var delay = 30 + i * 40;
            tasks.Add(Task.Run(async () =>
            {
                await Task.Delay(delay);
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (!Motion.IsAlive(v)) { v.Opacity = 1; v.TranslationY = 0; return; }
                    await Task.WhenAll(v.FadeToAsync(1, 200, Easing.CubicOut), v.TranslateToAsync(0, 0, 280, Easing.CubicOut));
                });
            }));
        }
        try { await Task.WhenAll(tasks); }
        finally { foreach (var v in items) { v.Opacity = 1; v.TranslationY = 0; Layer(v, false); } }
    }

    private static void Layer(VisualElement v, bool on)
    {
#if ANDROID
        try
        {
            if (v.Handler?.PlatformView is Android.Views.View native)
                native.SetLayerType(on ? Android.Views.LayerType.Hardware : Android.Views.LayerType.None, null);
        }
        catch { /* a layer is only an optimisation */ }
#endif
    }
}

/// <summary>
/// Animated numbers: <c>controls:Count.To="{Binding Score}"</c> on a Label rolls the number from its previous value to the new one
/// (optionally through <c>controls:Count.Format="+{0} XP"</c>) and gives it a tiny settle-pop at the end.
/// </summary>
public static class Count
{
    public static readonly BindableProperty ToProperty = BindableProperty.CreateAttached("To", typeof(int), typeof(Count), 0, propertyChanged: OnChanged);
    public static int GetTo(BindableObject b) => (int)b.GetValue(ToProperty);
    public static void SetTo(BindableObject b, int value) => b.SetValue(ToProperty, value);

    public static readonly BindableProperty FormatProperty = BindableProperty.CreateAttached("Format", typeof(string), typeof(Count), "{0}", propertyChanged: (b, _, _) => Show(b, GetTo(b)));
    public static string GetFormat(BindableObject b) => (string)b.GetValue(FormatProperty);
    public static void SetFormat(BindableObject b, string value) => b.SetValue(FormatProperty, value);

    private static void Show(BindableObject b, int value)
    {
        if (b is Label l) l.Text = string.Format(GetFormat(b) ?? "{0}", value);
    }

    private static void OnChanged(BindableObject b, object? oldValue, object? newValue)
    {
        if (b is not Label label) return;
        var from = oldValue is int o ? o : 0;
        var to = newValue is int n ? n : 0;
        if (Motion.Reduced || !Motion.IsAlive(label) || from == to) { Show(b, to); return; }

        label.AbortAnimation("count");
        var distance = Math.Abs(to - from);
        var ms = (uint)Math.Clamp(280 + distance * 0.9, 320, 1000);
        new Animation(v => Show(b, (int)Math.Round(v)), from, to, Easing.CubicOut)
            .Commit(label, "count", 16, ms, finished: (last, cancelled) =>
            {
                Show(b, to);
                if (!cancelled && distance >= 10) _ = Motion.PopAsync(label, 1.06);
            });
    }
}

/// <summary><c>controls:Smooth.Progress="{Binding X}"</c> on a ProgressBar: the bar glides to its value instead of jumping.</summary>
public static class Smooth
{
    public static readonly BindableProperty ProgressProperty = BindableProperty.CreateAttached("Progress", typeof(double), typeof(Smooth), 0d, propertyChanged: OnChanged);
    public static double GetProgress(BindableObject b) => (double)b.GetValue(ProgressProperty);
    public static void SetProgress(BindableObject b, double value) => b.SetValue(ProgressProperty, value);

    private static void OnChanged(BindableObject b, object? oldValue, object? newValue)
    {
        if (b is not ProgressBar bar) return;
        var to = Math.Clamp(newValue is double d ? d : 0, 0, 1);
        if (Motion.Reduced || !Motion.IsAlive(bar)) { bar.Progress = to; return; }
        _ = bar.ProgressTo(to, 620, Easing.CubicOut);
    }
}
