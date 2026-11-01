namespace SignFlow.Domain.Entities;

/// <summary>
/// What the contract assistant produced for one version of a contract (M12): which feature, which provider and model,
/// which prompt version, when, the text and the quotes it rests on. Replaces the summary stored on the contract. Every
/// output is shown labelled as model-generated (R-M12-3), and only with quotes checked word for word against the text.
/// </summary>
public class ModelOutput : TenantEntity
{
    public Guid DocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    /// <summary>summary | explainer</summary>
    public string Feature { get; private set; } = string.Empty;
    public string Provider { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public string PromptVersion { get; private set; } = string.Empty;
    /// <summary>The output as JSON, in the shape of its feature.</summary>
    public string ContentJson { get; private set; } = string.Empty;
    /// <summary>True when every statement kept has a verified quote; anything that failed the check was dropped.</summary>
    public bool Verified { get; private set; }
    /// <summary>A key that tells two outputs of one feature apart (the explainer: "2-&gt;3").</summary>
    public string? Key { get; private set; }

    private ModelOutput() { }

    public static ModelOutput Create(Guid documentId, int version, string feature, string provider, string model, string promptVersion,
        string contentJson, bool verified, string? key = null) => new()
    {
        DocumentId = documentId,
        VersionNumber = version,
        Feature = feature,
        Provider = provider,
        Model = model,
        PromptVersion = promptVersion,
        ContentJson = contentJson,
        Verified = verified,
        Key = key
    };
}
