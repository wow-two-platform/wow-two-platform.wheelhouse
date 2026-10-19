using System.Text.Json;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Application.Vaults;
using Wheelhouse.Application.Vaults.Changes;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Tests.E2E.Harness;

/// <summary>Records vault administration without contacting a vault.</summary>
public sealed class StubVaultGateway : IVaultGateway
{
    public string? LastVault { get; private set; }
    public VaultChange? LastChange { get; private set; }

    /// <summary>Every change applied, in order.</summary>
    public List<(string Vault, VaultChange Change)> Changes { get; } = [];

    /// <summary>The secret values a namespace holds, by namespace then key; a listed namespace lists these keys. A
    /// namespace missing here lists <c>DATABASE_URL</c>.</summary>
    public Dictionary<string, Dictionary<string, string>> Values { get; } = [];

    /// <summary>When set, reads of secret metadata fail as an unreachable vault would.</summary>
    public bool Unreachable { get; set; }

    /// <summary>Every values read: vault, namespace, keys and purpose.</summary>
    public List<(string Vault, string Namespace, IReadOnlyCollection<string> Keys, string Purpose)> Reads { get; } = [];

    public Task<AppResult<IReadOnlyDictionary<string, string>>> ReadValuesAsync(
        string vault, string ns, IReadOnlyCollection<string> keys, string purpose, CancellationToken ct)
    {
        Reads.Add((vault, ns, keys, purpose));
        var held = Values.TryGetValue(ns, out var values) ? values : new Dictionary<string, string> { ["DATABASE_URL"] = "postgres://stub" };
        IReadOnlyDictionary<string, string> found = keys.Where(held.ContainsKey).ToDictionary(key => key, key => held[key]);
        return Task.FromResult(AppResult<IReadOnlyDictionary<string, string>>.Ok(found));
    }

    public void Reset()
    {
        Changes.Clear();
        Values.Clear();
        Reads.Clear();
        Unreachable = false;
    }

    public Task<AppResult<JsonElement>> ReadAsync(VaultResource resource, string? vault, string? ns, CancellationToken ct) =>
        resource == VaultResource.Secrets && Unreachable
            ? Task.FromResult(AppResult<JsonElement>.Fail(WoW.Two.Sdk.Beta.Be.Foundation.Errors.AppErrorFactory.ExternalUnavailable("The vault is unreachable from Wheelhouse.")))
            : Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement<object>(resource switch
        {
            VaultResource.Vaults => new object[] { new { id = "pilot-vault", name = "Pilot vault", serverId = "pilot", status = "unsealed" } },
            VaultResource.Namespaces => new object[] { new { slug = "billing", name = "Billing", createdAtUtc = "1990-01-01T00:00:00Z" } },
            VaultResource.Secrets when ns is not null && Values.TryGetValue(ns, out var held) => held.Keys
                .Select(key => (object)new { @namespace = ns, key, state = "active", version = 1, updatedAtUtc = "1990-01-01T00:00:00Z" }).ToArray(),
            VaultResource.Secrets => new object[]
            {
                new { @namespace = ns, key = "DATABASE_URL", state = "active", version = 2, updatedAtUtc = "1990-01-01T00:00:00Z" }
            },
            _ => new object[]
            {
                new { id = Guid.Parse("11111111-1111-1111-1111-111111111111"), name = "management", createdAtUtc = "1990-01-01T00:00:00Z",
                    expiresAtUtc = (string?)null, isRevoked = false }
            }
        })));

    public Task<AppResult<JsonElement>> ApplyAsync(string vault, VaultChange change, CancellationToken ct)
    {
        LastVault = vault;
        LastChange = change;
        Changes.Add((vault, change));
        if (change is SecretSetChange set)
        {
            if (!Values.TryGetValue(set.Namespace, out var held))
                Values[set.Namespace] = held = [];
            held[set.Key] = set.Value;
        }
        object result = change is TokenMintChange mint
            ? new { id = Guid.NewGuid(), name = mint.Name, @namespace = mint.Namespace, token = "vt_shown_once" }
            : new { @namespace = change.Namespace, state = "active", version = 3 };
        return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(result)));
    }
}
