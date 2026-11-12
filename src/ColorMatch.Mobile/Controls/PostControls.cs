using Microsoft.Maui.Controls.Shapes;

namespace ColorMatch.Mobile.Controls;

/// <summary>Photo tile: rounded image with an optional colour chip (HEX) and a "collage" marker.</summary>
public sealed class PhotoCard : Border
{
    public static readonly BindableProperty ImageUrlProperty = BindableProperty.Create(nameof(ImageUrl), typeof(string), typeof(PhotoCard), null, propertyChanged: (b, _, _) => ((PhotoCard)b).Refresh());
    public static readonly BindableProperty HexProperty = BindableProperty.Create(nameof(Hex), typeof(string), typeof(PhotoCard), null, propertyChanged: (b, _, _) => ((PhotoCard)b).Refresh());
    public static readonly BindableProperty ShowChipProperty = BindableProperty.Create(nameof(ShowChip), typeof(bool), typeof(PhotoCard), true, propertyChanged: (b, _, _) => ((PhotoCard)b).Refresh());
    public static readonly BindableProperty IsCollageProperty = BindableProperty.Create(nameof(IsCollage), typeof(bool), typeof(PhotoCard), false, propertyChanged: (b, _, _) => ((PhotoCard)b).Refresh());
    public static readonly BindableProperty RadiusProperty = BindableProperty.Create(nameof(Radius), typeof(double), typeof(PhotoCard), 18d, propertyChanged: (b, _, v) => ((PhotoCard)b).StrokeShape = new RoundRectangle { CornerRadius = (float)(double)v });

    public string? ImageUrl { get => (string?)GetValue(ImageUrlProperty); set => SetValue(ImageUrlProperty, value); }
    public string? Hex { get => (string?)GetValue(HexProperty); set => SetValue(HexProperty, value); }
    public bool ShowChip { get => (bool)GetValue(ShowChipProperty); set => SetValue(ShowChipProperty, value); }
    public bool IsCollage { get => (bool)GetValue(IsCollageProperty); set => SetValue(IsCollageProperty, value); }
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }

    private readonly Image _image = new() { Aspect = Aspect.AspectFill };
    private readonly Grid _overlay = new() { Padding = 10, InputTransparent = true };

    public PhotoCard()
    {
        StrokeThickness = 0;
        Padding = 0;
        StrokeShape = new RoundRectangle { CornerRadius = 18 };
        this.SetAppThemeColor(BackgroundColorProperty, Color.FromArgb("#EEE9E1"), Color.FromArgb("#28241F"));
        Content = new Grid { Children = { _image, _overlay } };
    }

    private void Refresh()
    {
        _image.Source = string.IsNullOrEmpty(ImageUrl) ? null : ImageHelper.From(ImageUrl);
        // loading / missing image: show the post's own colour instead of an empty grey tile
        if (!string.IsNullOrEmpty(Hex)) BackgroundColor = ColorHelper.FromHex(Hex).WithAlpha(0.5f);
        _overlay.Children.Clear();

        if (ShowChip && !string.IsNullOrEmpty(Hex))
        {
            var chip = new Border
            {
                StrokeThickness = 0, Padding = new Thickness(8, 4, 10, 4), StrokeShape = new RoundRectangle { CornerRadius = 10 },
                HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.End,
                Content = new HorizontalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        new Border { WidthRequest = 12, HeightRequest = 12, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 6 }, BackgroundColor = ColorHelper.FromHex(Hex), VerticalOptions = LayoutOptions.Center },
                        new Label { Text = Hex, FontFamily = "SansSemibold", FontSize = 11, CharacterSpacing = 0.8, VerticalOptions = LayoutOptions.Center, LineHeight = 1 }
                    }
                }
            };
            chip.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#1D1A17"));
            _overlay.Children.Add(chip);
        }

        if (IsCollage)
        {
            var mark = new Border
            {
                WidthRequest = 28, HeightRequest = 28, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 14 },
                HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start,
                Content = new IconView { Kind = "layers", Size = 15, StrokeWidth = 2 }
            };
            mark.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#1D1A17"));
            SemanticProperties.SetDescription(mark, "Kolaj");
            _overlay.Children.Add(mark);
        }
    }
}

/// <summary>
/// Two-column staggered grid (Pinterest-style) for discovery. Tiles get varied heights from a stable per-post ratio,
/// and each new tile goes into the currently shorter column. Raises <see cref="LoadMoreCommand"/> near the bottom.
/// </summary>
public sealed class MasonryView : ScrollView
{
    public static readonly BindableProperty ItemsProperty = BindableProperty.Create(nameof(Items), typeof(System.Collections.IEnumerable), typeof(MasonryView), null, propertyChanged: (b, o, n) => ((MasonryView)b).OnItemsChanged(o, n));
    public static readonly BindableProperty ItemCommandProperty = BindableProperty.Create(nameof(ItemCommand), typeof(System.Windows.Input.ICommand), typeof(MasonryView), null);
    public static readonly BindableProperty LoadMoreCommandProperty = BindableProperty.Create(nameof(LoadMoreCommand), typeof(System.Windows.Input.ICommand), typeof(MasonryView), null);

    public System.Collections.IEnumerable? Items { get => (System.Collections.IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public System.Windows.Input.ICommand? ItemCommand { get => (System.Windows.Input.ICommand?)GetValue(ItemCommandProperty); set => SetValue(ItemCommandProperty, value); }
    public System.Windows.Input.ICommand? LoadMoreCommand { get => (System.Windows.Input.ICommand?)GetValue(LoadMoreCommandProperty); set => SetValue(LoadMoreCommandProperty, value); }

    public static readonly BindableProperty HeaderProperty = BindableProperty.Create(nameof(Header), typeof(View), typeof(MasonryView), null, propertyChanged: (b, _, v) => ((MasonryView)b)._header.Content = v as View);
    /// <summary>Optional view shown above the two columns, scrolling with them.</summary>
    public View? Header { get => (View?)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    private readonly ContentView _header = new();

    private const double Gap = 12, Side = 20;
    private static readonly double[] Ratios = [1.25, 1.0, 1.5, 1.15, 1.4, 0.95];

    private readonly VerticalStackLayout _left = new() { Spacing = 14 };
    private readonly VerticalStackLayout _right = new() { Spacing = 14 };
    private double _leftHeight, _rightHeight, _tileWidth;

    public MasonryView()
    {
        _header.Padding = new Thickness(Side, 0, 0, 0);
        var columns = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = Gap, Padding = new Thickness(Side, 4, Side, 112),
            Children = { _left, _right }
        };
        Grid.SetColumn(_right, 1);
        Content = new VerticalStackLayout { Children = { _header, columns } };
        SizeChanged += (_, _) =>
        {
            var w = (Width - 2 * Side - Gap) / 2;
            if (w > 0 && Math.Abs(w - _tileWidth) > 1) { _tileWidth = w; Rebuild(); }
        };
        Scrolled += (_, e) =>
        {
            if (ContentSize.Height > 0 && e.ScrollY + Height >= ContentSize.Height - 500 && LoadMoreCommand?.CanExecute(null) == true)
                LoadMoreCommand.Execute(null);
        };
    }

    private void OnItemsChanged(object? oldValue, object? newValue)
    {
        if (oldValue is System.Collections.Specialized.INotifyCollectionChanged o) o.CollectionChanged -= OnCollectionChanged;
        if (newValue is System.Collections.Specialized.INotifyCollectionChanged n) n.CollectionChanged += OnCollectionChanged;
        Rebuild();
    }

    private void OnCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewStartingIndex >= Count() - e.NewItems!.Count)
        {
            foreach (var item in e.NewItems) AddTile(item);   // pagination: append only
            return;
        }
        Rebuild();
    }

    private int Count() { var n = 0; if (Items is not null) foreach (var _ in Items) n++; return n; }

    private void Rebuild()
    {
        _left.Children.Clear(); _right.Children.Clear();
        _leftHeight = _rightHeight = 0;
        if (Items is null || _tileWidth <= 0) return;
        foreach (var item in Items) AddTile(item);
    }

    private void AddTile(object item)
    {
        if (_tileWidth <= 0 || item is not ViewModels.PostItemViewModel post) return;

        var ratio = Ratios[(post.Id.GetHashCode() & 0x7fffffff) % Ratios.Length];
        var photo = new PhotoCard { ImageUrl = post.ImageUrl, Hex = post.ColorHex, IsCollage = post.IsCollage, ShowChip = true, Radius = 18, HeightRequest = _tileWidth * ratio };
        // footer: who (left) and how liked (right), then one line of the note if there is one
        var info = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 6, Margin = new Thickness(2, 0) };
        info.Add(new AvatarView { SourceUrl = post.AuthorAvatar, Name = post.AuthorName, Size = 20 }, 0, 0);
        info.Add(new Label { Text = post.AuthorName, FontSize = 12, FontFamily = "SansSemibold", VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation }, 1, 0);
        var likes = new HorizontalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };
        likes.Add(new IconView { Kind = "heart", Size = 13, Muted = !post.LikedByMe, Accent = post.LikedByMe, Filled = post.LikedByMe, StrokeWidth = 2 });
        likes.Add(new Label { Text = post.LikeText, FontSize = 12, VerticalOptions = LayoutOptions.Center, Style = Application.Current?.Resources.TryGetValue("Muted", out var muted) == true ? muted as Style : null });
        info.Add(likes, 2, 0);

        var tile = new VerticalStackLayout { Spacing = 6, Children = { photo, info } };
        if (post.HasCaption)
            tile.Add(new Label { Text = post.Caption, FontSize = 12, MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation, Margin = new Thickness(2, 0), LineHeight = 1.25 });
        SemanticProperties.SetDescription(tile, $"{post.AuthorName} gönderisi");
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => { if (ItemCommand?.CanExecute(post) == true) ItemCommand.Execute(post); };
        tile.GestureRecognizers.Add(tap);

        var h = _tileWidth * ratio + (post.HasCaption ? 70 : 34);
        if (_leftHeight <= _rightHeight) { _left.Children.Add(tile); _leftHeight += h; }
        else { _right.Children.Add(tile); _rightHeight += h; }
    }
}
