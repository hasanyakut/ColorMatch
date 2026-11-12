using Microsoft.Maui.Controls.Shapes;

namespace ColorMatch.Mobile.Controls;

/// <summary>Round avatar: image when available, otherwise initials on a deterministic soft colour.</summary>
public sealed class AvatarView : Border
{
    public static readonly BindableProperty SourceUrlProperty = BindableProperty.Create(nameof(SourceUrl), typeof(string), typeof(AvatarView), null, propertyChanged: (b, _, _) => ((AvatarView)b).Rebuild());
    public static readonly BindableProperty NameProperty = BindableProperty.Create(nameof(Name), typeof(string), typeof(AvatarView), "", propertyChanged: (b, _, _) => ((AvatarView)b).Rebuild());
    public static readonly BindableProperty SizeProperty = BindableProperty.Create(nameof(Size), typeof(double), typeof(AvatarView), 40d, propertyChanged: (b, _, _) => ((AvatarView)b).Rebuild());

    public string? SourceUrl { get => (string?)GetValue(SourceUrlProperty); set => SetValue(SourceUrlProperty, value); }
    public string Name { get => (string)GetValue(NameProperty); set => SetValue(NameProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    private static readonly string[] Palette = ["#E86C52", "#F4C542", "#2BB5B0", "#6B8CC7", "#B07CC6", "#8CA38A", "#D98E5F"];

    public AvatarView()
    {
        StrokeThickness = 0;
        Padding = 0;
        HorizontalOptions = VerticalOptions = LayoutOptions.Center;
        Rebuild();
    }

    private void Rebuild()
    {
        WidthRequest = HeightRequest = Size;
        StrokeShape = new RoundRectangle { CornerRadius = (float)(Size / 2) };
        SemanticProperties.SetDescription(this, $"{Name} profil fotoğrafı");

        if (!string.IsNullOrEmpty(SourceUrl))
        {
            BackgroundColor = Colors.Transparent;
            Content = new Image { Source = ImageHelper.From(SourceUrl), Aspect = Aspect.AspectFill, WidthRequest = Size, HeightRequest = Size };
            return;
        }

        var name = string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim();
        var color = Color.FromArgb(Palette[Math.Abs(name.GetHashCode()) % Palette.Length]);
        BackgroundColor = color;
        Content = new Label
        {
            Text = name[..1].ToUpperInvariant(),
            TextColor = ColorHelper.InkOn(color),
            FontFamily = "SansSemibold",
            FontSize = Size * 0.42,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
    }
}
