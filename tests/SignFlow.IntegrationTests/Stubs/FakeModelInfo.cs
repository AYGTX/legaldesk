using SignFlow.Application.Common.Interfaces;

namespace SignFlow.IntegrationTests.Stubs;

/// <summary>A server with a model configured; whether it is used is the organisation's choice.</summary>
internal sealed class FakeModelInfo : IModelInfo
{
    public string Provider => "groq";
    public string Model => "test-model";
    public bool IsConfigured { get; set; } = true;
}
