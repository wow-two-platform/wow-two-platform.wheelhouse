namespace Wheelhouse.Application.Inventory;

/// <summary>Defines a command that changes what the runner's inventory snapshot holds; once it succeeds, the control
/// plane exports the snapshot again.</summary>
public interface IInventoryCommand;
