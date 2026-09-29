using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinDocker.Core.Localization;
using WinDocker.Core.Models;
using WinDocker.Core.Services;

namespace WinDocker.Core.ViewModels;

public sealed partial class ImagesViewModel : PageViewModelBase
{
    private readonly IDockerService docker;
    private readonly IDialogService dialogs;
    private int refreshVersion;
    private bool hasLoaded;

    public ImagesViewModel(IDockerService docker, IDialogService dialogs, ILocalizer localizer)
        : base(localizer)
    {
        ArgumentNullException.ThrowIfNull(docker);
        ArgumentNullException.ThrowIfNull(dialogs);

        this.docker = docker;
        this.dialogs = dialogs;
        Images.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ObservableCollection<ImageInfo> Images { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    public partial ImageInfo? SelectedImage { get; set; }

    /// <summary>True once a load has succeeded and found nothing; false while loading or when the engine could not be read.</summary>
    public bool IsEmpty => hasLoaded && Images.Count == 0;

    [RelayCommand]
    private Task RefreshAsync() => RunSafeAsync(RefreshCoreAsync);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RemoveAsync()
    {
        if (SelectedImage is not { } image)
        {
            return;
        }

        var displayName = image.IsUntagged ? image.ShortId : image.Reference;
        var confirmed = await dialogs.ConfirmDeleteAsync(
            Localizer.GetString(ResourceKeys.ConfirmDeleteImageTitle),
            Localizer.Format(ResourceKeys.ConfirmDeleteImageMessage, displayName));
        if (!confirmed)
        {
            return;
        }

        await RunMutationAsync(() => docker.RemoveImageAsync(image.Reference), RefreshCoreAsync);
    }

    private bool HasSelection() => SelectedImage is not null;

    private async Task RefreshCoreAsync()
    {
        var version = ++refreshVersion;
        var images = await docker.ListImagesAsync();
        if (version != refreshVersion)
        {
            return;
        }

        var selectedReference = SelectedImage?.Reference;
        hasLoaded = true;
        ReplaceAll(Images, images);
        OnPropertyChanged(nameof(IsEmpty));
        SelectedImage = selectedReference is null ? null : Images.FirstOrDefault(image => image.Reference == selectedReference);
    }
}
