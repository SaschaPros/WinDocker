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
