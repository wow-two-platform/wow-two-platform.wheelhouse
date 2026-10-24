namespace Wheelhouse.Application.Abstractions;

/// <summary>Describes this running console: the environment it serves, when it started and the hostname it answers on,
/// so it can tell its own target, whether a deployment of it began before this process did, and what its own
/// environments' site is.</summary>
public interface IConsoleIdentity
{
    /// <summary>Gets the environment this console serves: <c>dev</c> or <c>prod</c>.</summary>
    string Environment { get; }

    /// <summary>Gets when this console process started.</summary>
    DateTimeOffset BootedAt { get; }

    /// <summary>Gets the hostname this console answers on, such as the control host's tailnet name: its own site's URL as
    /// a Wheelhouse deploy renders it (<c>Sites:console:Url</c>), else the first host name its <c>AllowedHosts</c>
    /// lists, as the kit's <c>console-&lt;env&gt;.json</c> writes it; <c>null</c> when it names none (<c>*</c>). Both
    /// consoles share it; dev answers on another port.</summary>
    string? Host { get; }
}
