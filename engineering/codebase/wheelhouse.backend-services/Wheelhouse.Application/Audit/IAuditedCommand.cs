namespace Wheelhouse.Application.Audit;

/// <summary>Marks a command whose outcome the audit trail records. Every member is display-safe text: it names what
/// the owner acted on, never a secret value, token or setting.</summary>
public interface IAuditedCommand
{
    /// <summary>Gets the action's stable name, such as <c>deployment.start</c>.</summary>
    string AuditAction { get; }

    /// <summary>Gets what the action acts on: a target, a product or a vault path.</summary>
    string AuditSubject { get; }

    /// <summary>Gets optional context, such as the release a deployment takes.</summary>
    string? AuditDetail => null;
}
