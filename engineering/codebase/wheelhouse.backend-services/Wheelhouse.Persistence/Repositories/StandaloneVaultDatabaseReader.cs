using Npgsql;
using Wheelhouse.Application.Abstractions;

namespace Wheelhouse.Persistence.Repositories;

/// <summary>Reads a standalone Secrets Vault's PostgreSQL database (its own, or a restore of its dump) as stored, read
/// only: namespaces, data keys, secrets and every version. Access tokens and the old audit trail are left behind.</summary>
internal sealed class StandaloneVaultDatabaseReader : IStandaloneVaultReader
{
    /// <inheritdoc />
    public async Task<StandaloneVaultStore> ReadAsync(string connectionString, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        // Every statement below reads; the session refuses writes besides.
        await using (var readOnly = new NpgsqlCommand("SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY", connection))
            await readOnly.ExecuteNonQueryAsync(ct);

        var namespaces = new List<(Guid Id, string Slug)>();
        await using (var command = new NpgsqlCommand("SELECT id, slug FROM namespaces ORDER BY slug", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                namespaces.Add((reader.GetGuid(0), reader.GetString(1)));

        var keys = new Dictionary<Guid, Dictionary<Guid, StandaloneSealed>>();
        await using (var command = new NpgsqlCommand("SELECT id, namespace_id, wrapped_key, nonce, tag FROM data_keys", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                var space = reader.GetGuid(1);
                if (!keys.TryGetValue(space, out var held))
                    keys[space] = held = [];
                held[reader.GetGuid(0)] = new StandaloneSealed((byte[])reader[2], (byte[])reader[3], (byte[])reader[4]);
            }

        var versions = new Dictionary<Guid, List<StandaloneVersion>>();
        await using (var command = new NpgsqlCommand(
                         "SELECT secret_id, version, data_key_id, cipher_text, nonce, tag, created_by, created_at_utc " +
                         "FROM secret_versions ORDER BY secret_id, version", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                var secret = reader.GetGuid(0);
                if (!versions.TryGetValue(secret, out var list))
                    versions[secret] = list = [];
                list.Add(new StandaloneVersion(reader.GetInt32(1), reader.GetGuid(2),
                    new StandaloneSealed((byte[])reader[3], (byte[])reader[4], (byte[])reader[5]),
                    reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7)));
            }

        var secrets = new Dictionary<Guid, List<StandaloneSecret>>();
        await using (var command = new NpgsqlCommand(
                         "SELECT id, namespace_id, key, state, current_version FROM secrets ORDER BY key", connection))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
            {
                var space = reader.GetGuid(1);
                if (!secrets.TryGetValue(space, out var list))
                    secrets[space] = list = [];
                var state = reader.GetValue(3)?.ToString() ?? "";
                list.Add(new StandaloneSecret(reader.GetString(2),
                    string.Equals(state, "disabled", StringComparison.OrdinalIgnoreCase) || state == "1",
                    reader.GetInt32(4), versions.GetValueOrDefault(reader.GetGuid(0)) ?? []));
            }

        return new StandaloneVaultStore([.. namespaces.Select(space => new StandaloneNamespace(space.Slug,
            keys.GetValueOrDefault(space.Id) ?? new Dictionary<Guid, StandaloneSealed>(),
            secrets.GetValueOrDefault(space.Id) ?? []))]);
    }
}
