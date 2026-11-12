using ColorMatch.Application.Interfaces;

namespace ColorMatch.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string Section = "Storage";
    /// <summary>Folder for uploaded files (absolute, or relative to the application content root).</summary>
    public string RootPath { get; set; } = "storage";
}

/// <summary>Local-disk implementation of <see cref="IFileStorage"/>. Swap for blob/object storage later.</summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(string rootAbsolutePath)
    {
        _root = Path.GetFullPath(rootAbsolutePath);
        Directory.CreateDirectory(_root);
    }

    public string RootPath => _root;

    public async Task SaveAsync(string key, Stream content, CancellationToken ct)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(fs, ct);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct) => Task.FromResult(File.Exists(Resolve(key)));

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        try { File.Delete(Resolve(key)); } catch (IOException) { /* best effort */ }
        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Contains('\\') || key.Contains("..") || Path.IsPathRooted(key))
            throw new ArgumentException("Invalid storage key.", nameof(key));
        var full = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Invalid storage key.", nameof(key));
        return full;
    }
}
