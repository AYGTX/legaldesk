namespace SignFlow.Application.Common.Models;

/// <summary>Optional parts of the contract review, from the "Features" settings section.</summary>
public class ReviewFeatures
{
    /// <summary>
    /// The regulatory compliance check against the « veille réglementaire » registry. Off by default: the registry
    /// only holds demo summaries, and Harvey's review already grounds each risk in verified law. Turning it back on
    /// restores the compliance findings, score and panel.
    /// </summary>
    public bool RegulatoryCompliance { get; set; }
}
