using WinDocker.Core.Settings;

namespace WinDocker.Core.Tests.Settings;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "windocker-tests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private void WriteFile(string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, content);
    }

    [Fact]
    public void Load_ReturnsTheDefaultsWhenTheFileIsMissing()
    {
        var settings = new JsonSettingsStore(FilePath).Load();

        Assert.Equal(new AppSettings(), settings);
        Assert.Equal(5, settings.RefreshIntervalSeconds);
        Assert.False(Directory.Exists(directory));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("""{"refreshIntervalSeconds": 10""")]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    [InlineData("""{"refreshIntervalSeconds": "abc"}""")]
    [InlineData("""{"refreshIntervalSeconds": 1.5}""")]
    [InlineData("""{"refreshIntervalSeconds": {}}""")]
    [InlineData("""{"refreshIntervalSeconds": 99999999999}""")]
    public void Load_ReturnsTheDefaultsForContentThatIsNotValidSettings(string content)
    {
        WriteFile(content);

        Assert.Equal(new AppSettings(), new JsonSettingsStore(FilePath).Load());
    }

    [Fact]
    public void Load_ReturnsTheDefaultsForBinaryGarbage()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(FilePath, [0xFF, 0xFE, 0x00, 0x01, 0x80, 0x81]);

        Assert.Equal(new AppSettings(), new JsonSettingsStore(FilePath).Load());
    }

    [Fact]
    public void Load_ReturnsTheDefaultsWhenThePathIsADirectory()
    {
        Directory.CreateDirectory(FilePath);

        Assert.Equal(new AppSettings(), new JsonSettingsStore(FilePath).Load());
    }

    [Fact]
    public void Load_UsesTheDefaultForAMissingProperty()
    {
        WriteFile("{}");

        Assert.Equal(5, new JsonSettingsStore(FilePath).Load().RefreshIntervalSeconds);
    }

    [Theory]
    [InlineData("""{"refreshIntervalSeconds": 10}""", 10)]
    [InlineData("""{"RefreshIntervalSeconds": 30}""", 30)]
    [InlineData("""{"refreshIntervalSeconds": 0}""", 0)]
    [InlineData("""{"refreshIntervalSeconds": 7}""", 7)]
    [InlineData("""{"refreshIntervalSeconds": -3}""", -3)]
    [InlineData("""{"refreshIntervalSeconds": 10, "somethingNew": true}""", 10)]
    public void Load_ReadsTheStoredValueAsItIs(string content, int expected)
    {
        WriteFile(content);

        Assert.Equal(expected, new JsonSettingsStore(FilePath).Load().RefreshIntervalSeconds);
    }

    [Fact]
    public void Load_AcceptsAFileWithAByteOrderMark()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, """{"refreshIntervalSeconds": 60}""", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(60, new JsonSettingsStore(FilePath).Load().RefreshIntervalSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(60)]
    public void SaveThenLoad_RoundTrips(int seconds)
    {
        var store = new JsonSettingsStore(FilePath);

        store.Save(new AppSettings(seconds));

        Assert.Equal(new AppSettings(seconds), store.Load());
        Assert.Equal(new AppSettings(seconds), new JsonSettingsStore(FilePath).Load());
    }

    [Fact]
    public void Save_CreatesMissingDirectories()
    {
        var nested = Path.Combine(directory, "a", "b", "settings.json");

        new JsonSettingsStore(nested).Save(new AppSettings(10));

        Assert.True(File.Exists(nested));
        Assert.Equal(10, new JsonSettingsStore(nested).Load().RefreshIntervalSeconds);
    }

    [Fact]
    public void Save_ReplacesTheFileAndLeavesNothingElseBehind()
    {
        var store = new JsonSettingsStore(FilePath);
        store.Save(new AppSettings(10));

        store.Save(new AppSettings(30));

        Assert.Equal(30, store.Load().RefreshIntervalSeconds);
        Assert.Equal([FilePath], Directory.GetFileSystemEntries(directory));
    }

    [Fact]
    public void Save_ReplacesACorruptFile()
    {
        WriteFile("garbage");
        var store = new JsonSettingsStore(FilePath);

        store.Save(new AppSettings(2));

        Assert.Equal(2, store.Load().RefreshIntervalSeconds);
    }

    [Fact]
    public void Save_WritesReadableJsonWithoutAByteOrderMark()
    {
        new JsonSettingsStore(FilePath).Save(new AppSettings(10));

        var bytes = File.ReadAllBytes(FilePath);
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Contains("\"refreshIntervalSeconds\": 10", text);
        Assert.Contains('\n', text);
    }

    [Fact]
    public void Save_WorksWithARelativePath()
    {
        // The folder is created below the working directory, not the temp folder: on Windows the two can be on different
        // drives, and Path.GetRelativePath then has no relative form and returns the absolute path. The working directory
        // itself stays untouched because the tests run in parallel.
        var workingDirectory = Directory.GetCurrentDirectory();
        var folder = "windocker-settings-" + Guid.NewGuid().ToString("N");
        var relative = Path.Combine(folder, "relative.json");
        var absolute = Path.Combine(workingDirectory, relative);
        Assert.False(Path.IsPathRooted(relative));

        try
        {
            new JsonSettingsStore(relative).Save(new AppSettings(2));

            Assert.Equal(2, new JsonSettingsStore(absolute).Load().RefreshIntervalSeconds);
        }
        finally
        {
            var folderPath = Path.Combine(workingDirectory, folder);
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, recursive: true);
            }
        }
    }

    [Fact]
    public void DefaultPath_IsSettingsJsonBelowAWinDockerFolderInLocalApplicationData()
    {
        var path = JsonSettingsStore.DefaultPath;

        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), path);
        Assert.Equal("settings.json", Path.GetFileName(path));
        Assert.Equal("WinDocker", Path.GetFileName(Path.GetDirectoryName(path)));
    }

    [Fact]
    public void Constructor_RejectsAMissingPath()
    {
        Assert.Throws<ArgumentNullException>(() => new JsonSettingsStore(null!));
        Assert.Throws<ArgumentException>(() => new JsonSettingsStore(""));
        Assert.Throws<ArgumentException>(() => new JsonSettingsStore("  "));
    }
}
