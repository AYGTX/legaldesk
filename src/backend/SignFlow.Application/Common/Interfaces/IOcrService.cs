namespace SignFlow.Application.Common.Interfaces;

/// <summary>Text recognition on a PDF's page images, for scanned contracts (R-M2-4).</summary>
public interface IOcrService
{
    /// <summary>False when no text recognition is installed on this server: a scanned contract then cannot be read.</summary>
    bool IsAvailable { get; }

    /// <summary>The text of every page, in order, separated by blank lines. Throws when recognition fails.</summary>
    Task<string> RecognizeAsync(Stream pdf, CancellationToken cancellationToken = default);
}
