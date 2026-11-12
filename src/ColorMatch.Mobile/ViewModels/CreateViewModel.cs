using System.Collections.ObjectModel;
using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.Models;
using ColorMatch.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

namespace ColorMatch.Mobile.ViewModels;

public sealed record BackgroundSwatch(string Hex, string Name);

/// <summary>One selectable option in a horizontal chip list (template, format, swatch).</summary>
public partial class ChoiceItem<T>(T value, string title, string? subtitle = null) : ObservableObject
{
    public T Value { get; } = value;
    public string Title { get; } = title;
    public string? Subtitle { get; } = subtitle;
    [ObservableProperty] private bool isSelected;
}

/// <summary>One selected photo as a small thumbnail in the Photos tab.</summary>
public sealed record PhotoThumb(int Index, ImageSource Source);

public partial class CreateViewModel(IPostPublisher publisher, IGallerySaver gallery, ImageService images, IGameService game) : BaseViewModel
{
    private readonly List<SKBitmap> _previewBitmaps = [];
    private readonly List<string> _sourcePaths = [];
    private CancellationTokenSource? _renderCts;

    public const int MaxPhotos = 6;

    public ObservableCollection<ChoiceItem<CollageTemplate>> Templates { get; } =
        new(CollageTemplate.All.Select(t => new ChoiceItem<CollageTemplate>(t, t.Name, t.Description)));
    public ObservableCollection<ChoiceItem<CollageFormat>> Formats { get; } =
        new(CollageFormat.All.Select(f => new ChoiceItem<CollageFormat>(f, f.Name)));
    public ObservableCollection<ChoiceItem<BackgroundSwatch>> Backgrounds { get; } =
        new(new[]
        {
            new BackgroundSwatch("#F7F5F2", "Krem"), new BackgroundSwatch("#FFFFFF", "Beyaz"), new BackgroundSwatch("#1B1B1F", "Gece"),
            new BackgroundSwatch("#E86C52", "Mercan"), new BackgroundSwatch("#F4C542", "Güneş"), new BackgroundSwatch("#2BB5B0", "Turkuaz"),
            new BackgroundSwatch("#6B8CC7", "Gök"), new BackgroundSwatch("#B07CC6", "Mor"), new BackgroundSwatch("#8CA38A", "Adaçayı"),
        }.Select(s => new ChoiceItem<BackgroundSwatch>(s, s.Name)));

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPhotos)), NotifyPropertyChangedFor(nameof(NoPhotos)), NotifyPropertyChangedFor(nameof(PhotoCountText))]
    private int photoCount;
    [ObservableProperty] private ImageSource? previewSource;
    [ObservableProperty] private bool showPalette = true;
    [ObservableProperty] private string? infoMessage;
    [ObservableProperty] private bool infoIsError;
    [ObservableProperty] private double previewAspect = 9d / 16d;

    // ---- editor tabs: Layouts / Colors / Photos ----
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsLayoutsTab)), NotifyPropertyChangedFor(nameof(IsColorsTab)), NotifyPropertyChangedFor(nameof(IsPhotosTab))]
    private string selectedTab = "layouts";
    public bool IsLayoutsTab => SelectedTab == "layouts";
    public bool IsColorsTab => SelectedTab == "colors";
    public bool IsPhotosTab => SelectedTab == "photos";
    public ObservableCollection<PhotoThumb> Thumbs { get; } = [];
    public bool CanAddPhotos => PhotoCount < MaxPhotos;
    public bool CanExport => HasPhotos && !IsBusy;

    [RelayCommand] private void SelectTab(string tab) => SelectedTab = tab;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsBusy) or nameof(PhotoCount)) { OnPropertyChanged(nameof(CanExport)); OnPropertyChanged(nameof(CanAddPhotos)); }
    }

    private static ImageSource MakeThumb(SKBitmap bmp)
    {
        var scale = 180f / Math.Max(bmp.Width, bmp.Height);
        var w = Math.Max(1, (int)(bmp.Width * scale)); var h = Math.Max(1, (int)(bmp.Height * scale));
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        using (var paint = new SKPaint { IsAntialias = true }) surface.Canvas.DrawBitmap(bmp, new SKRect(0, 0, w, h), paint);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 80);
        var bytes = data.ToArray();
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    private void RebuildThumbs()
    {
        Thumbs.Clear();
        for (var i = 0; i < _previewBitmaps.Count; i++) Thumbs.Add(new PhotoThumb(i, MakeThumb(_previewBitmaps[i])));
    }

    /// <summary>Adds more photos to the collage (up to the limit) without discarding the current ones.</summary>
    [RelayCommand]
    private async Task AddPhotosAsync()
    {
        var room = MaxPhotos - _previewBitmaps.Count;
        if (room <= 0) { Say($"En fazla {MaxPhotos} fotoğraf ekleyebilirsin.", true); return; }
        InfoMessage = null;
        try
        {
            var picked = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { Title = "Fotoğraf ekle", SelectionLimit = room });
            if (picked is null || picked.Count == 0) return;
            IsBusy = true;
            foreach (var file in picked.Take(room))
            {
                var prepared = await images.PrepareAsync(file.FullPath);
                _sourcePaths.Add(prepared.Path);
                _previewBitmaps.Add(await Task.Run(() => ImageService.LoadUpright(prepared.Path, 720)));
            }
            PhotoCount = _previewBitmaps.Count;
            RebuildThumbs();
            await RenderPreviewAsync();
        }
        catch (PermissionException) { Say("Fotoğraflara erişim izni verilmedi.", true); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Say("Fotoğraflar eklenemedi.", true); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RemovePhotoAsync(PhotoThumb thumb)
    {
        if (thumb.Index < 0 || thumb.Index >= _previewBitmaps.Count) return;
        _previewBitmaps[thumb.Index].Dispose();
        _previewBitmaps.RemoveAt(thumb.Index);
        _sourcePaths.RemoveAt(thumb.Index);
        PhotoCount = _previewBitmaps.Count;
        if (PhotoCount == 0) { PreviewSource = null; Thumbs.Clear(); return; }
        RebuildThumbs();
        await RenderPreviewAsync();
    }

    private CollageTemplate _template = CollageTemplate.All[0];
    private CollageFormat _format = CollageFormat.Story;
    private Rgb _background = Rgb.FromHex("#F7F5F2");

    public bool HasPhotos => PhotoCount > 0;
    public bool NoPhotos => PhotoCount == 0;
    public string PhotoCountText => $"{PhotoCount} / {MaxPhotos} fotoğraf";

    public void Init()
    {
        if (Templates.All(t => !t.IsSelected)) Templates[0].IsSelected = true;
        if (Formats.All(t => !t.IsSelected)) Formats[0].IsSelected = true;
        if (Backgrounds.All(t => !t.IsSelected)) Backgrounds[0].IsSelected = true;
    }

    partial void OnShowPaletteChanged(bool value) => _ = RenderPreviewAsync();

    [RelayCommand]
    private async Task PickPhotosAsync()
    {
        InfoMessage = null;
        try
        {
            var picked = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { Title = "Kolaj için fotoğraflar", SelectionLimit = MaxPhotos });
            if (picked is null || picked.Count == 0) return;

            IsBusy = true;
            ClearPhotos();
            foreach (var file in picked.Take(MaxPhotos))
            {
                var prepared = await images.PrepareAsync(file.FullPath);
                _sourcePaths.Add(prepared.Path);
                _previewBitmaps.Add(await Task.Run(() => ImageService.LoadUpright(prepared.Path, 720)));
            }
            PhotoCount = _previewBitmaps.Count;
            RebuildThumbs();
            await RenderPreviewAsync();
        }
        catch (PermissionException) { Say("Fotoğraflara erişim izni verilmedi.", true); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Say("Fotoğraflar yüklenemedi.", true); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task UseTodayColorAsync()
    {
        try
        {
            var today = await game.GetHuntsAsync();
            // the medium hunt is the classic "colour of the day"
            var medium = today.Hunts.FirstOrDefault(h => h.Difficulty == "Medium") ?? today.Hunts[0];
            SetBackground(Rgb.FromHex(medium.Hex));
            foreach (var b in Backgrounds) b.IsSelected = false;
            await RenderPreviewAsync();
        }
        catch (ApiException ex) { Say(ex.Message, true); }
    }

    [RelayCommand]
    private async Task SelectTemplateAsync(ChoiceItem<CollageTemplate> item)
    {
        foreach (var t in Templates) t.IsSelected = t == item;
        _template = item.Value;
        await RenderPreviewAsync();
    }

    [RelayCommand]
    private async Task SelectFormatAsync(ChoiceItem<CollageFormat> item)
    {
        foreach (var t in Formats) t.IsSelected = t == item;
        _format = item.Value;
        PreviewAspect = _format.Width / (double)_format.Height;
        await RenderPreviewAsync();
    }

    [RelayCommand]
    private async Task SelectBackgroundAsync(ChoiceItem<BackgroundSwatch> item)
    {
        foreach (var t in Backgrounds) t.IsSelected = t == item;
        SetBackground(Rgb.FromHex(item.Value.Hex));
        await RenderPreviewAsync();
    }

    private void SetBackground(Rgb rgb) => _background = rgb;

    [RelayCommand]
    private void RemovePhotos() { ClearPhotos(); PreviewSource = null; InfoMessage = null; Thumbs.Clear(); }

    private void ClearPhotos()
    {
        foreach (var b in _previewBitmaps) b.Dispose();
        _previewBitmaps.Clear();
        _sourcePaths.Clear();
        PhotoCount = 0;
    }

    private async Task RenderPreviewAsync()
    {
        if (_previewBitmaps.Count == 0) return;
        _renderCts?.Cancel();
        var cts = _renderCts = new CancellationTokenSource();
        try
        {
            var spec = new CollageSpec(_template, _format, _background, ShowPalette, _previewBitmaps.ToList());
            var bytes = await Task.Run(() =>
            {
                using var bmp = CollageRenderer.Render(spec, 0.32f);
                using var img = SKImage.FromBitmap(bmp);
                using var data = img.Encode(SKEncodedImageFormat.Jpeg, 85);
                return data.ToArray();
            }, cts.Token);
            if (cts.IsCancellationRequested) return;
            PreviewSource = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Full-resolution render into a JPEG file in the cache.</summary>
    private async Task<string> RenderFinalAsync()
    {
        var paths = _sourcePaths.ToList();
        var template = _template; var format = _format; var bg = _background; var palette = ShowPalette;
        return await Task.Run(async () =>
        {
            var bitmaps = paths.Select(p => ImageService.LoadUpright(p, 1400)).ToList();
            try
            {
                using var bmp = CollageRenderer.Render(new CollageSpec(template, format, bg, palette, bitmaps), 1f);
                return await ImageService.SaveJpegAsync(bmp, images.NewCachePath(), 94);
            }
            finally { foreach (var b in bitmaps) b.Dispose(); }
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!HasPhotos) return;
        await RunAsync(async () =>
        {
            var file = await RenderFinalAsync();
            var where = await gallery.SaveAsync(file, $"ColorMatch_{DateTime.Now:yyyyMMdd_HHmmss}");
            Say($"Kaydedildi: {where}", false);
        }, "Kolaj kaydedilemedi.");
    }

    [RelayCommand]
    private async Task ShareAsync()
    {
        if (!HasPhotos) return;
        await RunAsync(async () =>
        {
            var file = await RenderFinalAsync();
            await Share.Default.RequestAsync(new ShareFileRequest { Title = "ColorMatch kolajı", File = new ShareFile(file, "image/jpeg") });
        }, "Kolaj paylaşılamadı.");
    }

    // the note is asked for in an in-app bottom sheet (CaptionSheet), not a native dialog
    [ObservableProperty] private bool isCaptionOpen;
    [ObservableProperty] private string captionText = "";

    [RelayCommand]
    private void Publish()
    {
        if (!HasPhotos || IsBusy) return;
        CaptionText = "";
        IsCaptionOpen = true;
    }

    [RelayCommand] private void CancelPublish() => IsCaptionOpen = false;

    [RelayCommand]
    private async Task ConfirmPublishAsync()
    {
        if (!HasPhotos) return;
        var caption = (CaptionText ?? "").Trim();
        IsCaptionOpen = false;

        await RunAsync(async () =>
        {
            var file = await RenderFinalAsync();
            var colorHex = _background.ToHex();
            await publisher.PublishCollageAsync(file, caption, colorHex);
            Say("Kolajın akışta yayınlandı!", false);
            await Shell.Current.GoToAsync("//feed");
        }, "Kolaj yayınlanamadı.");
    }

    private async Task RunAsync(Func<Task> action, string failure)
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            InfoMessage = null;
            await action();
        }
        catch (ApiException ex) { Say(ex.Message, true); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Say(failure, true); }
        finally { IsBusy = false; }
    }

    private void Say(string message, bool error) { InfoMessage = message; InfoIsError = error; }
}
