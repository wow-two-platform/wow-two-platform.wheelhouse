using System.Text.Json;

namespace Wheelhouse.Application.Deployments.Settings.Models;

/// <summary>Represents what a deploy sends its target to render: the values, and what each key was, without values.</summary>
/// <param name="Values"><c>{"values": {key: value}}</c>, for the runner's stdin only.</param>
/// <param name="Keys">Each key's kind, version and fingerprint, for the render record.</param>
public sealed record RenderValues(JsonElement Values, IReadOnlyDictionary<string, RenderedKey> Keys)
{
    /// <summary>Returns the render without its values.</summary>
    public override string ToString() => nameof(RenderValues) + " { " + Keys.Count + " keys }";
}
