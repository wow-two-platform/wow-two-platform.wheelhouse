# v0.10 Iteration 2 — Backend blocks (transient plan)

*Last updated: 2026-10-30*

Transient: deleted when Iteration 2 closes.

## What

| Task | Wheelhouse source | Note from the sweep |
|---|---|---|
| Commit-keyed GitHub reader cache | `Wheelhouse.Infrastructure/Products/GitHubRepositoryReader.cs` | Then remove the `IRepositoryFileSource` and `IProductManifestSource` ports, which remap the SDK's `FileRead` |
| Memo whose forced refresh joins a read in flight | `Infrastructure/Deployments/ServerVitalsMemo.cs`, `RunnerServerVitals.cs` | HybridCache has no refresh that joins |
| Inventory format rules → validation vocabulary | `Application/Inventory/Constants/InventoryPatternConstants.cs`, `Application/Inventory/Extensions/InventoryRuleExtensions.cs` (with `MustBeHttpsAddress`), `Application/InfraJobs/InfraPatterns.cs`, `Api/Requests/VaultRules.cs` | Slug, `owner/repo`, image, hostname, SSH and IPv4 rules; the backend half of the shared vocabulary; Wheelhouse's slug regex differs from `Guards.NotSlug` |
| Root-key broker, canary and fingerprints | `Infrastructure/SecretsVault/FileVaultKeySource.cs` (stamp, owner-only mode), `Application/SecretsVault/Services/SecretsVaultCrypto.cs`, `SecretsVaultKeyCache.cs`, `RootKeyFingerprint.cs` | The SDK `SealService` reads its env-only broker once and never reseals; `AddEnvelopeCryptography` registers it unused |
| Multi-repository SDK fake | `Tests.Unit/Fakes/ScriptedRepositoryFiles.cs`, `Tests.E2E/Harness/StubRepositoryFileSource.cs` | The SDK `FakeGitHubClient` holds one head and files by path |

## How

- Build every block in `wow-two-sdk-beta.be` and release once, then re-pin `BackendSdkVersion` and delete the
  Wheelhouse copies
- The vault blocks must keep byte parity: ciphertext, canary and fingerprints written by the current code decrypt and
  match after the move, proven by fixed vectors captured from the current code first
