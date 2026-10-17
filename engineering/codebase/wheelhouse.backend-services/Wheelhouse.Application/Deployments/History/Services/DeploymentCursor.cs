using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Wheelhouse.Application.Deployments.History.Models;

namespace Wheelhouse.Application.Deployments.History.Services;

/// <summary>Encodes a page's position as the last item it showed: its submission instant and identifier, the history's
/// order. Opaque to clients; a new submission never shifts a later page.</summary>
public static class DeploymentCursor
{
    /// <summary>Encodes the position after an entry.</summary>
    /// <param name="entry">The last entry of a page.</param>
    /// <returns>The cursor.</returns>
    public static string After(DeploymentEntry entry) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            entry.Record.SubmittedAt.UtcTicks.ToString(CultureInfo.InvariantCulture) + ":" + entry.Record.Id));

    /// <summary>Decodes a cursor.</summary>
    /// <param name="cursor">The cursor a page answered with.</param>
    /// <param name="position">The instant's ticks and the identifier.</param>
    /// <returns><c>true</c> when the cursor is well formed.</returns>
    public static bool TryRead(string cursor, out (long Ticks, string Id) position)
    {
        position = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 200)
            return false;
        string text;
        try
        {
            text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = text.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == text.Length - 1
            || !long.TryParse(text.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            return false;
        position = (ticks, text[(separator + 1)..]);
        return true;
    }

    /// <summary>Whether an entry comes after a position in the history's newest-first order.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="position">The decoded cursor.</param>
    /// <returns><c>true</c> when the entry belongs on a later page.</returns>
    public static bool IsAfter(DeploymentEntry entry, (long Ticks, string Id) position)
    {
        var ticks = entry.Record.SubmittedAt.UtcTicks;
        return ticks < position.Ticks
            || (ticks == position.Ticks && string.CompareOrdinal(entry.Record.Id, position.Id) < 0);
    }
}
