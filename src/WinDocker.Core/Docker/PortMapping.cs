namespace WinDocker.Core.Docker;

/// <summary>A container port and its optional host binding, decoupled from the Docker client types.</summary>
public readonly record struct PortMapping(string? Ip, int PrivatePort, int? PublicPort, string Type);
