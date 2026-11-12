using Android.Content;
using Android.Provider;
using ColorMatch.Mobile.Services;

namespace ColorMatch.Mobile.Platforms.AndroidImpl;

/// <summary>Writes into MediaStore (Pictures/ColorMatch). Needs no storage permission on Android 10+.</summary>
public sealed class AndroidGallerySaver : IGallerySaver
{
    public Task<string> SaveAsync(string jpegPath, string displayName)
        => Task.Run(() =>
        {
            var context = Android.App.Application.Context;
            var resolver = context.ContentResolver ?? throw new InvalidOperationException("ContentResolver yok.");

            var values = new ContentValues();
            values.Put(MediaStore.IMediaColumns.DisplayName, displayName + ".jpg");
            values.Put(MediaStore.IMediaColumns.MimeType, "image/jpeg");
            values.Put(MediaStore.IMediaColumns.RelativePath, "Pictures/ColorMatch");
            values.Put(MediaStore.IMediaColumns.IsPending, 1);

            var uri = resolver.Insert(MediaStore.Images.Media.ExternalContentUri!, values)
                      ?? throw new InvalidOperationException("Galeriye kayıt oluşturulamadı.");
            using (var output = resolver.OpenOutputStream(uri) ?? throw new InvalidOperationException("Galeri dosyası açılamadı."))
            using (var input = File.OpenRead(jpegPath))
                input.CopyTo(output);

            values.Clear();
            values.Put(MediaStore.IMediaColumns.IsPending, 0);
            resolver.Update(uri, values, null, null);
            return "Galeri › Pictures/ColorMatch";
        });
}
