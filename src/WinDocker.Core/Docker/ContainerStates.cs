namespace WinDocker.Core.Docker;

/// <summary>Rules derived from the container state strings reported by the Docker engine.</summary>
public static class ContainerStates
{
    public const string Created = "created";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Restarting = "restarting";
    public const string Removing = "removing";
    public const string Exited = "exited";
    public const string Dead = "dead";

    public static bool CanStart(string? state) => state is Created or Exited;

    public static bool CanStop(string? state) => state is Running or Restarting or Paused;

    public static bool RequiresForceRemove(string? state) => state is not (Created or Exited or Dead);
}
