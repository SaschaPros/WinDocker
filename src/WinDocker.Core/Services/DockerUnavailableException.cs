namespace WinDocker.Core.Services;

/// <summary>The Docker engine cannot be reached (not running, no permission, misconfigured endpoint or timed out).</summary>
public sealed class DockerUnavailableException : Exception
{
    public DockerUnavailableException()
    {
    }

    public DockerUnavailableException(string message)
        : base(message)
    {
    }

    public DockerUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
