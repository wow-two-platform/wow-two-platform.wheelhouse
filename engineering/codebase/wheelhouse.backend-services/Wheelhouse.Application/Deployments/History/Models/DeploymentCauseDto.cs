namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents why a deployment ended in its state, with where it started.</summary>
/// <param name="Kind">What caused it: <c>service</c>, <c>step</c>, <c>gate</c>, <c>freeze</c>, <c>validation</c>,
/// <c>platform</c>, <c>busy</c>, <c>runner</c> or <c>operator</c>.</param>
/// <param name="Origin">Where it started: a service's key or a step's name; null when nothing narrower is known.</param>
/// <param name="Message">The display-safe explanation.</param>
/// <param name="At">When it happened, when known.</param>
public sealed record DeploymentCauseDto(string Kind, string? Origin, string? Message, DateTimeOffset? At);
