using Microsoft.Maui.Controls.Shapes;

namespace ColorMatch.Mobile.Controls;

/// <summary>Round achievement medal with a caption. Locked ones are muted, unlocked ones use the soft accent.</summary>
public sealed class AchievementBadge : ContentView
{
    public static readonly BindableProperty IconProperty = BindableProperty.Create(nameof(Icon), typeof(string), typeof(AchievementBadge), "award", propertyChanged: (b, _, _) => ((AchievementBadge)b).Build());
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(AchievementBadge), "", propertyChanged: (b, _, _) => ((AchievementBadge)b).Build());
    public static readonly BindableProperty HintProperty = BindableProperty.Create(nameof(Hint), typeof(string), typeof(AchievementBadge), "", propertyChanged: (b, _, _) => ((AchievementBadge)b).Build());
    public static readonly BindableProperty IsUnlockedProperty = BindableProperty.Create(nameof(IsUnlocked), typeof(bool), typeof(AchievementBadge), false, propertyChanged: (b, _, _) => ((AchievementBadge)b).Build());

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    /// <summary>How to earn it (shown to screen readers and as the caption of a locked badge).</summary>
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public bool IsUnlocked { get => (bool)GetValue(IsUnlockedProperty); set => SetValue(IsUnlockedProperty, value); }

    public AchievementBadge()
    {
        WidthRequest = 84;
        Build();
    }

    private void Build()
    {
        var disc = new Border
        {
            WidthRequest = 60, HeightRequest = 60, StrokeShape = new RoundRectangle { CornerRadius = 30 }, StrokeThickness = IsUnlocked ? 0 : 1.5,
            HorizontalOptions = LayoutOptions.Center,
            Content = new IconView { Kind = IsUnlocked ? Icon : "lock", Size = 24, StrokeWidth = 1.9, Accent = IsUnlocked, Muted = !IsUnlocked }
        };
        if (IsUnlocked) disc.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#E1EAFD"), Color.FromArgb("#1B2A4A"));
        else
        {
            disc.BackgroundColor = Colors.Transparent;
            disc.SetAppTheme<Brush>(Border.StrokeProperty, new SolidColorBrush(Color.FromArgb("#D9D6D0")), new SolidColorBrush(Color.FromArgb("#3A3A44")));
        }

        var caption = new Label
        {
            Text = Title, FontSize = 11, HorizontalTextAlignment = TextAlignment.Center, LineHeight = 1.15, MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation,
            FontFamily = IsUnlocked ? "SansSemibold" : "SansRegular", Opacity = IsUnlocked ? 1 : 0.75
        };
        Content = new VerticalStackLayout { Spacing = 8, Children = { disc, caption } };
        SemanticProperties.SetDescription(this, IsUnlocked ? $"{Title}, kazanıldı" : $"{Title}, kilitli. {Hint}");
    }
}
