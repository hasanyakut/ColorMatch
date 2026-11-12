using System.ComponentModel;
using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.ViewModels;

namespace ColorMatch.Mobile.Views;

public partial class ResultPage : ContentPage
{
    private readonly ResultViewModel _vm;
    private bool _animated;
    private bool _appeared;
    private double _trackWidth;

    public ResultPage(ResultViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.PropertyChanged += OnVmChanged;
        XpBlock.Opacity = 0; // revealed a moment after the score has settled
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _appeared = true;
        LayoutPhoto();
        // everything except the XP block slides in; the score then counts up
        AnimateScore();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // nothing may keep animating a page that has left the screen
        this.AbortAnimation("score");
        Motion.Cancel(XpBlock); Motion.Cancel(ScoreLabel); Motion.Cancel(VerdictIcon);
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ResultViewModel.Score) when _appeared:
                AnimateScore();
                break;
            case nameof(ResultViewModel.PhotoAspect):
            case nameof(ResultViewModel.RegionX):
            case nameof(ResultViewModel.RegionY):
            case nameof(ResultViewModel.RegionRadius):
            case nameof(ResultViewModel.LocalPhoto):
                LayoutPhoto();
                break;
        }
    }

    private void OnPageSizeChanged(object? sender, EventArgs e) => LayoutPhoto();

    private void OnTrackSizeChanged(object? sender, EventArgs e)
    {
        _trackWidth = Track.Width;
        if (_animated && _trackWidth > 0) Fill.WidthRequest = _trackWidth * Math.Clamp(_vm.Score, 0, 1000) / 1000.0; // keep the fill correct after a rotation / resize
    }

    /// <summary>Fits the photo to the page width (limited in height) and puts the marker where the colour was sampled.</summary>
    private void LayoutPhoto()
    {
        if (!_vm.HasPhoto || Width <= 0 || Height <= 0) return;
        var aspect = _vm.PhotoAspect <= 0 ? 1 : _vm.PhotoAspect;
        var w = Width - 40; var h = w / aspect;
        var maxH = Height * 0.30;
        if (h > maxH) { h = maxH; w = h * aspect; }
        Photo.WidthRequest = Math.Max(1, w);
        Photo.HeightRequest = Math.Max(1, h);

        var d = Math.Max(16, _vm.RegionRadius * Math.Min(w, h) * 2);
        Ring.WidthRequest = Ring.HeightRequest = d;
        Ring.TranslationX = _vm.RegionX * w - d / 2;
        Ring.TranslationY = _vm.RegionY * h - d / 2;
    }

    /// <summary>The score counts up from 0 to the real value; the progress fill moves in the same animation so the two stay in sync.</summary>
    private void AnimateScore()
    {
        var score = _vm.Score;
        if (_animated) { ScoreLabel.Text = score.ToString(); return; }
        if (score <= 0) { ScoreLabel.Text = "0"; _ = RevealXpAsync(false); return; }
        _animated = true;
        _trackWidth = Track.Width;

        new Animation(v =>
            {
                ScoreLabel.Text = ((int)Math.Round(v)).ToString();
                if (_trackWidth > 0) Fill.WidthRequest = Math.Max(0, _trackWidth * v / 1000.0);
            }, 0, score, Easing.CubicOut)
            .Commit(this, "score", 16, Motion.Reduced ? 1u : 900u, finished: (_, _) =>
            {
                ScoreLabel.Text = score.ToString();
                if (_trackWidth > 0) Fill.WidthRequest = _trackWidth * score / 1000.0;
                _ = RevealXpAsync(_vm.IsHighScore);
            });
    }

    /// <summary>XP appears shortly after the score settles. Very high scores get one gentle pop, nothing more.</summary>
    private async Task RevealXpAsync(bool celebrate)
    {
        if (celebrate)
        {
            await Task.WhenAll(Motion.PopAsync(ScoreLabel, 1.04), Motion.PopAsync(VerdictIcon, 1.2));
        }
        else
        {
            // every score settles with a small pop, high scores with a bigger one above
            await Motion.PopAsync(ScoreLabel, 1.05);
        }
        await Task.Delay(celebrate ? 120 : 180);
        
        await Motion.FadeInAsync(XpBlock, Motion.Card, 12);
        await Task.Delay(200);
        await PlayCelebrationsAsync();
    }

    private sealed record Banner(string Icon, string Title, string? Sub, Color Tint, bool Burst);

    private static readonly Color[] Confetti = [Color.FromArgb("#C6F432"), Color.FromArgb("#FF4F8B"), Color.FromArgb("#4353FF"), Color.FromArgb("#3FE0B5"), Color.FromArgb("#FFD54A"), Color.FromArgb("#7FD8FF")];

    /// <summary>What deserves a moment of its own, in the order the player earned it.</summary>
    private List<Banner> CollectBanners()
    {
        var list = new List<Banner>();
        if (_vm.Result is not { } r) return list;
        if (r.LeveledUp) list.Add(new("award", $"Seviye {r.Level}", r.TitleChanged ? $"Yeni unvan: {r.Title}" : "Seviye atladın", Color.FromArgb("#C6F432"), true));
        if (r.StreakExtended && r.CurrentStreak > 1) list.Add(new("zap", $"{r.CurrentStreak} günlük seri", "Seri devam ediyor", Color.FromArgb("#F2C94C"), false));
        if (r.Bingo is { } b)
        {
            if (b.NewLines.Count > 0) list.Add(new("grid", b.NewLines.Count > 1 ? "Bingo çizgileri!" : "Bingo çizgisi!", string.Join(", ", b.NewLines.Select(l => l.Name)), Color.FromArgb("#A55CFF"), false));
            if (b.BoardCompleted) list.Add(new("trophy", "Bingo tahtası tamamlandı", $"+{b.BoardXpAwarded} XP", Color.FromArgb("#A55CFF"), true));
        }
        if (r.AtlasUnlock is { } u)
            list.Add(u.CollectionCompleted
                ? new("palette", "Koleksiyon tamamlandı", u.CollectionName, ColorHelper.FromHex(u.Hex), true)
                : new("palette", "Atlas'a eklendi", u.Name, ColorHelper.FromHex(u.Hex), false));
        if (r.Duel is { DuelResolved: true } d && d.Duel.Outcome == "win") list.Add(new("trophy", "Düelloyu kazandın!", $"+{d.XpAwarded} XP", Color.FromArgb("#C6F432"), true));
        return list;
    }

    private async Task PlayCelebrationsAsync()
    {
        foreach (var banner in CollectBanners())
        {
            if (!Motion.IsAlive(Celebration) || !_appeared) return;
            var ink = ColorHelper.InkOn(banner.Tint);
            var view = new Border
            {
                StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 22 }, BackgroundColor = banner.Tint, Padding = new Thickness(16, 12),
                Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Opacity = 0.30f, Radius = 16, Offset = new Point(0, 6) },
                Opacity = 0, Scale = 0.88, TranslationY = -24,
                Content = new Grid
                {
                    ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 12,
                    Children =
                    {
                        new IconView { Kind = banner.Icon, Size = 28, Tint = ink, StrokeWidth = 2.2, VerticalOptions = LayoutOptions.Center },
                        new VerticalStackLayout
                        {
                            VerticalOptions = LayoutOptions.Center, Spacing = 0,
                            Children =
                            {
                                new Label { Text = banner.Title, FontFamily = "SansBold", FontSize = 17, TextColor = ink },
                                new Label { Text = banner.Sub ?? "", FontFamily = "SansRegular", FontSize = 13, TextColor = ink.WithAlpha(0.85f), IsVisible = !string.IsNullOrEmpty(banner.Sub) },
                            },
                        },
                    },
                },
            };
            Grid.SetColumn((View)((Grid)view.Content).Children[1], 1);
            SemanticProperties.SetDescription(view, banner.Title + (banner.Sub is null ? "" : ". " + banner.Sub));
            Celebration.Add(view);

            Motion.Haptic();
            if (Motion.Reduced) { view.Opacity = 1; view.Scale = 1; view.TranslationY = 0; }
            else
            {
                if (banner.Burst) _ = BurstAsync();
                await Task.WhenAll(view.FadeToAsync(1, 200), view.TranslateToAsync(0, 0, 380, Easing.SpringOut), view.ScaleToAsync(1, 380, Easing.SpringOut));
            }
            await Task.Delay(1900);
            if (Motion.IsAlive(view) && !Motion.Reduced) await view.FadeToAsync(0, 220);
            Celebration.Remove(view);
        }
    }

    /// <summary>A short ring of coloured dots flying out from behind the banner.</summary>
    private async Task BurstAsync()
    {
        if (!Motion.IsAlive(BurstLayer)) return;
        var dots = new List<Border>();
        const int n = 14;
        for (var i = 0; i < n; i++)
        {
            var dot = new Border { WidthRequest = 9, HeightRequest = 9, StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 }, BackgroundColor = Confetti[i % Confetti.Length], Opacity = 0 };
            BurstLayer.Add(dot);
            dots.Add(dot);
        }
        var tasks = new List<Task>();
        for (var i = 0; i < n; i++)
        {
            var angle = i * 2 * Math.PI / n; var dist = 120 + (i % 3) * 28;
            var dot = dots[i];
            tasks.Add(Task.Run(async () =>
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    dot.Opacity = 1;
                    await Task.WhenAll(dot.TranslateToAsync(Math.Cos(angle) * dist, Math.Sin(angle) * dist, 650, Easing.CubicOut), dot.FadeToAsync(0, 650, Easing.CubicIn));
                });
            }));
        }
        await Task.WhenAll(tasks);
        foreach (var d in dots) BurstLayer.Remove(d);
    }
}
