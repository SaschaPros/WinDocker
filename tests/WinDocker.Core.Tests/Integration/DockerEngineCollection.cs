namespace WinDocker.Core.Tests.Integration;

/// <summary>
/// The tests that use the Docker engine form one collection: they share one <see cref="DockerEngineFixture"/> and run one class
/// after the other. Otherwise the fixture of a class that finishes first would remove the leftovers, which are found by their
/// label, of a class that is still running, and prunes of different classes would collide.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DockerEngineCollection : ICollectionFixture<DockerEngineFixture>
{
    public const string Name = "Docker engine";
}
