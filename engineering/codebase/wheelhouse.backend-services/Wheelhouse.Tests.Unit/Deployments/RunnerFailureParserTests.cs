using AwesomeAssertions;
using Wheelhouse.Infrastructure.Deployments.Parsers;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace Wheelhouse.Tests.Unit.Deployments;

/// <summary>
/// Tests for <see cref="RunnerFailureParser"/>: only the runner's <c>reason</c> reaches the operator — a refused
/// precondition becomes a conflict, a failed step an unavailable error, and anything else stays opaque.
/// </summary>
public sealed class RunnerFailureParserTests
{
    private readonly RunnerFailureParser _parser = new();

    [Fact]
    public void Refused_precondition_is_a_conflict_naming_the_key()
    {
        var error = _parser.Parse("""{"status": "rejected", "failure": "Rejected", "reason": "Missing required setting: management:Billing:SecretKey"}""");

        error!.Type.Should().Be(AppErrorType.Conflict);
        error.Message.Should().Be("Deployment rejected: Missing required setting: management:Billing:SecretKey");
    }

    [Fact]
    public void Failed_step_is_unavailable()
    {
        var error = _parser.Parse("""{"status": "rejected", "failure": "CommandFailed", "reason": "SSH host key verification failed (exit 255)"}""");

        error!.Type.Should().Be(AppErrorType.ExternalUnavailable);
        error.Message.Should().Be("Deployment step failed: SSH host key verification failed (exit 255)");
    }

    [Fact]
    public void Only_the_last_line_is_read()
    {
        var error = _parser.Parse("warning: password=DO_NOT_LOG\n{\"failure\": \"Rejected\", \"reason\": \"Invalid identifier\"}\n");

        error!.Message.Should().Be("Deployment rejected: Invalid identifier");
    }

    [Theory]
    [InlineData("")] // empty
    [InlineData("password=DO_NOT_LOG")] // not JSON
    [InlineData("""{"status": "rejected", "failure": "ValueError"}""")] // no safe reason
    [InlineData("""{"failure": "Rejected", "reason": 42}""")] // reason is not text
    [InlineData("""["Rejected"]""")] // not an object
    public void Output_without_a_safe_reason_stays_opaque(string standardError)
    {
        _parser.Parse(standardError).Should().BeNull();
    }

    [Fact]
    public void Reason_is_stripped_of_control_characters_and_bounded()
    {
        var error = _parser.Parse("{\"failure\": \"Rejected\", \"reason\": \"a\\u001b[31m" + new string('x', 400) + "\"}");

        error!.Message.Should().NotContain("\u001b");
        error.Message.Length.Should().Be("Deployment rejected: ".Length + 300);
    }
}
