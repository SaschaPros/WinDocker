using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using WinDocker.Core.Collections;
using WinDocker.Core.Columns;
using WinDocker.Core.Docker;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;
using WinDocker.Core.Settings;

namespace WinDocker.Core.ViewModels;

public sealed partial class ImagesViewModel : PageViewModelBase
{
    private readonly IDockerService docker;
    private readonly IDialogService dialogs;
    private readonly ListLayout layout;
    private int refreshVersion;
    private bool hasLoaded;
    private IReadOnlyList<ImageInfo> latest = [];
    private IReadOnlyList<ImageItem> selectedImages = [];

    public ImagesViewModel(
        IDockerService docker,
        IDialogService dialogs,
        ILocalizer localizer,
        SettingsService settings,
        ListLayouts layouts,
        TimeProvider timeProvider)
        : base(localizer, settings, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(layouts);

        this.docker = docker;
        this.dialogs = dialogs;
        layout = layouts.Images;
        // A plain subscription is fine: the page keeps its view model (NavigationCacheMode=Required), so both live as long as the app.
        layout.Changed += (_, _) => ApplyLatest();
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
        latest = images;
        ApplyLatest();
    }

    /// <summary>Shows the last loaded list in the order the layout asks for. Also runs when only the layout changed, without asking the engine again.</summary>
    private void ApplyLatest()
    {
        if (!hasLoaded)
        {
            return;
        }

        var selection = selectedImages;
        var sorted = ListSorter.Sort(latest, layout, ImageColumns.All, image => image.Reference);
        CollectionSync.Sync(
            Images,
            sorted,
            ImageKey.From,
            item => item.Key,
            image => new ImageItem(image),
            (item, image) => item.Update(image));
        OnPropertyChanged(nameof(IsEmpty));
        RestoreSelection(selection);
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

    /// <summary>Removes the dangling images after a warning, or with the option checked all unused ones. Needs no selection.</summary>
    [RelayCommand]
    private async Task PruneAsync()
    {
        var request = new ConfirmRequest(
            Localizer.GetString(ResourceKeys.ConfirmPruneImagesTitle),
            Localizer.GetString(ResourceKeys.ConfirmPruneImagesMessage),
            Localizer.GetString(ResourceKeys.DialogPruneButton),
            Localizer.GetString(ResourceKeys.ConfirmPruneImagesAllOption));
        var confirmation = await ConfirmAsync(dialogs, request);
        if (!confirmation.Confirmed)
        {
            return;
        }

        await RunMutationAsync(
            async () =>
            {
                var result = await docker.PruneImagesAsync(confirmation.OptionChecked);
                StatusMessage = Localizer.Format(ResourceKeys.PruneImagesResult, result.DeletedCount, DockerFormat.Size(result.SpaceReclaimed));
            },
            RefreshCoreAsync);
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

    /// <summary>
    /// After the list was brought in line: forgets removed rows, puts back rows that a moved row lost from the selection (a list view
    /// may treat a move as remove and insert and report the row as deselected), and re-evaluates the commands. Then tells the view
    /// to select the rows again.
    /// </summary>
    private void RestoreSelection(IReadOnlyList<ImageItem> before)
    {
        var present = Images.ToHashSet();
        var kept = before.Where(present.Contains).ToArray();
        if (kept.SequenceEqual(selectedImages))
        {
            RemoveCommand.NotifyCanExecuteChanged();
        }
        else
        {
            SetSelection(kept);
        }

        RaiseItemsSynced();
    }
}
