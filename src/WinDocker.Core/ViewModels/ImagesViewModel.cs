using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using WinDocker.Core.Collections;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Settings;

namespace WinDocker.Core.ViewModels;

public sealed partial class ImagesViewModel : PageViewModelBase
{
    private readonly IDockerService docker;
    private readonly IDialogService dialogs;
    private int refreshVersion;
    private bool hasLoaded;
    private IReadOnlyList<ImageItem> selectedImages = [];

    public ImagesViewModel(
        IDockerService docker,
        IDialogService dialogs,
        ILocalizer localizer,
        SettingsService settings,
        TimeProvider timeProvider)
        : base(localizer, settings, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(dialogs);

        this.docker = docker;
        this.dialogs = dialogs;
        Images.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<ImageItem> Images { get; } = [];

    /// <summary>The selected rows, as last pushed by <see cref="UpdateSelection"/> and minus the rows a reload removed.</summary>
    public IReadOnlyList<ImageItem> SelectedImages => selectedImages;

    /// <summary>True once a load has succeeded and found nothing; false while loading or when the engine could not be read.</summary>
    public bool IsEmpty => hasLoaded && Images.Count == 0;

    /// <summary>Called by the view with the rows it has selected, whenever the selection changes.</summary>
    public void UpdateSelection(IEnumerable<ImageItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        SetSelection(items.ToArray());
    }

    protected override async Task RefreshCoreAsync()
    {
        var version = ++refreshVersion;
        var images = await docker.ListImagesAsync();
        if (version != refreshVersion)
        {
            return;
        }

        hasLoaded = true;
        CollectionSync.Sync(
            Images,
            images,
            ImageKey.From,
            item => item.Key,
            image => new ImageItem(image),
            (item, image) => item.Update(image));
        OnPropertyChanged(nameof(IsEmpty));
        DropRemovedFromSelection();
    }

    [RelayCommand]
    private Task RefreshAsync() => RunRefreshAsync(RefreshCoreAsync);

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private async Task RemoveAsync()
    {
        var targets = SelectedImages.Select(item => item.Info).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var result = await ConfirmAsync(dialogs, CreateRemoveRequest(targets));
        if (!result.Confirmed)
        {
            return;
        }

        await RunBulkAsync(targets, image => docker.RemoveImageAsync(image.Reference), DisplayName, RefreshCoreAsync);
    }

    private static string DisplayName(ImageInfo image) => image.IsUntagged ? image.ShortId : image.Reference;

    private bool CanRemoveSelected() => HasSelection;

    private ConfirmRequest CreateRemoveRequest(IReadOnlyList<ImageInfo> targets)
    {
        var deleteText = Localizer.GetString(ResourceKeys.DialogDeleteButton);
        return targets.Count == 1
            ? new ConfirmRequest(
                Localizer.GetString(ResourceKeys.ConfirmDeleteImageTitle),
                Localizer.Format(ResourceKeys.ConfirmDeleteImageMessage, DisplayName(targets[0])),
                deleteText)
            : new ConfirmRequest(
                Localizer.GetString(ResourceKeys.ConfirmDeleteImagesTitle),
                Localizer.Format(ResourceKeys.ConfirmDeleteImagesMessage, targets.Count),
                deleteText);
    }

    private void SetSelection(IReadOnlyList<ImageItem> items)
    {
        selectedImages = items;
        OnPropertyChanged(nameof(SelectedImages));
        SetSelectionCount(items.Count);
        RemoveCommand.NotifyCanExecuteChanged();
    }

    /// <summary>After a reload: forgets removed rows, and re-evaluates the commands.</summary>
    private void DropRemovedFromSelection()
    {
        var present = Images.ToHashSet();
        if (selectedImages.All(present.Contains))
        {
            RemoveCommand.NotifyCanExecuteChanged();
            return;
        }

        SetSelection(selectedImages.Where(present.Contains).ToArray());
    }
}
