using System.Net.Http.Json;
using System.Text.Json;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>Reads the audit list as the console does: one token page, whose entries a test inspects.</summary>
public static class AuditReads
{
    /// <summary>Reads the <c>items</c> of one <c>GET /api/audit</c> page, newest first.</summary>
    /// <param name="admin">A signed-in owner's client.</param>
    /// <param name="query">The query string, such as <c>?pageSize=2</c>; none reads the newest page.</param>
    /// <returns>The page's entries, an array.</returns>
    public static async Task<JsonElement> AuditItemsAsync(this HttpClient admin, string query = "") =>
        (await admin.AuditPageAsync(query)).GetProperty("items");

    /// <summary>Reads one <c>GET /api/audit</c> page whole: <c>items</c>, <c>pageSize</c> and, while older entries
    /// remain, <c>nextPageToken</c>.</summary>
    /// <param name="admin">A signed-in owner's client.</param>
    /// <param name="query">The query string, such as <c>?pageSize=2</c>; none reads the newest page.</param>
    /// <returns>The page, the <c>data</c> of the answer.</returns>
    public static async Task<JsonElement> AuditPageAsync(this HttpClient admin, string query = "") =>
        (await admin.GetFromJsonAsync<JsonElement>("/api/audit" + query)).GetProperty("data");
}
