namespace ColorMatch.Mobile.Controls;

/// <summary>
/// ColorMatch motion system. One place for durations, easing, reduced-motion handling and the few reusable
/// animations the app uses. Rules: short, ease-out, scale/opacity first, no bounce, nothing runs forever.
/// </summary>
public static class Motion
{
    // ---- durations (ms) ----
    /// <summary>Press / tap feedback.</summary>
    public const uint Press = 110;
    /// <summary>Small state changes: a chip filling, a chevron turning, a heart appearing.</summary>
    public const uint Quick = 180;
    /// <summary>Navigation indicator (bottom bar highlight) and tab content hand-over.</summary>
    public const uint Nav = 220;
    /// <summary>Content appearing inside a screen.</summary>
    public const uint Content = 260;
    /// <summary>A card / block appearing.</summary>
    public const uint Card = 280;
    /// <summary>Success and reward moments.</summary>
    public const uint Reward = 380;

    public static Easing Out => Easing.CubicOut;
    public static Easing InOut => Easing.CubicInOut;
    public static Easing In => Easing.CubicIn;

    private static DateTime _checkedAt = DateTime.MinValue;
    private static bool _reduced;

    /// <summary>
    /// True when the user turned system animations off ("Remove animations" / animator scale 0). Every helper then jumps to the
    /// final state instead of animating. MAUI's own ticker also honours this switch; the check keeps our manual sequences consistent.
    /// </summary>
    public static bool Reduced
    {
        get
        {
            if ((DateTime.UtcNow - _checkedAt).TotalSeconds < 2) return _reduced;
            _checkedAt = DateTime.UtcNow;
#if ANDROID
            try
            {
                var scale = Android.Provider.Settings.Global.GetFloat(Android.App.Application.Context.ContentResolver, Android.Provider.Settings.Global.AnimatorDurationScale, 1f);
                _reduced = scale <= 0f;
            }
            catch { _reduced = false; }
#else
            _reduced = false;
#endif
            return _reduced;
        }
    }

    /// <summary>An element that left the screen (handler gone) must not be animated: the call would throw or leak.</summary>
    public static bool IsAlive(VisualElement? v) => v?.Handler is not null;

    public static void Cancel(VisualElement? v) { if (v is not null) v.CancelAnimations(); }

    /// <summary>Light haptic tick for taps that change something (like, tab switch, shutter). Silent when unsupported.</summary>
    public static void Haptic()
    {
        try { if (HapticFeedback.Default.IsSupported) HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
        catch { /* haptics are a nicety */ }
    }

    /// <summary>Fades (and optionally rises) one block into place. The only "entrance" animation used for screen content.</summary>
    public static async Task FadeInAsync(VisualElement v, uint ms = Content, double fromY = 12)
    {
        if (!IsAlive(v)) return;
        if (Reduced) { v.Opacity = 1; v.TranslationY = 0; return; }
        v.CancelAnimations();
        v.Opacity = 0; v.TranslationY = fromY;
        await Task.WhenAll(v.FadeToAsync(1, ms, Out), v.TranslateToAsync(0, 0, ms, Out));
    }

    /// <summary>Press feedback for things that are tapped: dips to ~94% and returns. Total ≈ <see cref="Press"/> × 2.</summary>
    public static async Task TapAsync(VisualElement v, double to = 0.94)
    {
        if (!IsAlive(v) || Reduced) return;
        v.CancelAnimations();
        await v.ScaleToAsync(to, 80, Out);
        if (!IsAlive(v)) return;
        await v.ScaleToAsync(1, 260, Easing.SpringOut);   // a small bounce back: the thing feels pressed, not just dimmed
    }

    /// <summary>Quick confirmation scale (liked heart, selected template): up a little, back. No spring.</summary>
    public static async Task PopAsync(VisualElement v, double peak = 1.12)
    {
        if (!IsAlive(v) || Reduced) return;
        v.CancelAnimations();
        await v.ScaleToAsync(peak, Press + 30, Out);
        if (!IsAlive(v)) return;
        await v.ScaleToAsync(1, Quick, Out);
    }

    /// <summary>Counts an integer label up from 0 (data reveal, not decoration).</summary>
    public static void CountUp(Label label, int to, string format = "{0}", uint durationMs = 800)
    {
        if (to <= 0 || Reduced) { label.Text = string.Format(format, to); return; }
        label.AbortAnimation("countup");
        new Animation(v => label.Text = string.Format(format, (int)Math.Round(v)), 0, to, Easing.CubicOut)
            .Commit(label, "countup", 16, durationMs, finished: (_, _) => label.Text = string.Format(format, to));
    }
}

/// <summary>
/// Attached "tap feedback": <c>controls:Interact.TapFeedback="True"</c> on a tappable Border / Grid / layout makes it dip when tapped
/// (and tick on supported devices). It adds its own gesture recogniser, so the element's real command keeps working unchanged.
/// </summary>
public static class Interact
{
    public static readonly BindableProperty TapFeedbackProperty = BindableProperty.CreateAttached("TapFeedback", typeof(bool), typeof(Interact), false, propertyChanged: OnChanged);
    public static bool GetTapFeedback(BindableObject b) => (bool)b.GetValue(TapFeedbackProperty);
    public static void SetTapFeedback(BindableObject b, bool value) => b.SetValue(TapFeedbackProperty, value);

    private static void OnChanged(BindableObject b, object? oldValue, object? newValue)
    {
        if (b is not View view || newValue is not true) return;
        if (view.GestureRecognizers.Any(g => g is TapGestureRecognizer { ClassId: "tapfeedback" })) return;
        var tap = new TapGestureRecognizer { ClassId = "tapfeedback" };
        tap.Tapped += async (_, _) => await Motion.TapAsync(view);
        view.GestureRecognizers.Add(tap);
    }
}
