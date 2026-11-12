namespace Wheelhouse.Application.Deployments.History.Models;

/// <summary>Represents what a database copy kept on its job: the source environment, both databases' names and how many
/// tables and rows it restored, which matched the source's row counts. Never a value or a credential.</summary>
/// <param name="From">The source target's slug, such as <c>harvestadapters-dev</c>.</param>
/// <param name="Source">The source database's name.</param>
/// <param name="Database">This environment's database, which the copy replaced.</param>
/// <param name="Tables">How many tables the copy compared; null until it compared them.</param>
/// <param name="Rows">How many rows those tables hold; null until it compared them.</param>
public sealed record DatabaseCopyDto(string? From, string? Source, string? Database, long? Tables, long? Rows);
