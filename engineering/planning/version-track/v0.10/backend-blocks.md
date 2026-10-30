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

## Built — be sdk `10.0.95-beta`

Parity: `Foundation.Tests/Security/EnvelopeParityVectorTests.cs` holds vectors captured from Wheelhouse's own
`SecretsVaultCrypto` and `RootKeyFingerprint`; the SDK opens and reproduces each.

| Wheelhouse today | SDK replacement | Adoption notes |
|---|---|---|
| `Infrastructure/Products/GitHubRepositoryReader.cs` | `IGitHubRepositoryReader` / `GitHubRepositoryReader` (`Integrations.GitHub`) | `AddGitHubRepositoryReader(o => …)`, section `Integrations:GitHub:Reader`, `HeadFor` 5 min, `CommitFor` 1 day; `ConfigureClient` stays; consumers drop `IRepositoryFileSource`, `GitHubRepositoryFileSource`, `RepositoryFileRead/Status` and branch on `FileRead` |
| `Infrastructure/Deployments/ServerVitalsMemo.cs` | `CoalescingMemo<AppResult<JsonElement>>` (`Caching.Memo`, singleton) | In `RunnerServerVitals`: `memo.GetAsync(read → CacheResult.Keep(Ok(data.Clone())) or Transient(read), holdFor, fresh, ct)`; the E2E reset `.Last = null` becomes `Forget()` |
| `InventoryPatternConstants`, `InfraPatterns` (PublicAddress, Hostname, Fingerprint, PublicKey, Digest), `VaultRules.Key` | `FormatPatternConstants` (`Foundation.Validation.Formats`) | Same pattern strings, new names: Slug→NameSlug, Asset→FileName, SiteHost→DomainName, SshHost→NetworkHost, Hostname→HostLabel, PublicAddress→Ipv4Address, Digest→Sha256Hex, Endpoint→HttpEndpoint, Network→DockerNetwork, SmokePath→RequestPath, Region→RegionCode, Key→ConfigurationKey; `MustBeSlug`→`MustBeNameSlug`, `MustMatch`→SDK `MustMatch`, same messages and codes; `MustBeHttpsAddress("https://www.ovh.com/manager/")` keeps its message; named rules such as `MustBeGitHubRepository` exist. Stay in Wheelhouse: EntryPoint, Admin, Node, ReservedAdmins, `VaultRules.Namespace`, `MustBeProductKey` / `DisplayName` / `ListEachOnce` |
| `IVaultKeySource`, `VaultKeyRead`, `FileVaultKeySource` | `IMasterKeyBroker`, `MasterKeyRead` (init properties, same codes), `FileMasterKeyBroker` | `CheckFile` → `CheckAccess`, same stamp and mode check; register `new FileMasterKeyBroker(path)` from `SecretsVaultSettings` plus `MasterKeyProofTracker` and `MasterKeyUnsealService` singletons (or `AddFileMasterKeyBroker(o => o.Path = …)`) |
| `SecretsVaultKeyCache` | `MasterKeyProofTracker` | |
| the body of `UnsealAsync` | `MasterKeyUnsealService.UnsealAsync(ProveAsync)` | Returns `mismatch`; Wheelhouse keeps its own log line |
| `RootKeyFingerprint` | `new KeyFingerprintHasher("wheelhouse.vault.root-key.v1")` (`.Pattern`) | |
| `SecretsVaultCrypto.Fingerprint`, `SealValue` / `OpenValue`, canary code | `new ValueFingerprintHasher("wheelhouse-vault/fingerprint").Hash(v, dek)`, `cipher.EncryptText` / `DecryptText`, `cipher.CreateCanary` / `VerifyCanary(root, CanaryKeyContext, CanaryContext)` | Map the entity fields to `KeyCanary`; Wheelhouse keeps the contexts, `Digest` and `NewKey` |
| `Tests.Unit/Fakes/ScriptedRepositoryFiles.cs`, `Tests.E2E/Harness/StubRepositoryFileSource.cs` | SDK `FakeGitHubClient` registered as `IGitHubClient` under the real reader | `Push` (optional `commit:` or a `FileRead` outcome), `PushEmpty`, `Unreachable`, `WithPushedOnly`, `Failure`, `HeadReads` / `TreeReads` / `FileReads`, `Clear`; records have no `fresh` flag, a new commit keeps the branch's files, and E2E needs `HeadFor = TimeSpan.Zero` since the reader caches per commit |
