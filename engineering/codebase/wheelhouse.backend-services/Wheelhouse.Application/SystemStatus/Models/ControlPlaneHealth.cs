namespace Wheelhouse.Application.SystemStatus.Models;

/// <summary>Describes the control plane's database readiness and build identity.</summary>
public sealed record ControlPlaneHealth(string Service, bool DatabaseReady, string Version, bool LocalRig);
