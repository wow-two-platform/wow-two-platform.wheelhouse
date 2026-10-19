namespace Wheelhouse.Application.Abstractions;

/// <summary>Tells whether this console may drive infrastructure: Tailscale, enrollment and placement. Only Wheelhouse
/// prod (and a run on the developer's machine) may; deployed dev never holds a products host's keys or the Tailscale
/// client.</summary>
public interface IInfraAccess
{
    /// <summary>Gets why this console may not drive infrastructure, or <c>null</c> when it may.</summary>
    string? Refusal { get; }
}
