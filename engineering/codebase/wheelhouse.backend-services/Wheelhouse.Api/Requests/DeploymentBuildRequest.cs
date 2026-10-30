using System.ComponentModel.DataAnnotations;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Api.Requests;

/// <summary>Names the one commit to build and, for a product built from several repositories, which of them holds it (its
/// primary one when omitted); accepts no branch, workflow or input of the caller's choosing.</summary>
public sealed record DeploymentBuildRequest(
    [Required, RegularExpression("^[a-f0-9]{40}$")] string Commit,
    [RegularExpression(FormatPatternConstants.GitHubRepository)] string? Repository = null);
