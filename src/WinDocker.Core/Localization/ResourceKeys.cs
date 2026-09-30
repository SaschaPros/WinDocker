namespace WinDocker.Core.Localization;

/// <summary>
/// Keys of the texts that view models request through <see cref="Services.ILocalizer"/>.
/// The values are the resource names; the texts live in the app's resource file.
/// </summary>
public static class ResourceKeys
{
    /// <summary>The Docker engine cannot be reached. No placeholders.</summary>
    public const string ErrorDockerUnavailable = "Error_DockerUnavailable";

    /// <summary>Title of the dialog that confirms deleting a container. No placeholders.</summary>
    public const string ConfirmDeleteContainerTitle = "ConfirmDeleteContainer_Title";

    /// <summary>Confirms deleting a stopped container. {0} = container name.</summary>
    public const string ConfirmDeleteContainerMessage = "ConfirmDeleteContainer_Message";

    /// <summary>Confirms deleting a container that is still running (it will be forced). {0} = container name.</summary>
    public const string ConfirmDeleteRunningContainerMessage = "ConfirmDeleteRunningContainer_Message";

    /// <summary>Title of the dialog that confirms deleting an image. No placeholders.</summary>
    public const string ConfirmDeleteImageTitle = "ConfirmDeleteImage_Title";

    /// <summary>Confirms deleting an image. {0} = repository:tag, or the short image ID for untagged images.</summary>
    public const string ConfirmDeleteImageMessage = "ConfirmDeleteImage_Message";

    /// <summary>Title of the dialog that confirms deleting a volume. No placeholders.</summary>
    public const string ConfirmDeleteVolumeTitle = "ConfirmDeleteVolume_Title";

    /// <summary>Confirms deleting a volume. {0} = volume name.</summary>
    public const string ConfirmDeleteVolumeMessage = "ConfirmDeleteVolume_Message";

    /// <summary>Log line count without a search. {0} = total number of lines (int).</summary>
    public const string LogsLineCount = "Logs_LineCount";

    /// <summary>Log line count with a search. {0} = number of matching lines (int), {1} = total number of lines (int).</summary>
    public const string LogsMatchSummary = "Logs_MatchSummary";
}
