using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

internal sealed class NullHtmlTemplatePdfGenerator : IHtmlTemplatePdfGenerator
{
    public Task<Stream> GenerateAsync(string renderedHtmlContent, string title, List<HtmlTemplatePdfSignature> signatures, CancellationToken ct = default)
        => Task.FromResult<Stream>(new MemoryStream());
}
