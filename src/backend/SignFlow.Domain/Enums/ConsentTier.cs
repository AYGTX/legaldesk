namespace SignFlow.Domain.Enums;

public enum ConsentTier
{
    Necessary = 1,   // always required, cannot be declined
    Functional = 2,  // preferences, language, saved state
    Analytics = 3    // usage tracking, performance
}
