namespace SignFlow.UnitTests.Application;

/// <summary>
/// The contract look lives in two files because the web app and the PDF renderer ship separately. If they drift, the
/// editor preview stops matching the sealed PDF.
/// </summary>
public class ContractThemeSyncTests
{
    private const string WebCopy = "src/frontend/src/styles/contract-theme.css";
    private const string PdfCopy = "src/backend/SignFlow.Infrastructure/Services/Pdf/ContractTheme.css";

    [Fact]
    public void Web_and_pdf_copies_of_the_contract_theme_are_identical()
    {
        var root = FindRepoRoot();

        Assert.Equal(Read(root, WebCopy), Read(root, PdfCopy));
    }

    private static string Read(string root, string relative) =>
        File.ReadAllText(Path.Combine(root, relative)).Replace("\r\n", "\n").TrimEnd();

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, WebCopy)))
                return dir.FullName;

        throw new InvalidOperationException($"Could not find {WebCopy} above {AppContext.BaseDirectory}.");
    }
}
