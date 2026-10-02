using Wheelhouse.Domain.Integrations.Entities;
using WoW.Two.Sdk.Backend.Beta.Data.Abstractions;

namespace Wheelhouse.Application.Abstractions;

/// <summary>Defines the store of integration keys, kept by the hash of their secret; the secret is never stored.
/// Listing returns every key newest first, revoked ones included.</summary>
public interface IIntegrationKeysRepository : IRepository<IntegrationKeyEntity, Guid>;
