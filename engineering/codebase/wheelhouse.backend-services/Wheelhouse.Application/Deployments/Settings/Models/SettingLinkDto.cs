namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents the source of an owner's link: another product's key in the same environment, named by one of
/// that product's targets there. Names only; never a value.</summary>
/// <param name="Target">The source's target slug, such as <c>ocharo-dev</c>.</param>
/// <param name="Key">The source key, such as <c>Identity:Server:Clients:8:ClientSecret</c>.</param>
public sealed record SettingLinkDto(string Target, string Key);
