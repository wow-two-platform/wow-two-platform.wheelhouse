namespace Wheelhouse.Tests.Unit.Fakes;

/// <summary>Represents one call the fake GitHub received.</summary>
/// <param name="Key">The method and path.</param>
/// <param name="Host">The host it was sent to.</param>
/// <param name="Authorization">The <c>Authorization</c> header, when one was sent.</param>
public sealed record FakeGitHubCall(string Key, string Host, string? Authorization);
