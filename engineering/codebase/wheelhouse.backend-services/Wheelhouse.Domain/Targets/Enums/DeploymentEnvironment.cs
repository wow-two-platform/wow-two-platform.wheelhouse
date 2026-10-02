namespace Wheelhouse.Domain.Targets.Enums;

/// <summary>Refers to which stage of a product a target runs.</summary>
public enum DeploymentEnvironment
{
    /// <summary>Takes a build of any commit or branch.</summary>
    Dev,

    /// <summary>Takes published releases and builds of the product's <c>test</c> branch.</summary>
    Test,

    /// <summary>Takes only a published release that succeeded on test.</summary>
    Prod,
}
