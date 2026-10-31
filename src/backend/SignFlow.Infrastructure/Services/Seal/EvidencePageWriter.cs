using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SignFlow.Application.Common.Interfaces;
using SignFlow.Infrastructure.Services.Pdf;

namespace SignFlow.Infrastructure.Services.Seal;

/// <summary>
/// The evidence page(s) appended to a completed contract before it is sealed (R-M5-8): who took part, what each did,
/// when, from where, how they proved their address, the SHA-256 of every version, the audit digest, and the code to
/// check the sealed file. Written in French and English, as the contract may be read by either side.
/// </summary>
public class EvidencePageWriter : IEvidencePageWriter
{
    private const double Margin = 48;
    private static readonly XFont Title = new(AppFontResolver.FamilyName, 15, XFontStyleEx.Bold);
    private static readonly XFont Heading = new(AppFontResolver.FamilyName, 10.5, XFontStyleEx.Bold);
    private static readonly XFont Body = new(AppFontResolver.FamilyName, 8.5, XFontStyleEx.Regular);
    private static readonly XFont Mono = new(AppFontResolver.FamilyName, 7.5, XFontStyleEx.Regular);

    public Task<byte[]> AppendAsync(byte[] pdf, EvidenceSheet sheet, CancellationToken ct = default)
    {
        using var input = new MemoryStream(pdf);
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var writer = new PageWriter(document);

        writer.Line("Preuves de signature / Signature evidence", Title, 22);
        writer.Line(sheet.Title, Heading, 16);
        writer.Line($"{sheet.OrganisationName} · terminé le / completed {Utc(sheet.CompletedAt)}", Body);
        writer.Line($"Code de vérification / Verification code : {sheet.VerificationCode}", Heading);
        writer.Line($"Vérifier ce fichier / Check this file : {sheet.VerifyUrl}", Body, 18);

        writer.Line("Participants", Heading, 15);
        foreach (var p in sheet.Participants)
        {
            writer.Line($"{p.Name} <{p.Email}> · {p.Role} · {p.Outcome}{(p.At is { } at ? " · " + Utc(at) : string.Empty)}", Body);
            var proof = p.VerificationMethod switch
            {
                "EmailCode" => "adresse confirmée par code e-mail / address confirmed by e-mailed code",
                "Account" => "connecté à son compte LegalDesk / signed in to their LegalDesk account",
                null => "adresse non confirmée / address not confirmed",
                var other => other
            };
            writer.Line($"    {proof}{(p.VerifiedAt is { } v ? " · " + Utc(v) : string.Empty)}{(p.Ip is null ? string.Empty : " · IP " + p.Ip)}", Body);
            foreach (var s in p.Signatures)
                writer.Line("    " + s, Mono);
            writer.Gap(5);
        }

        writer.Gap(8);
        writer.Line("Versions du document / Document versions (SHA-256)", Heading, 15);
        foreach (var (version, sha) in sheet.Versions)
            writer.Line($"v{version} · {sha}", Mono);

        writer.Gap(8);
        writer.Line("Journal d'audit / Audit trail", Heading, 15);
        writer.Line($"{sheet.AuditEntries} entrées / entries · empreinte / digest SHA-256 : {sheet.AuditDigest}", Mono);

        writer.Gap(14);
        writer.Line("Signature électronique avancée scellée par l'organisation, horodatée par une autorité RFC 3161 ;", Body);
        writer.Line("ce n'est pas une signature électronique qualifiée. / Advanced electronic signatures sealed by the", Body);
        writer.Line("organisation and timestamped by an RFC 3161 authority; not a qualified electronic signature.", Body);

        using var output = new MemoryStream();
        document.Save(output);
        return Task.FromResult(output.ToArray());
    }

    private static string Utc(DateTime t) => t.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>Writes lines top to bottom, adding A4 pages as needed; long lines are wrapped on spaces.</summary>
    private sealed class PageWriter(PdfDocument document)
    {
        private XGraphics? _gfx;
        private PdfPage? _page;
        private double _y;

        public void Gap(double points) => _y += points;

        public void Line(string text, XFont font, double lineHeight = 12)
        {
            foreach (var part in Wrap(text, font))
            {
                Ensure(lineHeight);
                _gfx!.DrawString(part, font, XBrushes.Black, new XPoint(Margin, _y));
                _y += lineHeight;
            }
        }

        private void Ensure(double lineHeight)
        {
            if (_page is not null && _y + lineHeight <= _page.Height.Point - Margin) return;
            _gfx?.Dispose();
            _page = document.AddPage();
            _page.Width = XUnit.FromMillimeter(210);
            _page.Height = XUnit.FromMillimeter(297);
            _gfx = XGraphics.FromPdfPage(_page);
            _y = Margin + 10;
        }

        private IEnumerable<string> Wrap(string text, XFont font)
        {
            Ensure(0);
            var width = _page!.Width.Point - 2 * Margin;
            var line = string.Empty;
            foreach (var word in text.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && _gfx!.MeasureString(candidate, font).Width > width)
                {
                    yield return line;
                    line = word;
                }
                else line = candidate;
            }
            if (line.Length > 0) yield return line;
        }
    }
}
