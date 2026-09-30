using WinDocker.Core.Docker;

namespace WinDocker.Core.Tests.Docker;

public class DockerFormatTests
{
    [Theory]
    [InlineData("sha256:0123456789abcdef0123456789abcdef", "0123456789ab")]
    [InlineData("0123456789abcdef0123456789abcdef", "0123456789ab")]
    [InlineData("0123456789ab", "0123456789ab")]
    [InlineData("sha256:abc", "abc")]
    [InlineData("abc", "abc")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ShortId_StripsDigestPrefixAndKeepsTwelveCharacters(string? id, string expected) =>
        Assert.Equal(expected, DockerFormat.ShortId(id));

    [Fact]
    public void ContainerName_RemovesTheLeadingSlash() =>
        Assert.Equal("web", DockerFormat.ContainerName(["/web"]));

    [Fact]
    public void ContainerName_PrefersTheOwnNameOverLinkAliases()
    {
        Assert.Equal("web", DockerFormat.ContainerName(["/proxy/web", "/web"]));
        Assert.Equal("web", DockerFormat.ContainerName(["/web", "/proxy/web"]));
    }

    [Fact]
    public void ContainerName_FallsBackToTheFirstNameWhenThereAreOnlyAliases() =>
        Assert.Equal("proxy/web", DockerFormat.ContainerName(["/proxy/web", "/other/web"]));

    [Fact]
    public void ContainerName_IsEmptyWithoutNames()
    {
        Assert.Equal(string.Empty, DockerFormat.ContainerName(null));
        Assert.Equal(string.Empty, DockerFormat.ContainerName([]));
    }

    [Fact]
    public void Ports_FormatsPublishedPortsLikeDockerPs() =>
        Assert.Equal("0.0.0.0:8080->80/tcp", DockerFormat.Ports([new PortMapping("0.0.0.0", 80, 8080, "tcp")]));

    [Fact]
    public void Ports_FormatsExposedOnlyPorts() =>
        Assert.Equal(
            "53/udp, 80/tcp",
            DockerFormat.Ports([new PortMapping(null, 80, null, "tcp"), new PortMapping(null, 53, null, "udp")]));

    [Fact]
    public void Ports_PutsIpv6AddressesInBrackets() =>
        Assert.Equal("[::]:8080->80/tcp", DockerFormat.Ports([new PortMapping("::", 80, 8080, "tcp")]));

    [Fact]
    public void Ports_ListsIpv4BeforeIpv6ForTheSameBinding() =>
        Assert.Equal(
            "0.0.0.0:8080->80/tcp, [::]:8080->80/tcp",
            DockerFormat.Ports([new PortMapping("::", 80, 8080, "tcp"), new PortMapping("0.0.0.0", 80, 8080, "tcp")]));

    [Fact]
    public void Ports_RemovesDuplicates() =>
        Assert.Equal(
            "80/tcp",
            DockerFormat.Ports([new PortMapping(null, 80, null, "tcp"), new PortMapping(null, 80, null, "tcp")]));

    [Fact]
    public void Ports_OrdersByContainerPort() =>
        Assert.Equal(
            "22/tcp, 0.0.0.0:8080->80/tcp, 0.0.0.0:443->443/tcp",
            DockerFormat.Ports(
            [
                new PortMapping("0.0.0.0", 443, 443, "tcp"),
                new PortMapping("0.0.0.0", 80, 8080, "tcp"),
                new PortMapping(null, 22, null, "tcp"),
            ]));

    [Fact]
    public void Ports_TreatsAZeroPublicPortAsUnpublished() =>
        Assert.Equal("80/tcp", DockerFormat.Ports([new PortMapping("0.0.0.0", 80, 0, "tcp")]));

    [Fact]
    public void Ports_IsEmptyWithoutPorts()
    {
        Assert.Equal(string.Empty, DockerFormat.Ports(null));
        Assert.Equal(string.Empty, DockerFormat.Ports([]));
    }

    [Theory]
    [InlineData(0L, "0B")]
    [InlineData(1L, "1B")]
    [InlineData(999L, "999B")]
    [InlineData(1_000L, "1kB")]
    [InlineData(1_500L, "1.5kB")]
    [InlineData(12_345L, "12.3kB")]
    [InlineData(123_456L, "123kB")]
    [InlineData(7_830_000L, "7.83MB")]
    [InlineData(7_834_999L, "7.83MB")]
    [InlineData(142_000_000L, "142MB")]
    [InlineData(1_020_000_000L, "1.02GB")]
    [InlineData(5_500_000_000_000L, "5.5TB")]
    [InlineData(long.MaxValue, "9.22EB")]
    public void Size_UsesDecimalUnitsWithThreeSignificantDigits(long bytes, string expected) =>
        Assert.Equal(expected, DockerFormat.Size(bytes));

    [Theory]
    [InlineData(99_950L, "100kB")]
    [InlineData(9_995_000L, "10MB")]
    [InlineData(999_499L, "999kB")]
    [InlineData(999_500L, "1MB")]
    [InlineData(999_999_999L, "1GB")]
    public void Size_CarriesRoundingOverIntoTheNextUnit(long bytes, string expected) =>
        Assert.Equal(expected, DockerFormat.Size(bytes));

    [Fact]
    public void Size_TreatsNegativeValuesAsZero() =>
        Assert.Equal("0B", DockerFormat.Size(-1));

    [Fact]
    public void Size_IsCultureInvariant()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            comma.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = comma;

            Assert.Equal("7.83MB", DockerFormat.Size(7_830_000));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void DaemonMessage_ExtractsTheMessageOfAnErrorObject() =>
        Assert.Equal("No such container: abc", DockerFormat.DaemonMessage("""{"message":"No such container: abc"}"""));

    [Fact]
    public void DaemonMessage_FallsBackToTheRawBody()
    {
        Assert.Equal("plain text", DockerFormat.DaemonMessage("  plain text\n"));
        Assert.Equal("""{"other":1}""", DockerFormat.DaemonMessage("""{"other":1}"""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DaemonMessage_IsNullForAnEmptyBody(string? body) =>
        Assert.Null(DockerFormat.DaemonMessage(body));
}
