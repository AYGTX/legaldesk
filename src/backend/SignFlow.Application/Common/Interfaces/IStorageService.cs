namespace SignFlow.Application.Common.Interfaces;

public interface IStorageService
{
    Task<string> UploadAsync(Stream content, string fileName, string contentType, string folder, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string path, CancellationToken cancellationToken = default);
    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);
    Task<string> GetSignedUrlAsync(string path, TimeSpan expiry, CancellationToken cancellationToken = default);
    Task<long> GetFileSizeAsync(string path, CancellationToken cancellationToken = default);
    Task CopyAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default);
}
