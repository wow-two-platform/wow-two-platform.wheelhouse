namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents one finding a config check step names: the setting and the rule it broke, never its value nor the
/// rule's message.</summary>
/// <param name="Kind"><c>invalid</c> (a missing or rejected value), <c>unknown</c> (a key no settings record binds) or
/// <c>stranded</c> (a change that leaves people no way in).</param>
/// <param name="Setting">The setting's configuration path; null when the runner could not read it as one.</param>
/// <param name="Code">The rule's short kebab-case id; null for an unknown key.</param>
/// <param name="Count">How many people a stranding change leaves with no way in; null for any other finding.</param>
public sealed record DeploymentStepFindingDto(string Kind, string? Setting, string? Code, int? Count);
