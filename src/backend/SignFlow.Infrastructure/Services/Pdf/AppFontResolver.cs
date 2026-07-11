using PdfSharp.Fonts;

namespace SignFlow.Infrastructure.Services.Pdf;

/// <summary>
/// PDFsharp's cross-platform core build ships with no fonts and no OS font lookup — it throws unless
/// <see cref="GlobalFontSettings.FontResolver"/> is assigned (see
/// https://docs.pdfsharp.net/link/font-resolving.html). This resolves the single family PDFsharp is
/// asked for in this app ("Arial", used by <see cref="PdfStampService"/>) to a real TTF file, trying
/// Windows' font folder first and falling back to the common Linux metric-compatible fonts so the same
/// code works in the Docker image described in docker-compose.yml.
/// </summary>
public class AppFontResolver : IFontResolver
{
    public const string FamilyName = "Arial";

    private static readonly (string Regular, string Bold, string Italic, string BoldItalic)[] CandidateSets =
    [
        // Windows
        (@"C:\Windows\Fonts\arial.ttf", @"C:\Windows\Fonts\arialbd.ttf", @"C:\Windows\Fonts\ariali.ttf", @"C:\Windows\Fonts\arialbi.ttf"),
        // Debian/Ubuntu with fonts-liberation installed (metric-compatible with Arial)
        ("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
         "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
         "/usr/share/fonts/truetype/liberation/LiberationSans-Italic.ttf",
         "/usr/share/fonts/truetype/liberation/LiberationSans-BoldItalic.ttf"),
        // Debian/Ubuntu with fonts-dejavu-core installed
        ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
         "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
         "/usr/share/fonts/truetype/dejavu/DejaVuSans-Oblique.ttf",
         "/usr/share/fonts/truetype/dejavu/DejaVuSans-BoldOblique.ttf")
    ];

    private readonly Lazy<(byte[] Regular, byte[]? Bold, byte[]? Italic, byte[]? BoldItalic)> _fonts = new(LoadFonts);

    public string DefaultFontName => FamilyName;

    public byte[] GetFont(string faceName)
    {
        var (regular, bold, italic, boldItalic) = _fonts.Value;
        return faceName switch
        {
            $"{FamilyName}#b" => bold ?? regular,
            $"{FamilyName}#i" => italic ?? regular,
            $"{FamilyName}#bi" => boldItalic ?? regular,
            _ => regular
        };
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        // Every family request maps to our one bundled/OS face — this app only ever asks for "Arial".
        var faceName = (isBold, isItalic) switch
        {
            (true, true) => $"{FamilyName}#bi",
            (true, false) => $"{FamilyName}#b",
            (false, true) => $"{FamilyName}#i",
            _ => FamilyName
        };
        return new FontResolverInfo(faceName);
    }

    private static (byte[] Regular, byte[]? Bold, byte[]? Italic, byte[]? BoldItalic) LoadFonts()
    {
        foreach (var (regularPath, boldPath, italicPath, boldItalicPath) in CandidateSets)
        {
            if (!File.Exists(regularPath)) continue;

            return (
                File.ReadAllBytes(regularPath),
                File.Exists(boldPath) ? File.ReadAllBytes(boldPath) : null,
                File.Exists(italicPath) ? File.ReadAllBytes(italicPath) : null,
                File.Exists(boldItalicPath) ? File.ReadAllBytes(boldItalicPath) : null);
        }

        throw new InvalidOperationException(
            "No usable TTF font found (checked Windows Arial and Linux Liberation/DejaVu paths). " +
            "Install 'fonts-liberation' (or 'fonts-dejavu-core') in the API's Docker image, or add another candidate path.");
    }
}
