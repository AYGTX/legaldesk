namespace SignFlow.Application.Common.Interfaces;

/// <summary>Draws QR codes (two-factor setup) without a client-side library.</summary>
public interface IQrCodes
{
    /// <summary>A data: URI of a PNG encoding <paramref name="text"/>.</summary>
    string PngDataUri(string text);
}
