namespace Wheelhouse.Application.Abstractions;

/// <summary>Describes this running console: the environment it serves and when it started, so it can tell its own
/// target and whether a deployment of it began before this process did.</summary>
public interface IConsoleIdentity
{
    /// <summary>Gets the environment this console serves: <c>dev</c> or <c>prod</c>.</summary>
    string Environment { get; }

    /// <summary>Gets when this console process started.</summary>
    DateTimeOffset BootedAt { get; }
}
