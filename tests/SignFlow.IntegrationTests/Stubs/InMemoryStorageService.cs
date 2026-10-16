using System.Collections.Concurrent;
using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

/// <summary>
/// Keeps uploaded bytes so a test can read back what the code under test actually wrote.
/// <see cref="NullStorageService"/> discards them, which is fine for flow tests but useless when
/// the file itself is the thing being verified.
/// </summary>
internal sealed class InMemoryStorageService : IStorageService
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new();

    public IReadOnlyDictionary<string, byte[]> Files => _files;

    public async Task<string> UploadAsync(Stream content, string fileName, string contentType, string folder, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        content.Position = 0;
        await content.CopyToAsync(ms, cancellationToken);

        var path = $"{folder}/{Guid.NewGuid():N}_{fileName}";
        _files[path] = ms.ToArray();
        return path;
    }

    public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default)
        => _files.TryGetValue(path, out var bytes)
            ? Task.FromResult<Stream>(new MemoryStream(bytes))
            : throw new FileNotFoundException($"File not found: {path}");

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        _files.TryRemove(path, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(_files.ContainsKey(path));

    public Task<string> GetSignedUrlAsync(string path, TimeSpan expiry, CancellationToken cancellationToken = default)
        => Task.FromResult(path);

    public Task<long> GetFileSizeAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult(_files.TryGetValue(path, out var bytes) ? bytes.LongLength : 0L);

    public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        if (_files.TryGetValue(sourcePath, out var bytes)) _files[destinationPath] = bytes;
        return Task.CompletedTask;
    }
}
