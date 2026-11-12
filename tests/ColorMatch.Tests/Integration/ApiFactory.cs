using System.Net.Http.Headers;
using System.Net.Http.Json;
using ColorMatch.Application.DTOs;
using ColorMatch.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace ColorMatch.Tests.Integration;

/// <summary>
/// Boots the real API against a throw-away SQL Server LocalDB database (real constraints, real migrations).
/// Override the server with the COLORMATCH_TEST_SQL environment variable (connection string).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "ColorMatchTests_" + Guid.NewGuid().ToString("N");
    private readonly string _storage = Path.Combine(Path.GetTempPath(), "colormatch-tests-" + Guid.NewGuid().ToString("N"));

    private string ConnectionString
    {
        get
        {
            var template = Environment.GetEnvironmentVariable("COLORMATCH_TEST_SQL")
                           ?? @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            return template.TrimEnd(';') + ";Database=" + _dbName;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Jwt:Key", "integration-test-signing-key-0123456789-abcdef");
        builder.UseSetting("Database:AutoMigrate", "true");
        builder.UseSetting("Storage:RootPath", _storage);
        builder.UseSetting("RateLimiting:AuthPerMinute", "10000");
        builder.UseSetting("RateLimiting:AttemptsPerMinute", "10000");
        builder.UseSetting("Challenge:TimeZoneId", "UTC");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                using var scope = Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeleted();
            }
            catch { /* best effort */ }
            try { if (Directory.Exists(_storage)) Directory.Delete(_storage, true); } catch { }
        }
        base.Dispose(disposing);
    }

    // ---- helpers ----
    private static int _counter;

    public async Task<(HttpClient Client, AuthResponse Auth)> RegisterAsync(string? name = null)
    {
        name ??= "user" + Interlocked.Increment(ref _counter) + "x" + Random.Shared.Next(1000, 9999);
        var client = CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest($"{name}@example.com", name, "Passw0rd!"));
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"{(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return (client, auth);
    }

    public static byte[] SolidJpeg(string hex, int size = 200)
    {
        var rgb = ColorMatch.ColorEngine.ColorConversion.Rgb.FromHex(hex);
        using var bmp = new SKBitmap(size, size);
        bmp.Erase(new SKColor(rgb.R, rgb.G, rgb.B));
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray();
    }

    public static MultipartFormDataContent HuntForm(byte[] jpeg, double x = 0.5, double y = 0.5, double r = 0.1)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "Photo", "photo.jpg");
        form.Add(new StringContent(x.ToString(System.Globalization.CultureInfo.InvariantCulture)), "RegionX");
        form.Add(new StringContent(y.ToString(System.Globalization.CultureInfo.InvariantCulture)), "RegionY");
        form.Add(new StringContent(r.ToString(System.Globalization.CultureInfo.InvariantCulture)), "RegionRadius");
        return form;
    }

    public static MultipartFormDataContent FileForm(byte[] bytes, string contentType = "image/jpeg", string name = "a.jpg")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", name);
        return form;
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFactory>;
