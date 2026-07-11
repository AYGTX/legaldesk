using Microsoft.Extensions.Options;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Infrastructure.Extensions;

namespace SignFlow.Infrastructure.Services.Storage;

public class LocalStorageService(IOptions<StorageSettings> settings) : IStorageService
{
    private readonly string _basePath = settings.Value.LocalPath;

    public async Task<string> UploadAsync(Stream content, string fileName, string contentType, string folder, CancellationToken cancellationToken = default)
    {
        var safeFileName = Path.GetFileNameWithoutExtension(fileName) + "_" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(fileName);
        var relativePath = Path.Combine(folder, safeFileName).Replace('\\', '/');
        var fullPath = Path.Combine(_basePath, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return relativePath;
    }

    public async Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_basePath, path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {path}");

        var ms = new MemoryStream();
        await using var fs = File.OpenRead(fullPath);
        await fs.CopyToAsync(ms, cancellationToken);
        ms.Position = 0;
        return ms;
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_basePath, path);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_basePath, path);
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task<string> GetSignedUrlAsync(string path, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        // Local storage returns direct path — in production use HMAC-signed token
        return Task.FromResult($"/api/v1/files/{path.Replace('/', '_')}");
    }

    public Task<long> GetFileSizeAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_basePath, path);
        var info = new FileInfo(fullPath);
        return Task.FromResult(info.Length);
    }

    public async Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
    {
        var src = Path.Combine(_basePath, sourcePath);
        var dst = Path.Combine(_basePath, destinationPath);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        await using var srcStream = File.OpenRead(src);
        await using var dstStream = File.Create(dst);
        await srcStream.CopyToAsync(dstStream, cancellationToken);
    }
}
