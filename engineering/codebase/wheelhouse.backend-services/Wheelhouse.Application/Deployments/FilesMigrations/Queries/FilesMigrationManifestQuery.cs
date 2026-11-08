using Wheelhouse.Application.Deployments.FilesMigrations.Models;
using WoW.Two.Sdk.Beta.Be.Mediator.Cqrs;
using WoW.Two.Sdk.Beta.Be.Mediator.Result;

namespace Wheelhouse.Application.Deployments.FilesMigrations.Queries;

/// <summary>Reads the sources the manifests of the release a dev target runs list, and the Files targets of its
/// environment the files can move into.</summary>
/// <param name="Target">The target whose legacy files would move.</param>
public sealed record FilesMigrationManifestQuery(string Target) : IQuery<AppResult<FilesMigrationManifestDto>>;
