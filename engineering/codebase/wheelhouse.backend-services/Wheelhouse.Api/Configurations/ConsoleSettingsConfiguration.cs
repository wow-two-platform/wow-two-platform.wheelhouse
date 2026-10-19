using Wheelhouse.Infrastructure.Settings;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;

namespace Wheelhouse.Api.Configurations;

/// <summary>Registers the console's own host facts, the <c>Console</c> section.</summary>
public static class ConsoleSettingsConfiguration
{
    /// <summary>Binds <c>Console:PeerUrl</c> when first resolved, so a test host's configuration applies, and fails the
    /// boot on a value that is not empty or an absolute https origin.</summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The application builder.</returns>
    public static WebApplicationBuilder AddConsoleSettings(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        builder.Services.AddValidatedOptions<ConsoleSettings>(
            settings => configuration.GetSection("Console").Bind(settings),
            options => options.Validate(
                settings => settings.IsValid,
                "Console:PeerUrl must be empty or the other console's address as https://host[:port], without a path, "
                + "query or credentials."));
        return builder;
    }
}
