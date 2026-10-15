using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using WoW.Two.Sdk.Backend.Beta.Testing.Web;

namespace Wheelhouse.Tests.E2E.Support;

/// <summary>
/// Reads an RFC 7807 <see cref="ProblemDetails"/> body off an error response — the failure shape the host emits through the SDK
/// <c>ToProblem</c> and its HTTP ProblemDetails boundary. Used by the E2E error tests to assert it is ProblemDetails (with <c>title</c>/
/// <c>status</c>), never the success <c>ApiResponse&lt;T&gt;</c> envelope.
/// </summary>
public static class ProblemDetailsExtensions
{
    /// <summary>Deserializes the response body as <see cref="ProblemDetails"/> using the shared E2E JSON options.</summary>
    /// <param name="response">The error HTTP response to read.</param>
    /// <returns>The parsed problem document (never <see langword="null"/>).</returns>
    /// <exception cref="InvalidOperationException">The body could not be read as ProblemDetails.</exception>
    public static async Task<ProblemDetails> ReadProblemAsync(this HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestJsonConstants.Options);
        if (problem is null)
        {
            var raw = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Expected a ProblemDetails body for {(int)response.StatusCode}, but it did not deserialize. Raw: {raw}");
        }

        return problem;
    }
}
