namespace Wheelhouse.Domain.Products.Enums;

/// <summary>Refers to what deploys a product, as its catalog entry's <c>deploy</c> says. Only a product Wheelhouse
/// deploys takes automatic deploys and promotions; the owner can still deploy any product by hand.</summary>
public enum ProductDeployMode
{
    /// <summary>Wheelhouse deploys it: releases go to dev on their own and are promoted to prod.</summary>
    Wheelhouse,

    /// <summary>Its own CI deploys it, as Wheelhouse itself is; the control plane never deploys it on its own.</summary>
    Ci,

    /// <summary>Nothing deploys it.</summary>
    None,
}
