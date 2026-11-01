namespace SignFlow.Domain.Enums;

/// <summary>How a contract's term renews (R-M10-1).</summary>
public enum RenewalMode
{
    None = 0,
    /// <summary>Renews by itself unless notice is given.</summary>
    Automatic = 1,
    /// <summary>Renews only if a party takes up the option.</summary>
    Option = 2
}

public enum NoticeUnit
{
    Days = 0,
    Months = 1
}

/// <summary>The form the contract requires for a notice.</summary>
public enum NoticeForm
{
    Letter = 0,
    Email = 1,
    RegisteredMail = 2
}

/// <summary>What the organisation decided about the term (R-M10-4). Any decision stops the alert ladder.</summary>
public enum TermDecision
{
    None = 0,
    Renew = 1,
    Renegotiate = 2,
    Terminate = 3,
    LetExpire = 4
}

public enum ObligationKind
{
    Payment = 0,
    Report = 1,
    InsuranceCertificate = 2,
    Audit = 3,
    Renewal = 4,
    Other = 5
}

public enum ObligationRecurrence
{
    None = 0,
    Monthly = 1,
    Quarterly = 2,
    Yearly = 3
}

/// <summary>Where an obligation came from: a person, or Harvey's key data with the contract's words (R-M10-5, R-M10-6).</summary>
public enum ObligationSource
{
    Person = 0,
    Harvey = 1
}

public enum ObligationStatus
{
    Upcoming = 0,
    Done = 1,
    Missed = 2,
    Waived = 3
}

public enum ContractEventKind
{
    Rollover = 0,
    Decision = 1,
    NoticeSent = 2
}
