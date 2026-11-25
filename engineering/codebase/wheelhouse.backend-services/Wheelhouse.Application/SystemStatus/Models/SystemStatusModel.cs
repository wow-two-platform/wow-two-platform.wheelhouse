namespace Wheelhouse.Application.SystemStatus.Models;

/// <summary>Describes the running console.</summary>
public sealed record SystemStatusModel(string Service, string Status, string Version, string Environment, bool LocalRig);
