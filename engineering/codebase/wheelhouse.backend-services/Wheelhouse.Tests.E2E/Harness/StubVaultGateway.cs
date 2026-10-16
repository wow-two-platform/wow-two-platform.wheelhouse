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

    public Task<AppResult<JsonElement>> ReadAsync(VaultResource resource, string? vault, string? ns, CancellationToken ct) =>
        Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement<object>(resource switch
        {
            VaultResource.Vaults => new object[] { new { id = "pilot-vault", name = "Pilot vault", serverId = "pilot", status = "unsealed" } },
            VaultResource.Namespaces => new object[] { new { slug = "billing", name = "Billing", createdAtUtc = "1990-01-01T00:00:00Z" } },
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
        object result = change is TokenMintChange mint
            ? new { id = Guid.NewGuid(), name = mint.Name, @namespace = mint.Namespace, token = "vt_shown_once" }
            : new { @namespace = change.Namespace, state = "active", version = 3 };
        return Task.FromResult(AppResult<JsonElement>.Ok(JsonSerializer.SerializeToElement(result)));
    }
}
