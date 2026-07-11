namespace SignFlow.Domain.Enums;

/// <summary>
/// Who is responsible for filling a PDF-template field at runtime.
/// </summary>
public enum PdfFieldFillRole
{
    /// <summary>Filled by the person creating the request (the initiator), before it is sent.</summary>
    Requester,

    /// <summary>Filled/signed by a workflow signer (approver) during the signing flow.</summary>
    Approver,

    /// <summary>Auto-populated by the system from an <c>AutoFillFrom</c> source; never shown to a human filler.</summary>
    System
}
