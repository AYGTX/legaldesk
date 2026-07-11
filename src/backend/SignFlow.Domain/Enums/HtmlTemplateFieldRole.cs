namespace SignFlow.Domain.Enums;

public enum HtmlTemplateFieldRole
{
    /// <summary>Filled by the person creating the request (before sending).</summary>
    Initiator,

    /// <summary>Signed/approved by the designated approver.</summary>
    Approver
}
