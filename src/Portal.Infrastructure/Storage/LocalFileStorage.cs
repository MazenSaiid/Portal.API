using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Portal.Application.Common.Interfaces;

namespace Portal.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Absolute, or relative to the content root. Keep it outside wwwroot so files are never served directly.</summary>
    public string RootPath { get; init; } = "App_Data/uploads";
}

/// <summary>Stores files on the local disk under random names. Swap for blob storage in the cloud.</summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<StorageOptions> options, IHostEnvironment environment)
    {
        _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.RootPath));
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        var key = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        await using var file = new FileStream(PathFor(key), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, ct);
        return key;
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        Stream? stream = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>Keys are generated here, but still refuse anything that could escape the root folder.</summary>
    private string PathFor(string key)
    {
        if (key != Path.GetFileName(key) || key.Contains(".."))
            throw new ArgumentException("Invalid storage key.", nameof(key));
        return Path.Combine(_root, key);
    }
}
