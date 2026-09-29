using Docker.DotNet;
using Docker.DotNet.NPipe;

namespace WinDocker.Core.Services;

internal static class DockerClientFactory
{
    /// <summary>How long a missing named pipe is retried before Docker is considered unreachable.</summary>
    internal static readonly TimeSpan NamedPipeConnectTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Builds a client for the endpoint the docker CLI would use: <c>DOCKER_HOST</c>, then the current docker
    /// context, then <c>npipe://./pipe/docker_engine</c> on Windows or <c>unix:///var/run/docker.sock</c> elsewhere.
    /// </summary>
    public static IDockerClient CreateDefault() => Create(DockerConfig.Instance.GetEndpoint());

    /// <summary>
    /// The library retries a missing named pipe for ten seconds before it gives up, which is a long wait
    /// when Docker Desktop is simply not running, so the pipe transport gets a shorter connect timeout.
    /// </summary>
    public static IDockerClient Create(Uri endpoint)
    {
        var builder = new DockerClientBuilder().WithEndpoint(endpoint);

        return endpoint.Scheme == "npipe"
            ? builder.WithTransportOptions(new NPipeTransportOptions { ConnectTimeout = NamedPipeConnectTimeout }).Build()
            : builder.Build();
    }
}
