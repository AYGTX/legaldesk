namespace SignFlow.Infrastructure.Services.Pdf;

/// <summary>The stylesheet every HTML contract is drawn with. The same file is loaded by the web app, so the editor
/// preview, the signing page and the sealed PDF look alike.</summary>
public static class ContractTheme
{
    private static readonly Lazy<string> css = new(() =>
    {
        using var stream = typeof(ContractTheme).Assembly.GetManifestResourceStream("ContractTheme.css")
            ?? throw new InvalidOperationException("ContractTheme.css is not embedded in the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    public static string Css => css.Value;
}
