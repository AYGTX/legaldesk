namespace SignFlow.Domain.Enums;

/// <summary>Where the words of a contract's current version come from (R-M2-4).</summary>
public enum ContractTextSource
{
    /// <summary>The PDF's own text layer.</summary>
    Embedded = 0,

    /// <summary>Text recognition on the page images: the PDF had (almost) no text of its own.</summary>
    Ocr = 1,

    /// <summary>No text of its own and text recognition was unavailable or found nothing: it cannot be reviewed.</summary>
    Unreadable = 2
}
