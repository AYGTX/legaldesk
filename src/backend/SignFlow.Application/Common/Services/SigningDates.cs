using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SignFlow.Domain.Entities;

namespace SignFlow.Application.Common.Services;

/// <summary>Dates the server writes on a contract: the organisation's day, in its format (R-M5-7).</summary>
public static class SigningDates
{
    public static string Today(string? timeZoneId, string? format, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZoneId) ? "Africa/Tunis" : timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
        }
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(now, DateTimeKind.Utc), zone);
        return local.ToString(string.IsNullOrWhiteSpace(format) ? "dd/MM/yyyy" : format, CultureInfo.InvariantCulture);
    }
}

/// <summary>SHA-256 of the exact contract a participant is shown (R-M5-5): the stored file's, or for a contract built
/// from an HTML template, the template text with the answers.</summary>
public static class DocumentFingerprint
{
    public static string? Of(Document document)
    {
        if (!string.IsNullOrWhiteSpace(document.Hash))
            return document.Hash.ToLowerInvariant();
        if (document.HtmlContentSnapshot is null)
            return null;
        var text = document.HtmlContentSnapshot + "\n" + (document.FilledValuesJson ?? string.Empty);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
