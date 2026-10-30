using System.Text;
using SignFlow.Application.Common.Services;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace SignFlow.Infrastructure.Services.Pdf;

/// <summary>Opens the file strictly with PdfPig: anything it cannot parse is not accepted as a contract.</summary>
public class PdfInspector : IPdfInspector
{
    // An encrypted PDF names its security handler in the trailer (ISO 32000-1, 7.6.1). PdfPig does not always say
    // so (it may fail inside decryption instead), so the file is checked for the entry before it is opened.
    private static readonly byte[] EncryptKey = Encoding.ASCII.GetBytes("/Encrypt");

    public PdfInspection Inspect(Stream pdf)
    {
        var start = pdf.Position;
        var encrypted = Contains(pdf, EncryptKey);
        pdf.Position = start;
        if (encrypted)
            return new PdfInspection(Readable: false, Encrypted: true, 0);

        try
        {
            using var document = PdfDocument.Open(pdf, new ParsingOptions { UseLenientParsing = false });
            return new PdfInspection(Readable: true, Encrypted: false, document.NumberOfPages);
        }
        catch (PdfDocumentEncryptedException)
        {
            return new PdfInspection(Readable: false, Encrypted: true, 0);
        }
        catch (Exception)
        {
            return new PdfInspection(Readable: false, Encrypted: false, 0);
        }
    }

    private static bool Contains(Stream stream, byte[] needle)
    {
        // Read in blocks, keeping the last few bytes so a key split across two blocks is still found.
        var buffer = new byte[64 * 1024 + needle.Length];
        var carried = 0;
        int read;
        while ((read = stream.Read(buffer, carried, buffer.Length - carried)) > 0)
        {
            var filled = carried + read;
            if (buffer.AsSpan(0, filled).IndexOf(needle) >= 0) return true;
            carried = Math.Min(needle.Length - 1, filled);
            Array.Copy(buffer, filled - carried, buffer, 0, carried);
        }
        return false;
    }
}
