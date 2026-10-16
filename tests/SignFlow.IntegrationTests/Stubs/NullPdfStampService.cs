using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

internal sealed class NullPdfStampService : IPdfStampService
{
    public Task<Stream> StampAsync(string sourcePath, IEnumerable<StampFieldDto> fields, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream>(new MemoryStream());
}
