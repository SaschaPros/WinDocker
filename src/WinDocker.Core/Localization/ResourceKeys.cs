namespace WinDocker.Core.Localization;

/// <summary>
/// Keys of the texts that view models request through <see cref="Services.ILocalizer"/>.
/// The values are the resource names; the texts live in the app's resource file.
/// </summary>
public static class ResourceKeys
{
    /// <summary>The Docker engine cannot be reached. No placeholders.</summary>
    public const string ErrorDockerUnavailable = "Error_DockerUnavailable";

    /// <summary>Primary button of the delete confirmations. No placeholders.</summary>
    public const string DialogDeleteButton = "Dialog_DeleteButton";

    /// <summary>Primary button of the prune confirmations. No placeholders.</summary>
    public const string DialogPruneButton = "Dialog_PruneButton";

    /// <summary>Primary button of the confirmation that removes a compose project. No placeholders.</summary>
    public const string DialogRemoveButton = "Dialog_RemoveButton";

    /// <summary>Title of the dialog that confirms deleting a container. No placeholders.</summary>
    public const string ConfirmDeleteContainerTitle = "ConfirmDeleteContainer_Title";

    /// <summary>Confirms deleting a stopped container. {0} = container name.</summary>
    public const string ConfirmDeleteContainerMessage = "ConfirmDeleteContainer_Message";

    /// <summary>Confirms deleting a container that is still running (it will be forced). {0} = container name.</summary>
    public const string ConfirmDeleteRunningContainerMessage = "ConfirmDeleteRunningContainer_Message";

    /// <summary>Title of the dialog that confirms deleting several containers. No placeholders.</summary>
    public const string ConfirmDeleteContainersTitle = "ConfirmDeleteContainers_Title";

    /// <summary>Confirms deleting several stopped containers. {0} = number of containers (int).</summary>
    public const string ConfirmDeleteContainersMessage = "ConfirmDeleteContainers_Message";

    /// <summary>Confirms deleting several containers of which some are still running (they will be forced). {0} = number of containers (int), {1} = number of running ones (int).</summary>
    public const string ConfirmDeleteContainersRunningMessage = "ConfirmDeleteContainersRunning_Message";

    /// <summary>Title of the dialog that confirms deleting an image. No placeholders.</summary>
    public const string ConfirmDeleteImageTitle = "ConfirmDeleteImage_Title";

    /// <summary>Confirms deleting an image. {0} = repository:tag, or the short image ID for untagged images.</summary>
    public const string ConfirmDeleteImageMessage = "ConfirmDeleteImage_Message";

    /// <summary>Title of the dialog that confirms deleting several images. No placeholders.</summary>
    public const string ConfirmDeleteImagesTitle = "ConfirmDeleteImages_Title";

    /// <summary>Confirms deleting several images. {0} = number of images (int).</summary>
    public const string ConfirmDeleteImagesMessage = "ConfirmDeleteImages_Message";

    /// <summary>Title of the dialog that confirms deleting a volume. No placeholders.</summary>
    public const string ConfirmDeleteVolumeTitle = "ConfirmDeleteVolume_Title";

    /// <summary>Confirms deleting a volume. {0} = volume name.</summary>
    public const string ConfirmDeleteVolumeMessage = "ConfirmDeleteVolume_Message";

    /// <summary>Title of the dialog that confirms deleting several volumes. No placeholders.</summary>
    public const string ConfirmDeleteVolumesTitle = "ConfirmDeleteVolumes_Title";

    /// <summary>Confirms deleting several volumes. {0} = number of volumes (int).</summary>
    public const string ConfirmDeleteVolumesMessage = "ConfirmDeleteVolumes_Message";

    /// <summary>Title of the dialog that confirms pruning containers. No placeholders.</summary>
    public const string ConfirmPruneContainersTitle = "ConfirmPruneContainers_Title";

    /// <summary>Warns that pruning removes all stopped containers for good. No placeholders.</summary>
    public const string ConfirmPruneContainersMessage = "ConfirmPruneContainers_Message";

    /// <summary>Title of the dialog that confirms pruning images. No placeholders.</summary>
    public const string ConfirmPruneImagesTitle = "ConfirmPruneImages_Title";

    /// <summary>Warns that pruning removes all dangling images (untagged and unused) for good. No placeholders.</summary>
    public const string ConfirmPruneImagesMessage = "ConfirmPruneImages_Message";

    /// <summary>Check box of the image prune dialog: also remove unused images that have tags. No placeholders.</summary>
    public const string ConfirmPruneImagesAllOption = "ConfirmPruneImages_AllOption";

    /// <summary>Title of the dialog that confirms pruning volumes. No placeholders.</summary>
    public const string ConfirmPruneVolumesTitle = "ConfirmPruneVolumes_Title";

    /// <summary>Warns that pruning removes all anonymous volumes that no container uses, and their data, for good. No placeholders.</summary>
    public const string ConfirmPruneVolumesMessage = "ConfirmPruneVolumes_Message";

    /// <summary>Check box of the volume prune dialog: also remove unused named volumes. No placeholders.</summary>
    public const string ConfirmPruneVolumesNamedOption = "ConfirmPruneVolumes_NamedOption";

    /// <summary>What a container prune removed. {0} = number of containers (int), {1} = reclaimed space as text, for example "12.3MB".</summary>
    public const string PruneContainersResult = "Prune_ContainersResult";

    /// <summary>What an image prune removed. {0} = number of images (int), {1} = reclaimed space as text, for example "12.3MB".</summary>
    public const string PruneImagesResult = "Prune_ImagesResult";

    /// <summary>What a volume prune removed. {0} = number of volumes (int), {1} = reclaimed space as text, for example "12.3MB".</summary>
    public const string PruneVolumesResult = "Prune_VolumesResult";

    /// <summary>Compose project status: all containers run. {0} = number of running containers (int), {1} = number of containers (int).</summary>
    public const string ComposeStatusRunning = "Compose_StatusRunning";

    /// <summary>Compose project status: some containers run, some do not. {0} = number of running containers (int), {1} = number of containers (int).</summary>
    public const string ComposeStatusPartial = "Compose_StatusPartial";

    /// <summary>Compose project status: no container runs. No placeholders.</summary>
    public const string ComposeStatusExited = "Compose_StatusExited";

    /// <summary>Title of the dialog that confirms removing compose projects. No placeholders.</summary>
    public const string ConfirmComposeDownTitle = "ConfirmComposeDown_Title";

    /// <summary>Confirms stopping and removing one compose project with its containers and networks. {0} = project name.</summary>
    public const string ConfirmComposeDownMessage = "ConfirmComposeDown_Message";

    /// <summary>Confirms stopping and removing several compose projects with their containers and networks. {0} = number of projects (int).</summary>
    public const string ConfirmComposeDownMultipleMessage = "ConfirmComposeDownMultiple_Message";

    /// <summary>Check box of the compose remove dialog: also remove the volumes of the projects. No placeholders.</summary>
    public const string ConfirmComposeDownVolumesOption = "ConfirmComposeDown_VolumesOption";

    /// <summary>First line of the error shown when several operations of one action failed. {0} = number of failed operations (int), {1} = number of operations (int).</summary>
    public const string BulkPartialFailure = "Bulk_PartialFailure";

    /// <summary>Settings: label of the interval choice that turns automatic refresh off. No placeholders.</summary>
    public const string SettingsIntervalOff = "Settings_IntervalOff";

    /// <summary>Settings: label of an automatic refresh interval. {0} = seconds (int).</summary>
    public const string SettingsIntervalSeconds = "Settings_IntervalSeconds";

    /// <summary>Log line count without a search. {0} = total number of lines (int).</summary>
    public const string LogsLineCount = "Logs_LineCount";

    /// <summary>Log line count with a search. {0} = number of matching lines (int), {1} = total number of lines (int).</summary>
    public const string LogsMatchSummary = "Logs_MatchSummary";
}
