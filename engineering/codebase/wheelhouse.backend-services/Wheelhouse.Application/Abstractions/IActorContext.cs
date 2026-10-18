namespace Wheelhouse.Application.Abstractions;

/// <summary>Names the actor the current request acts for: a signed-in user, or an integration key.</summary>
public interface IActorContext
{
    /// <summary>Gets the actor: the signed-in user's login, or <c>key:{name}</c> for an integration key.</summary>
    string Actor { get; }
}
