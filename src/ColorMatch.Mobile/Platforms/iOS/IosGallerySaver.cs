using ColorMatch.Mobile.Services;
using UIKit;

namespace ColorMatch.Mobile.Platforms.IosImpl;

/// <summary>iOS implementation (compiled only when building on macOS; not verified in this environment).</summary>
public sealed class IosGallerySaver : IGallerySaver
{
    public Task<string> SaveAsync(string jpegPath, string displayName)
    {
        var tcs = new TaskCompletionSource<string>();
        var image = UIImage.FromFile(jpegPath);
        if (image is null) { tcs.SetException(new InvalidOperationException("Görsel okunamadı.")); return tcs.Task; }
        image.SaveToPhotosAlbum((_, error) =>
        {
            if (error is null) tcs.SetResult("Fotoğraflar");
            else tcs.SetException(new InvalidOperationException(error.LocalizedDescription));
        });
        return tcs.Task;
    }
}
