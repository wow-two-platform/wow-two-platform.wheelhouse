namespace Wheelhouse.Domain.Deployments.Enums;

/// <summary>Refers to what the owner does to the release an environment runs, outside a deploy (decision 45): one
/// service, or the whole environment for a stop or a start.</summary>
public enum ControlOperation
{
    /// <summary>Stops the containers; they, their volumes and routes stay, and deploys wait until a start.</summary>
    Stop = 0,

    /// <summary>Starts the containers of the release the environment runs again; nothing is pulled or recreated.</summary>
    Start = 1,

    /// <summary>Stops and starts one service's container on the same release.</summary>
    Restart = 2,
}
