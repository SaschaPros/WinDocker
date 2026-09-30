namespace WinDocker.Core.Docker;

/// <summary>Names of the labels that docker compose puts on the containers, networks and volumes of a project.</summary>
internal static class ComposeLabelNames
{
    public const string Project = "com.docker.compose.project";
    public const string Service = "com.docker.compose.service";
    public const string WorkingDir = "com.docker.compose.project.working_dir";
    public const string ConfigFiles = "com.docker.compose.project.config_files";
    public const string OneOff = "com.docker.compose.oneoff";
}
