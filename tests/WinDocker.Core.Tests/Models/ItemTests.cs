using WinDocker.Core.Models;

namespace WinDocker.Core.Tests.Models;

public class ItemTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    private static ContainerInfo Container(string status = "Up 5 seconds") =>
        new("c1", "web", "nginx", "cmd", Created, "running", status, "80/tcp");

    private static ImageInfo Image(string tag = "latest", long size = 10) => new("sha256:aaa", "nginx", tag, Created, size);

    private static VolumeInfo Volume(string mountpoint = "/data") => new("data", "local", mountpoint, null);

    private static List<string?> Changes(System.ComponentModel.INotifyPropertyChanged item)
    {
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        return raised;
    }

    [Fact]
    public void ContainerItem_ExposesTheKeyAndTheInitialInfo()
    {
        var info = Container();

        var item = new ContainerItem(info);

        Assert.Equal("c1", item.Id);
        Assert.Same(info, item.Info);
    }

    [Fact]
    public void ContainerItem_Update_TakesOverADifferentRecordAndRaisesInfoChanged()
    {
        var item = new ContainerItem(Container("Up 5 seconds"));
        var raised = Changes(item);
        var newer = Container("Up 6 seconds");

        item.Update(newer);

        Assert.Same(newer, item.Info);
        Assert.Equal(["Info"], raised);
        Assert.Equal("c1", item.Id);
    }

    [Fact]
    public void ContainerItem_Update_IgnoresAnEqualRecord()
    {
        var original = Container();
        var item = new ContainerItem(original);
        var raised = Changes(item);

        item.Update(Container());

        Assert.Same(original, item.Info);
        Assert.Empty(raised);
    }

    [Fact]
    public void ImageItem_KeyCombinesIdRepositoryAndTag()
    {
        var item = new ImageItem(Image("1.27"));

        Assert.Equal(new ImageKey("sha256:aaa", "nginx", "1.27"), item.Key);
        Assert.Equal(ImageKey.From(Image("1.27")), item.Key);
        Assert.NotEqual(ImageKey.From(Image("latest")), item.Key);
    }

    [Fact]
    public void ImageItem_Update_RaisesOnlyForADifferentRecord()
    {
        var item = new ImageItem(Image(size: 10));
        var raised = Changes(item);

        item.Update(Image(size: 10));
        Assert.Empty(raised);

        var bigger = Image(size: 20);
        item.Update(bigger);

        Assert.Same(bigger, item.Info);
        Assert.Equal(["Info"], raised);
    }

    [Fact]
    public void ImageKey_TellsUntaggedRowsOfDifferentImagesApart()
    {
        var first = new ImageInfo("sha256:aaa", "<none>", "<none>", Created, 1);
        var second = new ImageInfo("sha256:bbb", "<none>", "<none>", Created, 1);

        Assert.NotEqual(ImageKey.From(first), ImageKey.From(second));
    }

    [Fact]
    public void VolumeItem_ExposesTheNameAndUpdatesOnlyOnChange()
    {
        var item = new VolumeItem(Volume());
        var raised = Changes(item);

        item.Update(Volume());
        Assert.Empty(raised);

        var moved = Volume("/other");
        item.Update(moved);

        Assert.Equal("data", item.Name);
        Assert.Same(moved, item.Info);
        Assert.Equal(["Info"], raised);
    }

    [Fact]
    public void Items_RejectMissingInfo()
    {
        Assert.Throws<ArgumentNullException>(() => new ContainerItem(null!));
        Assert.Throws<ArgumentNullException>(() => new ImageItem(null!));
        Assert.Throws<ArgumentNullException>(() => new VolumeItem(null!));
        Assert.Throws<ArgumentNullException>(() => new ContainerItem(Container()).Update(null!));
        Assert.Throws<ArgumentNullException>(() => new ImageItem(Image()).Update(null!));
        Assert.Throws<ArgumentNullException>(() => new VolumeItem(Volume()).Update(null!));
    }
}
