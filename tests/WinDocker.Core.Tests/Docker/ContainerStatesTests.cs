using WinDocker.Core.Docker;

namespace WinDocker.Core.Tests.Docker;

public class ContainerStatesTests
{
    [Theory]
    [InlineData("created", true)]
    [InlineData("exited", true)]
    [InlineData("running", false)]
    [InlineData("paused", false)]
    [InlineData("restarting", false)]
    [InlineData("removing", false)]
    [InlineData("dead", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CanStart_OnlyForCreatedAndExited(string? state, bool expected) =>
        Assert.Equal(expected, ContainerStates.CanStart(state));

    [Theory]
    [InlineData("running", true)]
    [InlineData("restarting", true)]
    [InlineData("paused", true)]
    [InlineData("created", false)]
    [InlineData("exited", false)]
    [InlineData("removing", false)]
    [InlineData("dead", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CanStop_OnlyForRunningRestartingAndPaused(string? state, bool expected) =>
        Assert.Equal(expected, ContainerStates.CanStop(state));

    [Theory]
    [InlineData("created", false)]
    [InlineData("exited", false)]
    [InlineData("dead", false)]
    [InlineData("running", true)]
    [InlineData("paused", true)]
    [InlineData("restarting", true)]
    [InlineData("removing", true)]
    [InlineData("something-new", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void RequiresForceRemove_ForEverythingExceptCreatedExitedAndDead(string? state, bool expected) =>
        Assert.Equal(expected, ContainerStates.RequiresForceRemove(state));
}
