using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

internal sealed class NullStorageService : IStorageService
{
    public Task<string> UploadAsync(Stream content, string fileName, string contentType, string folder, CancellationToken cancellationToken = default)
        => Task.FromResult($"{folder}/{fileName}");

    public Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream>(new MemoryStream());

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(true);
    public Task<string> GetSignedUrlAsync(string path, TimeSpan expiry, CancellationToken cancellationToken = default) => Task.FromResult(path);
    public Task<long> GetFileSizeAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(1024L);
    public Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
