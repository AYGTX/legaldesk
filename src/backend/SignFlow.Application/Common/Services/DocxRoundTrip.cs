using System.IO.Compression;
using System.Security;
using System.Text;

namespace SignFlow.Application.Common.Services;

/// <summary>Turns an edited Word file back into the PDF a revision needs (R-M7-3), by LibreOffice running headless.</summary>
public interface IDocxConverter
{
    /// <summary>The PDF of <paramref name="docx"/>. Throws <see cref="Exceptions.ConflictException"/> revision.docx_unavailable
    /// when the converter is not installed, revision.docx_failed when it cannot read the file.</summary>
    Task<Stream> ToPdfAsync(Stream docx, CancellationToken cancellationToken = default);
}

/// <summary>
/// The Word side of the round trip (R-M7-3): a version is downloaded as a plain DOCX of its text, one paragraph per
/// paragraph of the contract, so it can be edited in Word and uploaded back as the next version. Layout is not kept: the
/// text is what is negotiated, and the uploaded file is what gets reviewed and signed.
/// </summary>
public static class DocxRoundTrip
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public static bool IsDocx(string fileName) => fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    /// <summary>A DOCX is a zip holding word/document.xml; anything else is refused before the converter sees it.</summary>
    public static bool LooksLikeDocx(Stream stream)
    {
        var start = stream.Position;
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return zip.GetEntry("word/document.xml") is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        finally
        {
            stream.Position = start;
        }
    }

    public static byte[] Write(string title, string text)
    {
        var paragraphs = new List<string> { title };
        paragraphs.AddRange(text.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()));

        var body = new StringBuilder();
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var line = paragraphs[i];
            if (i > 0 && line.Length == 0 && paragraphs[i - 1].Length == 0) continue; // one empty line at most
            var style = i == 0 ? "<w:pPr><w:pStyle w:val=\"Title\"/></w:pPr>" : string.Empty;
            body.Append("<w:p>").Append(style)
                .Append("<w:r><w:t xml:space=\"preserve\">").Append(SecurityElement.Escape(Clean(line))).Append("</w:t></w:r></w:p>");
        }

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                <Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);
            Add(zip, "word/_rels/document.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            Add(zip, "word/styles.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                <w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Calibri"/><w:sz w:val="22"/></w:rPr></w:rPrDefault>
                <w:pPrDefault><w:pPr><w:spacing w:after="120"/></w:pPr></w:pPrDefault></w:docDefaults>
                <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
                <w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:rPr><w:b/><w:sz w:val="32"/></w:rPr></w:style>
                </w:styles>
                """);
            Add(zip, "word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                body +
                "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1418\" w:right=\"1418\" w:bottom=\"1418\" w:left=\"1418\" w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/></w:sectPr>" +
                "</w:body></w:document>");
        }
        return buffer.ToArray();
    }

    /// <summary>XML 1.0 has no place for control characters a PDF's text layer can carry.</summary>
    private static string Clean(string line) =>
        new(line.Where(c => c == '\t' || c >= ' ' && !char.IsSurrogate(c) || char.IsSurrogate(c)).ToArray());

    private static void Add(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }
}
