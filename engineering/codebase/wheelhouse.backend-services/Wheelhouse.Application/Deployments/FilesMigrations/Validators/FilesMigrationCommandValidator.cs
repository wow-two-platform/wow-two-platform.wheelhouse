using FluentValidation;
using Wheelhouse.Application.Deployments.FilesMigrations.Commands;
using Wheelhouse.Application.Deployments.FilesMigrations.Services;
using WoW.Two.Sdk.Beta.Be.Foundation.Validation.Formats;

namespace Wheelhouse.Application.Deployments.FilesMigrations.Validators;

/// <summary>Validates a files migration: a known step, a wave of 1 or more, source ids, a Files target, an https URL
/// ending with <c>/</c>, and an import id when one is named (a verify always names one). No message quotes a value.</summary>
public sealed class FilesMigrationCommandValidator : AbstractValidator<FilesMigrationCommand>
{
    /// <summary>Holds how many sources one request may name.</summary>
    public const int MaxSources = 200;

    /// <summary>Holds the pattern an import id follows.</summary>
    public const string ImportId = "^[A-Za-z0-9_-]{1,64}$";

    /// <summary>Configures the rules.</summary>
    public FilesMigrationCommandValidator()
    {
        RuleFor(x => x.Target).MustBeNameSlug();
        RuleFor(x => x.FilesTarget).MustBeNameSlug();
        RuleFor(x => x.Step).Must(step => FilesMigrationCommand.Steps.Contains(step))
            .WithMessage("'Step' must be plan, push or verify.");
        RuleFor(x => x.Wave!.Value).GreaterThanOrEqualTo(1).When(x => x.Wave is not null)
            .WithMessage("'Wave' must be 1 or more.");
        RuleFor(x => x.Sources!).Must(ids => ids.Count <= MaxSources && ids.All(id => System.Text.RegularExpressions.Regex.IsMatch(id ?? "", FilesMigrationManifestParser.Name)))
            .When(x => x.Sources is not null)
            .WithMessage("'Sources' must be source ids: lowercase letters, digits, '-' and '_'.");
        RuleFor(x => x.FilesUrl).Must(IsFilesUrl)
            .WithMessage("'FilesUrl' must be an https URL that ends with '/'.");
        RuleFor(x => x.Import!).Matches(ImportId).When(x => x.Import is not null)
            .WithMessage("'Import' must be an import id.");
        RuleFor(x => x.Import).NotNull().When(x => x.Step == "verify")
            .WithMessage("A verify names the import to check.");
    }

    /// <summary>Whether <paramref name="url"/> is an absolute https URL with a host, no query or fragment, ending with
    /// <c>/</c>.</summary>
    public static bool IsFilesUrl(string? url) =>
        url is not null && url.EndsWith('/') && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && uri.Host.Length > 0;
}
