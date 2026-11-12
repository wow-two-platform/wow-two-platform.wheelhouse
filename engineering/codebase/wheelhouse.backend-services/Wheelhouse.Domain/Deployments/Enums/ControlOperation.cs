namespace Wheelhouse.Domain.Deployments.Enums;

/// <summary>Refers to what the owner does to the release an environment runs, outside a deploy (decision 45): one
/// service, or the whole environment for a stop, a start, a reset or a copy.</summary>
public enum ControlOperation
{
    /// <summary>Stops the containers; they, their volumes and routes stay, and deploys wait until a start.</summary>
    Stop = 0,

    /// <summary>Starts the containers of the release the environment runs again; nothing is pulled or recreated.</summary>
    Start = 1,

    /// <summary>Stops and starts one service's container on the same release.</summary>
    Restart = 2,

    /// <summary>Snapshots the environment's database, stops every service, drops and recreates the database through the
    /// platform PostgreSQL and sets its migration levels to 0; the next deploy's migrate step rebuilds the schema and
    /// starts the services (v0.9). Naming a migration set instead rolls only that set back to 0 with the running release's
    /// own scripts and stops only the service hosting it; the rest of the database keeps its data.</summary>
    Reset = 3,

    /// <summary>Runs the owner's command once in a one-off container of one service's running release image, with the
    /// service's settings, mounts and the platform network (v0.13), such as a first owner's <c>admin create</c>; nothing
    /// stops, and the job keeps the exit code and the last output line, never the arguments.</summary>
    Run = 4,

    /// <summary>Copies another environment's database into this one's on the same platform PostgreSQL: snapshots this
    /// database, stops every service, dumps the source, drops and recreates this database for its owner, restores the dump
    /// as that owner, compares every table's row count with the source, and marks the migration sets <c>behind</c>
    /// (source <c>copy</c>); the next deploy's migrate step reads the copied schema and starts the services. The source
    /// environment must already be stopped, so nothing writes during the copy.</summary>
    Copy = 5,
}
