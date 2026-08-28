namespace ClearC.Core.Safety;

public enum SafetyDecisionKind
{
    Allowed,
    ConfirmationRequired,
    Denied
}

public sealed record SafetyDecision(SafetyDecisionKind Kind, string Reason);
