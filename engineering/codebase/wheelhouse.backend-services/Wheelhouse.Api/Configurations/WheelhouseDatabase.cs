namespace Wheelhouse.Api.Configurations;

/// <summary>Names the configuration key the Wheelhouse database connection string is read from, and the tag of its readiness check.</summary>
internal static class WheelhouseDatabase
{
    /// <summary>The configuration key holding the Wheelhouse connection string (set in <c>appsettings.json</c>; env <c>DB_CONNECTION</c> overrides via the SDK persistence bundle).</summary>
    public const string ConnectionStringConfigKey = "ConnectionStrings:Wheelhouse";

    /// <summary>The tag of the database readiness check <c>/api/system/ready</c> reports; <c>/health</c> skips it.</summary>
    public const string ReadinessTag = "ready";
}
