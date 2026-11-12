namespace ColorMatch.Mobile.Controls;

/// <summary>
/// A real frosted-glass backdrop for the bottom bar. MAUI has no cross-platform "blur what is behind me" view, so on Android this
/// draws the page content that lies under the bar into a small bitmap, blurs it and shows it at the bottom of the bar.
/// <para>It is built not to cost frames:</para>
/// <list type="bullet">
/// <item>it only works when something under the bar may have changed (the page scrolled, was laid out, or just appeared) — an idle
/// screen costs nothing;</item>
/// <item>only the drawing of the snapshot happens on the UI thread; the blur runs on a worker thread;</item>
/// <item>the result goes straight into the native image view as a bitmap (no PNG encode / decode);</item>
/// <item>the bar itself is never hidden for the snapshot: its siblings are drawn instead, so taking a snapshot does not invalidate the bar.</item>
/// </list>
/// Other platforms keep the plain translucent bar.
/// </summary>
public sealed class BackdropBlur
{
    private readonly Image _target;
    private readonly VisualElement _glass;
    private readonly VisualElement _bar;
    private readonly Page _page;
    private readonly Func<Color> _background;
    private IDispatcherTimer? _timer;

    /// <summary>Raised on the UI thread after the first snapshot is shown (the bar can then lighten its tint).</summary>
    public event Action? FirstFrame;
    private bool _first = true;

    public BackdropBlur(Image target, VisualElement glass, VisualElement bar, Page page, Func<Color> background)
    {
        _target = target; _glass = glass; _bar = bar; _page = page; _background = background;
    }

#if ANDROID
    private const float Scale = 0.18f;  // snapshot size relative to the bar: plenty for a blur
    private const int Radius = 4;       // box radius at snapshot size, three passes ≈ a Gaussian

    private bool _dirty = true, _busy;
    private int _settle;                // a few extra snapshots after activity stops, so the last frame is the resting one
    private Android.Views.ViewTreeObserver? _observer;
    private EventHandler? _scrollHandler, _layoutHandler;
    private Android.Graphics.Bitmap? _shown;
#endif

    public void Start()
    {
#if ANDROID
        if (_timer is not null) return;
        _dirty = true; _settle = 6;   // ~1 s of snapshots while the page settles in
        HookObserver();
        _timer = _glass.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(130);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
#endif
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
#if ANDROID
        UnhookObserver();
#endif
    }

#if ANDROID
    private void HookObserver()
    {
        UnhookObserver();
        if (_page.Handler?.PlatformView is not Android.Views.View root) return;
        _observer = root.ViewTreeObserver;
        if (_observer is null || !_observer.IsAlive) { _observer = null; return; }
        _scrollHandler = (_, _) => { _dirty = true; _settle = 2; };
        _layoutHandler = (_, _) => { _dirty = true; _settle = 2; };
        _observer.ScrollChanged += _scrollHandler;
        _observer.GlobalLayout += _layoutHandler;
    }

    private void UnhookObserver()
    {
        try
        {
            if (_observer is { IsAlive: true })
            {
                if (_scrollHandler is not null) _observer.ScrollChanged -= _scrollHandler;
                if (_layoutHandler is not null) _observer.GlobalLayout -= _layoutHandler;
            }
        }
        catch { /* the view tree may already be gone */ }
        _observer = null; _scrollHandler = null; _layoutHandler = null;
    }

    private void Tick()
    {
        if (_busy) return;
        if (!_dirty)
        {
            if (_settle <= 0) return;      // nothing moved: do nothing at all
            _settle--;
        }
        _dirty = false;
        Capture();
    }

    private void Capture()
    {
        try
        {
            if (!Motion.IsAlive(_glass) || _page.Handler?.PlatformView is not Android.Views.View) return;
            if (_glass.Handler?.PlatformView is not Android.Views.View glass || glass.Width <= 0 || glass.Height <= 0 || !glass.IsShown) return;
            if (_bar.Handler?.PlatformView is not Android.Views.View bar || bar.Parent is not Android.Views.ViewGroup host) return;

            var gl = new int[2];
            glass.GetLocationInWindow(gl);
            var w = Math.Max(8, (int)(glass.Width * Scale));
            var h = Math.Max(8, (int)(glass.Height * Scale));

            var bmp = Android.Graphics.Bitmap.CreateBitmap(w, h, Android.Graphics.Bitmap.Config.Argb8888!)!;
            var bg = _background();
            bmp.EraseColor(Android.Graphics.Color.Argb(255, (int)(bg.Red * 255), (int)(bg.Green * 255), (int)(bg.Blue * 255)));
            using (var canvas = new Android.Graphics.Canvas(bmp))
            {
                canvas.Scale(Scale, Scale);
                // everything that shares the bar's container, except the bar: that is what lies under it
                var loc = new int[2];
                for (var i = 0; i < host.ChildCount; i++)
                {
                    var child = host.GetChildAt(i);
                    if (child is null || ReferenceEquals(child, bar) || child.Visibility != Android.Views.ViewStates.Visible) continue;
                    child.GetLocationInWindow(loc);
                    // skip siblings that do not reach under the bar
                    if (loc[1] + child.Height <= gl[1] || loc[1] >= gl[1] + glass.Height) continue;
                    var save = canvas.Save();
                    canvas.Translate(loc[0] - gl[0], loc[1] - gl[1]);
                    canvas.ClipRect(gl[0] - loc[0], gl[1] - loc[1], gl[0] - loc[0] + glass.Width, gl[1] - loc[1] + glass.Height);
                    child.Draw(canvas);
                    canvas.RestoreToCount(save);
                }
            }

            _busy = true;
            Task.Run(() =>
            {
                try
                {
                    var px = new int[w * h];
                    bmp.GetPixels(px, 0, w, 0, 0, w, h);
                    for (var pass = 0; pass < 3; pass++) { BoxBlur(px, w, h, Radius, horizontal: true); BoxBlur(px, w, h, Radius, horizontal: false); }
                    bmp.SetPixels(px, 0, w, 0, 0, w, h);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BackdropBlur worker: " + ex.Message); }

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        if (_target.Handler?.PlatformView is Android.Widget.ImageView view)
                        {
                            view.SetScaleType(Android.Widget.ImageView.ScaleType.FitXy);
                            view.SetImageBitmap(bmp);
                            var old = _shown; _shown = bmp;
                            old?.Recycle(); old?.Dispose();
                            if (_first) { _first = false; FirstFrame?.Invoke(); }
                        }
                        else { bmp.Recycle(); bmp.Dispose(); }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine("BackdropBlur show: " + ex.Message); }
                    finally { _busy = false; }
                });
            });
        }
        catch (Exception ex) { _busy = false; System.Diagnostics.Debug.WriteLine("BackdropBlur: " + ex.Message); }
    }

    /// <summary>One separable box-blur pass over opaque ARGB pixels (edges clamp).</summary>
    private static void BoxBlur(int[] px, int w, int h, int r, bool horizontal)
    {
        var lines = horizontal ? h : w;
        var len = horizontal ? w : h;
        var buf = new int[len];
        var win = 2 * r + 1;
        for (var l = 0; l < lines; l++)
        {
            int Idx(int i) => horizontal ? l * w + Math.Clamp(i, 0, len - 1) : Math.Clamp(i, 0, len - 1) * w + l;
            int sr = 0, sg = 0, sb = 0;
            for (var i = -r; i <= r; i++) { var c = px[Idx(i)]; sr += (c >> 16) & 255; sg += (c >> 8) & 255; sb += c & 255; }
            for (var i = 0; i < len; i++)
            {
                buf[i] = unchecked((int)0xFF000000) | ((sr / win) << 16) | ((sg / win) << 8) | (sb / win);
                var add = px[Idx(i + r + 1)]; var sub = px[Idx(i - r)];
                sr += ((add >> 16) & 255) - ((sub >> 16) & 255);
                sg += ((add >> 8) & 255) - ((sub >> 8) & 255);
                sb += (add & 255) - (sub & 255);
            }
            for (var i = 0; i < len; i++) px[horizontal ? l * w + i : i * w + l] = buf[i];
        }
    }
#endif
}
