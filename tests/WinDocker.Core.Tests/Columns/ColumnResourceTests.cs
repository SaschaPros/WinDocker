using System.Xml.Linq;
using WinDocker.Core.Columns;

namespace WinDocker.Core.Tests.Columns;

public class ColumnResourceTests
{
    [Fact]
    public void EveryColumnHasAHeaderTextInTheResourceFile()
    {
        var names = ResourceNames();

        foreach (var column in new IEnumerable<IColumnDefinition>[] { ContainerColumns.All, ComposeColumns.All, ImageColumns.All, VolumeColumns.All }.SelectMany(columns => columns))
        {
            Assert.Contains(column.HeaderResourceKey, names);
        }
    }

    private static HashSet<string> ResourceNames()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src", "WinDocker", "Strings", "en-US", "Resources.resw");
            if (File.Exists(path))
            {
                return XDocument.Load(path).Root!.Elements("data").Select(data => (string)data.Attribute("name")!).ToHashSet();
            }
        }

        throw new FileNotFoundException("Resources.resw was not found above the test binaries.");
    }
}
