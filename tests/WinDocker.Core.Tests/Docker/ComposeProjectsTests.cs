using WinDocker.Core.Docker;
using WinDocker.Core.Models;

namespace WinDocker.Core.Tests.Docker;

public class ComposeProjectsTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private static ContainerInfo Member(
        string id,
        string project,
        string service,
        string state = ContainerStates.Running,
        int createdSecond = 0,
        bool oneOff = false,
        string? workingDir = "/srv/shop",
        string? configFiles = "/srv/shop/compose.yaml",
        string? name = null) =>
        new(
            id,
            name ?? $"{project}-{service}-1",
            "image",
            "cmd",
            Start.AddSeconds(createdSecond),
            state,
            state,
            string.Empty,
            new ComposeLabels(project, service, workingDir, configFiles, oneOff));

    private static ContainerInfo Plain(string id, string state = ContainerStates.Running) =>
        new(id, "plain", "image", "cmd", Start, state, state, string.Empty);

    [Fact]
    public void FromContainers_GroupsTheContainersByProject()
    {
        var projects = ComposeProjects.FromContainers(
        [
            Member("c1", "shop", "web"),
            Member("c2", "blog", "app"),
            Member("c3", "shop", "db", createdSecond: 1),
        ]);

        Assert.Equal(["blog", "shop"], projects.Select(project => project.Name));
        Assert.Equal(["c2"], projects[0].Containers.Select(container => container.Id));
        Assert.Equal(["c1", "c3"], projects[1].Containers.Select(container => container.Id));
    }

    [Fact]
    public void FromContainers_IgnoresContainersThatBelongToNoProject()
    {
        var projects = ComposeProjects.FromContainers([Plain("p1"), Member("c1", "shop", "web"), Plain("p2", ContainerStates.Exited)]);

        var project = Assert.Single(projects);
        Assert.Equal("shop", project.Name);
        Assert.Equal(1, project.TotalCount);
    }

    [Fact]
    public void FromContainers_HasNoProjectsWithoutContainersOrWithoutLabels()
    {
        Assert.Empty(ComposeProjects.FromContainers([]));
        Assert.Empty(ComposeProjects.FromContainers([Plain("p1"), Plain("p2")]));
    }

    [Fact]
    public void FromContainers_CountsTheRunningAndAllContainers()
    {
        var project = Assert.Single(ComposeProjects.FromContainers(
        [
            Member("c1", "shop", "web", ContainerStates.Running),
            Member("c2", "shop", "db", ContainerStates.Exited),
            Member("c3", "shop", "cache", ContainerStates.Created),
            Member("c4", "shop", "queue", ContainerStates.Paused),
            Member("c5", "shop", "mail", ContainerStates.Restarting),
            Member("c6", "shop", "search", ContainerStates.Running),
        ]));

        Assert.Equal(2, project.RunningCount);
        Assert.Equal(6, project.TotalCount);
    }

    [Fact]
    public void FromContainers_ListsTheServicesSortedAndWithoutRepeats()
    {
        var project = Assert.Single(ComposeProjects.FromContainers(
        [
            Member("c1", "shop", "web", createdSecond: 0),
            Member("c2", "shop", "db", createdSecond: 1),
            Member("c3", "shop", "web", createdSecond: 2, name: "shop-web-2"),
            Member("c4", "shop", "Cache", createdSecond: 3),
            Member("c5", "shop", string.Empty, createdSecond: 4),
        ]));

        Assert.Equal(["Cache", "db", "web"], project.Services);
        Assert.Equal("Cache, db, web", project.ServicesText);
    }

    [Fact]
    public void FromContainers_TakesTheWorkingDirectoryAndTheConfigFilesFromTheFirstContainerThatHasThem()
    {
        var project = Assert.Single(ComposeProjects.FromContainers(
        [
            Member("c1", "shop", "web", createdSecond: 0, workingDir: null, configFiles: string.Empty),
            Member("c2", "shop", "db", createdSecond: 1, workingDir: "/srv/db", configFiles: "/srv/db/compose.yaml"),
            Member("c3", "shop", "cache", createdSecond: 2, workingDir: "/srv/other", configFiles: "/srv/other/compose.yaml"),
        ]));

        Assert.Equal("/srv/db", project.WorkingDir);
        Assert.Equal("/srv/db/compose.yaml", project.ConfigFiles);
    }

    [Fact]
    public void FromContainers_HasNoWorkingDirectoryOrConfigFilesWhenNoContainerHasThem()
    {
        var project = Assert.Single(ComposeProjects.FromContainers(
            [Member("c1", "shop", "web", workingDir: null, configFiles: null)]));

        Assert.Null(project.WorkingDir);
        Assert.Null(project.ConfigFiles);
    }

    [Fact]
    public void FromContainers_MarksOneOffContainersButCountsThem()
    {
        var project = Assert.Single(ComposeProjects.FromContainers(
        [
            Member("c1", "shop", "web"),
            Member("c2", "shop", "migrate", ContainerStates.Exited, createdSecond: 1, oneOff: true),
        ]));

        Assert.Equal([false, true], project.Containers.Select(container => container.IsOneOff));
        Assert.Equal(2, project.TotalCount);
        Assert.Equal(1, project.RunningCount);
        Assert.Contains("migrate", project.Services);
    }

    [Fact]
    public void FromContainers_OrdersTheProjectsByNameIgnoringCase()
    {
        var projects = ComposeProjects.FromContainers(
        [
            Member("c1", "gamma", "web"),
            Member("c2", "Alpha", "web"),
            Member("c3", "beta", "web"),
        ]);

        Assert.Equal(["Alpha", "beta", "gamma"], projects.Select(project => project.Name));
    }

    [Fact]
    public void FromContainers_KeepsProjectsThatDifferOnlyInCaseApartAndOrdersThemDeterministically()
    {
        var forward = ComposeProjects.FromContainers([Member("c1", "shop", "web"), Member("c2", "Shop", "web")]);
        var backward = ComposeProjects.FromContainers([Member("c2", "Shop", "web"), Member("c1", "shop", "web")]);

        Assert.Equal(["Shop", "shop"], forward.Select(project => project.Name));
        Assert.Equal(forward, backward);
    }

    [Fact]
    public void FromContainers_UsesTheCreationTimeOfTheOldestContainerAsTheProjectTime()
    {
        var project = Assert.Single(ComposeProjects.FromContainers(
        [
            Member("c1", "shop", "web", createdSecond: 30),
            Member("c2", "shop", "db", createdSecond: 10),
            Member("c3", "shop", "cache", createdSecond: 20),
        ]));

        Assert.Equal(Start.AddSeconds(10), project.CreatedAt);
    }

    [Fact]
    public void FromContainers_ListsTheContainersOldestFirstAndBreaksTiesByNameThenId()
    {
        var project = Assert.Single(ComposeProjects.FromContainers(
        [
            Member("c1", "shop", "web", createdSecond: 5),
            Member("c2", "shop", "db", createdSecond: 5, name: "shop-db-1"),
            Member("c3", "shop", "cache", createdSecond: 0),
            Member("c4", "shop", "db", createdSecond: 5, name: "shop-db-1"),
        ]));

        Assert.Equal(["c3", "c2", "c4", "c1"], project.Containers.Select(container => container.Id));
    }

    [Fact]
    public void FromContainers_GivesTheSameResultForAnyOrderOfTheInput()
    {
        ContainerInfo[] containers =
        [
            Member("c1", "shop", "web", createdSecond: 2),
            Member("c2", "shop", "db", createdSecond: 2),
            Member("c3", "blog", "app", ContainerStates.Exited),
            Plain("p1"),
            Member("c4", "shop", "cache", createdSecond: 1),
        ];

        var expected = ComposeProjects.FromContainers(containers);

        Assert.Equal(expected, ComposeProjects.FromContainers(containers.Reverse()));
        Assert.Equal(expected, ComposeProjects.FromContainers([containers[3], containers[0], containers[4], containers[2], containers[1]]));
    }

    [Fact]
    public void FromContainers_ProducesEqualProjectsForEqualInputs()
    {
        var first = ComposeProjects.FromContainers([Member("c1", "shop", "web"), Member("c2", "shop", "db")]);
        var second = ComposeProjects.FromContainers([Member("c1", "shop", "web"), Member("c2", "shop", "db")]);

        Assert.NotSame(first[0].Containers, second[0].Containers);
        Assert.Equal(first[0], second[0]);
        Assert.Equal(first[0].GetHashCode(), second[0].GetHashCode());
    }

    [Fact]
    public void FromContainers_ReflectsAChangeOfStateInTheProject()
    {
        var before = ComposeProjects.FromContainers([Member("c1", "shop", "web", ContainerStates.Running)]);
        var after = ComposeProjects.FromContainers([Member("c1", "shop", "web", ContainerStates.Exited)]);

        Assert.NotEqual(before[0], after[0]);
        Assert.Equal(1, before[0].RunningCount);
        Assert.Equal(0, after[0].RunningCount);
    }

    [Fact]
    public void FromContainers_MapsTheFieldsOfTheContainers()
    {
        var container = Assert.Single(Assert.Single(ComposeProjects.FromContainers(
            [Member("c1", "shop", "web", ContainerStates.Paused, createdSecond: 7, oneOff: true, name: "shop-web-run-1")])).Containers);

        Assert.Equal(new ComposeContainer("c1", "shop-web-run-1", "web", ContainerStates.Paused, Start.AddSeconds(7), IsOneOff: true), container);
    }

    [Fact]
    public void FromContainers_RejectsMissingContainers() =>
        Assert.Throws<ArgumentNullException>(() => ComposeProjects.FromContainers(null!));
}
