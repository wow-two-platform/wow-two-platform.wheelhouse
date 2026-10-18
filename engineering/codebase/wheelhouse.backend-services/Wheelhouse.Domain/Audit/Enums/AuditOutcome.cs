namespace Wheelhouse.Domain.Audit.Enums;

/// <summary>How an audited action ended.</summary>
public enum AuditOutcome
{
    /// <summary>The action completed; a deployment it queued reports its own result later.</summary>
    Succeeded = 0,

    /// <summary>The action was refused or failed; nothing it asked for happened.</summary>
    Failed
}
